using Microsoft.EntityFrameworkCore;
using Pos.Application.Returns.CreateReturn;
using Pos.Application.Shifts.CloseShift;
using Pos.Application.Shifts.Reports;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Shifts;

/// <summary>B9 — hoàn tiền mặt trong ca phải hiện trong X/Z report và khớp tiền mặt dự kiến (quỹ).</summary>
public class ShiftReportCashRefundTests
{
    /// <summary>Bán 2 (tiền mặt, LineTotal 22.000) rồi trả 1 (hoàn 11.000 tiền mặt). Trả về OrderLineId.</summary>
    private static async Task<Guid> SellThenRefundOneCashAsync(TestPosDbContext db)
    {
        await TestData.SeedAsync(db); // ExpectedCash 500.000, ca mở
        var orderId = await TestData.CreateCompletedOrderAsync(db, qty: 2m); // bán tiền mặt 22.000
        var line = (await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId)).Lines.First();

        await new CreateReturnHandler(db).Handle(new CreateReturnCommand
        {
            OriginalOrderId = orderId,
            ShiftId = TestData.ShiftId,
            Reason = "Khách trả",
            ManagerApproved = true,
            RefundMethod = PaymentMethod.Cash,
            Lines = new[] { new ReturnLineInput(line.Id, 1m) },
        }, default);
        return orderId;
    }

    [Fact]
    public async Task XReport_ShowsCashRefund_AndExpectedCashIsConsistent()
    {
        using var db = TestPosDbContext.Create();
        await SellThenRefundOneCashAsync(db);

        var x = await new GetShiftReportHandler(db).Handle(new GetShiftReportQuery(TestData.ShiftId), default);

        Assert.Equal(22_000m, x.CashSales);
        Assert.Equal(11_000m, x.CashRefunds);
        // Bất biến quỹ (B9): ExpectedCash = đầu ca + bán tiền mặt + thu − chi − hoàn tiền mặt.
        Assert.Equal(x.OpeningFloat + x.CashSales + x.CashIn - x.CashOut - x.CashRefunds, x.ExpectedCash);
        Assert.Equal(511_000m, x.ExpectedCash); // 500.000 + 22.000 − 11.000
    }

    [Fact]
    public async Task ZReport_CarriesCashRefund_AfterClose()
    {
        using var db = TestPosDbContext.Create();
        await SellThenRefundOneCashAsync(db);

        var z = await new CloseShiftHandler(db).Handle(new CloseShiftCommand
        {
            ShiftId = TestData.ShiftId, CountedCash = 511_000m,
        }, default);

        Assert.True(z.IsClosed);
        Assert.Equal(11_000m, z.CashRefunds);
        Assert.Equal(0m, z.Variance); // đếm khớp tiền mặt dự kiến
    }
}
