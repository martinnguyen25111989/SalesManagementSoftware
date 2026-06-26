using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Catalog.CreateProduct;

/// <summary>
/// Tạo / cập nhật một mặt hàng (B3): Product + 1 Variant mặc định + giá hiện hành (PriceItem) + (tùy chọn) Barcode.
/// Idempotent theo <see cref="ProductId"/>. Ghi thẳng vào DB (PostgreSQL online / SQLite offline).
/// Quy tắc B3: tên bắt buộc, SKU duy nhất, giá không âm, thuế suất gắn ở cấp sản phẩm.
/// </summary>
public sealed record CreateProductCommand : IRequest<ProductAdminResult>
{
    public Guid ProductId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? Barcode { get; init; }

    /// <summary>Ngành hàng (theo tên) — tạo mới nếu chưa có.</summary>
    public string Category { get; init; } = string.Empty;
    public string BaseUnit { get; init; } = "Cái";
    public VatRate TaxRate { get; init; } = VatRate.Ten;

    /// <summary>Giá bán hiện hành (B5) — đưa vào bảng giá chung.</summary>
    public decimal Price { get; init; }
    public bool IsActive { get; init; } = true;

    // ── Tồn kho ban đầu (B8) ──────────────────────────────────────────────
    /// <summary>Chi nhánh ghi tồn đầu kỳ. Bắt buộc nếu <see cref="InitialStock"/> &gt; 0.</summary>
    public Guid StoreId { get; init; }
    public string? DeviceId { get; init; }

    /// <summary>
    /// Tồn kho ban đầu (đơn vị cơ bản) — chỉ áp dụng khi TẠO MỚI mặt hàng. &gt; 0 → tự ghi 1
    /// <c>StockTransaction</c> điều chỉnh (B8 append-only) để màn tồn kho thấy ngay. Sửa mặt hàng đã có
    /// KHÔNG đụng tồn (biến động tồn đi qua Nhập hàng / kiểm kê).
    /// </summary>
    public decimal InitialStock { get; init; }

    /// <summary>Giá vốn của tồn ban đầu (B8) — vào bình quân gia quyền. Mặc định lấy theo giá bán nếu để 0.</summary>
    public decimal InitialCost { get; init; }
}

public sealed record ProductAdminResult(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string Name,
    string Category,
    string Unit,
    VatRate TaxRate,
    decimal Price,
    bool IsActive);
