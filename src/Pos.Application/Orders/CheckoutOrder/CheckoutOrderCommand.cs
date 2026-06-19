using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Orders.CheckoutOrder;

/// <summary>
/// Chốt đơn (B4 Draft → Completed): ghi nhận thanh toán (đa phương thức — B6) và
/// trừ tồn append-only (B8). Idempotent theo OrderId: gọi lại đơn đã Completed → trả kết quả cũ,
/// KHÔNG trừ tồn / thu tiền lần hai (CLAUDE.md — idempotency cho payment).
/// </summary>
public sealed record CheckoutOrderCommand : IRequest<CheckoutResult>
{
    public Guid OrderId { get; init; }

    /// <summary>Các dòng thanh toán; tổng Amount phải bằng GrandTotal (B6).</summary>
    public IReadOnlyList<PaymentInput> Payments { get; init; } = new List<PaymentInput>();

    /// <summary>Tiền khách đưa (tiền mặt) để tính tiền thối; null = không tính.</summary>
    public decimal? CashTendered { get; init; }
}

public sealed record PaymentInput(PaymentMethod Method, decimal Amount, string? ExternalRef = null);

public sealed record CheckoutResult(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal GrandTotal,
    decimal TotalPaid,
    decimal ChangeDue,
    bool OpenDrawer);
