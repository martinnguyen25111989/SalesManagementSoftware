using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Reports.PaymentReconciliation;

/// <summary>
/// Đối soát theo phương thức thanh toán (B12 — Tiền/Quỹ): tổng thu theo từng phương thức,
/// trừ hoàn trả theo phương thức, ra số thực thu trong kỳ.
/// </summary>
public sealed record PaymentReconciliationQuery(ReportFilter Filter)
    : IRequest<IReadOnlyList<PaymentMethodTotal>>;

public sealed record PaymentMethodTotal(PaymentMethod Method, decimal Gross, decimal Refunds, decimal Net);

public sealed class PaymentReconciliationHandler
    : IRequestHandler<PaymentReconciliationQuery, IReadOnlyList<PaymentMethodTotal>>
{
    private readonly IPosDbContext _db;

    public PaymentReconciliationHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<PaymentMethodTotal>> Handle(PaymentReconciliationQuery q, CancellationToken ct)
    {
        var f = q.Filter;
        var orderIds = ReportFilters.SalesOrders(_db, f).Select(o => o.Id);

        var gross = await _db.Payments
            .Where(p => orderIds.Contains(p.OrderId))
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Method, x => x.Amount, ct);

        var refunds = await _db.ReturnOrders
            .Where(r => r.CreatedUtc >= f.FromUtc && r.CreatedUtc < f.ToUtc
                && (f.StoreId == null || r.StoreId == f.StoreId)
                && (f.ShiftId == null || r.ShiftId == f.ShiftId))
            .GroupBy(r => r.RefundMethod)
            .Select(g => new { Method = g.Key, Amount = g.Sum(x => x.RefundAmount) })
            .ToDictionaryAsync(x => x.Method, x => x.Amount, ct);

        return gross.Keys.Union(refunds.Keys)
            .Select(m =>
            {
                decimal g = gross.GetValueOrDefault(m);
                decimal r = refunds.GetValueOrDefault(m);
                return new PaymentMethodTotal(m, g, r, g - r);
            })
            .OrderBy(x => x.Method)
            .ToList();
    }
}
