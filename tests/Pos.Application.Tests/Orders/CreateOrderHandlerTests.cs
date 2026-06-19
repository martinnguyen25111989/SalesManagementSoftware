using Pos.Application.Common;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Orders;

public class CreateOrderHandlerTests
{
    private static CreateOrderCommand BaseCommand(Guid variantId, decimal qty = 2m) => new()
    {
        StoreId = TestData.StoreId,
        ShiftId = TestData.ShiftId,
        CashierId = TestData.CashierId,
        Lines = new[] { new CreateOrderLine(variantId, qty) },
    };

    [Fact]
    public async Task Creates_Draft_With_PriceFromList_And_StorePrefixNumber()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, price: 12000m, tax: VatRate.Eight);
        var handler = new CreateOrderHandler(db);

        var result = await handler.Handle(BaseCommand(variantId), default);

        Assert.Equal(OrderStatus.Draft, result.Status);
        Assert.StartsWith("HCM01-", result.OrderNumber);
        Assert.Equal(24000m, result.Subtotal);
        Assert.Equal(1920m, result.TaxTotal);   // 8% của 24.000
        Assert.Equal(25920m, result.GrandTotal);
        Assert.Equal(12000m, result.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task IsIdempotent_OnOrderId_NoDuplicate()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 10000m, VatRate.Ten);
        var handler = new CreateOrderHandler(db);
        var cmd = BaseCommand(variantId) with { OrderId = Guid.NewGuid() };

        var first = await handler.Handle(cmd, default);
        var second = await handler.Handle(cmd, default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Single(db.Orders);
    }

    [Fact]
    public async Task Rejects_When_ShiftClosed()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db, openShift: false);
        var variantId = await TestData.AddProductAsync(db, 10000m, VatRate.Ten);
        var handler = new CreateOrderHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(BaseCommand(variantId), default));
    }

    [Fact]
    public async Task Rejects_When_NoLines()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var handler = new CreateOrderHandler(db);
        var cmd = BaseCommand(Guid.NewGuid()) with { Lines = Array.Empty<CreateOrderLine>() };

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(cmd, default));
    }

    [Fact]
    public async Task UnitPriceOverride_TakesPrecedenceOverPriceList()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Zero);
        var handler = new CreateOrderHandler(db);
        var cmd = BaseCommand(variantId, qty: 1m) with
        {
            Lines = new[] { new CreateOrderLine(variantId, 1m, UnitPriceOverride: 9000m) },
        };

        var result = await handler.Handle(cmd, default);

        Assert.Equal(9000m, result.Lines[0].UnitPrice);
        Assert.Equal(9000m, result.GrandTotal); // thuế 0%
    }
}
