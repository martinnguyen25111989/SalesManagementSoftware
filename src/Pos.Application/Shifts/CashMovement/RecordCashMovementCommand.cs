using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Shifts.CashMovements;

/// <summary>
/// Thu/chi tiền mặt ngoài bán hàng trong ca (B9): nộp quỹ, chi vặt, rút bớt tiền.
/// Điều chỉnh ExpectedCash của ca (In: +, Out: −).
/// </summary>
public sealed record RecordCashMovementCommand : IRequest<CashMovementResult>
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ShiftId { get; init; }
    public CashMovementType Type { get; init; }
    public decimal Amount { get; init; }
    public string? Reason { get; init; }
}

public sealed record CashMovementResult(Guid Id, Guid ShiftId, CashMovementType Type, decimal Amount, decimal ExpectedCash);
