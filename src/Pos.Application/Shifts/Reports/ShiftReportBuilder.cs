using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Operations;

namespace Pos.Application.Shifts.Reports;

/// <summary>Dựng <see cref="ShiftReport"/> từ đơn đã chốt + thu/chi tiền mặt của ca (dùng chung cho X &amp; Z report).</summary>
internal static class ShiftReportBuilder
{
    public static async Task<ShiftReport> BuildAsync(IPosDbContext db, Shift shift, CancellationToken ct)
    {
        // Đơn đã thu tiền trong ca: tính cả đơn sau đó bị trả một phần/toàn phần — tiền bán vẫn đã
        // vào quỹ trong ca này; phần hoàn được hạch toán riêng ở CashRefunds (tránh "mất" doanh thu).
        var orders = await db.Orders
            .Where(o => o.ShiftId == shift.Id && (o.Status == OrderStatus.Completed
                || o.Status == OrderStatus.PartiallyReturned || o.Status == OrderStatus.Returned))
            .Include(o => o.Payments)
            .ToListAsync(ct);

        var payments = orders.SelectMany(o => o.Payments).ToList();
        var byMethod = payments
            .GroupBy(p => p.Method)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        decimal cashSales = byMethod.GetValueOrDefault(PaymentMethod.Cash);

        var movements = await db.CashMovements
            .Where(m => m.ShiftId == shift.Id)
            .ToListAsync(ct);
        decimal cashIn = movements.Where(m => m.Type == CashMovementType.In).Sum(m => m.Amount);
        decimal cashOut = movements.Where(m => m.Type == CashMovementType.Out).Sum(m => m.Amount);

        // Hoàn tiền mặt cho hàng trả trong ca (B7/B9) — chỉ phần hoàn bằng tiền mặt rời quỹ.
        decimal cashRefunds = await db.ReturnOrders
            .Where(r => r.ShiftId == shift.Id && r.RefundMethod == PaymentMethod.Cash)
            .SumAsync(r => r.RefundAmount, ct);

        return new ShiftReport(
            ShiftId: shift.Id,
            StoreId: shift.StoreId,
            RegisterId: shift.RegisterId,
            CashierId: shift.CashierId,
            IsClosed: shift.CloseAt is not null,
            OpenAt: shift.OpenAt,
            CloseAt: shift.CloseAt,
            OpeningFloat: shift.OpeningFloat,
            CashSales: cashSales,
            CashIn: cashIn,
            CashOut: cashOut,
            CashRefunds: cashRefunds, // hoàn tiền mặt cho hàng trả trong ca (B7/B9) — đã rời quỹ
            ExpectedCash: shift.ExpectedCash,
            CountedCash: shift.CloseAt is null ? null : shift.CountedCash,
            Variance: shift.CloseAt is null ? null : shift.Variance,
            OrderCount: orders.Count,
            GrandTotalSales: orders.Sum(o => o.GrandTotal),
            PaymentsByMethod: byMethod);
    }
}
