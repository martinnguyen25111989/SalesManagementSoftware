namespace Pos.Hardware.Abstractions;

/// <summary>
/// Máy in hóa đơn (ESC/POS). Mỗi nền tảng có một implementation, nạp runtime qua DI
/// (Technical.md mục 10). Ưu tiên in qua mạng (TCP 9100) để chạy giống nhau mọi OS.
/// </summary>
public interface IReceiptPrinter
{
    Task PrintAsync(ReceiptDocument doc, CancellationToken ct = default);
    Task<bool> IsAvailableAsync();
}

/// <summary>Mô tả nội dung hóa đơn cần in (độc lập driver/OS).</summary>
public sealed class ReceiptDocument
{
    public string StoreName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.Now;

    public IList<ReceiptLine> Lines { get; set; } = new List<ReceiptLine>();

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>Nội dung QR (vd đường dẫn tra cứu HĐĐT / VietQR thanh toán).</summary>
    public string? QrContent { get; set; }
    public string? Footer { get; set; }

    /// <summary>Khổ giấy: 58 hoặc 80 (mm).</summary>
    public int PaperWidthMm { get; set; } = 80;

    /// <summary>Gửi lệnh kick mở két ngay sau khi in.</summary>
    public bool OpenDrawerAfterPrint { get; set; }
}

public sealed class ReceiptLine
{
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
