using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Reports.SalesSummary;

/// <summary>
/// Báo cáo bán hàng (B12): doanh thu theo kỳ, số đơn, giá trị TB/đơn; bóc tách theo ngày &amp; thu ngân.
/// Doanh thu thuần = doanh thu gộp − hoàn trả trong kỳ.
/// </summary>
public sealed record SalesSummaryQuery(ReportFilter Filter) : IRequest<SalesSummaryResult>;

public sealed record SalesSummaryResult(
    int OrderCount,
    decimal GrossSales,
    decimal Refunds,
    decimal NetSales,
    decimal AverageOrderValue,
    IReadOnlyList<DailySales> Daily,
    IReadOnlyList<CashierSales> ByCashier);

public sealed record DailySales(DateOnly Date, int OrderCount, decimal GrossSales);
public sealed record CashierSales(Guid CashierId, int OrderCount, decimal GrossSales);

public sealed class SalesSummaryHandler : IRequestHandler<SalesSummaryQuery, SalesSummaryResult>
{
    private readonly IPosDbContext _db;

    public SalesSummaryHandler(IPosDbContext db) => _db = db;

    public async Task<SalesSummaryResult> Handle(SalesSummaryQuery q, CancellationToken ct)
    {
        var f = q.Filter;
        var orders = await ReportFilters.SalesOrders(_db, f)
            .Select(o => new { o.CreatedUtc, o.CashierId, o.GrandTotal })
            .ToListAsync(ct);

        decimal gross = orders.Sum(o => o.GrandTotal);
        int count = orders.Count;

        // Hoàn trả trong kỳ (theo chi nhánh/ca nếu có lọc; thu ngân không gắn trên phiếu trả).
        decimal refunds = await _db.ReturnOrders
            .Where(r => r.CreatedUtc >= f.FromUtc && r.CreatedUtc < f.ToUtc
                && (f.StoreId == null || r.StoreId == f.StoreId)
                && (f.ShiftId == null || r.ShiftId == f.ShiftId))
            .SumAsync(r => r.RefundAmount, ct);

        var daily = orders
            .GroupBy(o => DateOnly.FromDateTime(o.CreatedUtc))
            .OrderBy(g => g.Key)
            .Select(g => new DailySales(g.Key, g.Count(), g.Sum(x => x.GrandTotal)))
            .ToList();

        var byCashier = orders
            .GroupBy(o => o.CashierId)
            .Select(g => new CashierSales(g.Key, g.Count(), g.Sum(x => x.GrandTotal)))
            .OrderByDescending(c => c.GrossSales)
            .ToList();

        decimal avg = count == 0 ? 0m : Math.Round(gross / count, 0, MidpointRounding.AwayFromZero);

        return new SalesSummaryResult(count, gross, refunds, gross - refunds, avg, daily, byCashier);
    }
}
