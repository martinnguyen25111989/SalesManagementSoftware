using MediatR;

namespace Pos.Application.Shifts.OpenShift;

/// <summary>
/// Mở ca (B9): đếm &amp; nhập quỹ đầu ca. Khởi tạo ExpectedCash = OpeningFloat.
/// Idempotent theo ShiftId (GUID client-gen). Mỗi máy (Register) chỉ 1 ca mở tại một thời điểm.
/// </summary>
public sealed record OpenShiftCommand : IRequest<ShiftResult>
{
    public Guid ShiftId { get; init; } = Guid.NewGuid();
    public Guid StoreId { get; init; }
    public Guid RegisterId { get; init; }
    public Guid CashierId { get; init; }
    public string? DeviceId { get; init; }
    public decimal OpeningFloat { get; init; }
}

public sealed record ShiftResult(
    Guid Id,
    Guid StoreId,
    Guid RegisterId,
    Guid CashierId,
    decimal OpeningFloat,
    decimal ExpectedCash,
    DateTime OpenAt);
