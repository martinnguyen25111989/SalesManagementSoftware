using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Shifts.Reports;

/// <summary>Lấy báo cáo ca (X nếu đang mở, Z nếu đã đóng) — không thay đổi trạng thái.</summary>
public sealed record GetShiftReportQuery(Guid ShiftId) : IRequest<ShiftReport>;

public sealed class GetShiftReportHandler : IRequestHandler<GetShiftReportQuery, ShiftReport>
{
    private readonly IPosDbContext _db;

    public GetShiftReportHandler(IPosDbContext db) => _db = db;

    public async Task<ShiftReport> Handle(GetShiftReportQuery query, CancellationToken ct)
    {
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == query.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {query.ShiftId}.");
        return await ShiftReportBuilder.BuildAsync(_db, shift, ct);
    }
}
