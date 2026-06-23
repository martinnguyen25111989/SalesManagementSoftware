using Microsoft.EntityFrameworkCore;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Orders;

/// <summary>B13 — làm tròn tiền mặt end-to-end: RoundingAdj đúng dấu &amp; sổ quỹ (ExpectedCash) khớp.</summary>
public class CashRoundingFlowTests
{
    [Fact]
    public async Task CashRoundedOrder_RecordsAdj_AndShiftCashMatchesRoundedTotal()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // ExpectedCash 500.000
        var variantId = await TestData.AddProductAsync(db, 12_345m, VatRate.Eight);

        // 12.345 + VAT 8% (988) = 13.333 → làm tròn 1.000 = 13.000 (adj −333).
        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            CashRoundingUnit = 1_000m,
            Lines = new[] { new CreateOrderLine(variantId, 1m) },
        }, default);

        Assert.Equal(13_000m, draft.GrandTotal);
        Assert.Equal(-333m, draft.RoundingAdj);

        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        // Sổ quỹ khớp tổng đã làm tròn: 500.000 + 13.000.
        var shift = await db.Shifts.FindAsync(TestData.ShiftId);
        Assert.Equal(513_000m, shift!.ExpectedCash);

        var order = await db.Orders.FindAsync(draft.Id);
        Assert.Equal(-333m, order!.RoundingAdj);
    }
}
