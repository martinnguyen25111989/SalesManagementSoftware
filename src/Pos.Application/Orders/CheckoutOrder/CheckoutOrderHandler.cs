using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Customers.Loyalty;
using Pos.Application.Inventory;
using Pos.Domain.Common;
using Pos.Domain.Customers;
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
        if (cmd.Payments.Any(p => p.Amount <= 0m))
            throw new BusinessRuleException("Số tiền thanh toán phải lớn hơn 0.");
        decimal totalPaid = cmd.Payments.Sum(p => p.Amount);
        if (totalPaid != order.GrandTotal)
            throw new BusinessRuleException(
                $"Tổng thanh toán {totalPaid:N0} khác tổng đơn {order.GrandTotal:N0} (B6).");

        // B6: thẻ/QR/ví phải có mã tham chiếu xác nhận đã nhận tiền — không tự "Paid" khi tiền chưa về.
        var unconfirmed = cmd.Payments.FirstOrDefault(p =>
            p.Method is PaymentMethod.Card or PaymentMethod.VietQR or PaymentMethod.Wallet
            && string.IsNullOrWhiteSpace(p.ExternalRef));
        if (unconfirmed is not null)
            throw new BusinessRuleException(
                $"Thanh toán {unconfirmed.Method} cần mã tham chiếu xác nhận đã nhận tiền (B6).");

        // B10: đổi điểm làm phương thức thanh toán chưa hỗ trợ — không nhận thầm để khỏi "tiêu" điểm sai.
        if (cmd.Payments.Any(p => p.Method == PaymentMethod.Point))
            throw new BusinessRuleException("Thanh toán bằng điểm chưa được hỗ trợ.");

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

        // B10: bán chịu (Debt) — cần KH có hồ sơ + còn hạn mức; vượt hạn mức cần Manager duyệt.
        decimal debtAmount = cmd.Payments.Where(p => p.Method == PaymentMethod.Debt).Sum(p => p.Amount);
        if (debtAmount > 0m)
        {
            if (order.CustomerId is not { } customerId)
                throw new BusinessRuleException("Bán chịu cần khách hàng có hồ sơ (B10).");

            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                ?? throw new NotFoundException($"Không tìm thấy khách hàng {customerId}.");

            // Gom ở client: SQLite (store offline) không dịch được Sum(decimal) trong SQL.
            decimal outstanding = (await _db.Receivables
                    .Where(r => r.CustomerId == customerId)
                    .Select(r => r.Outstanding)
                    .ToListAsync(ct))
                .Sum();
            decimal available = customer.CreditLimit - outstanding;
            if (debtAmount > available && !cmd.ManagerApproved)
                throw new BusinessRuleException(
                    $"Bán chịu {debtAmount:N0} vượt hạn mức còn lại {available:N0} — cần Manager duyệt (B10).");

            _db.Receivables.Add(new Receivable
            {
                CustomerId = customerId,
                OrderId = order.Id,
                Amount = debtAmount,
                Paid = 0m,
                Outstanding = debtAmount,
                DueDate = cmd.DebtDueDate,
            });
        }

        // B8: chính sách bán âm kho theo cấu hình chi nhánh (chặn / cảnh báo + cần quyền / cho qua).
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == order.StoreId, ct)
            ?? throw new NotFoundException($"Không tìm thấy chi nhánh {order.StoreId}.");
        if (store.NegativeStockPolicy != NegativeStockPolicy.Allow)
        {
            foreach (var line in order.Lines)
            {
                decimal onHand = await StockLedger.OnHandAsync(_db, order.StoreId, line.VariantId, ct);
                if (line.Qty <= onHand) continue;
                if (store.NegativeStockPolicy == NegativeStockPolicy.Block)
                    throw new BusinessRuleException(
                        $"Tồn không đủ (còn {onHand:N0}, cần {line.Qty:N0}) — chi nhánh chặn bán âm kho (B8).");
                if (!cmd.ManagerApproved) // Warn
                    throw new BusinessRuleException(
                        $"Bán vượt tồn (còn {onHand:N0}, cần {line.Qty:N0}) — cần Manager duyệt (B8).");
            }
        }

        // B8: trừ tồn append-only qua StockLedger (Sale, QtyChange âm). Quy đổi đơn vị 1:1
        //     (chưa hỗ trợ UnitConversion); giá vốn đóng dấu theo bình quân hiện hành (acquisitionCost=null).
        foreach (var line in order.Lines)
            await StockLedger.ApplyAsync(_db, order.StoreId, line.VariantId, -line.Qty,
                StockTransactionType.Sale, order.Id, order.DeviceId, null, ct);

        // B9: cập nhật tiền mặt dự kiến của ca theo phần thu tiền mặt.
        decimal cashAmount = cmd.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        if (cashAmount > 0m)
        {
            shift.ExpectedCash += cashAmount;
            Touch(shift);
        }

        order.Status = OrderStatus.Completed;
        // B10: phần bán chịu chưa thu tiền → đơn hoàn tất nhưng công nợ chưa trả (Unpaid/Partial).
        order.PaymentStatus = debtAmount <= 0m ? PaymentStatus.Paid
            : debtAmount >= order.GrandTotal ? PaymentStatus.Unpaid
            : PaymentStatus.Partial;
        Touch(order);

        // B10: tích điểm theo doanh thu thực (no-op nếu khách lẻ / chi nhánh tắt tích điểm).
        await LoyaltyService.AccrueAsync(_db, store, order, ct);

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
