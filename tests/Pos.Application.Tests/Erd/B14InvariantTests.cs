using Microsoft.EntityFrameworkCore;
using Pos.Application.Inventory.Queries;
using Pos.Application.Inventory.ReceiveStock;
using Pos.Application.Invoicing.IssueEInvoice;
using Pos.Application.Invoicing.Revise;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Erd;

/// <summary>B14.6 — bất biến hành vi của mô hình dữ liệu (kiểm bằng luồng thật).</summary>
public class B14InvariantTests
{
    [Fact]
    public async Task B14_6_Stock_IsAppendOnly_LedgerSumEqualsSnapshot()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        await TestData.AddSupplierAsync(db);
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);

        await new ReceiveStockHandler(db).Handle(new ReceiveStockCommand
        {
            StoreId = TestData.StoreId, SupplierId = TestData.SupplierId,
            Lines = new[] { new ReceiveLine(variantId, 10m, 6_000m) },
        }, default);

        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 3m) },
        }, default);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id, Payments = new[] { new PaymentInput(PaymentMethod.Cash, draft.GrandTotal) },
        }, default);

        // Append-only: 2 bản ghi biến động (nhập + bán), không sửa 1 dòng tồn.
        Assert.Equal(2, await db.StockTransactions.CountAsync(s => s.VariantId == variantId));

        // Nguồn sự thật = SUM(QtyChange); snapshot phải khớp.
        var handler = new GetStockOnHandHandler(db);
        var snapshot = (await handler.Handle(new GetStockOnHandQuery(TestData.StoreId, variantId), default)).Single();
        var ledger = (await handler.Handle(new GetStockOnHandQuery(TestData.StoreId, variantId, FromLedger: true), default)).Single();
        Assert.Equal(7m, snapshot.OnHand);
        Assert.Equal(snapshot.OnHand, ledger.OnHand);
    }

    [Fact]
    public async Task B14_6_Payments_SumEquals_GrandTotal()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 12_000m, VatRate.Eight);
        var draft = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = TestData.StoreId, ShiftId = TestData.ShiftId, CashierId = TestData.CashierId,
            Lines = new[] { new CreateOrderLine(variantId, 2m) }, // GrandTotal 25.920
        }, default);

        // Thanh toán hỗn hợp (B14.6: PAYMENT là bảng con nhiều dòng, SUM = GrandTotal).
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = draft.Id,
            Payments = new[]
            {
                new PaymentInput(PaymentMethod.Cash, 20_000m),
                new PaymentInput(PaymentMethod.Card, 5_920m, "AUTH-1"),
            },
        }, default);

        var payments = await db.Payments.Where(p => p.OrderId == draft.Id).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.Equal(draft.GrandTotal, payments.Sum(p => p.Amount));
    }

    [Fact]
    public async Task B14_6_OneOrder_ManyEInvoiceDocs_ChainedByOriginal()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var orderId = await TestData.CreateCompletedOrderAsync(db);
        var provider = new FakeEInvoiceProvider();

        var issued = await new IssueEInvoiceHandler(db, provider)
            .Handle(new IssueEInvoiceCommand { OrderId = orderId }, default);
        await new ReviseEInvoiceHandler(db, provider).Handle(new ReviseEInvoiceCommand
        {
            OriginalInvoiceId = issued.EInvoiceId, Type = EInvoiceType.Cancel, Reason = "Hủy",
        }, default);

        // 1 đơn → nhiều chứng từ HĐĐT, liên kết chuỗi qua OriginalInvoiceId.
        var docs = await db.EInvoices.Where(e => e.OrderId == orderId).ToListAsync();
        Assert.Equal(2, docs.Count);
        var cancelDoc = docs.Single(e => e.Type == EInvoiceType.Cancel);
        Assert.Equal(issued.EInvoiceId, cancelDoc.OriginalInvoiceId);
    }
}
