using MediatR;
using Pos.Application.Shifts.Reports;

namespace Pos.Application.Shifts.CloseShift;

/// <summary>
/// Đóng ca (B9): đếm tiền cuối ca → Variance = CountedCash − ExpectedCash, sinh Z-report.
/// Chặn nếu còn đơn Hold chưa xử lý. Lệch quỹ vượt ngưỡng cần Manager duyệt (B2).
/// Idempotent: ca đã đóng → trả lại Z-report.
/// </summary>
public sealed record CloseShiftCommand : IRequest<ShiftReport>
{
    public Guid ShiftId { get; init; }
    public decimal CountedCash { get; init; }

    /// <summary>Manager đã duyệt khi lệch quỹ vượt ngưỡng (B2). Chưa có Auth/PIN → truyền cờ.</summary>
    public bool ManagerApproved { get; init; }
}
