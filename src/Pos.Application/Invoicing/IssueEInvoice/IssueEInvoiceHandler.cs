using MediatR;
using Pos.Application.Common;
using Pos.Application.Invoicing.Abstractions;

namespace Pos.Application.Invoicing.IssueEInvoice;

public sealed class IssueEInvoiceHandler : IRequestHandler<IssueEInvoiceCommand, IssueEInvoiceResult>
{
    private readonly IPosDbContext _db;
    private readonly IEInvoiceProvider _provider;

    public IssueEInvoiceHandler(IPosDbContext db, IEInvoiceProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task<IssueEInvoiceResult> Handle(IssueEInvoiceCommand cmd, CancellationToken ct)
    {
        var buyer = cmd.BuyerName is null && cmd.BuyerTaxCode is null && cmd.BuyerPhone is null && cmd.BuyerAddress is null
            ? null
            : new BuyerOverride(cmd.BuyerName, cmd.BuyerTaxCode, cmd.BuyerPhone, cmd.BuyerAddress);

        var einv = await EInvoiceIssuer.IssueOriginalAsync(_db, _provider, cmd.OrderId, buyer, ct);

        return new IssueEInvoiceResult(
            einv.Id, einv.OrderId, einv.Status, einv.CqtCode, einv.InvoiceNo, einv.Serial, einv.ErrorMessage);
    }
}
