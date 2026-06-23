using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Pricing;
using Pos.Domain.Common;

namespace Pos.Application.Reports.ProductSales;

/// <summary>
/// Báo cáo sản phẩm &amp; lợi nhuận (B12): theo biến thể — số lượng bán, doanh thu (trước thuế),
/// giá vốn (B8 đóng dấu khi bán) và lãi gộp. Sắp theo SL bán giảm dần (đầu = bán chạy, cuối = bán chậm).
/// </summary>
public sealed record ProductSalesQuery(ReportFilter Filter) : IRequest<IReadOnlyList<ProductSalesRow>>;

public sealed record ProductSalesRow(
    Guid VariantId,
    string ProductName,
    decimal QtySold,
    decimal RevenueExVat,
    decimal Cost,
    decimal GrossProfit);

public sealed class ProductSalesHandler : IRequestHandler<ProductSalesQuery, IReadOnlyList<ProductSalesRow>>
{
    private readonly IPosDbContext _db;

    public ProductSalesHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProductSalesRow>> Handle(ProductSalesQuery q, CancellationToken ct)
    {
        var orders = await ReportFilters.SalesOrders(_db, q.Filter)
            .Include(o => o.Lines)
            .ToListAsync(ct);

        // SL + doanh thu trước thuế theo biến thể (tính lại taxable bằng OrderCalculator để khớp B13).
        var qty = new Dictionary<Guid, decimal>();
        var revenue = new Dictionary<Guid, decimal>();
        foreach (var o in orders)
        {
            var lines = o.Lines.ToList();
            decimal orderDiscount = o.DiscountTotal - lines.Sum(l => l.LineDiscount);
            var calc = OrderCalculator.Calculate(
                lines.Select(l => new OrderCalcLine(l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate)).ToList(),
                orderDiscount);
            for (int i = 0; i < lines.Count; i++)
            {
                qty[lines[i].VariantId] = qty.GetValueOrDefault(lines[i].VariantId) + lines[i].Qty;
                revenue[lines[i].VariantId] = revenue.GetValueOrDefault(lines[i].VariantId) + calc.Lines[i].Taxable;
            }
        }

        // Giá vốn: từ biến động tồn loại Sale gắn các đơn đã lọc (đóng dấu UnitCost bình quân khi bán).
        var orderIds = orders.Select(o => o.Id).ToList();
        var costByVariant = await _db.StockTransactions
            .Where(s => s.Type == StockTransactionType.Sale && s.RefId != null && orderIds.Contains(s.RefId.Value))
            .GroupBy(s => s.VariantId)
            .Select(g => new { VariantId = g.Key, Cost = g.Sum(x => -x.QtyChange * x.UnitCost) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Cost, ct);

        var variantIds = qty.Keys.ToList();
        var names = await _db.Variants.Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Product != null ? v.Product.Name : "Hàng hóa", ct);

        return variantIds
            .Select(id =>
            {
                decimal rev = revenue.GetValueOrDefault(id);
                decimal cost = costByVariant.GetValueOrDefault(id);
                return new ProductSalesRow(
                    id, names.GetValueOrDefault(id, "Hàng hóa"), qty[id], rev, cost, rev - cost);
            })
            .OrderByDescending(r => r.QtySold)
            .ToList();
    }
}
