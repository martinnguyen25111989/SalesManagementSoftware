using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalog.Queries;
using Pos.Application.Tests.Support;
using Pos.Domain.Catalog;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Catalog;

public class GetSalesCatalogTests
{
    private static (Product product, Variant variant) AddProduct(
        TestPosDbContext db, string sku, string name, decimal price, VatRate tax,
        ProductStatus status = ProductStatus.Active, Guid? categoryId = null, bool withPrice = true)
    {
        var product = new Product { Name = name, Sku = sku, TaxRate = tax, Status = status, CategoryId = categoryId };
        var variant = new Variant { ProductId = product.Id, VariantSku = sku };
        db.Products.Add(product);
        db.Variants.Add(variant);
        if (withPrice)
            db.PriceItems.Add(new PriceItem { VariantId = variant.Id, Price = price });
        return (product, variant);
    }

    [Fact]
    public async Task Returns_ActiveProductsWithPrice_WithTaxAndCategory()
    {
        using var db = TestPosDbContext.Create();
        var cat = new Category { Name = "Cà phê" };
        db.Categories.Add(cat);
        AddProduct(db, "CF01", "Cà phê đen", 18_000m, VatRate.Eight, categoryId: cat.Id);
        await db.SaveChangesAsync();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        var item = Assert.Single(items);
        Assert.Equal("Cà phê đen", item.Name);
        Assert.Equal("Cà phê", item.Category);
        Assert.Equal(18_000m, item.Price);
        Assert.Equal(VatRate.Eight, item.TaxRate);
    }

    [Fact]
    public async Task Excludes_ProductWithoutPrice()
    {
        using var db = TestPosDbContext.Create();
        AddProduct(db, "NP01", "Chưa có giá", 0m, VatRate.Ten, withPrice: false);
        await db.SaveChangesAsync();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Excludes_DiscontinuedProduct()
    {
        using var db = TestPosDbContext.Create();
        AddProduct(db, "DC01", "Ngừng bán", 10_000m, VatRate.Ten, status: ProductStatus.Discontinued);
        await db.SaveChangesAsync();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        Assert.Empty(items);
    }

    [Fact]
    public async Task UsesLatestPrice_WhenMultiplePriceItems()
    {
        using var db = TestPosDbContext.Create();
        var (_, variant) = AddProduct(db, "P01", "SP", 10_000m, VatRate.Ten, withPrice: false);
        db.PriceItems.Add(new PriceItem { VariantId = variant.Id, Price = 10_000m, CreatedUtc = DateTime.UtcNow.AddDays(-2) });
        db.PriceItems.Add(new PriceItem { VariantId = variant.Id, Price = 12_500m, CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        Assert.Equal(12_500m, Assert.Single(items).Price); // giá mới nhất
    }

    [Fact]
    public async Task NoCategory_FallsBackTo_Khac()
    {
        using var db = TestPosDbContext.Create();
        AddProduct(db, "X1", "Không nhóm", 5_000m, VatRate.Ten);
        await db.SaveChangesAsync();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        Assert.Equal("Khác", Assert.Single(items).Category);
    }
}
