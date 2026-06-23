using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;

namespace Pos.Application.Invoicing.ProcessPending;

public sealed class ProcessPendingEInvoicesHandler
    : IRequestHandler<ProcessPendingEInvoicesCommand, ProcessPendingResult>
{
    private readonly IPosDbContext _db;
    private readonly IEInvoiceProvider _provider;

    public ProcessPendingEInvoicesHandler(IPosDbContext db, IEInvoiceProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task<ProcessPendingResult> Handle(ProcessPendingEInvoicesCommand cmd, CancellationToken ct)
    {
        // Phát hành theo đúng thứ tự bán (CreatedUtc) để số hóa đơn không nhảy cóc.
        var pendingOrderIds = await _db.EInvoices
            .Where(e => e.Type == EInvoiceType.Original && e.Status == EInvoiceStatus.Pending)
            .OrderBy(e => e.CreatedUtc)
            .Take(cmd.MaxBatch)
            .Select(e => e.OrderId)
            .ToListAsync(ct);

        int issued = 0, rejected = 0, stillPending = 0;
        foreach (var orderId in pendingOrderIds)
        {
            var einv = await EInvoiceIssuer.IssueOriginalAsync(_db, _provider, orderId, buyer: null, ct);
            switch (einv.Status)
            {
                case EInvoiceStatus.Issued: issued++; break;
                case EInvoiceStatus.Rejected: rejected++; break;
                default: stillPending++; break;
            }
        }

        return new ProcessPendingResult(pendingOrderIds.Count, issued, rejected, stillPending);
    }
}
