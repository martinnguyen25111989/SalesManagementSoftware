using System.Net;
using Microsoft.Extensions.Options;
using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;
using Pos.Infrastructure.Invoicing.EasyInvoice;

namespace Pos.Infrastructure.Tests.Invoicing;

public class EasyInvoiceProviderTests
{
    private const string LoginJson = """{"access_token":"tok-123","expires_in":3600}""";

    private static EInvoiceRequest SampleRequest() => new(
        TransactionId: Guid.NewGuid(),
        SellerTaxCode: "0312345678",
        TemplateCode: "1",
        Serial: "C25MAA",
        Type: EInvoiceType.Original,
        BuyerName: "Khách lẻ",
        BuyerTaxCode: null, BuyerPhone: null, BuyerAddress: null,
        Items: new[]
        {
            new EInvoiceItem("Cà phê", "Ly", 2m, 12_000m, 8m, "8%", 24_000m, 1_920m),
        },
        VatByRate: new[] { new EInvoiceVatLine(8m, "8%", 24_000m, 1_920m) },
        TotalAmountWithoutVat: 24_000m,
        TotalVat: 1_920m,
        TotalAmount: 25_920m,
        AmountInWords: "Hai mươi lăm nghìn chín trăm hai mươi đồng");

    private static (EasyInvoiceProvider provider, ScriptedHttpHandler handler) Build(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        var opt = new EInvoiceOptions
        {
            BaseUrl = "https://test.local", Username = "u", Password = "p", SellerTaxCode = "0312345678",
        };
        var handler = new ScriptedHttpHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri(opt.BaseUrl) };
        var provider = new EasyInvoiceProvider(http, Options.Create(opt), new EasyInvoiceTokenCache());
        return (provider, handler);
    }

    [Fact]
    public async Task Issue_Success_ParsesCqt_AndSendsBearer()
    {
        var (provider, handler) = Build((req, _) =>
            req.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, """{"cqtCode":"CQT-9","invoiceNo":"0000009"}"""));

        var result = await provider.IssueAsync(SampleRequest());

        Assert.Equal(EInvoiceOutcome.Issued, result.Outcome);
        Assert.Equal("CQT-9", result.CqtCode);
        Assert.Equal("0000009", result.InvoiceNo);
        Assert.Contains(handler.Calls, c => c.Path.EndsWith("CreateInvoice"));
    }

    [Fact]
    public async Task Issue_PayloadCarriesTransactionId_AsIdempotencyKey()
    {
        var req = SampleRequest();
        var (provider, handler) = Build((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, """{"cqtCode":"CQT-1"}"""));

        await provider.IssueAsync(req);

        var create = handler.Calls.Single(c => c.Path.EndsWith("CreateInvoice"));
        Assert.Contains(req.TransactionId.ToString(), create.Body);
        Assert.Contains("0312345678", create.Body); // sellerTaxCode
    }

    [Fact]
    public async Task Issue_401_RelogsInOnce_ThenSucceeds()
    {
        int createCalls = 0, loginCalls = 0;
        var (provider, _) = Build((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("Login"))
            {
                loginCalls++;
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson);
            }
            createCalls++;
            return createCalls == 1
                ? ScriptedHttpHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, """{"cqtCode":"CQT-2"}""");
        });

        var result = await provider.IssueAsync(SampleRequest());

        Assert.Equal(EInvoiceOutcome.Issued, result.Outcome);
        Assert.Equal(2, loginCalls);   // login lần đầu + re-login sau 401
        Assert.Equal(2, createCalls);
    }

    [Fact]
    public async Task Issue_ServerError_IsTransient()
    {
        var (provider, _) = Build((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.InternalServerError, "boom"));

        var result = await provider.IssueAsync(SampleRequest());

        Assert.Equal(EInvoiceOutcome.TransientError, result.Outcome);
    }

    [Fact]
    public async Task Issue_BadData_IsBusinessError()
    {
        var (provider, _) = Build((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.BadRequest, """{"message":"Sai thuế suất"}"""));

        var result = await provider.IssueAsync(SampleRequest());

        Assert.Equal(EInvoiceOutcome.BusinessError, result.Outcome);
        Assert.Contains("Sai thuế suất", result.ErrorMessage);
    }

    [Fact]
    public async Task Issue_DuplicateTransaction_QueriesExistingCqt()
    {
        var (provider, handler) = Build((r, _) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("Login")) return ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson);
            if (path.EndsWith("CreateInvoice")) return ScriptedHttpHandler.Json(HttpStatusCode.Conflict, "dup");
            return ScriptedHttpHandler.Json(HttpStatusCode.OK, """{"status":"issued","cqtCode":"CQT-OLD"}""");
        });

        var result = await provider.IssueAsync(SampleRequest());

        Assert.Equal(EInvoiceOutcome.Issued, result.Outcome);
        Assert.Equal("CQT-OLD", result.CqtCode);
        Assert.Contains(handler.Calls, c => c.Path.EndsWith("GetStatus")); // đã tra cứu thay vì tạo mới
    }

    [Fact]
    public async Task Cancel_CallsCancelEndpoint()
    {
        var (provider, handler) = Build((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await provider.CancelAsync("INV-KEY", "Khách hủy");

        Assert.Equal(EInvoiceOutcome.Issued, result.Outcome);
        Assert.Contains(handler.Calls, c => c.Path.EndsWith("CancelInvoice"));
    }

    [Fact]
    public async Task Adjust_CallsAdjustEndpoint_WithOriginalKey()
    {
        var (provider, handler) = Build((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("Login")
                ? ScriptedHttpHandler.Json(HttpStatusCode.OK, LoginJson)
                : ScriptedHttpHandler.Json(HttpStatusCode.OK, """{"cqtCode":"CQT-ADJ"}"""));

        var result = await provider.AdjustAsync("INV-KEY", SampleRequest() with { Type = EInvoiceType.Adjust });

        Assert.Equal(EInvoiceOutcome.Issued, result.Outcome);
        var adjust = handler.Calls.Single(c => c.Path.EndsWith("AdjustInvoice"));
        Assert.Contains("INV-KEY", adjust.Body);
    }
}
