using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Reports;

/// <summary>
/// Bộ lọc chung cho báo cáo (B12): khoảng thời gian [FromUtc, ToUtc) và tùy chọn theo chi nhánh /
/// thu ngân / ca. ToUtc là cận trên loại trừ (nửa mở) để gộp ngày không trùng/sót.
/// </summary>
public sealed record ReportFilter(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? StoreId = null,
    Guid? CashierId = null,
    Guid? ShiftId = null);

internal static class ReportFilters
{
    /// <summary>
    /// Tập đơn tính doanh thu: đã thu tiền trong kỳ (Completed/PartiallyReturned/Returned) — phần hoàn
    /// hạch toán riêng để không "mất" doanh thu (đồng bộ ShiftReportBuilder B9).
    /// </summary>
    public static IQueryable<Order> SalesOrders(IPosDbContext db, ReportFilter f) =>
        db.Orders.Where(o =>
            (o.Status == OrderStatus.Completed
             || o.Status == OrderStatus.PartiallyReturned
             || o.Status == OrderStatus.Returned)
            && o.CreatedUtc >= f.FromUtc && o.CreatedUtc < f.ToUtc
            && (f.StoreId == null || o.StoreId == f.StoreId)
            && (f.CashierId == null || o.CashierId == f.CashierId)
            && (f.ShiftId == null || o.ShiftId == f.ShiftId));
}
