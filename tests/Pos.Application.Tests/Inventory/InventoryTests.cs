using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Inventory.AdjustStock;
using Pos.Application.Inventory.Queries;
using Pos.Application.Inventory.ReceiveStock;
using Pos.Application.Inventory.TransferStock;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Inventory;

public class ReceiveStockTests
{
    [Fact]
    public async Task Receive_IncreasesOnHand_AndComputesTotal()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);

        var result = await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 5000m) },
        }, default);

        Assert.Equal(50_000m, result.Total);
        Assert.Equal(10m, result.Lines[0].OnHand);
        var tx = await db.StockTransactions.SingleAsync();
        Assert.Equal(StockTransactionType.Purchase, tx.Type);
        Assert.Equal(10m, tx.QtyChange);
        Assert.Equal(5000m, tx.UnitCost);
    }

    [Fact]
    public async Task Receive_IsIdempotent()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        var cmd = new ReceiveStockCommand
        {
            ReceiptId = Guid.NewGuid(), StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 5000m) },
        };
        var handler = new ReceiveStockHandler(db);

        await handler.Handle(cmd, default);
        await handler.Handle(cmd, default);

        Assert.Equal(1, await db.PurchaseReceipts.CountAsync());
        Assert.Equal(10m, (await db.StockBalances.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Receive_RejectsUnknownSupplier()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);

        await Assert.ThrowsAsync<NotFoundException>(() => new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = Guid.NewGuid(),
            Lines = new[] { new ReceiveLine(variantId, 1m, 1000m) },
        }, default));
    }

    [Fact]
    public async Task Receive_MultipleLinesSameVariant_KeepsSingleBalance()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);

        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 3m, 1000m), new ReceiveLine(variantId, 7m, 1000m) },
        }, default);

        Assert.Equal(1, await db.StockBalances.CountAsync());
        Assert.Equal(10m, (await db.StockBalances.SingleAsync()).Quantity);
    }
}

public class AdjustStockTests
{
    private static async Task<Guid> SeedWithStockAsync(TestPosDbContext db, decimal qty)
    {
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, qty, 1000m) },
        }, default);
        return variantId;
    }

    [Fact]
    public async Task StockTake_RecordsDifference_AndUpdatesOnHand()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedWithStockAsync(db, 10m);

        var result = await new AdjustStockHandler(db).Handle(new AdjustStockCommand
        {
            StoreId = TestData.StoreId, VariantId = variantId, CountedQty = 8m,
            Reason = "Hao hụt", ManagerApproved = true,
        }, default);

        Assert.Equal(10m, result.PreviousQty);
        Assert.Equal(-2m, result.Difference);
        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(8m, balance.Quantity);
    }

    [Fact]
    public async Task Rejects_WhenNotApproved_OrNoReason()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedWithStockAsync(db, 10m);
        var handler = new AdjustStockHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new AdjustStockCommand
        { StoreId = TestData.StoreId, VariantId = variantId, CountedQty = 8m, Reason = "x", ManagerApproved = false }, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new AdjustStockCommand
        { StoreId = TestData.StoreId, VariantId = variantId, CountedQty = 8m, Reason = " ", ManagerApproved = true }, default));
    }

    [Fact]
    public async Task IsIdempotent()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedWithStockAsync(db, 10m);
        var cmd = new AdjustStockCommand
        { AdjustId = Guid.NewGuid(), StoreId = TestData.StoreId, VariantId = variantId, CountedQty = 8m, Reason = "Hao hụt", ManagerApproved = true };
        var handler = new AdjustStockHandler(db);

        await handler.Handle(cmd, default);
        await handler.Handle(cmd, default);

        Assert.Equal(8m, (await db.StockBalances.SingleAsync()).Quantity); // không điều chỉnh 2 lần
        Assert.Equal(1, await db.StockTransactions.CountAsync(s => s.Type == StockTransactionType.StockTake));
    }
}

public class TransferStockTests
{
    private static async Task<Guid> SeedTwoStoresWithStockAsync(TestPosDbContext db, decimal qty)
    {
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddStore2Async(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, qty, 1000m) },
        }, default);
        return variantId;
    }

    [Fact]
    public async Task Transfer_MovesBetweenStores_TwoLegsMatch()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedTwoStoresWithStockAsync(db, 10m);

        var result = await new TransferStockHandler(db).Handle(new TransferStockCommand
        {
            FromStoreId = TestData.StoreId, ToStoreId = TestData.Store2Id,
            Lines = new[] { new TransferLine(variantId, 4m) },
        }, default);

        Assert.Equal(6m, result.Lines[0].FromOnHand);
        Assert.Equal(4m, result.Lines[0].ToOnHand);
        Assert.Equal(1, await db.StockTransactions.CountAsync(s => s.Type == StockTransactionType.TransferOut));
        Assert.Equal(1, await db.StockTransactions.CountAsync(s => s.Type == StockTransactionType.TransferIn));
    }

    [Fact]
    public async Task Transfer_RejectsSameStore()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedTwoStoresWithStockAsync(db, 10m);

        await Assert.ThrowsAsync<BusinessRuleException>(() => new TransferStockHandler(db).Handle(new TransferStockCommand
        {
            FromStoreId = TestData.StoreId, ToStoreId = TestData.StoreId,
            Lines = new[] { new TransferLine(variantId, 1m) },
        }, default));
    }

    [Fact]
    public async Task Transfer_IsIdempotent()
    {
        using var db = TestPosDbContext.Create();
        var variantId = await SeedTwoStoresWithStockAsync(db, 10m);
        var cmd = new TransferStockCommand
        {
            TransferId = Guid.NewGuid(), FromStoreId = TestData.StoreId, ToStoreId = TestData.Store2Id,
            Lines = new[] { new TransferLine(variantId, 4m) },
        };
        var handler = new TransferStockHandler(db);

        await handler.Handle(cmd, default);
        await handler.Handle(cmd, default);

        Assert.Equal(6m, await OnHand(db, TestData.StoreId, variantId));   // không chuyển 2 lần
        Assert.Equal(4m, await OnHand(db, TestData.Store2Id, variantId));
    }

    private static async Task<decimal> OnHand(TestPosDbContext db, Guid storeId, Guid variantId) =>
        (await db.StockBalances.FirstAsync(b => b.StoreId == storeId && b.VariantId == variantId)).Quantity;
}

public class GetStockOnHandTests
{
    [Fact]
    public async Task Snapshot_And_Ledger_Agree()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 1000m) },
        }, default);
        var handler = new GetStockOnHandHandler(db);

        var snapshot = await handler.Handle(new GetStockOnHandQuery(TestData.StoreId, variantId), default);
        var ledger = await handler.Handle(new GetStockOnHandQuery(TestData.StoreId, variantId, FromLedger: true), default);

        Assert.Equal(10m, snapshot.Single().OnHand);
        Assert.Equal(10m, ledger.Single().OnHand);
    }
}
