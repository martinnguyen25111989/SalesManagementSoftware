using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Orders;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Orders.HoldOrder;
using Pos.Application.Orders.ResumeOrder;
using Pos.Application.Orders.VoidOrder;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ISender _mediator;

    public OrdersController(ISender mediator) => _mediator = mediator;

    /// <summary>
    /// Tạo đơn (Draft). Idempotency-Key (header) = OrderId — gửi lại cùng key không tạo đơn trùng
    /// (CLAUDE.md). Nếu body có OrderId thì ưu tiên body; header dùng khi body bỏ trống.
    /// </summary>
    [HttpPost]
    public Task<ActionResult<OrderResult>> Create(
        [FromBody] CreateOrderCommand command,
        [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
        CancellationToken ct)
    {
        if (command.OrderId == Guid.Empty && idempotencyKey is { } key && key != Guid.Empty)
            command = command with { OrderId = key };
        return Run(() => _mediator.Send(command, ct));
    }

    /// <summary>
    /// Chốt đơn: thanh toán (đa phương thức) + trừ tồn (B6/B8). Idempotent theo OrderId —
    /// gọi lại đơn đã Completed trả về kết quả cũ, không thu tiền/trừ tồn lần hai.
    /// </summary>
    [HttpPost("{id:guid}/checkout")]
    public Task<ActionResult<CheckoutResult>> Checkout(Guid id, [FromBody] CheckoutOrderCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command with { OrderId = id }, ct));

    /// <summary>Giữ đơn (Hold/Park): Draft → OnHold.</summary>
    [HttpPost("{id:guid}/hold")]
    public Task<ActionResult<OrderStateResult>> Hold(Guid id, CancellationToken ct)
        => Run(() => _mediator.Send(new HoldOrderCommand(id), ct));

    /// <summary>Mở lại đơn đang giữ: OnHold → Draft.</summary>
    [HttpPost("{id:guid}/resume")]
    public Task<ActionResult<OrderStateResult>> Resume(Guid id, CancellationToken ct)
        => Run(() => _mediator.Send(new ResumeOrderCommand(id), ct));

    /// <summary>Hủy đơn chưa hoàn tất (cần quyền + lý do): Draft/OnHold → Voided.</summary>
    [HttpPost("{id:guid}/void")]
    public Task<ActionResult<OrderStateResult>> Void(Guid id, [FromBody] VoidOrderCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command with { OrderId = id }, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }
}
