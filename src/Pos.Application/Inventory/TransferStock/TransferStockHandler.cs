using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Inventory.TransferStock;

public sealed class TransferStockHandler : IRequestHandler<TransferStockCommand, TransferStockResult>
{
    private readonly IPosDbContext _db;

    public TransferStockHandler(IPosDbContext db) => _db = db;

    public async Task<TransferStockResult> Handle(TransferStockCommand cmd, CancellationToken ct)
    {
        // Idempotency: vế xuất (TransferOut) đã ghi cho TransferId.
        bool already = await _db.StockTransactions
            .AnyAsync(s => s.RefId == cmd.TransferId && s.Type == StockTransactionType.TransferOut, ct);
        if (already)
            return await BuildResultAsync(cmd, ct);

        if (cmd.FromStoreId == cmd.ToStoreId)
            throw new BusinessRuleException("Kho nguồn và kho đích phải khác nhau.");
        if (cmd.Lines.Count == 0)
            throw new BusinessRuleException("Phiếu chuyển kho phải có ít nhất 1 dòng.");
        if (cmd.Lines.Any(l => l.Qty <= 0m))
            throw new BusinessRuleException("Số lượng chuyển phải lớn hơn 0.");

        foreach (var l in cmd.Lines)
        {
            // Giá vốn theo hàng đi: lấy bình quân kho nguồn để kho đích blend đúng (B8) — đọc trước khi xuất.
            decimal sourceAvg = await StockLedger.AvgCostAsync(_db, cmd.FromStoreId, l.VariantId, ct);
            // Hai vế ghi cùng SaveChanges → khớp tuyệt đối, không mất hàng giữa 2 kho.
            await StockLedger.ApplyAsync(_db, cmd.FromStoreId, l.VariantId, -l.Qty,
                StockTransactionType.TransferOut, cmd.TransferId, cmd.DeviceId, null, ct);
            await StockLedger.ApplyAsync(_db, cmd.ToStoreId, l.VariantId, l.Qty,
                StockTransactionType.TransferIn, cmd.TransferId, cmd.DeviceId, sourceAvg, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await BuildResultAsync(cmd, ct);
    }

    private async Task<TransferStockResult> BuildResultAsync(TransferStockCommand cmd, CancellationToken ct)
    {
        var lines = new List<TransferLineResult>();
        foreach (var l in cmd.Lines)
        {
            decimal from = await StockLedger.OnHandAsync(_db, cmd.FromStoreId, l.VariantId, ct);
            decimal to = await StockLedger.OnHandAsync(_db, cmd.ToStoreId, l.VariantId, ct);
            lines.Add(new TransferLineResult(l.VariantId, l.Qty, from, to));
        }
        return new TransferStockResult(cmd.TransferId, cmd.FromStoreId, cmd.ToStoreId, lines);
    }
}
