using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Returns.CreateReturn;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReturnsController : ControllerBase
{
    private readonly ISender _mediator;

    public ReturnsController(ISender mediator) => _mediator = mediator;

    /// <summary>Tạo phiếu trả hàng / hoàn tiền (B7). Cần quyền Manager + lý do.</summary>
    [HttpPost]
    public async Task<ActionResult<ReturnResult>> Create([FromBody] CreateReturnCommand command, CancellationToken ct)
    {
        try
        {
            return Ok(await _mediator.Send(command, ct));
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
