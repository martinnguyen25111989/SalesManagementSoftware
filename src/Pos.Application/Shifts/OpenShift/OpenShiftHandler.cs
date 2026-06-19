using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Operations;

namespace Pos.Application.Shifts.OpenShift;

public sealed class OpenShiftHandler : IRequestHandler<OpenShiftCommand, ShiftResult>
{
    private readonly IPosDbContext _db;

    public OpenShiftHandler(IPosDbContext db) => _db = db;

    public async Task<ShiftResult> Handle(OpenShiftCommand cmd, CancellationToken ct)
    {
        // Idempotency: cùng ShiftId đã có → trả ca cũ.
        var existing = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == cmd.ShiftId, ct);
        if (existing is not null)
            return ToResult(existing);

        if (cmd.OpeningFloat < 0m)
            throw new BusinessRuleException("Quỹ đầu ca không được âm.");

        var register = await _db.Registers.FirstOrDefaultAsync(r => r.Id == cmd.RegisterId, ct)
            ?? throw new NotFoundException($"Không tìm thấy máy POS {cmd.RegisterId}.");

        // Mỗi máy chỉ 1 ca mở (B9).
        bool hasOpen = await _db.Shifts.AnyAsync(s => s.RegisterId == cmd.RegisterId && s.CloseAt == null, ct);
        if (hasOpen)
            throw new BusinessRuleException($"Máy {register.Name} đang có ca mở — đóng ca trước khi mở ca mới.");

        var shift = new Shift
        {
            Id = cmd.ShiftId,
            StoreId = cmd.StoreId,
            DeviceId = cmd.DeviceId,
            RegisterId = cmd.RegisterId,
            CashierId = cmd.CashierId,
            OpeningFloat = cmd.OpeningFloat,
            ExpectedCash = cmd.OpeningFloat, // tiền mặt dự kiến khởi đầu = quỹ đầu ca
            OpenAt = DateTime.UtcNow,
        };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync(ct);

        return ToResult(shift);
    }

    private static ShiftResult ToResult(Shift s) => new(
        s.Id, s.StoreId, s.RegisterId, s.CashierId, s.OpeningFloat, s.ExpectedCash, s.OpenAt);
}
