using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Inventory;
using Pos.Domain.Returns;
using Pos.Domain.Sales;

namespace Pos.Application.Returns.CreateReturn;

public sealed class CreateReturnHandler : IRequestHandler<CreateReturnCommand, ReturnResult>
{
    private readonly IPosDbContext _db;

    public CreateReturnHandler(IPosDbContext db) => _db = db;

    public async Task<ReturnResult> Handle(CreateReturnCommand cmd, CancellationToken ct)
    {
        // Idempotency theo ReturnId.
        var existing = await _db.ReturnOrders
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == cmd.ReturnId, ct);
        if (existing is not null)
        {
            var ord = await _db.Orders.Include(o => o.Lines)
                .FirstAsync(o => o.Id == existing.OriginalOrderId, ct);
            return BuildResult(existing, ord);
        }

        // B7: cần quyền Manager + lý do.
        if (!cmd.ManagerApproved)
            throw new BusinessRuleException("Trả hàng cần Manager duyệt (B7).");
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new BusinessRuleException("Trả hàng phải có lý do.");
        if (cmd.Lines.Count == 0)
            throw new BusinessRuleException("Phiếu trả phải có ít nhất 1 dòng.");

        // Hóa đơn gốc phải tồn tại & đã hoàn tất.
        var order = await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == cmd.OriginalOrderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy hóa đơn gốc {cmd.OriginalOrderId}.");
        if (order.Status is not (OrderStatus.Completed or OrderStatus.PartiallyReturned))
            throw new BusinessRuleException($"Chỉ trả hàng cho đơn đã hoàn tất (hiện {order.Status}).");

        // Ca xử lý trả phải đang mở (để hạch toán hoàn tiền mặt vào quỹ).
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == cmd.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {cmd.ShiftId}.");
        if (shift.CloseAt is not null)
            throw new BusinessRuleException("Ca đã đóng — không xử lý trả hàng.");

        var orderLines = order.Lines.ToDictionary(l => l.Id);

        // Số lượng đã trả trước đó theo từng dòng (không cho trả quá số đã mua).
        var priorReturnOrderIds = await _db.ReturnOrders
            .Where(r => r.OriginalOrderId == order.Id)
            .Select(r => r.Id)
            .ToListAsync(ct);
        var priorReturnedByLine = await _db.ReturnLines
            .Where(rl => priorReturnOrderIds.Contains(rl.ReturnOrderId))
            .GroupBy(rl => rl.OrderLineId)
            .Select(g => new { OrderLineId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.OrderLineId, x => x.Qty, ct);

        var returnOrder = new ReturnOrder
        {
            Id = cmd.ReturnId,
            StoreId = order.StoreId,
            DeviceId = cmd.DeviceId,
            OriginalOrderId = order.Id,
            Reason = cmd.Reason,
            RefundMethod = cmd.RefundMethod,
            ApprovedBy = cmd.ApprovedBy,
        };

        // Tổng số đã trả (gồm phiếu này) theo dòng — để xác định trả toàn phần/một phần.
        var returnedNow = new Dictionary<Guid, decimal>();
        var lineResults = new List<ReturnLineResult>(cmd.Lines.Count);
        decimal refundTotal = 0m;

        foreach (var input in cmd.Lines)
        {
            if (input.Qty <= 0m)
                throw new BusinessRuleException("Số lượng trả phải lớn hơn 0.");
            if (!orderLines.TryGetValue(input.OrderLineId, out var ol))
                throw new NotFoundException($"Dòng {input.OrderLineId} không thuộc hóa đơn gốc.");

            decimal alreadyReturned = priorReturnedByLine.GetValueOrDefault(ol.Id);
            if (alreadyReturned + input.Qty > ol.Qty)
                throw new BusinessRuleException(
                    $"Trả quá số đã mua cho dòng {ol.Id}: đã mua {ol.Qty:N0}, đã trả {alreadyReturned:N0}, trả thêm {input.Qty:N0}.");

            // Hoàn theo tỷ lệ trên tổng dòng đã gồm CK tổng phân bổ & VAT (B7 edge: KM tổng đơn).
            decimal refund = RoundDong(ol.LineTotal / ol.Qty * input.Qty);
            refundTotal += refund;

            returnOrder.Lines.Add(new ReturnLine
            {
                ReturnOrderId = returnOrder.Id,
                OrderLineId = ol.Id,
                Qty = input.Qty,
                RestockToInventory = input.RestockToInventory,
            });

            // B8: nhập lại tồn (append-only) trừ hàng lỗi/hủy.
            if (input.RestockToInventory)
            {
                _db.StockTransactions.Add(new StockTransaction
                {
                    StoreId = order.StoreId,
                    DeviceId = cmd.DeviceId,
                    VariantId = ol.VariantId,
                    Type = StockTransactionType.Return,
                    QtyChange = input.Qty,
                    UnitCost = 0m,
                    RefId = returnOrder.Id,
                });

                var balance = await _db.StockBalances
                    .FirstOrDefaultAsync(b => b.VariantId == ol.VariantId && b.StoreId == order.StoreId, ct);
                if (balance is null)
                    _db.StockBalances.Add(new StockBalance { VariantId = ol.VariantId, StoreId = order.StoreId, Quantity = input.Qty });
                else
                {
                    balance.Quantity += input.Qty;
                    balance.MarkModified();
                }
            }

            returnedNow[ol.Id] = input.Qty;
            lineResults.Add(new ReturnLineResult(ol.Id, ol.VariantId, input.Qty, refund, input.RestockToInventory));
        }

        returnOrder.RefundAmount = refundTotal;
        // PK GUID client-gen → Add tường minh (đã add Lines qua navigation, nhưng root cần Add).
        _db.ReturnOrders.Add(returnOrder);

        // B9: hoàn tiền mặt làm giảm tiền mặt dự kiến trong quỹ.
        if (cmd.RefundMethod == PaymentMethod.Cash)
        {
            shift.ExpectedCash -= refundTotal;
            shift.MarkModified();
        }

        // Cập nhật trạng thái hóa đơn gốc: trả toàn phần hay một phần (B4).
        bool fullyReturned = order.Lines.All(l =>
            priorReturnedByLine.GetValueOrDefault(l.Id) + returnedNow.GetValueOrDefault(l.Id) >= l.Qty);
        order.Status = fullyReturned ? OrderStatus.Returned : OrderStatus.PartiallyReturned;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.MarkModified();

        await _db.SaveChangesAsync(ct);

        return new ReturnResult(returnOrder.Id, order.Id, order.Status, refundTotal, cmd.RefundMethod, lineResults);
    }

    /// <summary>Dựng lại kết quả từ phiếu trả đã lưu (đường idempotent) — tính lại hoàn/dòng từ hóa đơn gốc.</summary>
    private static ReturnResult BuildResult(ReturnOrder r, Order original)
    {
        var olById = original.Lines.ToDictionary(l => l.Id);
        var lines = r.Lines.Select(rl =>
        {
            var ol = olById[rl.OrderLineId];
            decimal refund = RoundDong(ol.LineTotal / ol.Qty * rl.Qty);
            return new ReturnLineResult(rl.OrderLineId, ol.VariantId, rl.Qty, refund, rl.RestockToInventory);
        }).ToList();
        return new ReturnResult(r.Id, r.OriginalOrderId, original.Status, r.RefundAmount, r.RefundMethod, lines);
    }

    private static decimal RoundDong(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
