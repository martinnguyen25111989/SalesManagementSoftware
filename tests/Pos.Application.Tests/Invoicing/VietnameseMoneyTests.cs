using Microsoft.EntityFrameworkCore;
using Pos.Application.Invoicing;
using Pos.Application.Invoicing.IssueEInvoice;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Invoicing;

public class VietnameseMoneyTests
{
    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(11_000, "Mười một nghìn đồng")]
    [InlineData(21_000, "Hai mươi mốt nghìn đồng")]
    [InlineData(25_920, "Hai mươi lăm nghìn chín trăm hai mươi đồng")]
    [InlineData(100, "Một trăm đồng")]
    [InlineData(1_000_005, "Một triệu không trăm lẻ năm đồng")]
    [InlineData(1_000_000, "Một triệu đồng")]
    public void ToWords_FormatsVnd(long amount, string expected)
    {
        Assert.Equal(expected, VietnameseMoney.ToWords(amount));
    }
}

public class EInvoiceRequestMoneyTests
{
    [Fact]
    public async Task Request_TotalsReconcile_WithB13()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12_000m, VatRate.Eight);
        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 2m) }, // 2×12.000, VAT 8% → 25.920
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        var provider = new FakeEInvoiceProvider();
        await new IssueEInvoiceHandler(db, provider).Handle(new IssueEInvoiceCommand { OrderId = draft.Id }, default);

        var req = provider.LastRequest!;
        Assert.Equal(24_000m, req.TotalAmountWithoutVat);
        Assert.Equal(1_920m, req.TotalVat);
        Assert.Equal(25_920m, req.TotalAmount);
        Assert.Equal(req.TotalAmountWithoutVat, req.Items.Sum(i => i.AmountWithoutVat)); // khử lệch tiền
        Assert.Equal(req.TotalVat, req.Items.Sum(i => i.VatAmount));
        var vat8 = Assert.Single(req.VatByRate);
        Assert.Equal(8m, vat8.VatPercent);
        Assert.Equal("Hai mươi lăm nghìn chín trăm hai mươi đồng", req.AmountInWords);
    }
}
