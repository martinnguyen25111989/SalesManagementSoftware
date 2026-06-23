using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Customers.Queries;
using Pos.Application.Customers.RecordDebtPayment;
using Pos.Application.Customers.UpsertCustomer;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Returns.CreateReturn;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Customers;

public class UpsertCustomerTests
{
    [Fact]
    public async Task Creates_And_IsUpsertById()
    {
        using var db = TestPosDbContext.Create();
        var id = Guid.NewGuid();
        var handler = new UpsertCustomerHandler(db);

        await handler.Handle(new UpsertCustomerCommand
        { CustomerId = id, Name = "An", Phone = "0901", CreditLimit = 50_000m }, default);
        var r = await handler.Handle(new UpsertCustomerCommand
        { CustomerId = id, Name = "An Updated", Phone = "0901", CreditLimit = 80_000m }, default);

        Assert.Equal("An Updated", r.Name);
        Assert.Equal(80_000m, r.CreditLimit);
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Rejects_DuplicatePhone_OnDifferentCustomer()
    {
        using var db = TestPosDbContext.Create();
        var handler = new UpsertCustomerHandler(db);
        await handler.Handle(new UpsertCustomerCommand { Name = "A", Phone = "0901" }, default);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new UpsertCustomerCommand { Name = "B", Phone = "0901" }, default));
    }

    [Fact]
    public async Task Lookup_ByPhone_ReturnsPointsAndOutstanding()
    {
        using var db = TestPosDbContext.Create();
        await new UpsertCustomerHandler(db).Handle(new UpsertCustomerCommand
        { Name = "A", Phone = "0901", CreditLimit = 100_000m }, default);

        var r = await new GetCustomerByPhoneHandler(db).Handle(new GetCustomerByPhoneQuery("0901"), default);

        Assert.NotNull(r);
        Assert.Equal(0m, r!.PointBalance);
        Assert.Equal(0m, r.Outstanding);
        Assert.Null(await new GetCustomerByPhoneHandler(db).Handle(new GetCustomerByPhoneQuery("0999"), default));
    }
}

public class LoyaltyTests
{
    /// <summary>Đơn Draft gắn khách, thuế 10% (price 10.000). Trả về (orderId, grandTotal).</summary>
    private static async Task<(Guid orderId, decimal grand)> DraftForCustomerAsync(
        TestPosDbContext db, Guid customerId, decimal qty)
    {
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);
        var r = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            CustomerId = customerId, Lines = new[] { new CreateOrderLine(variantId, qty) },
        }, default);
        return (r.Id, r.GrandTotal);
    }

    [Fact]
    public async Task Accrues_OnPreTaxRevenue_AfterCheckout()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.EnableLoyaltyAsync(db, vndPerPoint: 1_000m); // 1 điểm / 1.000đ doanh thu trước thuế
        var customerId = await TestData.AddCustomerAsync(db);
        var (orderId, grand) = await DraftForCustomerAsync(db, customerId, qty: 1m); // net 10.000 → 10 điểm

        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Cash, grand) },
        }, default);

        Assert.Equal(10m, (await db.Customers.FindAsync(customerId))!.PointBalance);
        Assert.Equal(10, await db.LoyaltyTxns.Where(t => t.OrderId == orderId).SumAsync(t => t.PointChange));
    }

    [Fact]
    public async Task PartialReturn_RevokesProportionalPoints_NotNegative()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.EnableLoyaltyAsync(db, vndPerPoint: 1_000m);
        var customerId = await TestData.AddCustomerAsync(db);
        var (orderId, grand) = await DraftForCustomerAsync(db, customerId, qty: 2m); // net 20.000 → 20 điểm
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Cash, grand) },
        }, default);
        var line = (await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId)).Lines.First();

        // Trả 1/2 → hoàn nửa tiền → thu hồi 10/20 điểm.
        await new CreateReturnHandler(db).Handle(new CreateReturnCommand
        {
            OriginalOrderId = orderId, ShiftId = TestData.ShiftId, Reason = "Trả", ManagerApproved = true,
            RefundMethod = PaymentMethod.Cash, Lines = new[] { new ReturnLineInput(line.Id, 1m) },
        }, default);

        Assert.Equal(10m, (await db.Customers.FindAsync(customerId))!.PointBalance);
        Assert.Equal(10, await db.LoyaltyTxns.Where(t => t.OrderId == orderId).SumAsync(t => t.PointChange));
    }

    [Fact]
    public async Task NoCustomer_OrDisabled_AccruesNothing()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db); // loyalty tắt mặc định
        var orderId = await TestData.CreateDraftOrderAsync(db); // khách lẻ
        var order = await db.Orders.FindAsync(orderId);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Cash, order!.GrandTotal) },
        }, default);

        Assert.Empty(db.LoyaltyTxns);
    }
}

