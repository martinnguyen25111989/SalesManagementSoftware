using Pos.Application.Common;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Tests.Orders;

/// <summary>B6: thẻ/QR/ví phải có mã tham chiếu xác nhận đã nhận tiền (không tự "Paid").</summary>
public class PaymentConfirmationTests
{
    private static async Task<Order> DraftAsync(TestPosDbContext db)
    {
        var id = await TestData.CreateDraftOrderAsync(db);
        return (await db.Orders.FindAsync(id))!;
    }

    [Fact]
    public async Task Rejects_VietQR_WithoutReference()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var order = await DraftAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = order.Id,
                Payments = new[] { new PaymentInput(PaymentMethod.VietQR, order.GrandTotal) },
            }, default));
    }

    [Fact]
    public async Task Accepts_VietQR_WithReference()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var order = await DraftAsync(db);

        var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = order.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.VietQR, order.GrandTotal, "FT999") },
        }, default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.False(result.OpenDrawer); // không phải tiền mặt
    }

    [Fact]
    public async Task Rejects_NonPositivePaymentAmount()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var order = await DraftAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = order.Id,
                Payments = new[]
                {
                    new PaymentInput(PaymentMethod.Cash, order.GrandTotal + 1000m),
                    new PaymentInput(PaymentMethod.Cash, -1000m),
                },
            }, default));
    }
}
