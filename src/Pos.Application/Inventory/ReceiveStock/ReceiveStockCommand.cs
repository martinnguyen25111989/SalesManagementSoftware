using MediatR;

namespace Pos.Application.Inventory.ReceiveStock;

/// <summary>
/// Nhập hàng (GRN / Purchase Receipt — B8): nhận hàng từ NCC có giá vốn, cộng tồn (+).
/// Idempotent theo ReceiptId. Số lượng theo đơn vị cơ bản.
/// </summary>
public sealed record ReceiveStockCommand : IRequest<ReceiveStockResult>
{
    public Guid ReceiptId { get; init; } = Guid.NewGuid();
    public Guid StoreId { get; init; }
    public Guid SupplierId { get; init; }
    public string? DeviceId { get; init; }

    public IReadOnlyList<ReceiveLine> Lines { get; init; } = new List<ReceiveLine>();
}

public sealed record ReceiveLine(Guid VariantId, decimal Qty, decimal UnitCost);

public sealed record ReceiveStockResult(Guid ReceiptId, decimal Total, IReadOnlyList<StockLineResult> Lines);

public sealed record StockLineResult(Guid VariantId, decimal QtyChange, decimal OnHand);
