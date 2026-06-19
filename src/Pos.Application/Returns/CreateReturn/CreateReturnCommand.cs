using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Returns.CreateReturn;

/// <summary>
/// Trả hàng / hoàn tiền (B7). Luôn tham chiếu hóa đơn gốc; không trả quá số đã mua;
/// nhập lại tồn (trừ hàng lỗi); hoàn tiền theo phương thức gốc. Cần quyền Manager + lý do.
/// Idempotent theo ReturnId. HĐĐT điều chỉnh (B11) &amp; AuditLog: bổ sung khi có module tương ứng.
/// </summary>
public sealed record CreateReturnCommand : IRequest<ReturnResult>
{
    public Guid ReturnId { get; init; } = Guid.NewGuid();
    public Guid OriginalOrderId { get; init; }
    public Guid ShiftId { get; init; }
    public Guid ApprovedBy { get; init; }
    public string? DeviceId { get; init; }

    public string Reason { get; init; } = string.Empty;
    public PaymentMethod RefundMethod { get; init; } = PaymentMethod.Cash;

    /// <summary>Manager đã duyệt (B7 — trả hàng cần quyền).</summary>
    public bool ManagerApproved { get; init; }

    public IReadOnlyList<ReturnLineInput> Lines { get; init; } = new List<ReturnLineInput>();
}

/// <param name="RestockToInventory">false = hàng lỗi/hủy, không nhập lại kho (B7).</param>
public sealed record ReturnLineInput(Guid OrderLineId, decimal Qty, bool RestockToInventory = true);

public sealed record ReturnResult(
    Guid ReturnId,
    Guid OriginalOrderId,
    OrderStatus OriginalOrderStatus,
    decimal RefundAmount,
    PaymentMethod RefundMethod,
    IReadOnlyList<ReturnLineResult> Lines);

public sealed record ReturnLineResult(Guid OrderLineId, Guid VariantId, decimal Qty, decimal RefundAmount, bool Restocked);
