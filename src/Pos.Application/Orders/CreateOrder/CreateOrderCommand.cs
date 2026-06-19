using MediatR;
using Pos.Application.Pricing;
using Pos.Domain.Common;

namespace Pos.Application.Orders.CreateOrder;

/// <summary>
/// Tạo đơn (Draft) — bước đầu luồng bán hàng (B4). <see cref="OrderId"/> đóng vai trò
/// Idempotency-Key (CLAUDE.md): gửi lại cùng OrderId → không tạo đơn trùng.
/// Chưa trừ tồn kho ở bước này (trừ tồn xảy ra khi checkout/hoàn tất theo workflow).
/// </summary>
public sealed record CreateOrderCommand : IRequest<OrderResult>
{
    /// <summary>GUID sinh ở client = khóa chính &amp; idempotency key.</summary>
    public Guid OrderId { get; init; } = Guid.NewGuid();

    public Guid StoreId { get; init; }
    public Guid ShiftId { get; init; }
    public Guid CashierId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? DeviceId { get; init; }

    /// <summary>CK/KM thủ công trên tổng đơn (cộng thêm phần KM tự động từ engine).</summary>
    public decimal OrderDiscount { get; init; }

    /// <summary>Đơn vị làm tròn tiền mặt (vd 1000); 0 = không làm tròn.</summary>
    public decimal CashRoundingUnit { get; init; }

    /// <summary>Khuyến mãi tự động áp (B5) — engine đánh giá theo điều kiện/ưu tiên.</summary>
    public IReadOnlyList<PromotionDef> Promotions { get; init; } = new List<PromotionDef>();

    /// <summary>Mã voucher khách nhập (B5).</summary>
    public string? VoucherCode { get; init; }

    /// <summary>Hạng thành viên của khách (cho KM theo hạng).</summary>
    public Guid? CustomerTierId { get; init; }

    /// <summary>Manager đã duyệt giảm giá tay vượt ngưỡng (B2/B5).</summary>
    public bool ManagerApproved { get; init; }

    public IReadOnlyList<CreateOrderLine> Lines { get; init; } = new List<CreateOrderLine>();
}

/// <summary>Một dòng yêu cầu. <see cref="UnitPriceOverride"/> null = lấy theo bảng giá.</summary>
public sealed record CreateOrderLine(
    Guid VariantId,
    decimal Qty,
    decimal LineDiscount = 0m,
    decimal? UnitPriceOverride = null,
    string? Note = null);

public sealed record OrderResult(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal RoundingAdj,
    decimal GrandTotal,
    IReadOnlyList<OrderLineResult> Lines);

public sealed record OrderLineResult(
    Guid VariantId,
    decimal Qty,
    decimal UnitPrice,
    decimal LineDiscount,
    VatRate TaxRate,
    decimal Tax,
    decimal LineTotal);
