using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Customers.RecordDebtPayment;

/// <summary>
/// Khách trả nợ (B10): phân bổ số tiền vào các công nợ còn lại (theo hạn trả sớm trước).
/// Idempotent theo <see cref="PaymentId"/>. Trả tiền mặt tại quầy thì cộng vào quỹ ca (B9).
/// </summary>
public sealed record RecordDebtPaymentCommand : IRequest<DebtPaymentResult>
{
    public Guid PaymentId { get; init; } = Guid.NewGuid();
    public Guid CustomerId { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; } = PaymentMethod.Cash;

    /// <summary>Ca thu nợ (bắt buộc nếu trả tiền mặt để hạch toán quỹ); null nếu trả qua kênh khác.</summary>
    public Guid? ShiftId { get; init; }
    public string? DeviceId { get; init; }
    public Guid StoreId { get; init; }
}

public sealed record DebtPaymentResult(
    Guid PaymentId, Guid CustomerId, decimal Amount, decimal OutstandingAfter);
