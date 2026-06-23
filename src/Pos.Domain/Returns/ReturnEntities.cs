using Pos.Domain.Common;

namespace Pos.Domain.Returns;

/// <summary>Phiếu trả hàng (B7) — luôn tham chiếu hóa đơn gốc.</summary>
public class ReturnOrder : TransactionEntity
{
    public Guid OriginalOrderId { get; set; }

    /// <summary>Ca xử lý trả hàng (B9) — để hạch toán hoàn tiền mặt vào quỹ &amp; X/Z report.</summary>
    public Guid ShiftId { get; set; }
    public string? Reason { get; set; }
    public decimal RefundAmount { get; set; }
    public PaymentMethod RefundMethod { get; set; }
    public Guid ApprovedBy { get; set; }

    public ICollection<ReturnLine> Lines { get; set; } = new List<ReturnLine>();
}

public class ReturnLine : EntityBase
{
    public Guid ReturnOrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public decimal Qty { get; set; }

    /// <summary>false nếu hàng lỗi/hủy (không nhập lại kho).</summary>
    public bool RestockToInventory { get; set; } = true;
}
