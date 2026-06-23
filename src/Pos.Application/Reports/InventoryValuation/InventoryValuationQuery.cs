using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Reports.InventoryValuation;

/// <summary>
/// Báo cáo giá trị tồn kho hiện tại (B12 — Tồn kho): theo biến thể trong 1 chi nhánh (hoặc toàn hệ thống),
/// giá trị = tồn × giá vốn bình quân (B8). Snapshot hiện tại, không lọc theo thời gian.
/// </summary>
public sealed record InventoryValuationQuery(Guid? StoreId = null) : IRequest<InventoryValuationResult>;

public sealed record InventoryValuationResult(
    decimal TotalValue,
    IReadOnlyList<InventoryValueRow> Items);

public sealed record InventoryValueRow(
    Guid StoreId, Guid VariantId, string ProductName, decimal Quantity, decimal AvgCost, decimal Value);

public sealed class InventoryValuationHandler : IRequestHandler<InventoryValuationQuery, InventoryValuationResult>
{
    private readonly IPosDbContext _db;

    public InventoryValuationHandler(IPosDbContext db) => _db = db;

    public async Task<InventoryValuationResult> Handle(InventoryValuationQuery q, CancellationToken ct)
    {
        var balances = await _db.StockBalances
            .Where(b => b.Quantity != 0m && (q.StoreId == null || b.StoreId == q.StoreId))
            .Select(b => new { b.StoreId, b.VariantId, b.Quantity, b.AvgCost })
            .ToListAsync(ct);

        var variantIds = balances.Select(b => b.VariantId).Distinct().ToList();
        var names = await _db.Variants.Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Product != null ? v.Product.Name : "Hàng hóa", ct);

        var items = balances
            .Select(b => new InventoryValueRow(
                b.StoreId, b.VariantId, names.GetValueOrDefault(b.VariantId, "Hàng hóa"),
                b.Quantity, b.AvgCost, b.Quantity * b.AvgCost))
            .OrderByDescending(i => i.Value)
            .ToList();

        return new InventoryValuationResult(items.Sum(i => i.Value), items);
    }
}
