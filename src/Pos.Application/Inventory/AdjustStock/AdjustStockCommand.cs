using MediatR;

namespace Pos.Application.Inventory.AdjustStock;

/// <summary>
/// Kiểm kê / điều chỉnh tồn (B8): đếm thực tế → sinh biến động chênh lệch (hao hụt/thừa).
/// Cần quyền (B2: Warehouse + Manager) + lý do. Idempotent theo AdjustId.
/// </summary>
public sealed record AdjustStockCommand : IRequest<AdjustStockResult>
{
    public Guid AdjustId { get; init; } = Guid.NewGuid();
    public Guid StoreId { get; init; }
    public Guid VariantId { get; init; }

    /// <summary>Số lượng đếm thực tế (đơn vị cơ bản).</summary>
    public decimal CountedQty { get; init; }

    public string Reason { get; init; } = string.Empty;
    public string? DeviceId { get; init; }

    /// <summary>Manager đã duyệt (B2). Chưa có Auth/PIN → truyền cờ.</summary>
    public bool ManagerApproved { get; init; }
}

public sealed record AdjustStockResult(
    Guid AdjustId, Guid VariantId, decimal PreviousQty, decimal CountedQty, decimal Difference);
