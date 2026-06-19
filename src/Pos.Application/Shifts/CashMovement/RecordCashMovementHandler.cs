using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Operations;

namespace Pos.Application.Shifts.CashMovements;

public sealed class RecordCashMovementHandler : IRequestHandler<RecordCashMovementCommand, CashMovementResult>
{
    private readonly IPosDbContext _db;

    public RecordCashMovementHandler(IPosDbContext db) => _db = db;

    public async Task<CashMovementResult> Handle(RecordCashMovementCommand cmd, CancellationToken ct)
    {
        // Idempotency theo Id.
        var existing = await _db.CashMovements.FirstOrDefaultAsync(m => m.Id == cmd.Id, ct);
        if (existing is not null)
        {
            var s = await _db.Shifts.FirstAsync(x => x.Id == existing.ShiftId, ct);
            return new CashMovementResult(existing.Id, existing.ShiftId, existing.Type, existing.Amount, s.ExpectedCash);
        }

        if (cmd.Amount <= 0m)
            throw new BusinessRuleException("Số tiền thu/chi phải lớn hơn 0.");

        var shift = await _db.Shifts.FirstOrDefaultAsync(x => x.Id == cmd.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {cmd.ShiftId}.");
        if (shift.CloseAt is not null)
            throw new BusinessRuleException("Ca đã đóng — không ghi nhận thu/chi.");

        var movement = new CashMovement
        {
            Id = cmd.Id,
            ShiftId = cmd.ShiftId,
            Type = cmd.Type,
            Amount = cmd.Amount,
            Reason = cmd.Reason,
        };
        // PK GUID client-gen → Add tường minh (xem ghi chú ở CheckoutOrderHandler).
        _db.CashMovements.Add(movement);

        shift.ExpectedCash += cmd.Type == CashMovementType.In ? cmd.Amount : -cmd.Amount;
        shift.LastModifiedUtc = DateTime.UtcNow;
        shift.Version++;
        shift.SyncStatus = SyncStatus.Pending;

        await _db.SaveChangesAsync(ct);

        return new CashMovementResult(movement.Id, movement.ShiftId, movement.Type, movement.Amount, shift.ExpectedCash);
    }
}
