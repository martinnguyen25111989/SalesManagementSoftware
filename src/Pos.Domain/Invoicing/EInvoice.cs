using Pos.Domain.Common;

namespace Pos.Domain.Invoicing;

/// <summary>
/// Hóa đơn điện tử (B11/B11-A). Tách khỏi Order: 1 đơn → nhiều chứng từ
/// (gốc → điều chỉnh/thay thế/hủy) liên kết chuỗi qua OriginalInvoiceId.
/// </summary>
public class EInvoice : TransactionEntity
{
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = "EasyInvoice";

    /// <summary>Mã của cơ quan thuế (CQT).</summary>
    public string? CqtCode { get; set; }
    public string? InvoiceNo { get; set; }

    /// <summary>Ký hiệu hóa đơn (chứa 'M' nếu khởi tạo từ máy tính tiền), vd 1C25MAA.</summary>
    public string? Serial { get; set; }

    public EInvoiceType Type { get; set; } = EInvoiceType.Original;
    public Guid? OriginalInvoiceId { get; set; }
    public EInvoiceStatus Status { get; set; } = EInvoiceStatus.Pending;

    public string? XmlPath { get; set; }
    public string? PdfPath { get; set; }
}
