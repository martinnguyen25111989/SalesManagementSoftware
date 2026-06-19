using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Orders.ResumeOrder;

public sealed class ResumeOrderHandler : IRequestHandler<ResumeOrderCommand, OrderStateResult>
{
    private readonly IPosDbContext _db;

    public ResumeOrderHandler(IPosDbContext db) => _db = db;

    public async Task<OrderStateResult> Handle(ResumeOrderCommand cmd, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy đơn {cmd.OrderId}.");

        if (order.Status == OrderStatus.Draft)
            return new OrderStateResult(order.Id, order.OrderNumber, order.Status);

        if (order.Status != OrderStatus.OnHold)
            throw new BusinessRuleException($"Chỉ mở lại được đơn OnHold (hiện {order.Status}).");

        order.Status = OrderStatus.Draft;
        order.MarkModified();
        await _db.SaveChangesAsync(ct);

        return new OrderStateResult(order.Id, order.OrderNumber, order.Status);
    }
}
