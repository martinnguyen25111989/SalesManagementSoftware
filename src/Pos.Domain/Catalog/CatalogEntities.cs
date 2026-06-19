using Pos.Domain.Common;

namespace Pos.Domain.Catalog;

/// <summary>Ngành hàng / nhóm sản phẩm (có thể lồng cây qua ParentId).</summary>
public class Category : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}

/// <summary>Sản phẩm (B3). Thuế suất gắn ở cấp sản phẩm phục vụ HĐĐT.</summary>
public class Product : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string BaseUnit { get; set; } = "Cái";
    public VatRate TaxRate { get; set; } = VatRate.Ten;
    public ProductStatus Status { get; set; } = ProductStatus.Active;

    public ICollection<Variant> Variants { get; set; } = new List<Variant>();
}

/// <summary>Biến thể (size/màu/dung lượng…). Mỗi biến thể 1 SKU + nhiều barcode.</summary>
public class Variant : EntityBase
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string VariantSku { get; set; } = string.Empty;

    /// <summary>Thuộc tính dạng JSON, vd {"size":"M","color":"đỏ"}.</summary>
    public string? Attributes { get; set; }

    /// <summary>Bán theo cân (kg/gram).</summary>
    public bool IsWeighed { get; set; }

    public ICollection<Barcode> Barcodes { get; set; } = new List<Barcode>();
}

/// <summary>Mã vạch — 1 biến thể có thể có nhiều mã; hỗ trợ mã cân điện tử.</summary>
public class Barcode : EntityBase
{
    public Guid VariantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public BarcodeType Type { get; set; } = BarcodeType.Ean13;
}

/// <summary>Quy đổi đơn vị (Thùng = 24 Lon). Tồn/bán quy về đơn vị cơ bản.</summary>
public class UnitConversion : EntityBase
{
    public Guid ProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;

    /// <summary>Hệ số quy đổi ra đơn vị cơ bản (vd Thùng -> 24).</summary>
    public decimal FactorToBase { get; set; } = 1m;
}

/// <summary>Bảng giá theo chi nhánh/thời điểm. StoreId null = áp toàn hệ thống.</summary>
public class PriceList : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public Guid? StoreId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public ICollection<PriceItem> Items { get; set; } = new List<PriceItem>();
}

public class PriceItem : EntityBase
{
    public Guid PriceListId { get; set; }
    public Guid VariantId { get; set; }
    public decimal Price { get; set; }
}
