using Pos.Domain.Common;

namespace Pos.Application.Invoicing.Abstractions;

/// <summary>
/// Cổng trừu tượng tới nhà cung cấp HĐĐT/TVAN (B11). POS KHÔNG kết nối thẳng cơ quan thuế —
/// adapter cụ thể (EasyInvoice/Viettel/VNPT…) hiện thực interface này ở tầng Infrastructure
/// và được nạp qua DI, để đổi NCC không phải sửa nghiệp vụ. Adapter thật + đăng ký máy tính tiền
/// kết nối thuế làm ở giai đoạn sau.
/// </summary>
public interface IEInvoiceProvider
{
    /// <summary>Phát hành HĐ, lấy mã CQT. Idempotent theo <see cref="EInvoiceRequest.TransactionId"/>.</summary>
    Task<EInvoiceResult> IssueAsync(EInvoiceRequest req, CancellationToken ct = default);

    /// <summary>Điều chỉnh HĐ gốc (gắn B7 trả hàng/sai sót).</summary>
    Task<EInvoiceResult> AdjustAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default);

    /// <summary>Thay thế HĐ gốc.</summary>
    Task<EInvoiceResult> ReplaceAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default);

    /// <summary>Hủy HĐ kèm lý do.</summary>
    Task<EInvoiceResult> CancelAsync(string invoiceKey, string reason, CancellationToken ct = default);

    /// <summary>Tra cứu trạng thái theo khóa giao dịch/khóa HĐ (đối soát khi nghi đã gửi).</summary>
    Task<EInvoiceStatus> QueryAsync(string invoiceKey, CancellationToken ct = default);
}

/// <summary>Kết quả gọi NCC, phân biệt 3 nhánh xử lý ở B11-A.5/A.7.</summary>
public enum EInvoiceOutcome
{
    /// <summary>2xx + có mã CQT → lưu Issued.</summary>
    Issued,

    /// <summary>Lỗi nghiệp vụ (4xx: sai thuế/MST/field) → Rejected, KHÔNG retry mù.</summary>
    BusinessError,

    /// <summary>Lỗi mạng/timeout/5xx → giữ Pending, retry có backoff.</summary>
    TransientError,
}

public sealed record EInvoiceResult(
    EInvoiceOutcome Outcome,
    string? CqtCode = null,
    string? InvoiceNo = null,
    string? Serial = null,
    string? ProviderRef = null,
    string? ErrorMessage = null)
{
    public static EInvoiceResult Issued(string cqtCode, string invoiceNo, string serial, string providerRef) =>
        new(EInvoiceOutcome.Issued, cqtCode, invoiceNo, serial, providerRef);

    public static EInvoiceResult Business(string message) =>
        new(EInvoiceOutcome.BusinessError, ErrorMessage: message);

    public static EInvoiceResult Transient(string message) =>
        new(EInvoiceOutcome.TransientError, ErrorMessage: message);
}

/// <summary>Dữ liệu hóa đơn gửi NCC (mapping POS→HĐĐT theo B11-A.4); số tiền đã khớp B13.</summary>
public sealed record EInvoiceRequest(
    Guid TransactionId,
    string SellerTaxCode,
    string TemplateCode,
    string Serial,
    EInvoiceType Type,
    string BuyerName,
    string? BuyerTaxCode,
    string? BuyerPhone,
    string? BuyerAddress,
    IReadOnlyList<EInvoiceItem> Items,
    IReadOnlyList<EInvoiceVatLine> VatByRate,
    decimal TotalAmountWithoutVat,
    decimal TotalVat,
    decimal TotalAmount,
    string AmountInWords,
    string? PaymentMethod = null,
    string? Note = null);

public sealed record EInvoiceItem(
    string ItemName,
    string UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatPercent,
    string VatRateLabel,
    decimal AmountWithoutVat,
    decimal VatAmount);

/// <summary>Tổng thuế tách theo từng thuế suất (yêu cầu HĐĐT).</summary>
public sealed record EInvoiceVatLine(decimal VatPercent, string VatRateLabel, decimal TaxableAmount, decimal VatAmount);
