using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Inventory.Queries;

/// <summary>
/// Tồn hiện có theo chi nhánh (B8). Mặc định đọc snapshot <c>StockBalance</c> (nhanh);
/// <paramref name="FromLedger"/> = true thì cộng dồn từ <c>StockTransaction</c> (nguồn sự thật —
/// dùng để đối soát/phát hiện lệch sau sync).
/// </summary>
public sealed record GetStockOnHandQuery(Guid StoreId, Guid? VariantId = null, bool FromLedger = false)
    : IRequest<IReadOnlyList<StockOnHandItem>>;

public sealed record StockOnHandItem(Guid VariantId, decimal OnHand, decimal AvgCost = 0m);

public sealed class GetStockOnHandHandler : IRequestHandler<GetStockOnHandQuery, IReadOnlyList<StockOnHandItem>>
{
    private readonly IPosDbContext _db;

    public GetStockOnHandHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<StockOnHandItem>> Handle(GetStockOnHandQuery q, CancellationToken ct)
    {
        if (q.FromLedger)
        {
            var ledger = await _db.StockTransactions
                .Where(s => s.StoreId == q.StoreId && (q.VariantId == null || s.VariantId == q.VariantId))
                .GroupBy(s => s.VariantId)
                .Select(g => new StockOnHandItem(g.Key, g.Sum(x => x.QtyChange), 0m))
                .ToListAsync(ct);
            return ledger;
        }

        var balances = await _db.StockBalances
            .Where(b => b.StoreId == q.StoreId && (q.VariantId == null || b.VariantId == q.VariantId))
            .Select(b => new StockOnHandItem(b.VariantId, b.Quantity, b.AvgCost))
            .ToListAsync(ct);
        return balances;
    }
}
