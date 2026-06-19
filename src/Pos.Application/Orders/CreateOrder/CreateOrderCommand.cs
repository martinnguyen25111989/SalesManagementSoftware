using MediatR;
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

    /// <summary>CK/KM trên tổng đơn (B5 bước 3), phân bổ về dòng để tính thuế.</summary>
    public decimal OrderDiscount { get; init; }

    /// <summary>Đơn vị làm tròn tiền mặt (vd 1000); 0 = không làm tròn.</summary>
    public decimal CashRoundingUnit { get; init; }

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
