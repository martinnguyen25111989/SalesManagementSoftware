using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

/// <summary>
/// Lõi B8: tồn = cộng dồn <see cref="StockTransaction"/> (append-only). Helper này thêm 1 bản ghi
/// biến động và cập nhật snapshot <see cref="StockBalance"/> (nguồn sự thật vẫn là transaction).
/// Dùng chung cho bán, trả, nhập, kiểm kê, chuyển kho để logic tồn nhất quán.
/// </summary>
internal static class StockLedger
{
    /// <summary>Ghi 1 biến động tồn theo đơn vị cơ bản (B8) cho 1 chi nhánh + cập nhật snapshot.</summary>
    public static async Task ApplyAsync(
        IPosDbContext db, Guid storeId, Guid variantId, decimal qtyChange,
        StockTransactionType type, Guid? refId, string? deviceId, decimal unitCost, CancellationToken ct)
    {
        db.StockTransactions.Add(new StockTransaction
        {
            StoreId = storeId,
            DeviceId = deviceId,
            VariantId = variantId,
            Type = type,
            QtyChange = qtyChange,
            UnitCost = unitCost,
            RefId = refId,
        });

        // Local-first: nhiều dòng cùng variant trong 1 thao tác không tạo 2 snapshot trùng khóa.
        var balance = db.StockBalances.Local.FirstOrDefault(b => b.VariantId == variantId && b.StoreId == storeId)
            ?? await db.StockBalances.FirstOrDefaultAsync(b => b.VariantId == variantId && b.StoreId == storeId, ct);

        if (balance is null)
            db.StockBalances.Add(new StockBalance { VariantId = variantId, StoreId = storeId, Quantity = qtyChange });
        else
        {
            balance.Quantity += qtyChange;
            balance.MarkModified();
        }
    }

    /// <summary>Tồn hiện tại theo snapshot (0 nếu chưa có bản ghi).</summary>
    public static async Task<decimal> OnHandAsync(IPosDbContext db, Guid storeId, Guid variantId, CancellationToken ct)
    {
        var local = db.StockBalances.Local.FirstOrDefault(b => b.VariantId == variantId && b.StoreId == storeId);
        if (local is not null) return local.Quantity;
        var balance = await db.StockBalances
            .FirstOrDefaultAsync(b => b.VariantId == variantId && b.StoreId == storeId, ct);
        return balance?.Quantity ?? 0m;
    }
}
