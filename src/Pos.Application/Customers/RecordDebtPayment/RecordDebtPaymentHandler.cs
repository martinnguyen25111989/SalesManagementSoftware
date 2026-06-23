using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Customers;

namespace Pos.Application.Customers.RecordDebtPayment;

public sealed class RecordDebtPaymentHandler : IRequestHandler<RecordDebtPaymentCommand, DebtPaymentResult>
{
    private readonly IPosDbContext _db;

    public RecordDebtPaymentHandler(IPosDbContext db) => _db = db;

    public async Task<DebtPaymentResult> Handle(RecordDebtPaymentCommand cmd, CancellationToken ct)
    {
        // Idempotency theo PaymentId — không thu nợ hai lần.
        var existing = await _db.DebtPayments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId, ct);
        if (existing is not null)
        {
            decimal current = await _db.Receivables
                .Where(r => r.CustomerId == existing.CustomerId).SumAsync(r => r.Outstanding, ct);
            return new DebtPaymentResult(existing.Id, existing.CustomerId, existing.Amount, current);
        }

        if (cmd.Amount <= 0m)
            throw new BusinessRuleException("Số tiền trả nợ phải lớn hơn 0.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cmd.CustomerId, ct)
            ?? throw new NotFoundException($"Không tìm thấy khách hàng {cmd.CustomerId}.");

        // Công nợ còn lại, trả hạn sớm trước (null = chưa kỳ hạn → xếp cuối).
        var receivables = await _db.Receivables
            .Where(r => r.CustomerId == cmd.CustomerId && r.Outstanding > 0m)
            .ToListAsync(ct);
        decimal totalOutstanding = receivables.Sum(r => r.Outstanding);
        if (cmd.Amount > totalOutstanding)
            throw new BusinessRuleException(
                $"Số tiền trả {cmd.Amount:N0} vượt công nợ còn lại {totalOutstanding:N0}.");

        decimal remaining = cmd.Amount;
        foreach (var r in receivables
            .OrderBy(r => r.DueDate ?? DateTime.MaxValue).ThenBy(r => r.CreatedUtc))
        {
            if (remaining <= 0m) break;
            decimal applied = Math.Min(remaining, r.Outstanding);
            r.Paid += applied;
            r.Outstanding -= applied;
            r.MarkModified();
            remaining -= applied;
        }

        var payment = new DebtPayment
        {
            Id = cmd.PaymentId,
            StoreId = cmd.StoreId,
            DeviceId = cmd.DeviceId,
            CustomerId = cmd.CustomerId,
            Amount = cmd.Amount,
            Method = cmd.Method,
            ShiftId = cmd.ShiftId,
        };
        _db.DebtPayments.Add(payment);

        // B9: thu nợ tiền mặt tại quầy → tăng tiền mặt dự kiến của ca đang mở.
        if (cmd.Method == PaymentMethod.Cash && cmd.ShiftId is { } shiftId)
        {
            var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct)
                ?? throw new NotFoundException($"Không tìm thấy ca {shiftId}.");
            if (shift.CloseAt is not null)
                throw new BusinessRuleException("Ca đã đóng — không ghi nhận thu nợ tiền mặt.");
            shift.ExpectedCash += cmd.Amount;
            shift.MarkModified();
        }

        await _db.SaveChangesAsync(ct);

        return new DebtPaymentResult(payment.Id, cmd.CustomerId, cmd.Amount, totalOutstanding - cmd.Amount);
    }
}
