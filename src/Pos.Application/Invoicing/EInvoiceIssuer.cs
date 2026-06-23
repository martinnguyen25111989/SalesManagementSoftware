using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;
using Pos.Domain.Invoicing;
using Pos.Domain.Sales;

namespace Pos.Application.Invoicing;

/// <summary>Thông tin người mua ghi đè khi KH yêu cầu HĐ có MST (B11 edge).</summary>
public sealed record BuyerOverride(string? Name, string? TaxCode, string? Phone, string? Address);

/// <summary>
/// Lõi phát hành HĐĐT gốc dùng chung cho lệnh phát hành tay và job drain hàng đợi (B11-A.5/A.6).
/// Idempotent theo Order.Id: đơn đã có HĐ Issued → trả về luôn, KHÔNG gọi NCC lần hai. Lỗi mạng →
/// giữ Pending để retry; lỗi nghiệp vụ → Rejected.
/// </summary>
internal static class EInvoiceIssuer
{
    public static async Task<EInvoice> IssueOriginalAsync(
        IPosDbContext db, IEInvoiceProvider provider, Guid orderId, BuyerOverride? buyer, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy đơn {orderId}.");
        if (order.Status is OrderStatus.Draft or OrderStatus.OnHold)
            throw new BusinessRuleException("Chỉ phát hành HĐĐT cho đơn đã hoàn tất thanh toán (B11).");

        var existing = await db.EInvoices
            .FirstOrDefaultAsync(e => e.OrderId == orderId && e.Type == EInvoiceType.Original, ct);
        // Đã có mã CQT → idempotent, không phát hành lại.
        if (existing is { Status: EInvoiceStatus.Issued })
            return existing;

        var req = await BuildRequestAsync(db, order, EInvoiceType.Original, buyer, note: null, ct);

        var result = await provider.IssueAsync(req, ct);

        var einv = existing ?? new EInvoice
        {
            OrderId = order.Id,
            StoreId = order.StoreId,
            DeviceId = order.DeviceId,
            Type = EInvoiceType.Original,
        };
        Apply(einv, result);
        if (existing is null) db.EInvoices.Add(einv);
        else einv.MarkModified();

        await db.SaveChangesAsync(ct);
        return einv;
    }

    /// <summary>Dựng request HĐĐT cho 1 đơn (load chi nhánh + tên hàng + người mua), dùng cho gốc &amp; điều chỉnh/thay thế.</summary>
    internal static async Task<EInvoiceRequest> BuildRequestAsync(
        IPosDbContext db, Order order, EInvoiceType type, BuyerOverride? buyer, string? note, CancellationToken ct)
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == order.StoreId, ct)
            ?? throw new NotFoundException($"Không tìm thấy chi nhánh {order.StoreId}.");

        var variantIds = order.Lines.Select(l => l.VariantId).Distinct().ToList();
        var items = (await db.Variants.Include(v => v.Product)
                .Where(v => variantIds.Contains(v.Id)).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => (v.Product?.Name ?? "Hàng hóa", v.Product?.BaseUnit ?? "Cái"));

        var (name, taxCode, phone, address) = await ResolveBuyerAsync(db, order, buyer, ct);

        return EInvoiceRequestBuilder.Build(order, items, store, type, name, taxCode, phone, address, note: note);
    }

    /// <summary>Áp kết quả NCC vào bản ghi HĐĐT theo 3 nhánh Issued/Rejected/Pending.</summary>
    internal static void Apply(EInvoice einv, EInvoiceResult result)
    {
        switch (result.Outcome)
        {
            case EInvoiceOutcome.Issued:
                einv.Status = EInvoiceStatus.Issued;
                einv.CqtCode = result.CqtCode;
                einv.InvoiceNo = result.InvoiceNo;
                einv.Serial = result.Serial;
                einv.ProviderRef = result.ProviderRef;
                einv.IssuedAt = DateTime.UtcNow;
                einv.ErrorMessage = null;
                break;
            case EInvoiceOutcome.BusinessError:
                einv.Status = EInvoiceStatus.Rejected;
                einv.ErrorMessage = result.ErrorMessage;
                break;
            default: // TransientError → giữ hàng đợi
                einv.Status = EInvoiceStatus.Pending;
                einv.ErrorMessage = result.ErrorMessage;
                break;
        }
    }

    private static async Task<(string Name, string? TaxCode, string? Phone, string? Address)> ResolveBuyerAsync(
        IPosDbContext db, Order order, BuyerOverride? buyer, CancellationToken ct)
    {
        if (buyer is not null && !string.IsNullOrWhiteSpace(buyer.Name))
            return (buyer.Name!, buyer.TaxCode, buyer.Phone, buyer.Address);

        if (order.CustomerId is { } customerId)
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct);
            if (c is not null)
                return (c.Name, buyer?.TaxCode ?? c.TaxCode, buyer?.Phone ?? c.Phone, buyer?.Address);
        }
        return ("Khách lẻ", buyer?.TaxCode, buyer?.Phone, buyer?.Address);
    }
}
