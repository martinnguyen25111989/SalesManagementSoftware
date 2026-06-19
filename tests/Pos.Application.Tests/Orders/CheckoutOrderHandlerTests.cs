using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Orders;

public class CheckoutOrderHandlerTests
{
    /// <summary>Dựng 1 đơn Draft sẵn (2 x 12.000, VAT 8% → 25.920) và trả về kết quả tạo đơn.</summary>
    private static async Task<OrderResult> CreateDraftAsync(TestPosDbContext db)
    {
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Eight);
        return await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId,
            ShiftId = TestData.ShiftId,
            CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 2m) },
        }, default);
    }

    [Fact]
    public async Task Completes_DeductsStock_AndUpdatesShiftCash()
    {
        using var db = TestPosDbContext.Create();
        var draft = await CreateDraftAsync(db);

        var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
            CashTendered = 30000m,
        }, default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        Assert.True(result.OpenDrawer);
        Assert.Equal(30000m - 25920m, result.ChangeDue);

        var stock = await db.StockTransactions.SingleAsync();
        Assert.Equal(StockTransactionType.Sale, stock.Type);
        Assert.Equal(-2m, stock.QtyChange);
        Assert.Equal(draft.Id, stock.RefId);

        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(-2m, balance.Quantity);

        var shift = await db.Shifts.FindAsync(TestData.ShiftId);
        Assert.Equal(500_000m + 25920m, shift!.ExpectedCash);
    }

    [Fact]
    public async Task Rejects_When_PaymentTotalMismatch()
    {
        using var db = TestPosDbContext.Create();
        var draft = await CreateDraftAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = draft.Id,
                Payments = new[] { new PaymentInput(PaymentMethod.Cash, 20000m) }, // thiếu
            }, default));
    }

    [Fact]
    public async Task MixedPayment_SumsToGrandTotal()
    {
        using var db = TestPosDbContext.Create();
        var draft = await CreateDraftAsync(db);

        var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[]
            {
                new PaymentInput(PaymentMethod.Cash, 20000m),
                new PaymentInput(PaymentMethod.VietQR, 5920m, "FT123"),
            },
        }, default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(25920m, result.TotalPaid);
        Assert.Equal(2, db.Payments.Count());
    }

    [Fact]
    public async Task IsIdempotent_NoDoubleStockDeduction()
    {
        using var db = TestPosDbContext.Create();
        var draft = await CreateDraftAsync(db);
        var cmd = new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        };
        var handler = new CheckoutOrderHandler(db);

        var first = await handler.Handle(cmd, default);
        var second = await handler.Handle(cmd, default);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(1, db.StockTransactions.Count());  // không trừ tồn lần hai
        Assert.Equal(1, db.Payments.Count());           // không thu tiền lần hai
    }
}
