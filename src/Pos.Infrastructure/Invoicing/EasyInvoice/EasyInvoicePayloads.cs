using Pos.Application.Invoicing.Abstractions;

namespace Pos.Infrastructure.Invoicing.EasyInvoice;

// ⚠️ Tên field JSON dưới đây là MẪU theo BusinessRules.md B11-A.4 — xác nhận theo tài liệu SoftDreams.

/// <summary>Body đăng nhập lấy token (B11-A.2).</summary>
public sealed record LoginPayload(string username, string password);

/// <summary>Phản hồi token; chấp nhận cả "access_token" lẫn "token".</summary>
public sealed record LoginResponse(string? access_token, string? token, int? expires_in)
{
    public string? Value => access_token ?? token;
}

public sealed record InvoiceItemPayload(
    string itemName,
    string unitName,
    decimal quantity,
    decimal unitPrice,
    decimal vatRate,
    string vatRateLabel,
    decimal amountWithoutVat,
    decimal vatAmount);

public sealed record VatByRatePayload(decimal vatRate, string vatRateLabel, decimal taxableAmount, decimal vatAmount);

/// <summary>Body phát hành/điều chỉnh/thay thế (B11-A.4). <c>transactionId</c> = Order.Id (idempotency, A.6).</summary>
public sealed record CreateInvoicePayload(
    string transactionId,
    string sellerTaxCode,
    string templateCode,
    string invoiceSeries,
    string invoiceType,
    string? originalInvoiceKey,
    string buyerName,
    string? buyerTaxCode,
    string? buyerPhone,
    string? buyerAddress,
    string? paymentMethod,
    IReadOnlyList<InvoiceItemPayload> items,
    IReadOnlyList<VatByRatePayload> vatAmountByRate,
    decimal totalAmountWithoutVat,
    decimal totalVatAmount,
    decimal totalAmount,
    string amountInWords,
    string? note);

/// <summary>Phản hồi phát hành — chấp nhận vài biến thể tên field mã CQT/số HĐ.</summary>
public sealed record CreateInvoiceResponse(
    string? cqtCode,
    string? taxAuthorityCode,
    string? invoiceNo,
    string? invoiceNumber,
    string? serial,
    string? transactionId,
    string? providerKey,
    string? errorCode,
    string? message)
{
    public string? ResolvedCqt => cqtCode ?? taxAuthorityCode;
    public string? ResolvedInvoiceNo => invoiceNo ?? invoiceNumber;
}

/// <summary>Phản hồi tra cứu trạng thái (B11-A.3).</summary>
public sealed record StatusResponse(string? status, string? cqtCode, string? taxAuthorityCode);

/// <summary>Ánh xạ <see cref="EInvoiceRequest"/> (POS) → body EasyInvoice (B11-A.4).</summary>
internal static class EasyInvoiceMapper
{
    public static CreateInvoicePayload ToPayload(EInvoiceRequest req, EInvoiceOptions opt, string? originalKey)
    {
        return new CreateInvoicePayload(
            transactionId: req.TransactionId.ToString(),
            sellerTaxCode: Fallback(req.SellerTaxCode, opt.SellerTaxCode),
            templateCode: Fallback(req.TemplateCode, opt.TemplateCode),
            invoiceSeries: Fallback(req.Serial, opt.InvoiceSeries),
            invoiceType: req.Type.ToString(),
            originalInvoiceKey: originalKey,
            buyerName: req.BuyerName,
            buyerTaxCode: req.BuyerTaxCode,
            buyerPhone: req.BuyerPhone,
            buyerAddress: req.BuyerAddress,
            paymentMethod: req.PaymentMethod,
            items: req.Items.Select(i => new InvoiceItemPayload(
                i.ItemName, i.UnitName, i.Quantity, i.UnitPrice, i.VatPercent, i.VatRateLabel,
                i.AmountWithoutVat, i.VatAmount)).ToList(),
            vatAmountByRate: req.VatByRate.Select(v => new VatByRatePayload(
                v.VatPercent, v.VatRateLabel, v.TaxableAmount, v.VatAmount)).ToList(),
            totalAmountWithoutVat: req.TotalAmountWithoutVat,
            totalVatAmount: req.TotalVat,
            totalAmount: req.TotalAmount,
            amountInWords: req.AmountInWords,
            note: req.Note);
    }

    private static string Fallback(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;
}
