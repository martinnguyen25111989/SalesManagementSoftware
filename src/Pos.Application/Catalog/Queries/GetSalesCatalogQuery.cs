using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Catalog;
using Pos.Domain.Common;

namespace Pos.Application.Catalog.Queries;

/// <summary>
/// Danh mục hàng để bán (B3): biến thể của sản phẩm đang kinh doanh, kèm giá hiện hành &amp; thuế suất.
/// Dùng cho màn hình bán hàng nạp hàng động từ DB.
/// </summary>
public sealed record GetSalesCatalogQuery : IRequest<IReadOnlyList<SalesCatalogItem>>;

public sealed record SalesCatalogItem(
    Guid VariantId,
    string Sku,
    string Name,
    string Category,
    decimal Price,
    VatRate TaxRate);

public sealed class GetSalesCatalogHandler : IRequestHandler<GetSalesCatalogQuery, IReadOnlyList<SalesCatalogItem>>
{
    private readonly IPosDbContext _db;

    public GetSalesCatalogHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<SalesCatalogItem>> Handle(GetSalesCatalogQuery query, CancellationToken ct)
    {
        var variants = await _db.Variants
            .Include(v => v.Product)
            .Where(v => v.Product != null && v.Product.Status == ProductStatus.Active)
            .ToListAsync(ct);

        var variantIds = variants.Select(v => v.Id).ToList();

        // Giá hiện hành = PriceItem mới nhất theo biến thể (B5). Gom ở bộ nhớ để dịch được trên mọi provider.
        var prices = (await _db.PriceItems
                .Where(p => variantIds.Contains(p.VariantId))
                .ToListAsync(ct))
            .GroupBy(p => p.VariantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedUtc).First().Price);

        var categories = await _db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return variants
            .Where(v => prices.ContainsKey(v.Id)) // chỉ bán hàng đã có giá
            .Select(v =>
            {
                var p = v.Product!;
                string category = p.CategoryId is { } cid && categories.TryGetValue(cid, out var n) ? n : "Khác";
                string name = string.IsNullOrWhiteSpace(p.Name) ? v.VariantSku : p.Name;
                string sku = string.IsNullOrWhiteSpace(v.VariantSku) ? p.Sku : v.VariantSku;
                return new SalesCatalogItem(v.Id, sku, name, category, prices[v.Id], p.TaxRate);
            })
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .ToList();
    }
}
