using Pos.Domain.Common;

namespace Pos.Application.Orders;

/// <summary>Kết quả các lệnh đổi trạng thái đơn (Hold/Resume/Void) — B4 state machine.</summary>
public sealed record OrderStateResult(Guid Id, string OrderNumber, OrderStatus Status);
