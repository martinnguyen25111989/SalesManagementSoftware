using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Customers;
using Pos.Domain.Organization;
using Pos.Domain.Sales;

namespace Pos.Application.Customers.Loyalty;

/// <summary>
/// Lõi tích/đổi điểm (B10). Tích theo doanh thu thực (sau chiết khấu, có/không thuế theo cấu hình chi nhánh);
/// trả hàng phải thu hồi điểm tương ứng (không làm âm điểm sai). Append-only qua <see cref="LoyaltyTxn"/>.
/// </summary>
internal static class LoyaltyService
{
    /// <summary>Cộng điểm cho đơn vừa chốt (no-op nếu khách lẻ hoặc chi nhánh tắt tích điểm).</summary>
    public static async Task AccrueAsync(IPosDbContext db, Store store, Order order, CancellationToken ct)
    {
        if (order.CustomerId is not { } customerId || store.LoyaltyVndPerPoint <= 0m)
            return;

        // Doanh thu tính điểm: sau CK, trước thuế (mặc định) hoặc tổng có thuế (cấu hình).
        decimal basis = store.LoyaltyEarnOnGrandTotal ? order.GrandTotal : order.Subtotal - order.DiscountTotal;
        int points = (int)Math.Floor(basis / store.LoyaltyVndPerPoint);
        if (points <= 0)
            return;

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException($"Không tìm thấy khách hàng {customerId}.");

        db.LoyaltyTxns.Add(new LoyaltyTxn { CustomerId = customerId, OrderId = order.Id, PointChange = points });
        customer.PointBalance += points;
        customer.MarkModified();
    }

    /// <summary>
    /// Thu hồi điểm khi trả hàng: theo tỉ lệ tiền hoàn / tổng đơn, chặn trên ở số điểm đơn này đã tích
    /// (trừ phần đã thu hồi trước đó) và không đẩy số dư xuống âm — "trừ lại điểm đúng".
    /// </summary>
    public static async Task RevokeForReturnAsync(
        IPosDbContext db, Order order, decimal refundTotal, CancellationToken ct)
    {
        if (order.CustomerId is not { } customerId || refundTotal <= 0m || order.GrandTotal <= 0m)
            return;

        // Điểm còn lại do đơn này đem lại = tổng PointChange của các txn gắn đơn (tích − đã thu hồi).
        int netEarned = await db.LoyaltyTxns
            .Where(t => t.OrderId == order.Id && t.CustomerId == customerId)
            .SumAsync(t => t.PointChange, ct);
        if (netEarned <= 0)
            return;

        int revoke = (int)Math.Round(netEarned * (refundTotal / order.GrandTotal), MidpointRounding.AwayFromZero);
        revoke = Math.Min(revoke, netEarned); // không thu hồi quá số đơn này đã tích

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException($"Không tìm thấy khách hàng {customerId}.");

        revoke = Math.Min(revoke, (int)customer.PointBalance); // không làm âm số dư
        if (revoke <= 0)
            return;

        db.LoyaltyTxns.Add(new LoyaltyTxn { CustomerId = customerId, OrderId = order.Id, PointChange = -revoke });
        customer.PointBalance -= revoke;
        customer.MarkModified();
    }
}
