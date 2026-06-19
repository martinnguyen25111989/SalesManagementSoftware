using MediatR;

namespace Pos.Application.Inventory.TransferStock;

/// <summary>
/// Chuyển kho giữa 2 chi nhánh (B8): xuất kho nguồn (−) và nhập kho đích (+) — 2 vế khớp,
/// ghi trong cùng 1 giao dịch (không để mất hàng giữa 2 kho). Idempotent theo TransferId.
/// </summary>
public sealed record TransferStockCommand : IRequest<TransferStockResult>
{
    public Guid TransferId { get; init; } = Guid.NewGuid();
    public Guid FromStoreId { get; init; }
    public Guid ToStoreId { get; init; }
    public string? DeviceId { get; init; }

    public IReadOnlyList<TransferLine> Lines { get; init; } = new List<TransferLine>();
}

public sealed record TransferLine(Guid VariantId, decimal Qty);

public sealed record TransferStockResult(
    Guid TransferId, Guid FromStoreId, Guid ToStoreId, IReadOnlyList<TransferLineResult> Lines);

public sealed record TransferLineResult(Guid VariantId, decimal Qty, decimal FromOnHand, decimal ToOnHand);
