using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalog.Queries;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Operations;
using Pos.Domain.Organization;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Tests.Sales;

/// <summary>
/// Bán hàng end-to-end trên SQLite thật (đúng provider client offline dùng) — bảo đảm truy vấn
/// (gồm gom giá theo biến thể) dịch được và luồng tạo đơn → chốt đơn chạy đúng B4/B6/B8/B9.
/// EF InMemory không bắt được lỗi dịch SQL nên test này là chốt chặn cho client.
/// </summary>
public class SqliteSalesFlowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PosDbContext> _options;

    private static readonly Guid StoreId = Guid.NewGuid();
    private static readonly Guid RegisterId = Guid.NewGuid();
    private static readonly Guid ShiftId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();

    public SqliteSalesFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open(); // giữ kết nối để DB in-memory tồn tại suốt test
        _options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(_connection).Options;

        using var db = new PosDbContext(_options);
        db.Database.EnsureCreated();
        Seed(db);
    }

    private PosDbContext NewDb() => new(_options);

    private static Guid Seed(PosDbContext db)
    {
        var tenant = new Tenant { Name = "T" };
        db.Tenants.Add(tenant);
        db.Stores.Add(new Store { Id = StoreId, TenantId = tenant.Id, Name = "HCM01", OrderPrefix = "HCM01" });
        db.Registers.Add(new Register { Id = RegisterId, StoreId = StoreId, Name = "Q1" });
        db.Shifts.Add(new Shift
        {
            Id = ShiftId, StoreId = StoreId, RegisterId = RegisterId, CashierId = CashierId,
            OpeningFloat = 500_000m, ExpectedCash = 500_000m, OpenAt = DateTime.UtcNow,
        });

        var category = new Category { Name = "Cà phê" };
        var priceList = new PriceList { Name = "Giá chung", EffectiveFrom = DateTime.UtcNow.AddDays(-1) };
        var product = new Product { Name = "Cà phê sữa", Sku = "CF02", TaxRate = VatRate.Eight, CategoryId = category.Id };
        var variant = new Variant { ProductId = product.Id, VariantSku = "CF02" };
        db.Categories.Add(category);
        db.PriceLists.Add(priceList);
        db.Products.Add(product);
        db.Variants.Add(variant);
        db.PriceItems.Add(new PriceItem { PriceListId = priceList.Id, VariantId = variant.Id, Price = 22_000m });
        db.SaveChanges();
        return variant.Id;
    }

    [Fact]
    public async Task Catalog_TranslatesOnSqlite_AndReturnsPricedActiveProduct()
    {
        await using var db = NewDb();

        var items = await new GetSalesCatalogHandler(db).Handle(new GetSalesCatalogQuery(), default);

        var item = Assert.Single(items);
        Assert.Equal("CF02", item.Sku);
        Assert.Equal(22_000m, item.Price);
        Assert.Equal("Cà phê", item.Category);
    }

    [Fact]
    public async Task CreateThenCheckout_Completes_DeductsStock_AndUpdatesShiftCash()
    {
        Guid variantId;
        await using (var read = NewDb())
            variantId = (await new GetSalesCatalogHandler(read).Handle(new GetSalesCatalogQuery(), default)).Single().VariantId;

        // Tạo đơn (B4/B5/B13): 2 × 22.000, VAT 8% → 47.520.
        OrderResult order;
        await using (var db = NewDb())
        {
            order = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
            {
                StoreId = StoreId, ShiftId = ShiftId, CashierId = CashierId,
                Lines = new[] { new CreateOrderLine(variantId, 2m) },
            }, default);
        }
        Assert.Equal(47_520m, order.GrandTotal);

        // Chốt đơn (B6/B8/B9): tiền mặt đủ.
        await using (var db = NewDb())
        {
            var result = await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
            {
                OrderId = order.Id,
                Payments = new[] { new PaymentInput(PaymentMethod.Cash, order.GrandTotal) },
                CashTendered = 50_000m,
            }, default);

            Assert.Equal(OrderStatus.Completed, result.Status);
            Assert.Equal(50_000m - 47_520m, result.ChangeDue);
        }

        await using (var verify = NewDb())
        {
            var saved = await verify.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Completed, saved.Status);

            var sale = await verify.StockTransactions.SingleAsync(s => s.Type == StockTransactionType.Sale);
            Assert.Equal(-2m, sale.QtyChange); // trừ tồn append-only (B8)

            var shift = await verify.Shifts.FirstAsync(s => s.Id == ShiftId);
            Assert.Equal(500_000m + 47_520m, shift.ExpectedCash); // quỹ tăng đúng phần tiền mặt (B9)

            // Gom ở client: SQLite không hỗ trợ Sum(decimal) trong SQL.
            var payments = await verify.Payments.Where(p => p.OrderId == order.Id).ToListAsync();
            Assert.Equal(order.GrandTotal, payments.Sum(p => p.Amount)); // SUM(Payment)=GrandTotal (B6)
        }
    }

    public void Dispose() => _connection.Dispose();
}
