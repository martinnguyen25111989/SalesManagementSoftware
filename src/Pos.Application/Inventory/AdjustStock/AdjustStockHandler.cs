using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Inventory.AdjustStock;

public sealed class AdjustStockHandler : IRequestHandler<AdjustStockCommand, AdjustStockResult>
{
    private readonly IPosDbContext _db;

    public AdjustStockHandler(IPosDbContext db) => _db = db;

    public async Task<AdjustStockResult> Handle(AdjustStockCommand cmd, CancellationToken ct)
    {
        // Idempotency: phiếu kiểm kê đã ghi (1 biến động StockTake gắn AdjustId).
        var existing = await _db.StockTransactions
            .FirstOrDefaultAsync(s => s.RefId == cmd.AdjustId && s.Type == StockTransactionType.StockTake, ct);
        if (existing is not null)
        {
            decimal current = await StockLedger.OnHandAsync(_db, cmd.StoreId, cmd.VariantId, ct);
            return new AdjustStockResult(cmd.AdjustId, cmd.VariantId, current - existing.QtyChange, current, existing.QtyChange);
        }

        // B2: kiểm kê/điều chỉnh tồn cần quyền + lý do.
        if (!cmd.ManagerApproved)
            throw new BusinessRuleException("Điều chỉnh tồn cần Manager duyệt (B2).");
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new BusinessRuleException("Điều chỉnh tồn phải có lý do.");
        if (cmd.CountedQty < 0m)
            throw new BusinessRuleException("Số lượng đếm không được âm.");

        decimal previous = await StockLedger.OnHandAsync(_db, cmd.StoreId, cmd.VariantId, ct);
        decimal diff = cmd.CountedQty - previous;

        // Ghi biến động chênh lệch (kể cả 0 để lưu vết đã kiểm kê).
        await StockLedger.ApplyAsync(_db, cmd.StoreId, cmd.VariantId, diff,
            StockTransactionType.StockTake, cmd.AdjustId, cmd.DeviceId, null, ct);

        await _db.SaveChangesAsync(ct);

        return new AdjustStockResult(cmd.AdjustId, cmd.VariantId, previous, cmd.CountedQty, diff);
    }
}
