using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory.ReceiveStock;

public sealed class ReceiveStockHandler : IRequestHandler<ReceiveStockCommand, ReceiveStockResult>
{
    private readonly IPosDbContext _db;

    public ReceiveStockHandler(IPosDbContext db) => _db = db;

    public async Task<ReceiveStockResult> Handle(ReceiveStockCommand cmd, CancellationToken ct)
    {
        // Idempotency theo ReceiptId.
        var existing = await _db.PurchaseReceipts.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == cmd.ReceiptId, ct);
        if (existing is not null)
            return await BuildResultAsync(existing.Id, existing.Total, existing.StoreId,
                existing.Lines.Select(l => (l.VariantId, l.Qty)), ct);

        if (cmd.Lines.Count == 0)
            throw new BusinessRuleException("Phiếu nhập phải có ít nhất 1 dòng.");
        if (cmd.Lines.Any(l => l.Qty <= 0m))
            throw new BusinessRuleException("Số lượng nhập phải lớn hơn 0.");

        _ = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == cmd.SupplierId, ct)
            ?? throw new NotFoundException($"Không tìm thấy nhà cung cấp {cmd.SupplierId}.");

        decimal total = cmd.Lines.Sum(l => l.Qty * l.UnitCost);
        var receipt = new PurchaseReceipt
        {
            Id = cmd.ReceiptId,
            StoreId = cmd.StoreId,
            DeviceId = cmd.DeviceId,
            SupplierId = cmd.SupplierId,
            Total = total,
        };

        foreach (var l in cmd.Lines)
        {
            receipt.Lines.Add(new GrnLine
            {
                ReceiptId = receipt.Id,
                VariantId = l.VariantId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
            });
            await StockLedger.ApplyAsync(_db, cmd.StoreId, l.VariantId, l.Qty,
                StockTransactionType.Purchase, receipt.Id, cmd.DeviceId, l.UnitCost, ct);
        }

        // PK GUID client-gen → Add root tường minh (GrnLines đã add qua navigation).
        _db.PurchaseReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(receipt.Id, total, cmd.StoreId, cmd.Lines.Select(l => (l.VariantId, l.Qty)), ct);
    }

    private async Task<ReceiveStockResult> BuildResultAsync(
        Guid receiptId, decimal total, Guid storeId, IEnumerable<(Guid VariantId, decimal Qty)> lines, CancellationToken ct)
    {
        var results = new List<StockLineResult>();
        foreach (var (variantId, qty) in lines)
        {
            decimal onHand = await StockLedger.OnHandAsync(_db, storeId, variantId, ct);
            results.Add(new StockLineResult(variantId, qty, onHand));
        }
        return new ReceiveStockResult(receiptId, total, results);
    }
}
