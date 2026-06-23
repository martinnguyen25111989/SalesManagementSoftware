using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Invoicing.Revise;

/// <summary>
/// Điều chỉnh / thay thế / hủy một HĐĐT đã phát hành (B11/B11-A.8) — gắn B7 trả hàng/sai sót.
/// KHÔNG sửa/xóa HĐ gốc; tạo chứng từ mới liên kết qua OriginalInvoiceId. Idempotent theo ReviseId.
/// </summary>
public sealed record ReviseEInvoiceCommand : IRequest<ReviseEInvoiceResult>
{
    public Guid ReviseId { get; init; } = Guid.NewGuid();
    public Guid OriginalInvoiceId { get; init; }

    /// <summary>Adjust / Replace / Cancel (Original không hợp lệ).</summary>
    public EInvoiceType Type { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record ReviseEInvoiceResult(
    Guid EInvoiceId, Guid OriginalInvoiceId, EInvoiceType Type, EInvoiceStatus Status,
    string? CqtCode, string? ErrorMessage);
