using MediatR;

namespace Pos.Application.Orders.VoidOrder;

/// <summary>
/// Hủy đơn trước khi hoàn tất (B4): Draft/OnHold → Voided. Cần quyền (B2) + lý do.
/// KHÔNG dùng để hủy đơn đã thanh toán/đã phát hành HĐĐT — việc đó phải qua điều chỉnh/thay thế/hủy
/// hóa đơn (B11). Idempotent: đơn đã Voided → trả nguyên trạng.
/// </summary>
public sealed record VoidOrderCommand : IRequest<OrderStateResult>
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>Manager đã duyệt (B2 — hủy đơn cần quyền). Chưa có Auth/PIN → truyền cờ.</summary>
    public bool ManagerApproved { get; init; }
}
