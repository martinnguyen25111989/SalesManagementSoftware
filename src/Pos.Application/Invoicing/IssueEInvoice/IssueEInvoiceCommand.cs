using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Invoicing.IssueEInvoice;

/// <summary>
/// Phát hành HĐĐT cho 1 đơn đã thanh toán (B11). Idempotent theo OrderId — không bao giờ cấp 2 mã CQT
/// cho 1 đơn. Offline/lỗi mạng → giữ trạng thái Pending (hàng đợi), drain sau bằng <c>ProcessPending</c>.
/// </summary>
public sealed record IssueEInvoiceCommand : IRequest<IssueEInvoiceResult>
{
    public Guid OrderId { get; init; }

    /// <summary>Khách yêu cầu HĐ có MST (B11 edge): ghi đè thông tin người mua khi phát hành.</summary>
    public string? BuyerName { get; init; }
    public string? BuyerTaxCode { get; init; }
    public string? BuyerPhone { get; init; }
    public string? BuyerAddress { get; init; }
}

public sealed record IssueEInvoiceResult(
    Guid EInvoiceId,
    Guid OrderId,
    EInvoiceStatus Status,
    string? CqtCode,
    string? InvoiceNo,
    string? Serial,
    string? ErrorMessage);
