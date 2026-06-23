namespace Pos.Infrastructure.Invoicing.EasyInvoice;

/// <summary>
/// Cấu hình tích hợp HĐĐT EasyInvoice/SoftDreams (B11-A.2). Giá trị thật (Username/Password, MST…)
/// nạp qua User Secrets / biến môi trường — KHÔNG commit. Đường dẫn endpoint &amp; tên field là MẪU
/// theo BusinessRules.md — ⚠️ xác nhận theo tài liệu API SoftDreams trước khi go-live, chỉnh qua cấu hình.
/// </summary>
public sealed class EInvoiceOptions
{
    public const string SectionName = "EInvoice";

    public string Provider { get; set; } = "EasyInvoice";

    /// <summary>Base URL test/prod (khác nhau theo môi trường) — bắt buộc khi bật adapter thật.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Định danh người bán mặc định (fallback khi Store chưa cấu hình).</summary>
    public string SellerTaxCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string InvoiceSeries { get; set; } = string.Empty;

    // Đường dẫn endpoint (mẫu B11-A.3) — override theo tài liệu thật.
    public string LoginPath { get; set; } = "/api/Account/Login";
    public string CreateInvoicePath { get; set; } = "/api/Invoice/CreateInvoice";
    public string GetStatusPath { get; set; } = "/api/Invoice/GetStatus";
    public string AdjustInvoicePath { get; set; } = "/api/Invoice/AdjustInvoice";
    public string ReplaceInvoicePath { get; set; } = "/api/Invoice/ReplaceInvoice";
    public string CancelInvoicePath { get; set; } = "/api/Invoice/CancelInvoice";

    /// <summary>Hạn token mặc định nếu phản hồi không trả expires_in (giây).</summary>
    public int DefaultTokenTtlSeconds { get; set; } = 3600;

    /// <summary>Đã đủ cấu hình để dùng adapter thật chưa (nếu không → giữ Null provider, offline).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);
}