public class DebtSaleTests
{
    private static async Task<(Guid orderId, decimal grand)> DraftForCustomerAsync(TestPosDbContext db, Guid customerId)
    {
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);
        var r = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            CustomerId = customerId, Lines = new[] { new CreateOrderLine(variantId, 1m) },
        }, default);
        return (r.Id, r.GrandTotal); // 11.000
    }

    [Fact]
    public async Task Debt_WithinLimit_CreatesReceivable_OrderUnpaid()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var customerId = await TestData.AddCustomerAsync(db, creditLimit: 100_000m);
        var (orderId, grand) = await DraftForCustomerAsync(db, customerId);

        var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Debt, grand) },
        }, default);

        Assert.Equal(PaymentStatus.Unpaid, result.PaymentStatus);
        var rec = await db.Receivables.SingleAsync();
        Assert.Equal(grand, rec.Outstanding);
        Assert.Equal(orderId, rec.OrderId);
    }

    [Fact]
    public async Task Debt_OverLimit_BlockedWithoutManager_AllowedWith()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var customerId = await TestData.AddCustomerAsync(db, creditLimit: 5_000m); // < 11.000
        var (orderId, grand) = await DraftForCustomerAsync(db, customerId);
        var handler = new CheckoutOrderHandler(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Debt, grand) },
        }, default));

        var ok = await handler.Handle(new CheckoutOrderCommand
        {
            OrderId = orderId, ManagerApproved = true,
            Payments = new[] { new PaymentInput(PaymentMethod.Debt, grand) },
        }, default);
        Assert.Equal(PaymentStatus.Unpaid, ok.PaymentStatus);
    }

    [Fact]
    public async Task Debt_WithoutCustomer_Rejected()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateDraftOrderAsync(db); // không gắn khách
        var order = await db.Orders.FindAsync(orderId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => new CheckoutOrderHandler(db).Handle(
            new CheckoutOrderCommand
            {
                OrderId = orderId, Payments = new[] { new PaymentInput(PaymentMethod.Debt, order!.GrandTotal) },
            }, default));
    }
}

public class DebtPaymentTests
{
    private static async Task<Guid> CustomerWithDebtAsync(TestPosDbContext db, decimal grand)
    {
        await TestData.SeedAsync(db);
        var customerId = await TestData.AddCustomerAsync(db, creditLimit: 100_000m);
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);
        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            CustomerId = customerId, Lines = new[] { new CreateOrderLine(variantId, 1m) },
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Debt, draft.GrandTotal) },
        }, default);
        return customerId;
    }

    [Fact]
    public async Task Repay_ReducesOutstanding_AndAddsCashToShift()
    {
        using var db = TestPosDbContext.Create();
        var customerId = await CustomerWithDebtAsync(db, 11_000m);
        var shiftCashBefore = (await db.Shifts.FindAsync(TestData.ShiftId))!.ExpectedCash;

        var r = await new RecordDebtPaymentHandler(db).Handle(new RecordDebtPaymentCommand
        {
            CustomerId = customerId, Amount = 4_000m, Method = PaymentMethod.Cash,
            ShiftId = TestData.ShiftId, StoreId = TestData.StoreId,
        }, default);

        Assert.Equal(7_000m, r.OutstandingAfter);
        Assert.Equal(7_000m, await db.Receivables.SumAsync(x => x.Outstanding));
        Assert.Equal(shiftCashBefore + 4_000m, (await db.Shifts.FindAsync(TestData.ShiftId))!.ExpectedCash);
    }

    [Fact]
    public async Task Repay_Overpay_Rejected()
    {
        using var db = TestPosDbContext.Create();
        var customerId = await CustomerWithDebtAsync(db, 11_000m);

        await Assert.ThrowsAsync<BusinessRuleException>(() => new RecordDebtPaymentHandler(db).Handle(
            new RecordDebtPaymentCommand
            {
                CustomerId = customerId, Amount = 20_000m, Method = PaymentMethod.Cash,
                ShiftId = TestData.ShiftId, StoreId = TestData.StoreId,
            }, default));
    }

    [Fact]
    public async Task Repay_IsIdempotent()
    {
        using var db = TestPosDbContext.Create();
        var customerId = await CustomerWithDebtAsync(db, 11_000m);
        var cmd = new RecordDebtPaymentCommand
        {
            PaymentId = Guid.NewGuid(), CustomerId = customerId, Amount = 4_000m,
            Method = PaymentMethod.Cash, ShiftId = TestData.ShiftId, StoreId = TestData.StoreId,
        };
        var handler = new RecordDebtPaymentHandler(db);

        await handler.Handle(cmd, default);
        await handler.Handle(cmd, default);

        Assert.Equal(7_000m, await db.Receivables.SumAsync(x => x.Outstanding)); // không trừ hai lần
        Assert.Equal(1, await db.DebtPayments.CountAsync());
    }
}
