using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Pricing;
using Pos.Domain.Common;

namespace Pos.Application.Reports.TaxInvoice;

/// <summary>
/// Báo cáo thuế / HĐĐT (B12): số lượng hóa đơn theo trạng thái &amp; loại (gồm hủy/điều chỉnh/thay thế)
/// và tổng thuế tách theo từng thuế suất trong kỳ.
/// </summary>
public sealed record TaxInvoiceQuery(ReportFilter Filter) : IRequest<TaxInvoiceResult>;

public sealed record TaxInvoiceResult(
    int Issued,
    int Pending,
    int Rejected,
    int Canceled,
    int AdjustCount,
    int ReplaceCount,
    int CancelCount,
    decimal TotalTax,
    IReadOnlyList<TaxByRate> TaxByRate);

public sealed record TaxByRate(decimal VatPercent, decimal TaxableAmount, decimal VatAmount);

public sealed class TaxInvoiceHandler : IRequestHandler<TaxInvoiceQuery, TaxInvoiceResult>
{
    private readonly IPosDbContext _db;

    public TaxInvoiceHandler(IPosDbContext db) => _db = db;

    public async Task<TaxInvoiceResult> Handle(TaxInvoiceQuery q, CancellationToken ct)
    {
        var f = q.Filter;

        var invoices = await _db.EInvoices
            .Where(e => e.CreatedUtc >= f.FromUtc && e.CreatedUtc < f.ToUtc
                && (f.StoreId == null || e.StoreId == f.StoreId))
            .Select(e => new { e.Status, e.Type })
            .ToListAsync(ct);

        // Thuế tách theo thuế suất từ các đơn bán trong kỳ (tính lại taxable/tax theo B13).
        var orders = await ReportFilters.SalesOrders(_db, f).Include(o => o.Lines).ToListAsync(ct);
        var taxable = new Dictionary<decimal, decimal>();
        var vat = new Dictionary<decimal, decimal>();
        foreach (var o in orders)
        {
            var lines = o.Lines.ToList();
            decimal orderDiscount = o.DiscountTotal - lines.Sum(l => l.LineDiscount);
            var calc = OrderCalculator.Calculate(
                lines.Select(l => new OrderCalcLine(l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate)).ToList(),
                orderDiscount);
            for (int i = 0; i < lines.Count; i++)
            {
                decimal pct = OrderCalculator.VatPercent(lines[i].TaxRate);
                taxable[pct] = taxable.GetValueOrDefault(pct) + calc.Lines[i].Taxable;
                vat[pct] = vat.GetValueOrDefault(pct) + calc.Lines[i].Tax;
            }
        }

        var byRate = taxable.Keys
            .Select(pct => new TaxByRate(pct, taxable[pct], vat.GetValueOrDefault(pct)))
            .OrderBy(r => r.VatPercent)
            .ToList();

        return new TaxInvoiceResult(
            Issued: invoices.Count(i => i.Status == EInvoiceStatus.Issued),
            Pending: invoices.Count(i => i.Status == EInvoiceStatus.Pending),
            Rejected: invoices.Count(i => i.Status == EInvoiceStatus.Rejected),
            Canceled: invoices.Count(i => i.Status == EInvoiceStatus.Canceled),
            AdjustCount: invoices.Count(i => i.Type == EInvoiceType.Adjust),
            ReplaceCount: invoices.Count(i => i.Type == EInvoiceType.Replace),
            CancelCount: invoices.Count(i => i.Type == EInvoiceType.Cancel),
            TotalTax: vat.Values.Sum(),
            TaxByRate: byRate);
    }
}
