using Pos.Domain.Common;

namespace Pos.Domain.Operations;

/// <summary>Ca làm việc & quỹ tiền (B9). Variance = CountedCash − ExpectedCash.</summary>
public class Shift : TransactionEntity
{
    public Guid RegisterId { get; set; }
    public Guid CashierId { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }
    public decimal Variance { get; set; }
    public DateTime OpenAt { get; set; } = DateTime.UtcNow;
    public DateTime? CloseAt { get; set; }

    public ICollection<CashMovement> CashMovements { get; set; } = new List<CashMovement>();
}

/// <summary>Thu/chi tiền mặt ngoài bán hàng trong ca.</summary>
public class CashMovement : EntityBase
{
    public Guid ShiftId { get; set; }
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
}
