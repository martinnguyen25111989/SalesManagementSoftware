using Pos.Domain.Common;

namespace Pos.Domain.Sales;

/// <summary>Đơn/Hóa đơn bán (B4). OrderNumber có prefix chi nhánh; định danh nội bộ = GUID.</summary>
public class Order : TransactionEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ShiftId { get; set; }
    public Guid CashierId { get; set; }
    public Guid? CustomerId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    // Tiền: decimal (B13). Tính theo thứ tự giá → CK dòng → CK tổng → thuế → làm tròn.
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundingAdj { get; set; }
    public decimal GrandTotal { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class OrderLine : EntityBase
{
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public VatRate TaxRate { get; set; } = VatRate.Ten;
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
}

/// <summary>Thanh toán — bảng con nhiều dòng (hỗ trợ thanh toán hỗn hợp). SUM(Amount)=GrandTotal.</summary>
public class Payment : EntityBase
{
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Mã giao dịch QR/thẻ để đối soát.</summary>
    public string? ExternalRef { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
