using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Catalog.Queries;

/// <summary>
/// Danh sách hàng hóa cho màn hình quản trị (B3): gồm cả mặt hàng ngừng bán, kèm giá hiện hành &amp;
/// tồn hiện có theo chi nhánh (B8) để vận hành theo dõi. Khác <see cref="GetSalesCatalogQuery"/>
/// (chỉ hàng đang bán &amp; đã có giá, dùng ở quầy POS).
/// </summary>
public sealed record GetAdminProductsQuery(Guid StoreId) : IRequest<IReadOnlyList<AdminProductItem>>;

public sealed record AdminProductItem(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string Name,
    string Category,
    string Unit,
    VatRate TaxRate,
    decimal Price,
    decimal OnHand,
    bool IsActive)
{
    /// <summary>Giá trị tồn ước tính theo giá bán (B12) — phục vụ màn hình tồn kho.</summary>
    public decimal StockValue => OnHand * Price;
}

public sealed class GetAdminProductsHandler
    : IRequestHandler<GetAdminProductsQuery, IReadOnlyList<AdminProductItem>>
{
    private readonly IPosDbContext _db;

    public GetAdminProductsHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminProductItem>> Handle(GetAdminProductsQuery q, CancellationToken ct)
    {
        var variants = await _db.Variants.Include(v => v.Product)
            .Where(v => v.Product != null)
            .ToListAsync(ct);

        var variantIds = variants.Select(v => v.Id).ToList();

        // Giá hiện hành = PriceItem mới nhất theo biến thể (gom ở bộ nhớ để chạy trên mọi provider).
        var prices = (await _db.PriceItems
                .Where(p => variantIds.Contains(p.VariantId))
                .ToListAsync(ct))
            .GroupBy(p => p.VariantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedUtc).First().Price);

        var onHand = (await _db.StockBalances
                .Where(b => b.StoreId == q.StoreId && variantIds.Contains(b.VariantId))
                .ToListAsync(ct))
            .ToDictionary(b => b.VariantId, b => b.Quantity);

        var categories = await _db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return variants
            .Select(v =>
            {
                var p = v.Product!;
                string category = p.CategoryId is { } cid && categories.TryGetValue(cid, out var n) ? n : "Khác";
                return new AdminProductItem(
                    p.Id, v.Id,
                    string.IsNullOrWhiteSpace(v.VariantSku) ? p.Sku : v.VariantSku,
                    string.IsNullOrWhiteSpace(p.Name) ? v.VariantSku : p.Name,
                    category, p.BaseUnit, p.TaxRate,
                    prices.GetValueOrDefault(v.Id),
                    onHand.GetValueOrDefault(v.Id),
                    p.Status == ProductStatus.Active);
            })
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .ToList();
    }
}

/// <summary>Danh sách tên ngành hàng (cho ô chọn ở màn hình tạo hàng hóa).</summary>
public sealed record GetCategoriesQuery : IRequest<IReadOnlyList<string>>;

public sealed class GetCategoriesHandler : IRequestHandler<GetCategoriesQuery, IReadOnlyList<string>>
{
    private readonly IPosDbContext _db;

    public GetCategoriesHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> Handle(GetCategoriesQuery q, CancellationToken ct) =>
        await _db.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(ct);
}
