using Pos.Domain.Common;

namespace Pos.Domain.Inventory;

public class Supplier : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
}

/// <summary>
/// Biến động tồn (B8) — append-only. Tồn = SUM(QtyChange) theo VariantId + StoreId.
/// RefId trỏ tới chứng từ nguồn (Order/GRN/Transfer/Return).
/// </summary>
public class StockTransaction : TransactionEntity
{
    public Guid VariantId { get; set; }
    public StockTransactionType Type { get; set; }

    /// <summary>+/- theo đơn vị cơ bản.</summary>
    public decimal QtyChange { get; set; }

    /// <summary>Giá vốn tại thời điểm phát sinh.</summary>
    public decimal UnitCost { get; set; }
    public Guid? RefId { get; set; }
}

/// <summary>Snapshot tồn để query nhanh (nguồn sự thật vẫn là StockTransaction).</summary>
public class StockBalance : EntityBase
{
    public Guid VariantId { get; set; }
    public Guid StoreId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Giá vốn bình quân gia quyền di động (B8) — cập nhật mỗi lần nhập, dùng làm giá vốn khi bán.</summary>
    public decimal AvgCost { get; set; }
}

/// <summary>Phiếu nhập hàng (GRN) từ nhà cung cấp, có giá vốn.</summary>
public class PurchaseReceipt : TransactionEntity
{
    public Guid SupplierId { get; set; }
    public decimal Total { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GrnLine> Lines { get; set; } = new List<GrnLine>();
}

public class GrnLine : EntityBase
{
    public Guid ReceiptId { get; set; }
    public Guid VariantId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
}
