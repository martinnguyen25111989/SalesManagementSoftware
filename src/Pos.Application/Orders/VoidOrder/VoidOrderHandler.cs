using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Application.Orders.VoidOrder;

public sealed class VoidOrderHandler : IRequestHandler<VoidOrderCommand, OrderStateResult>
{
    private readonly IPosDbContext _db;

    public VoidOrderHandler(IPosDbContext db) => _db = db;

    public async Task<OrderStateResult> Handle(VoidOrderCommand cmd, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct)
            ?? throw new NotFoundException($"Không tìm thấy đơn {cmd.OrderId}.");

        if (order.Status == OrderStatus.Voided)
            return new OrderStateResult(order.Id, order.OrderNumber, order.Status);

        // Đơn đã hoàn tất / đã trả → không hủy thẳng, phải qua nghiệp vụ HĐĐT (B11) hoặc trả hàng (B7).
        if (order.Status is OrderStatus.Completed or OrderStatus.Returned or OrderStatus.PartiallyReturned)
            throw new BusinessRuleException(
                "Đơn đã hoàn tất — phải hủy/điều chỉnh qua hóa đơn điện tử (B11), không hủy trực tiếp.");

        // B2: hủy đơn cần quyền + lý do (ghi AuditLog — bổ sung khi có module Audit).
        if (!cmd.ManagerApproved)
            throw new BusinessRuleException("Hủy đơn cần Manager duyệt (B2).");
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new BusinessRuleException("Hủy đơn phải có lý do.");

        order.Status = OrderStatus.Voided;
        order.MarkModified();
        await _db.SaveChangesAsync(ct);

        return new OrderStateResult(order.Id, order.OrderNumber, order.Status);
    }
}
