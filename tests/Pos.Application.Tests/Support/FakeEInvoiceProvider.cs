using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Support;

/// <summary>NCC HĐĐT giả lập cho test orchestration B11 (chưa cắm adapter thật).</summary>
public sealed class FakeEInvoiceProvider : IEInvoiceProvider
{
    public EInvoiceOutcome IssueOutcome { get; set; } = EInvoiceOutcome.Issued;
    public EInvoiceOutcome ReviseOutcome { get; set; } = EInvoiceOutcome.Issued;

    public List<Guid> IssueCalls { get; } = new();
    public List<(EInvoiceType Type, string Key)> ReviseCalls { get; } = new();
    public EInvoiceRequest? LastRequest { get; private set; }

    private int _seq;

    public Task<EInvoiceResult> IssueAsync(EInvoiceRequest req, CancellationToken ct = default)
    {
        IssueCalls.Add(req.TransactionId);
        LastRequest = req;
        return Task.FromResult(Make(IssueOutcome, req.Serial));
    }

    public Task<EInvoiceResult> AdjustAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default)
    {
        ReviseCalls.Add((EInvoiceType.Adjust, originalKey));
        LastRequest = req;
        return Task.FromResult(Make(ReviseOutcome, req.Serial));
    }

    public Task<EInvoiceResult> ReplaceAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default)
    {
        ReviseCalls.Add((EInvoiceType.Replace, originalKey));
        LastRequest = req;
        return Task.FromResult(Make(ReviseOutcome, req.Serial));
    }

    public Task<EInvoiceResult> CancelAsync(string invoiceKey, string reason, CancellationToken ct = default)
    {
        ReviseCalls.Add((EInvoiceType.Cancel, invoiceKey));
        return Task.FromResult(Make(ReviseOutcome, "CANCEL"));
    }

    public Task<EInvoiceStatus> QueryAsync(string invoiceKey, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceStatus.Issued);

    private EInvoiceResult Make(EInvoiceOutcome outcome, string serial) => outcome switch
    {
        EInvoiceOutcome.Issued => EInvoiceResult.Issued(
            $"CQT-{++_seq:D4}", $"{_seq:D7}", serial, $"REF-{_seq:D4}"),
        EInvoiceOutcome.BusinessError => EInvoiceResult.Business("Sai thuế suất/MST (giả lập)."),
        _ => EInvoiceResult.Transient("Timeout NCC (giả lập)."),
    };
}
