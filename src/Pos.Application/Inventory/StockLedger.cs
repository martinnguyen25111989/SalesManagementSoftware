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
    /// <summary>
    /// Ghi 1 biến động tồn theo đơn vị cơ bản (B8) cho 1 chi nhánh + cập nhật snapshot và giá vốn.
    /// <paramref name="acquisitionCost"/> = giá vốn nhập vào (nhập hàng / chuyển kho đến) → cập nhật
    /// bình quân gia quyền; null = biến động không mang giá vốn mới (bán, trả, kiểm kê) → đóng dấu
    /// và dùng giá vốn bình quân hiện hành (để tính lãi gộp).
    /// </summary>
    public static async Task ApplyAsync(
        IPosDbContext db, Guid storeId, Guid variantId, decimal qtyChange,
        StockTransactionType type, Guid? refId, string? deviceId, decimal? acquisitionCost, CancellationToken ct)
    {
        // Local-first: nhiều dòng cùng variant trong 1 thao tác không tạo 2 snapshot trùng khóa.
        var balance = db.StockBalances.Local.FirstOrDefault(b => b.VariantId == variantId && b.StoreId == storeId)
            ?? await db.StockBalances.FirstOrDefaultAsync(b => b.VariantId == variantId && b.StoreId == storeId, ct);

        decimal oldQty = balance?.Quantity ?? 0m;
        decimal oldAvg = balance?.AvgCost ?? 0m;

        // Giá vốn bình quân gia quyền di động: chỉ đổi khi nhập vào với giá vốn mới (qtyChange > 0).
        // Tồn cũ âm (bán vượt) không tính trọng số để tránh méo bình quân.
        decimal newAvg = oldAvg;
        decimal stampedCost = oldAvg;
        if (acquisitionCost is decimal cost)
        {
            stampedCost = cost;
            if (qtyChange > 0m)
            {
                decimal weightedOld = Math.Max(oldQty, 0m);
                decimal denom = weightedOld + qtyChange;
                newAvg = denom > 0m ? (weightedOld * oldAvg + qtyChange * cost) / denom : cost;
            }
        }

        db.StockTransactions.Add(new StockTransaction
        {
            StoreId = storeId,
            DeviceId = deviceId,
            VariantId = variantId,
            Type = type,
            QtyChange = qtyChange,
            UnitCost = stampedCost,
            RefId = refId,
        });

        if (balance is null)
            db.StockBalances.Add(new StockBalance
            {
                VariantId = variantId, StoreId = storeId, Quantity = qtyChange, AvgCost = newAvg,
            });
        else
        {
            balance.Quantity += qtyChange;
            balance.AvgCost = newAvg;
            balance.MarkModified();
        }
    }

    /// <summary>Giá vốn bình quân hiện hành (0 nếu chưa có bản ghi) — dùng cho chuyển kho mang giá vốn theo.</summary>
    public static async Task<decimal> AvgCostAsync(IPosDbContext db, Guid storeId, Guid variantId, CancellationToken ct)
    {
        var local = db.StockBalances.Local.FirstOrDefault(b => b.VariantId == variantId && b.StoreId == storeId);
        if (local is not null) return local.AvgCost;
        var balance = await db.StockBalances
            .FirstOrDefaultAsync(b => b.VariantId == variantId && b.StoreId == storeId, ct);
        return balance?.AvgCost ?? 0m;
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
