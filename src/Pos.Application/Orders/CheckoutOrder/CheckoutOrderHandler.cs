using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;

namespace Pos.Application.Orders.CheckoutOrder;

public sealed class CheckoutOrderHandler : IRequestHandler<CheckoutOrderCommand, CheckoutResult>
{
    private readonly IPosDbContext _db;

    public CheckoutOrderHandler(IPosDbContext db) => _db = db;

    public async Task<CheckoutResult> Handle(CheckoutOrderCommand cmd, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy đơn {cmd.OrderId}.");

        // Idempotency: đơn đã chốt → trả kết quả cũ, không trừ tồn/thu tiền lần hai.
        if (order.Status == OrderStatus.Completed)
            return BuildResult(order, cmd.CashTendered);

        // B4: chỉ chốt được đơn Draft (OnHold phải resume trước; Voided/Returned không hợp lệ).
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException($"Đơn ở trạng thái {order.Status} không thể chốt.");

        if (order.Lines.Count == 0 || order.GrandTotal <= 0m)
            throw new BusinessRuleException("Đơn rỗng / tổng tiền 0 — không cho chốt (B4).");

        // B9: mỗi đơn gắn 1 ca đang mở.
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == order.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {order.ShiftId}.");
        if (shift.CloseAt is not null)
            throw new BusinessRuleException("Ca đã đóng — không thể chốt đơn.");

        // B6: tổng các payment phải bằng GrandTotal (thanh toán hỗn hợp).
        if (cmd.Payments.Count == 0)
            throw new BusinessRuleException("Thiếu thông tin thanh toán.");
        decimal totalPaid = cmd.Payments.Sum(p => p.Amount);
        if (totalPaid != order.GrandTotal)
            throw new BusinessRuleException(
                $"Tổng thanh toán {totalPaid:N0} khác tổng đơn {order.GrandTotal:N0} (B6).");

        var now = DateTime.UtcNow;

        // Ghi nhận thanh toán. PK là GUID client-gen (non-default) → phải Add tường minh,
        // nếu chỉ thêm qua navigation EF coi là entity đã tồn tại (Modified) và lỗi khi lưu.
        foreach (var p in cmd.Payments)
        {
            var payment = new Payment
            {
                OrderId = order.Id,
                Method = p.Method,
                Amount = p.Amount,
                ExternalRef = p.ExternalRef,
                PaidAt = now,
            };
            order.Payments.Add(payment);   // cho BuildResult tính TotalPaid trong bộ nhớ
            _db.Payments.Add(payment);     // ép trạng thái Added
        }

        // B8: trừ tồn append-only — mỗi dòng 1 StockTransaction (Sale, QtyChange âm).
        //     Quy đổi đơn vị 1:1 (chưa hỗ trợ UnitConversion ở bước này); giá vốn = 0 (tính sau).
        foreach (var line in order.Lines)
        {
            _db.StockTransactions.Add(new StockTransaction
            {
                StoreId = order.StoreId,
                DeviceId = order.DeviceId,
                VariantId = line.VariantId,
                Type = StockTransactionType.Sale,
                QtyChange = -line.Qty,
                UnitCost = 0m,
                RefId = order.Id,
            });

            // Cập nhật snapshot tồn (nguồn sự thật vẫn là StockTransaction).
            var balance = await _db.StockBalances
                .FirstOrDefaultAsync(b => b.VariantId == line.VariantId && b.StoreId == order.StoreId, ct);
            if (balance is null)
            {
                // Bán âm kho: theo cấu hình (chặn/cảnh báo). Mặc định cho qua, đánh dấu âm.
                _db.StockBalances.Add(new StockBalance
                {
                    VariantId = line.VariantId,
                    StoreId = order.StoreId,
                    Quantity = -line.Qty,
                });
            }
            else
            {
                balance.Quantity -= line.Qty;
                Touch(balance);
            }
        }

        // B9: cập nhật tiền mặt dự kiến của ca theo phần thu tiền mặt.
        decimal cashAmount = cmd.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        if (cashAmount > 0m)
        {
            shift.ExpectedCash += cashAmount;
            Touch(shift);
        }

        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Paid;
        Touch(order);

        await _db.SaveChangesAsync(ct);
        return BuildResult(order, cmd.CashTendered);
    }

    private static void Touch(EntityBase e)
    {
        e.LastModifiedUtc = DateTime.UtcNow;
        e.Version++;
        e.SyncStatus = SyncStatus.Pending;
    }

    private static CheckoutResult BuildResult(Order order, decimal? cashTendered)
    {
        decimal totalPaid = order.Payments.Sum(p => p.Amount);
        decimal cashAmount = order.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        // B6: mở két tự động khi có thanh toán tiền mặt.
        bool openDrawer = cashAmount > 0m;
        // Tiền thối = tiền khách đưa − phần trả bằng tiền mặt (B6/B13).
        decimal change = cashTendered is { } t && t > cashAmount ? t - cashAmount : 0m;

        return new CheckoutResult(
            order.Id, order.OrderNumber, order.Status, order.PaymentStatus,
            order.GrandTotal, totalPaid, change, openDrawer);
    }
}
