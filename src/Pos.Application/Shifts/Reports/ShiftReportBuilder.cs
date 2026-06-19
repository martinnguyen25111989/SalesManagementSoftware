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
        // Đơn đã hoàn tất trong ca + thanh toán.
        var orders = await db.Orders
            .Where(o => o.ShiftId == shift.Id && o.Status == OrderStatus.Completed)
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
            CashRefunds: 0m, // hoàn tiền mặt (B7) — bổ sung khi có nghiệp vụ trả hàng
            ExpectedCash: shift.ExpectedCash,
            CountedCash: shift.CloseAt is null ? null : shift.CountedCash,
            Variance: shift.CloseAt is null ? null : shift.Variance,
            OrderCount: orders.Count,
            GrandTotalSales: orders.Sum(o => o.GrandTotal),
            PaymentsByMethod: byMethod);
    }
}
