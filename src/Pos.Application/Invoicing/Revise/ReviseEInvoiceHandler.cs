using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;
using Pos.Domain.Invoicing;

namespace Pos.Application.Invoicing.Revise;

public sealed class ReviseEInvoiceHandler : IRequestHandler<ReviseEInvoiceCommand, ReviseEInvoiceResult>
{
    private readonly IPosDbContext _db;
    private readonly IEInvoiceProvider _provider;

    public ReviseEInvoiceHandler(IPosDbContext db, IEInvoiceProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task<ReviseEInvoiceResult> Handle(ReviseEInvoiceCommand cmd, CancellationToken ct)
    {
        if (cmd.Type is not (EInvoiceType.Adjust or EInvoiceType.Replace or EInvoiceType.Cancel))
            throw new BusinessRuleException("Loại nghiệp vụ phải là Điều chỉnh/Thay thế/Hủy.");
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new BusinessRuleException("Điều chỉnh/thay thế/hủy HĐĐT phải có lý do (B11).");

        // Idempotency: chứng từ điều chỉnh đã tạo → trả lại.
        var existing = await _db.EInvoices.FirstOrDefaultAsync(e => e.Id == cmd.ReviseId, ct);
        if (existing is not null)
            return ToResult(existing);

        var original = await _db.EInvoices.FirstOrDefaultAsync(e => e.Id == cmd.OriginalInvoiceId, ct)
            ?? throw new NotFoundException($"Không tìm thấy HĐĐT gốc {cmd.OriginalInvoiceId}.");
        if (original.Status != EInvoiceStatus.Issued)
            throw new BusinessRuleException(
                $"Chỉ điều chỉnh/thay thế/hủy HĐ đã phát hành (hiện {original.Status}).");

        string originalKey = original.ProviderRef ?? original.CqtCode
            ?? throw new BusinessRuleException("HĐ gốc thiếu khóa NCC để điều chỉnh.");

        EInvoiceResult result;
        if (cmd.Type == EInvoiceType.Cancel)
        {
            result = await _provider.CancelAsync(originalKey, cmd.Reason, ct);
        }
        else
        {
            var order = await _db.Orders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == original.OrderId, ct)
                ?? throw new NotFoundException($"Không tìm thấy đơn {original.OrderId}.");
            var req = await EInvoiceIssuer.BuildRequestAsync(_db, order, cmd.Type, buyer: null, cmd.Reason, ct);
            result = cmd.Type == EInvoiceType.Adjust
                ? await _provider.AdjustAsync(originalKey, req, ct)
                : await _provider.ReplaceAsync(originalKey, req, ct);
        }

        var revise = new EInvoice
        {
            Id = cmd.ReviseId,
            OrderId = original.OrderId,
            StoreId = original.StoreId,
            DeviceId = original.DeviceId,
            Type = cmd.Type,
            OriginalInvoiceId = original.Id,
            ErrorMessage = cmd.Reason,
        };

        if (result.Outcome == EInvoiceOutcome.Issued)
        {
            // Hủy → chứng từ ở trạng thái Canceled; điều chỉnh/thay thế → Issued (có mã CQT mới).
            if (cmd.Type == EInvoiceType.Cancel)
            {
                revise.Status = EInvoiceStatus.Canceled;
                revise.ProviderRef = result.ProviderRef;
                original.Status = EInvoiceStatus.Canceled;
                original.MarkModified();
            }
            else
            {
                EInvoiceIssuer.Apply(revise, result);
            }
        }
        else if (result.Outcome == EInvoiceOutcome.BusinessError)
        {
            revise.Status = EInvoiceStatus.Rejected;
            revise.ErrorMessage = result.ErrorMessage;
        }
        else
        {
            revise.Status = EInvoiceStatus.Pending;
            revise.ErrorMessage = result.ErrorMessage;
        }

        _db.EInvoices.Add(revise);
        await _db.SaveChangesAsync(ct);

        return ToResult(revise);
    }

    private static ReviseEInvoiceResult ToResult(EInvoice e) =>
        new(e.Id, e.OriginalInvoiceId ?? Guid.Empty, e.Type, e.Status, e.CqtCode, e.ErrorMessage);
}
