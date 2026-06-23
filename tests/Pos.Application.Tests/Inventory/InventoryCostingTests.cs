using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Inventory.ReceiveStock;
using Pos.Application.Inventory.TransferStock;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Inventory;

/// <summary>B8 — giá vốn bình quân gia quyền di động + đóng dấu giá vốn khi bán (tính lãi gộp).</summary>
public class WeightedAverageCostTests
{
    [Fact]
    public async Task Receiving_AtDifferentCosts_BlendsMovingAverage()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);

        // 10 @ 1.000 rồi 10 @ 2.000 → bình quân = 1.500
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 1000m), new ReceiveLine(variantId, 10m, 2000m) },
        }, default);

        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(20m, balance.Quantity);
        Assert.Equal(1500m, balance.AvgCost);
    }

    [Fact]
    public async Task Sale_StampsCurrentAverageCost_OnTransaction()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 1000m), new ReceiveLine(variantId, 10m, 2000m) },
        }, default);

        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 3m) },
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        var sale = await db.StockTransactions.SingleAsync(s => s.Type == StockTransactionType.Sale);
        Assert.Equal(-3m, sale.QtyChange);
        Assert.Equal(1500m, sale.UnitCost);                       // giá vốn = bình quân hiện hành
        Assert.Equal(1500m, (await db.StockBalances.SingleAsync()).AvgCost); // bán không đổi bình quân
    }

    [Fact]
    public async Task Transfer_CarriesSourceAverageCost_ToDestination()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        await TestData.AddStore2Async(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 1500m) },
        }, default);

        await new TransferStockHandler(db).Handle(new TransferStockCommand
        {
            FromStoreId = TestData.StoreId, ToStoreId = TestData.Store2Id,
            Lines = new[] { new TransferLine(variantId, 4m) },
        }, default);

        var dest = await db.StockBalances.SingleAsync(b => b.StoreId == TestData.Store2Id);
        Assert.Equal(4m, dest.Quantity);
        Assert.Equal(1500m, dest.AvgCost); // kho đích nhận đúng giá vốn hàng đi
    }
}

/// <summary>B8 — chính sách bán khi tồn về âm theo cấu hình chi nhánh (Allow / Warn / Block).</summary>
public class NegativeStockPolicyTests
{
    private static async Task<OrderResult> SeedOrderWithoutStockAsync(TestPosDbContext db, NegativeStockPolicy policy)
    {
        await TestData.SeedAsync(db);
        var store = await db.Stores.FindAsync(TestData.StoreId);
        store!.NegativeStockPolicy = policy;
        await db.SaveChangesAsync();
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Ten);
        return await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 2m) },
        }, default);
    }

    [Fact]
    public async Task Allow_PermitsOversell()
    {
        using var db = TestPosDbContext.Create();
        var draft = await SeedOrderWithoutStockAsync(db, NegativeStockPolicy.Allow);

        var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(-2m, (await db.StockBalances.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Block_RejectsOversell()
    {
        using var db = TestPosDbContext.Create();
        var draft = await SeedOrderWithoutStockAsync(db, NegativeStockPolicy.Block);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
            }, default));

        // Không trừ tồn / không chuyển trạng thái khi bị chặn.
        Assert.False(await db.StockTransactions.AnyAsync());
        Assert.Equal(OrderStatus.Draft, (await db.Orders.FindAsync(draft.Id))!.Status);
    }

    [Fact]
    public async Task Warn_RejectsOversell_WithoutApproval()
    {
        using var db = TestPosDbContext.Create();
        var draft = await SeedOrderWithoutStockAsync(db, NegativeStockPolicy.Warn);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
            }, default));
    }

    [Fact]
    public async Task Warn_AllowsOversell_WithManagerApproval()
    {
        using var db = TestPosDbContext.Create();
        var draft = await SeedOrderWithoutStockAsync(db, NegativeStockPolicy.Warn);

        var ok = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, ManagerApproved = true,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        Assert.Equal(OrderStatus.Completed, ok.Status);
    }
}
