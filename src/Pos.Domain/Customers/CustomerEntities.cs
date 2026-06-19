using Pos.Domain.Common;

namespace Pos.Domain.Customers;

public class CustomerTier : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public decimal DiscountRate { get; set; }
}

/// <summary>Khách hàng (B10). SĐT là định danh chính ở VN; TaxCode cho HĐĐT có MST.</summary>
public class Customer : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TaxCode { get; set; }
    public Guid? TierId { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal PointBalance { get; set; }
}

/// <summary>Công nợ phải thu (bán chịu). OrderId null nếu là nợ ghi tay.</summary>
public class Receivable : EntityBase
{
    public Guid CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public decimal Paid { get; set; }
    public decimal Outstanding { get; set; }
    public DateTime? DueDate { get; set; }
}

/// <summary>Biến động điểm thưởng (+ tích / − đổi/thu hồi).</summary>
public class LoyaltyTxn : EntityBase
{
    public Guid CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public int PointChange { get; set; }
}
