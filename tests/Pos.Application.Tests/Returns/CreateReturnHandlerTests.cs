using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Returns.CreateReturn;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Tests.Returns;

public class CreateReturnHandlerTests
{
    private static async Task<(Guid orderId, OrderLine line)> CompletedAsync(TestPosDbContext db, decimal qty = 1m)
    {
        var id = await TestData.CreateCompletedOrderAsync(db, qty);
        var order = await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == id);
        return (id, order.Lines.First());
    }

    private static CreateReturnCommand Cmd(Guid orderId, Guid orderLineId, decimal qty, bool restock = true) => new()
    {
        OriginalOrderId = orderId,
        ShiftId = TestData.ShiftId,
        Reason = "Khách trả",
        ManagerApproved = true,
        RefundMethod = PaymentMethod.Cash,
        Lines = new[] { new ReturnLineInput(orderLineId, qty, restock) },
    };

    [Fact]
    public async Task FullReturn_Restocks_RefundsCash_MarksReturned()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 1m); // LineTotal 11.000 (10.000 + 10% VAT)
        var expectedAfterSale = (await db.Shifts.FindAsync(TestData.ShiftId))!.ExpectedCash;

        var result = await new CreateReturnHandler(db).Handle(Cmd(orderId, line.Id, 1m), default);

        Assert.Equal(11_000m, result.RefundAmount);
        Assert.Equal(OrderStatus.Returned, result.OriginalOrderStatus);

        var stock = await db.StockTransactions.Where(s => s.Type == StockTransactionType.Return).SingleAsync();
        Assert.Equal(1m, stock.QtyChange);

        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(0m, balance.Quantity); // -1 (bán) + 1 (trả)

        var shift = await db.Shifts.FindAsync(TestData.ShiftId);
        Assert.Equal(expectedAfterSale - 11_000m, shift!.ExpectedCash);
    }

    [Fact]
    public async Task PartialReturn_MarksPartiallyReturned_AndProratesRefund()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 2m); // LineTotal 22.000

        var result = await new CreateReturnHandler(db).Handle(Cmd(orderId, line.Id, 1m), default);

        Assert.Equal(11_000m, result.RefundAmount); // 22.000 / 2 * 1
        Assert.Equal(OrderStatus.PartiallyReturned, result.OriginalOrderStatus);
    }

    [Fact]
    public async Task DefectiveItem_NotRestocked_ButStillRefunds()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 1m);

        var result = await new CreateReturnHandler(db).Handle(Cmd(orderId, line.Id, 1m, restock: false), default);

        Assert.Equal(11_000m, result.RefundAmount);
        Assert.False(result.Lines[0].Restocked);
        Assert.Empty(await db.StockTransactions.Where(s => s.Type == StockTransactionType.Return).ToListAsync());
        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(-1m, balance.Quantity); // chỉ còn phần bán, không nhập lại
    }

    [Fact]
    public async Task Rejects_ReturnMoreThanPurchased()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 1m);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new CreateReturnHandler(db).Handle(Cmd(orderId, line.Id, 2m), default));
    }

    [Fact]
    public async Task CumulativeReturns_CannotExceedPurchased()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 2m);
        var handler = new CreateReturnHandler(db);

        await handler.Handle(Cmd(orderId, line.Id, 1m) with { ReturnId = Guid.NewGuid() }, default);
        await handler.Handle(Cmd(orderId, line.Id, 1m) with { ReturnId = Guid.NewGuid() }, default); // tới đây đủ 2

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Cmd(orderId, line.Id, 1m) with { ReturnId = Guid.NewGuid() }, default));

        var order = await db.Orders.FindAsync(orderId);
        Assert.Equal(OrderStatus.Returned, order!.Status);
    }

    [Fact]
    public async Task Rejects_WhenNotApproved_OrNoReason()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 1m);
        var handler = new CreateReturnHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Cmd(orderId, line.Id, 1m) with { ManagerApproved = false }, default));
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Cmd(orderId, line.Id, 1m) with { Reason = "  " }, default));
    }

    [Fact]
    public async Task Rejects_WhenOriginalNotCompleted()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var draftId = await TestData.CreateDraftOrderAsync(db);
        var line = (await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == draftId)).Lines.First();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new CreateReturnHandler(db).Handle(Cmd(draftId, line.Id, 1m), default));
    }

    [Fact]
    public async Task IsIdempotent_NoDoubleRefundOrRestock()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var (orderId, line) = await CompletedAsync(db, qty: 1m);
        var cmd = Cmd(orderId, line.Id, 1m);
        var handler = new CreateReturnHandler(db);

        var first = await handler.Handle(cmd, default);
        var second = await handler.Handle(cmd, default);

        Assert.Equal(first.RefundAmount, second.RefundAmount);
        Assert.Equal(1, await db.ReturnOrders.CountAsync());
        Assert.Equal(1, await db.StockTransactions.CountAsync(s => s.Type == StockTransactionType.Return));
        var shift = await db.Shifts.FindAsync(TestData.ShiftId);
        Assert.Equal(500_000m, shift!.ExpectedCash); // 500k +11k (bán) −11k (trả), không trừ 2 lần
    }
}
