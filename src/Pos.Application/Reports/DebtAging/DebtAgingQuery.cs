using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Reports.DebtAging;

/// <summary>
/// Báo cáo công nợ phải thu &amp; tuổi nợ (B12): dư nợ từng khách chia theo nhóm tuổi tính tại
/// <paramref name="AsOfUtc"/> theo hạn trả (DueDate; không kỳ hạn → tính theo ngày tạo).
/// </summary>
public sealed record DebtAgingQuery(DateTime AsOfUtc) : IRequest<DebtAgingResult>;

public sealed record DebtAgingResult(
    decimal TotalOutstanding,
    IReadOnlyList<CustomerAging> Customers);

public sealed record CustomerAging(
    Guid CustomerId,
    string Name,
    decimal Outstanding,
    decimal Current,   // chưa tới hạn / 0–30 ngày
    decimal Bucket31_60,
    decimal Bucket61_90,
    decimal Over90);

public sealed class DebtAgingHandler : IRequestHandler<DebtAgingQuery, DebtAgingResult>
{
    private readonly IPosDbContext _db;

    public DebtAgingHandler(IPosDbContext db) => _db = db;

    public async Task<DebtAgingResult> Handle(DebtAgingQuery q, CancellationToken ct)
    {
        var receivables = await _db.Receivables
            .Where(r => r.Outstanding > 0m)
            .Select(r => new { r.CustomerId, r.Outstanding, r.DueDate, r.CreatedUtc })
            .ToListAsync(ct);

        var names = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var rows = receivables
            .GroupBy(r => r.CustomerId)
            .Select(g =>
            {
                decimal cur = 0m, b60 = 0m, b90 = 0m, over = 0m;
                foreach (var r in g)
                {
                    int days = (int)(q.AsOfUtc.Date - (r.DueDate ?? r.CreatedUtc).Date).TotalDays;
                    if (days <= 30) cur += r.Outstanding;
                    else if (days <= 60) b60 += r.Outstanding;
                    else if (days <= 90) b90 += r.Outstanding;
                    else over += r.Outstanding;
                }
                return new CustomerAging(
                    g.Key, names.GetValueOrDefault(g.Key, "?"),
                    g.Sum(x => x.Outstanding), cur, b60, b90, over);
            })
            .OrderByDescending(c => c.Outstanding)
            .ToList();

        return new DebtAgingResult(rows.Sum(r => r.Outstanding), rows);
    }
}
