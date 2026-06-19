using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Inventory.AdjustStock;
using Pos.Application.Inventory.Queries;
using Pos.Application.Inventory.ReceiveStock;
using Pos.Application.Inventory.TransferStock;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly ISender _mediator;

    public InventoryController(ISender mediator) => _mediator = mediator;

    /// <summary>Nhập hàng từ NCC (GRN) — cộng tồn.</summary>
    [HttpPost("receive")]
    public Task<ActionResult<ReceiveStockResult>> Receive([FromBody] ReceiveStockCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command, ct));

    /// <summary>Kiểm kê / điều chỉnh tồn (cần quyền + lý do).</summary>
    [HttpPost("adjust")]
    public Task<ActionResult<AdjustStockResult>> Adjust([FromBody] AdjustStockCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command, ct));

    /// <summary>Chuyển kho giữa 2 chi nhánh (2 vế khớp).</summary>
    [HttpPost("transfer")]
    public Task<ActionResult<TransferStockResult>> Transfer([FromBody] TransferStockCommand command, CancellationToken ct)
        => Run(() => _mediator.Send(command, ct));

    /// <summary>Tồn hiện có theo chi nhánh (snapshot, hoặc cộng dồn từ ledger để đối soát).</summary>
    [HttpGet("on-hand")]
    public Task<ActionResult<IReadOnlyList<StockOnHandItem>>> OnHand(
        [FromQuery] Guid storeId, [FromQuery] Guid? variantId, [FromQuery] bool fromLedger, CancellationToken ct)
        => Run(() => _mediator.Send(new GetStockOnHandQuery(storeId, variantId, fromLedger), ct));

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
