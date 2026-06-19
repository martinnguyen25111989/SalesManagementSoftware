using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Shifts.CashMovements;
using Pos.Application.Shifts.CloseShift;
using Pos.Application.Shifts.OpenShift;
using Pos.Application.Shifts.Reports;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Tests.Shifts;

public class OpenShiftHandlerTests
{
    private static OpenShiftCommand Cmd(decimal openingFloat = 500_000m) => new()
    {
        StoreId = TestData.StoreId,
        RegisterId = TestData.RegisterId,
        CashierId = TestData.CashierId,
        OpeningFloat = openingFloat,
    };

    [Fact]
    public async Task Opens_WithExpectedCash_EqualToOpeningFloat()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);

        var result = await new OpenShiftHandler(db).Handle(Cmd(), default);

        Assert.Equal(500_000m, result.OpeningFloat);
        Assert.Equal(500_000m, result.ExpectedCash);
        Assert.NotEqual(default, result.OpenAt);
        Assert.Single(db.Shifts);
    }

    [Fact]
    public async Task Rejects_SecondOpen_OnSameRegister()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // đã có ca mở trên RegisterId

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new OpenShiftHandler(db).Handle(Cmd(), default));
    }

    [Fact]
    public async Task IsIdempotent_OnShiftId()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        var cmd = Cmd() with { ShiftId = Guid.NewGuid() };
        var handler = new OpenShiftHandler(db);

        var first = await handler.Handle(cmd, default);
        var second = await handler.Handle(cmd, default);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(db.Shifts);
    }

    [Fact]
    public async Task Rejects_When_RegisterNotFound()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedStoreAndRegisterAsync(db);
        var cmd = Cmd() with { RegisterId = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotFoundException>(
            () => new OpenShiftHandler(db).Handle(cmd, default));
    }
}

public class CashMovementHandlerTests
{
    [Fact]
    public async Task In_Increases_Out_Decreases_ExpectedCash()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // ExpectedCash 500.000
        var handler = new RecordCashMovementHandler(db);

        var afterIn = await handler.Handle(new RecordCashMovementCommand
        {
            ShiftId = TestData.ShiftId, Type = CashMovementType.In, Amount = 100_000m, Reason = "Nộp quỹ",
        }, default);
        Assert.Equal(600_000m, afterIn.ExpectedCash);

        var afterOut = await handler.Handle(new RecordCashMovementCommand
        {
            ShiftId = TestData.ShiftId, Type = CashMovementType.Out, Amount = 50_000m, Reason = "Chi vặt",
        }, default);
        Assert.Equal(550_000m, afterOut.ExpectedCash);
        Assert.Equal(2, db.CashMovements.Count());
    }

    [Fact]
    public async Task Rejects_OnClosedShift()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db, openShift: false);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new RecordCashMovementHandler(db).Handle(new RecordCashMovementCommand
            {
                ShiftId = TestData.ShiftId, Type = CashMovementType.In, Amount = 10_000m,
            }, default));
    }
}

public class CloseShiftHandlerTests
{
    [Fact]
    public async Task Closes_ComputesVariance_ReturnsZReport()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // ExpectedCash 500.000

        var z = await new CloseShiftHandler(db).Handle(new CloseShiftCommand
        {
            ShiftId = TestData.ShiftId, CountedCash = 480_000m, ManagerApproved = true,
        }, default);

        Assert.True(z.IsClosed);
        Assert.NotNull(z.CloseAt);
        Assert.Equal(480_000m, z.CountedCash);
        Assert.Equal(-20_000m, z.Variance);
    }

    [Fact]
    public async Task Blocks_When_OnHoldOrdersExist()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        db.Orders.Add(new Order
        {
            ShiftId = TestData.ShiftId, StoreId = TestData.StoreId,
            OrderNumber = "HCM01-000001", Status = OrderStatus.OnHold, GrandTotal = 1000m,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CloseShiftHandler(db).Handle(new CloseShiftCommand
            {
                ShiftId = TestData.ShiftId, CountedCash = 500_000m,
            }, default));
    }

    [Fact]
    public async Task VarianceOverThreshold_RequiresManagerApproval()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var handler = new CloseShiftHandler(db);
        var counted = 400_000m; // lệch −100.000 > ngưỡng 50.000

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new CloseShiftCommand { ShiftId = TestData.ShiftId, CountedCash = counted }, default));

        var z = await handler.Handle(new CloseShiftCommand
        {
            ShiftId = TestData.ShiftId, CountedCash = counted, ManagerApproved = true,
        }, default);
        Assert.Equal(-100_000m, z.Variance);
    }

    [Fact]
    public async Task IsIdempotent_AlreadyClosed_ReturnsZReport()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db, openShift: false);

        var z = await new CloseShiftHandler(db).Handle(new CloseShiftCommand
        {
            ShiftId = TestData.ShiftId, CountedCash = 123m,
        }, default);

        Assert.True(z.IsClosed);
    }
}

public class ShiftReportTests
{
    [Fact]
    public async Task XReport_Reflects_CashSale_WhileOpen()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // ExpectedCash 500.000, ca mở
        var variantId = await TestData.AddProductAsync(db, 12000m, VatRate.Eight);

        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 2m) },
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        var x = await new GetShiftReportHandler(db).Handle(new GetShiftReportQuery(TestData.ShiftId), default);

        Assert.False(x.IsClosed);
        Assert.Null(x.CountedCash);
        Assert.Equal(1, x.OrderCount);
        Assert.Equal(25920m, x.CashSales);
        Assert.Equal(25920m, x.PaymentsByMethod[PaymentMethod.Cash]);
        Assert.Equal(525_920m, x.ExpectedCash); // 500.000 + 25.920
        Assert.Equal(25920m, x.GrandTotalSales);
    }
}
