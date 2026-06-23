using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Invoicing.Abstractions;
using Pos.Application.Invoicing.IssueEInvoice;
using Pos.Application.Invoicing.ProcessPending;
using Pos.Application.Invoicing.Revise;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Invoicing;

public class IssueEInvoiceTests
{
    [Fact]
    public async Task Issue_Success_PersistsCqtCode_Issued()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider();

        var r = await new IssueEInvoiceHandler(db, provider).Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);

        Assert.Equal(EInvoiceStatus.Issued, r.Status);
        Assert.False(string.IsNullOrEmpty(r.CqtCode));
        Assert.Equal(1, await db.EInvoices.CountAsync());
    }

    [Fact]
    public async Task Issue_IsIdempotent_NoSecondCqtForSameOrder()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider();
        var handler = new IssueEInvoiceHandler(db, provider);

        var first = await handler.Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);
        var second = await handler.Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);

        Assert.Equal(first.CqtCode, second.CqtCode);
        Assert.Single(provider.IssueCalls);  // không gọi NCC lần hai
        Assert.Equal(1, await db.EInvoices.CountAsync());
    }

    [Fact]
    public async Task Issue_BusinessError_MarkedRejected()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider { IssueOutcome = EInvoiceOutcome.BusinessError };

        var r = await new IssueEInvoiceHandler(db, provider).Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);

        Assert.Equal(EInvoiceStatus.Rejected, r.Status);
        Assert.Null(r.CqtCode);
        Assert.False(string.IsNullOrEmpty(r.ErrorMessage));
    }

    [Fact]
    public async Task Offline_StaysPending_ThenDrains_WhenOnline()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider { IssueOutcome = EInvoiceOutcome.TransientError };

        // Mất mạng khi bán → giữ hàng đợi Pending (in phiếu tạm).
        var pending = await new IssueEInvoiceHandler(db, provider).Handle(
            new IssueEInvoiceCommand { OrderId = orderId }, default);
        Assert.Equal(EInvoiceStatus.Pending, pending.Status);

        // Có mạng lại → job drain phát hành lấy mã CQT.
        provider.IssueOutcome = EInvoiceOutcome.Issued;
        var result = await new ProcessPendingEInvoicesHandler(db, provider).Handle(
            new ProcessPendingEInvoicesCommand(), default);

        Assert.Equal(1, result.Issued);
        Assert.Equal(0, result.StillPending);
        var einv = await db.EInvoices.SingleAsync();
        Assert.Equal(EInvoiceStatus.Issued, einv.Status);
        Assert.False(string.IsNullOrEmpty(einv.CqtCode));
    }

    [Fact]
    public async Task Issue_WithBuyerTaxCode_FlowsToRequest()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider();

        await new IssueEInvoiceHandler(db, provider).Handle(new IssueEInvoiceCommand
        {
            OrderId = orderId, BuyerName = "Công ty ABC", BuyerTaxCode = "0312345678",
        }, default);

        Assert.Equal("Công ty ABC", provider.LastRequest!.BuyerName);
        Assert.Equal("0312345678", provider.LastRequest!.BuyerTaxCode);
    }

    [Fact]
    public async Task Issue_RejectsDraftOrder()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var draftId = await TestData.CreateDraftOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => new IssueEInvoiceHandler(db, new FakeEInvoiceProvider())
            .Handle(new IssueEInvoiceCommand { OrderId = draftId }, default));
    }
}

public class ReviseEInvoiceTests
{
    private static async Task<(Guid invoiceId, FakeEInvoiceProvider provider)> IssuedInvoiceAsync(TestPosDbContext db)
    {
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider();
        var r = await new IssueEInvoiceHandler(db, provider).Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);
        return (r.EInvoiceId, provider);
    }

    [Fact]
    public async Task Cancel_MarksOriginalCanceled_AndChainsDocument()
    {
        using var db = TestPosDbContext.Create();
        var (invoiceId, provider) = await IssuedInvoiceAsync(db);

        var r = await new ReviseEInvoiceHandler(db, provider).Handle(new ReviseEInvoiceCommand
        {
            OriginalInvoiceId = invoiceId, Type = EInvoiceType.Cancel, Reason = "Khách hủy",
        }, default);

        Assert.Equal(EInvoiceStatus.Canceled, r.Status);
        Assert.Equal(invoiceId, r.OriginalInvoiceId);
        Assert.Equal(EInvoiceStatus.Canceled, (await db.EInvoices.FindAsync(invoiceId))!.Status);
        Assert.Equal(2, await db.EInvoices.CountAsync());
    }

    [Fact]
    public async Task Adjust_LinksOriginal_AndIssuesNewDocument()
    {
        using var db = TestPosDbContext.Create();
        var (invoiceId, provider) = await IssuedInvoiceAsync(db);

        var r = await new ReviseEInvoiceHandler(db, provider).Handle(new ReviseEInvoiceCommand
        {
            OriginalInvoiceId = invoiceId, Type = EInvoiceType.Adjust, Reason = "Trả 1 phần (B7)",
        }, default);

        Assert.Equal(EInvoiceStatus.Issued, r.Status);
        Assert.Equal(EInvoiceType.Adjust, r.Type);
        Assert.False(string.IsNullOrEmpty(r.CqtCode));
        var adjust = await db.EInvoices.FirstAsync(e => e.Type == EInvoiceType.Adjust);
        Assert.Equal(invoiceId, adjust.OriginalInvoiceId);
    }

    [Fact]
    public async Task Revise_IsIdempotent()
    {
        using var db = TestPosDbContext.Create();
        var (invoiceId, provider) = await IssuedInvoiceAsync(db);
        var cmd = new ReviseEInvoiceCommand
        { ReviseId = Guid.NewGuid(), OriginalInvoiceId = invoiceId, Type = EInvoiceType.Cancel, Reason = "Hủy" };
        var handler = new ReviseEInvoiceHandler(db, provider);

        await handler.Handle(cmd, default);
        await handler.Handle(cmd, default);

        Assert.Single(provider.ReviseCalls);
        Assert.Equal(2, await db.EInvoices.CountAsync()); // gốc + 1 chứng từ hủy
    }

    [Fact]
    public async Task Revise_RejectsWithoutReason()
    {
        using var db = TestPosDbContext.Create();
        var (invoiceId, provider) = await IssuedInvoiceAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => new ReviseEInvoiceHandler(db, provider).Handle(
            new ReviseEInvoiceCommand { OriginalInvoiceId = invoiceId, Type = EInvoiceType.Adjust, Reason = " " },
            default));
    }
}
