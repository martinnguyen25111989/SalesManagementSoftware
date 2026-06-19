using MediatR;

namespace Pos.Application.Orders.ResumeOrder;

/// <summary>
/// Mở lại đơn đang giữ (B4): OnHold → Draft. Idempotent: đơn đã Draft → trả nguyên trạng.
/// </summary>
public sealed record ResumeOrderCommand(Guid OrderId) : IRequest<OrderStateResult>;
