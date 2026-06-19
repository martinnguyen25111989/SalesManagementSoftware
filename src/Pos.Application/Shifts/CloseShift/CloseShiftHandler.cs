using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Shifts.Reports;
using Pos.Domain.Common;

namespace Pos.Application.Shifts.CloseShift;

public sealed class CloseShiftHandler : IRequestHandler<CloseShiftCommand, ShiftReport>
{
    /// <summary>Ngưỡng lệch quỹ cần Manager duyệt (cấu hình được sau). VND.</summary>
    private const decimal VarianceApprovalThreshold = 50_000m;

    private readonly IPosDbContext _db;

    public CloseShiftHandler(IPosDbContext db) => _db = db;

    public async Task<ShiftReport> Handle(CloseShiftCommand cmd, CancellationToken ct)
    {
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == cmd.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {cmd.ShiftId}.");

        // Idempotent: đã đóng → trả Z-report cũ.
        if (shift.CloseAt is not null)
            return await ShiftReportBuilder.BuildAsync(_db, shift, ct);

        // B9: không cho đóng khi còn đơn Hold chưa xử lý.
        int onHold = await _db.Orders.CountAsync(o => o.ShiftId == shift.Id && o.Status == OrderStatus.OnHold, ct);
        if (onHold > 0)
            throw new BusinessRuleException($"Còn {onHold} đơn đang Hold — xử lý trước khi đóng ca.");

        decimal variance = cmd.CountedCash - shift.ExpectedCash;

        // B2: lệch quỹ vượt ngưỡng cần Manager duyệt.
        if (Math.Abs(variance) > VarianceApprovalThreshold && !cmd.ManagerApproved)
            throw new BusinessRuleException(
                $"Lệch quỹ {variance:N0} vượt ngưỡng {VarianceApprovalThreshold:N0} — cần Manager duyệt.");

        shift.CountedCash = cmd.CountedCash;
        shift.Variance = variance;
        shift.CloseAt = DateTime.UtcNow;
        shift.LastModifiedUtc = DateTime.UtcNow;
        shift.Version++;
        shift.SyncStatus = SyncStatus.Pending;

        await _db.SaveChangesAsync(ct);

        return await ShiftReportBuilder.BuildAsync(_db, shift, ct);
    }
}
