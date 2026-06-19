using Pos.Domain.Common;

namespace Pos.Application.Shifts.Reports;

/// <summary>
/// Báo cáo ca (B9/B12). Khi ca chưa đóng = <b>X-report</b> (xem giữa ca);
/// khi đã đóng = <b>Z-report</b> (chốt ca) với CountedCash/Variance.
/// </summary>
public sealed record ShiftReport(
    Guid ShiftId,
    Guid StoreId,
    Guid RegisterId,
    Guid CashierId,
    bool IsClosed,
    DateTime OpenAt,
    DateTime? CloseAt,
    decimal OpeningFloat,
    decimal CashSales,
    decimal CashIn,
    decimal CashOut,
    decimal CashRefunds,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Variance,
    int OrderCount,
    decimal GrandTotalSales,
    IReadOnlyDictionary<PaymentMethod, decimal> PaymentsByMethod);
