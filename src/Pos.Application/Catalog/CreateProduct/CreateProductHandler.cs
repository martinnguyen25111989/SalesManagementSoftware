using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Inventory;
using Pos.Domain.Catalog;
using Pos.Domain.Common;

namespace Pos.Application.Catalog.CreateProduct;

public sealed class CreateProductHandler : IRequestHandler<CreateProductCommand, ProductAdminResult>
{
    private readonly IPosDbContext _db;

    public CreateProductHandler(IPosDbContext db) => _db = db;

    public async Task<ProductAdminResult> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        // B3 — kiểm tra nghiệp vụ.
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new BusinessRuleException("Hàng hóa phải có tên.");
        var sku = cmd.Sku?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sku))
            throw new BusinessRuleException("Hàng hóa phải có mã (SKU).");
        if (cmd.Price < 0m)
            throw new BusinessRuleException("Giá bán không được âm.");
        if (string.IsNullOrWhiteSpace(cmd.Category))
            throw new BusinessRuleException("Hàng hóa phải thuộc một ngành hàng.");
        if (cmd.InitialStock < 0m)
            throw new BusinessRuleException("Tồn kho ban đầu không được âm.");
        if (cmd.InitialStock > 0m && cmd.StoreId == Guid.Empty)
            throw new BusinessRuleException("Cần chỉ định chi nhánh để ghi tồn kho ban đầu.");
        if (cmd.InitialCost < 0m)
            throw new BusinessRuleException("Giá vốn không được âm.");

        // SKU duy nhất (trên Product.Sku và Variant.VariantSku), bỏ qua chính mặt hàng này.
        bool skuTaken = await _db.Products.AnyAsync(p => p.Sku == sku && p.Id != cmd.ProductId, ct)
            || await _db.Variants.AnyAsync(v => v.VariantSku == sku && v.ProductId != cmd.ProductId, ct);
        if (skuTaken)
            throw new BusinessRuleException($"Mã SKU '{sku}' đã tồn tại.");

        var barcode = string.IsNullOrWhiteSpace(cmd.Barcode) ? null : cmd.Barcode.Trim();
        if (barcode is not null
            && await _db.Barcodes.AnyAsync(b => b.Code == barcode, ct))
            throw new BusinessRuleException($"Mã vạch '{barcode}' đã tồn tại.");

        // Ngành hàng theo tên — dùng lại nếu có, tạo mới nếu chưa.
        var categoryName = cmd.Category.Trim();
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Name == categoryName, ct);
        if (category is null)
        {
            category = new Category { Name = categoryName };
            _db.Categories.Add(category);
        }

        // Bảng giá chung — dùng lại nếu có, tạo mới nếu chưa.
        var priceList = await _db.PriceLists
            .OrderBy(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(p => p.StoreId == null, ct);
        if (priceList is null)
        {
            priceList = new PriceList { Name = "Bảng giá chung", EffectiveFrom = DateTime.UtcNow.AddYears(-1) };
            _db.PriceLists.Add(priceList);
        }

        // Upsert theo ProductId — cho sửa lại mặt hàng đã tạo.
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == cmd.ProductId, ct);
        Variant variant;
        bool isNew = product is null;
        if (product is null)
        {
            product = new Product { Id = cmd.ProductId };
            variant = new Variant { ProductId = product.Id, VariantSku = sku };
            // PK GUID client-gen → Add tường minh cả cha lẫn con (không chỉ qua navigation).
            _db.Products.Add(product);
            _db.Variants.Add(variant);
        }
        else
        {
            variant = product.Variants.FirstOrDefault()
                ?? throw new NotFoundException($"Mặt hàng {cmd.ProductId} thiếu biến thể.");
            product.MarkModified();
            variant.VariantSku = sku;
            variant.MarkModified();
        }

        product.Name = cmd.Name.Trim();
        product.Sku = sku;
        product.CategoryId = category.Id;
        product.BaseUnit = string.IsNullOrWhiteSpace(cmd.BaseUnit) ? "Cái" : cmd.BaseUnit.Trim();
        product.TaxRate = cmd.TaxRate;
        product.Status = cmd.IsActive ? ProductStatus.Active : ProductStatus.Discontinued;

        if (barcode is not null)
            _db.Barcodes.Add(new Barcode { VariantId = variant.Id, Code = barcode });

        // Giá hiện hành = thêm 1 PriceItem mới (GetSalesCatalog lấy bản mới nhất theo CreatedUtc).
        _db.PriceItems.Add(new PriceItem
        {
            PriceListId = priceList.Id,
            VariantId = variant.Id,
            Price = cmd.Price,
        });

        // Tồn kho ban đầu (B8): chỉ khi TẠO MỚI — ghi 1 biến động điều chỉnh + cập nhật snapshot/giá vốn.
        // Nguồn sự thật vẫn là StockTransaction (append-only). Sửa mặt hàng đã có không đụng tồn.
        if (isNew && cmd.InitialStock > 0m)
        {
            decimal openingCost = cmd.InitialCost > 0m ? cmd.InitialCost : cmd.Price;
            await StockLedger.ApplyAsync(_db, cmd.StoreId, variant.Id, cmd.InitialStock,
                StockTransactionType.Adjust, product.Id, cmd.DeviceId, openingCost, ct);
        }

        await _db.SaveChangesAsync(ct);

        return new ProductAdminResult(product.Id, variant.Id, product.Sku, product.Name,
            categoryName, product.BaseUnit, product.TaxRate, cmd.Price, cmd.IsActive);
    }
}
