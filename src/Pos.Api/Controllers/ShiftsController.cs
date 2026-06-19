using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Shifts.CashMovements;
using Pos.Application.Shifts.CloseShift;
using Pos.Application.Shifts.OpenShift;
using Pos.Application.Shifts.Reports;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShiftsController : ControllerBase
{
    private readonly ISender _mediator;

    public ShiftsController(ISender mediator) => _mediator = mediator;

    /// <summary>Mở ca (đếm quỹ đầu ca).</summary>
    [HttpPost("open")]
    public Task<ActionResult<ShiftResult>> Open([FromBody] OpenShiftCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command, ct));

    /// <summary>Đóng ca (đếm tiền cuối ca) → Z-report.</summary>
    [HttpPost("{id:guid}/close")]
    public Task<ActionResult<ShiftReport>> Close(Guid id, [FromBody] CloseShiftCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command with { ShiftId = id }, ct));

    /// <summary>Ghi nhận thu/chi tiền mặt trong ca.</summary>
    [HttpPost("{id:guid}/cash-movements")]
    public Task<ActionResult<CashMovementResult>> CashMovement(
        Guid id, [FromBody] RecordCashMovementCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command with { ShiftId = id }, ct));

    /// <summary>Báo cáo ca: X-report (đang mở) hoặc Z-report (đã đóng).</summary>
    [HttpGet("{id:guid}/report")]
    public Task<ActionResult<ShiftReport>> Report(Guid id, CancellationToken ct)
        => Run(() => _mediator.Send(new GetShiftReportQuery(id), ct));

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
