using Microsoft.EntityFrameworkCore;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Reports;
using Pos.Application.Reports.DebtAging;
using Pos.Application.Reports.InventoryValuation;
using Pos.Application.Reports.PaymentReconciliation;
using Pos.Application.Reports.ProductSales;
using Pos.Application.Reports.SalesSummary;
using Pos.Application.Reports.TaxInvoice;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;
using Pos.Domain.Customers;

namespace Pos.Application.Tests.Reports;

public class ReportsTests
{
    private static ReportFilter Wide() =>
        new(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), StoreId: TestData.StoreId);

    /// <summary>Bán <paramref name="qty"/> SP (giá 10.000, VAT 10%) đã nhập kho giá vốn 6.000; trả về (orderId, variantId, grand).</summary>
    private static async Task<(Guid orderId, Guid variantId, decimal grand)> SellAsync(
        TestPosDbContext db, decimal qty, decimal unitCost = 6_000m)
    {
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);
        await db.SaveChangesAsync();
        // Nhập tồn để có giá vốn bình quân.
        await new Pos.Application.Inventory.ReceiveStock.ReceiveStockHandler(db).Handle(
            new Pos.Application.Inventory.ReceiveStock.ReceiveStockCommand
            {
                StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
                Lines = new[] { new Pos.Application.Inventory.ReceiveStock.ReceiveLine(variantId, qty + 10m, unitCost) },
            }, default);

        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, qty) },
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);
        return (draft.Id, variantId, draft.GrandTotal);
    }

    [Fact]
    public async Task SalesSummary_CountsOrders_AndAverages()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 1m); // grand 11.000
        await SellAsync(db, 2m); // grand 22.000

        var r = await new SalesSummaryHandler(db).Handle(new SalesSummaryQuery(Wide()), default);

        Assert.Equal(2, r.OrderCount);
        Assert.Equal(33_000m, r.GrossSales);
        Assert.Equal(0m, r.Refunds);
        Assert.Equal(16_500m, r.AverageOrderValue);
        Assert.Single(r.Daily);
    }

    [Fact]
    public async Task SalesSummary_ExcludesOutOfRange()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 1m);

        var future = new ReportFilter(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), TestData.StoreId);
        var r = await new SalesSummaryHandler(db).Handle(new SalesSummaryQuery(future), default);

        Assert.Equal(0, r.OrderCount);
        Assert.Equal(0m, r.GrossSales);
    }

    [Fact]
    public async Task ProductSales_ComputesGrossProfit_FromStampedCost()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 2m, unitCost: 6_000m); // doanh thu trước thuế 20.000; giá vốn 2×6.000=12.000

        var rows = await new ProductSalesHandler(db).Handle(new ProductSalesQuery(Wide()), default);

        var row = Assert.Single(rows);
        Assert.Equal(2m, row.QtySold);
        Assert.Equal(20_000m, row.RevenueExVat);
        Assert.Equal(12_000m, row.Cost);
        Assert.Equal(8_000m, row.GrossProfit);
    }

    [Fact]
    public async Task PaymentReconciliation_TotalsByMethod()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 1m); // 11.000 tiền mặt
        await SellAsync(db, 2m); // 22.000 tiền mặt

        var rows = await new PaymentReconciliationHandler(db).Handle(
            new PaymentReconciliationQuery(Wide()), default);

        var cash = Assert.Single(rows, x => x.Method == PaymentMethod.Cash);
        Assert.Equal(33_000m, cash.Gross);
        Assert.Equal(33_000m, cash.Net);
    }

    [Fact]
    public async Task TaxInvoice_TaxByRate_FromSales()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 1m); // taxable 10.000, VAT 10% = 1.000

        var r = await new TaxInvoiceHandler(db).Handle(new TaxInvoiceQuery(Wide()), default);

        Assert.Equal(1_000m, r.TotalTax);
        var rate = Assert.Single(r.TaxByRate);
        Assert.Equal(10m, rate.VatPercent);
        Assert.Equal(10_000m, rate.TaxableAmount);
        Assert.Equal(1_000m, rate.VatAmount);
    }

    [Fact]
    public async Task InventoryValuation_ValuesStockAtAvgCost()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        await SellAsync(db, 2m, unitCost: 6_000m); // nhập 12, bán 2 → tồn 10 × 6.000 = 60.000

        var r = await new InventoryValuationHandler(db).Handle(
            new InventoryValuationQuery(TestData.StoreId), default);

        var row = Assert.Single(r.Items);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal(6_000m, row.AvgCost);
        Assert.Equal(60_000m, r.TotalValue);
    }

    [Fact]
    public async Task DebtAging_BucketsByDueDate()
    {
        using var db = TestPosDbContext.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, Name = "KH Nợ", CreditLimit = 1_000_000m });
        var asOf = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc);
        db.Receivables.Add(new Receivable
        { CustomerId = customerId, Amount = 100m, Outstanding = 100m, DueDate = asOf.AddDays(-10) });   // Current
        db.Receivables.Add(new Receivable
        { CustomerId = customerId, Amount = 200m, Outstanding = 200m, DueDate = asOf.AddDays(-45) });   // 31–60
        db.Receivables.Add(new Receivable
        { CustomerId = customerId, Amount = 400m, Outstanding = 400m, DueDate = asOf.AddDays(-120) });  // >90
        await db.SaveChangesAsync();

        var r = await new DebtAgingHandler(db).Handle(new DebtAgingQuery(asOf), default);

        var c = Assert.Single(r.Customers);
        Assert.Equal(700m, c.Outstanding);
        Assert.Equal(100m, c.Current);
        Assert.Equal(200m, c.Bucket31_60);
        Assert.Equal(400m, c.Over90);
        Assert.Equal(700m, r.TotalOutstanding);
    }
}
