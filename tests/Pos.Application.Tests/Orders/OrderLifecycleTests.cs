using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Orders.HoldOrder;
using Pos.Application.Orders.ResumeOrder;
using Pos.Application.Orders.VoidOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Orders;

public class HoldOrderHandlerTests
{
    [Fact]
    public async Task Holds_Draft_To_OnHold()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);

        var result = await new HoldOrderHandler(db).Handle(new HoldOrderCommand(id), default);

        Assert.Equal(OrderStatus.OnHold, result.Status);
        Assert.Equal(OrderStatus.OnHold, (await db.Orders.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task IsIdempotent_WhenAlreadyOnHold()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);
        var handler = new HoldOrderHandler(db);

        await handler.Handle(new HoldOrderCommand(id), default);
        var second = await handler.Handle(new HoldOrderCommand(id), default);

        Assert.Equal(OrderStatus.OnHold, second.Status);
    }

    [Fact]
    public async Task Rejects_WhenCompleted()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateCompletedOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new HoldOrderHandler(db).Handle(new HoldOrderCommand(id), default));
    }
}

public class ResumeOrderHandlerTests
{
    [Fact]
    public async Task Resumes_OnHold_To_Draft()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);
        await new HoldOrderHandler(db).Handle(new HoldOrderCommand(id), default);

        var result = await new ResumeOrderHandler(db).Handle(new ResumeOrderCommand(id), default);

        Assert.Equal(OrderStatus.Draft, result.Status);
    }

    [Fact]
    public async Task IsIdempotent_WhenAlreadyDraft()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);

        var result = await new ResumeOrderHandler(db).Handle(new ResumeOrderCommand(id), default);

        Assert.Equal(OrderStatus.Draft, result.Status);
    }

    [Fact]
    public async Task Rejects_WhenCompleted()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateCompletedOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new ResumeOrderHandler(db).Handle(new ResumeOrderCommand(id), default));
    }
}

public class VoidOrderHandlerTests
{
    private static VoidOrderCommand Approved(Guid id) =>
        new() { OrderId = id, Reason = "Khách đổi ý", ManagerApproved = true };

    [Fact]
    public async Task Voids_Draft_WithApprovalAndReason()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);

        var result = await new VoidOrderHandler(db).Handle(Approved(id), default);

        Assert.Equal(OrderStatus.Voided, result.Status);
    }

    [Fact]
    public async Task Voids_OnHold()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);
        await new HoldOrderHandler(db).Handle(new HoldOrderCommand(id), default);

        var result = await new VoidOrderHandler(db).Handle(Approved(id), default);

        Assert.Equal(OrderStatus.Voided, result.Status);
    }

    [Fact]
    public async Task Rejects_WhenNotApproved()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new VoidOrderHandler(db).Handle(
                new VoidOrderCommand { OrderId = id, Reason = "x", ManagerApproved = false }, default));
    }

    [Fact]
    public async Task Rejects_WhenNoReason()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new VoidOrderHandler(db).Handle(
                new VoidOrderCommand { OrderId = id, Reason = "  ", ManagerApproved = true }, default));
    }

    [Fact]
    public async Task Rejects_WhenCompleted_MustUseInvoiceCancel()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateCompletedOrderAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new VoidOrderHandler(db).Handle(Approved(id), default));
    }

    [Fact]
    public async Task IsIdempotent_WhenAlreadyVoided()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var id = await TestData.CreateDraftOrderAsync(db);
        var handler = new VoidOrderHandler(db);

        await handler.Handle(Approved(id), default);
        var second = await handler.Handle(Approved(id), default);

        Assert.Equal(OrderStatus.Voided, second.Status);
    }
}
