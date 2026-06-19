using MediatR;

namespace Pos.Application.Orders.HoldOrder;

/// <summary>
/// Giữ đơn (Hold/Park — B4): Draft → OnHold để phục vụ khách khác, sau đó resume.
/// Idempotent: đơn đã OnHold → trả nguyên trạng.
/// </summary>
public sealed record HoldOrderCommand(Guid OrderId) : IRequest<OrderStateResult>;
