using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Orders.HoldOrder;

public sealed class HoldOrderHandler : IRequestHandler<HoldOrderCommand, OrderStateResult>
{
    private readonly IPosDbContext _db;

    public HoldOrderHandler(IPosDbContext db) => _db = db;

    public async Task<OrderStateResult> Handle(HoldOrderCommand cmd, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy đơn {cmd.OrderId}.");

        if (order.Status == OrderStatus.OnHold)
            return new OrderStateResult(order.Id, order.OrderNumber, order.Status);

        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException($"Chỉ giữ được đơn Draft (hiện {order.Status}).");

        order.Status = OrderStatus.OnHold;
        order.MarkModified();
        await _db.SaveChangesAsync(ct);

        return new OrderStateResult(order.Id, order.OrderNumber, order.Status);
    }
}
