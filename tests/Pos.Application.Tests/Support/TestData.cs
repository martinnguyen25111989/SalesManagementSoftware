using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Customers;
using Pos.Domain.Inventory;
using Pos.Domain.Operations;
using Pos.Domain.Organization;

namespace Pos.Application.Tests.Support;

/// <summary>Tiện ích dựng dữ liệu nền cho test handler (chi nhánh, ca, sản phẩm + giá).</summary>
internal static class TestData
{
    public static readonly Guid StoreId = Guid.NewGuid();
    public static readonly Guid Store2Id = Guid.NewGuid();
    public static readonly Guid ShiftId = Guid.NewGuid();
    public static readonly Guid RegisterId = Guid.NewGuid();
    public static readonly Guid CashierId = Guid.NewGuid();
    public static readonly Guid SupplierId = Guid.NewGuid();

    /// <summary>Thêm 1 nhà cung cấp (cho test nhập hàng); trả về SupplierId.</summary>
    public static async Task<Guid> AddSupplierAsync(TestPosDbContext db)
    {
        db.Suppliers.Add(new Supplier { Id = SupplierId, Name = "NCC Demo" });
        await db.SaveChangesAsync();
        return SupplierId;
    }

    /// <summary>Thêm chi nhánh thứ 2 (cho test chuyển kho).</summary>
    public static async Task AddStore2Async(TestPosDbContext db)
    {
        db.Stores.Add(new Store { Id = Store2Id, Name = "Demo 2", OrderPrefix = "HCM02" });
        await db.SaveChangesAsync();
    }

    /// <summary>Seed chi nhánh + máy POS (chưa mở ca) — dùng cho test OpenShift.</summary>
    public static async Task SeedStoreAndRegisterAsync(TestPosDbContext db)
    {
        db.Stores.Add(new Store { Id = StoreId, Name = "Demo", OrderPrefix = "HCM01" });
        db.Registers.Add(new Register { Id = RegisterId, StoreId = StoreId, Name = "Quầy 1" });
        await db.SaveChangesAsync();
    }

    /// <param name="openShift">true = ca đang mở; false = ca đã đóng.</param>
    public static async Task SeedAsync(TestPosDbContext db, bool openShift = true)
    {
        await SeedStoreAndRegisterAsync(db);
        db.Shifts.Add(new Shift
        {
            Id = ShiftId,
            StoreId = StoreId,
            RegisterId = RegisterId,
            CashierId = CashierId,
            OpeningFloat = 500_000m,
            ExpectedCash = 500_000m, // mở ca khởi tạo ExpectedCash = OpeningFloat (B9)
            OpenAt = DateTime.UtcNow.AddHours(-1),
            CloseAt = openShift ? null : DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Thêm 1 sản phẩm + biến thể + giá; trả về VariantId.</summary>
    public static async Task<Guid> AddProductAsync(TestPosDbContext db, decimal price, VatRate tax)
    {
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        db.Products.Add(new Product { Id = productId, Name = "SP", Sku = "SP", TaxRate = tax });
        db.Variants.Add(new Variant { Id = variantId, ProductId = productId, VariantSku = "SP" });
        db.PriceItems.Add(new PriceItem { VariantId = variantId, Price = price });
        await db.SaveChangesAsync();
        return variantId;
    }

    /// <summary>Tạo 1 đơn Draft (1 dòng, thuế 10%) qua handler; trả về OrderId. Yêu cầu đã <see cref="SeedAsync"/>.</summary>
    public static async Task<Guid> CreateDraftOrderAsync(TestPosDbContext db, decimal qty = 1m)
    {
        var variantId = await AddProductAsync(db, 10_000m, VatRate.Ten);
        var r = await new CreateOrderHandler(db).Handle(new CreateOrderCommand
        {
            StoreId = StoreId, ShiftId = ShiftId, CashierId = CashierId,
            Lines = new[] { new CreateOrderLine(variantId, qty) },
        }, default);
        return r.Id;
    }

    /// <summary>Tạo đơn rồi chốt (Completed, tiền mặt) — dùng để test trả hàng / nhánh "đã hoàn tất".</summary>
    public static async Task<Guid> CreateCompletedOrderAsync(TestPosDbContext db, decimal qty = 1m)
    {
        var id = await CreateDraftOrderAsync(db, qty);
        var order = await db.Orders.FindAsync(id);
        await new CheckoutOrderHandler(db).Handle(new CheckoutOrderCommand
        {
            OrderId = id,
            Payments = new[] { new PaymentInput(PaymentMethod.Cash, order!.GrandTotal) },
        }, default);
        return id;
    }

    /// <summary>Thêm 1 khách hàng (B10) với hạn mức tín dụng; trả về CustomerId.</summary>
    public static async Task<Guid> AddCustomerAsync(
        TestPosDbContext db, decimal creditLimit = 0m, string phone = "0900000001")
    {
        var id = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = id, Name = "KH Demo", Phone = phone, CreditLimit = creditLimit });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Bật tích điểm cho chi nhánh: 1 điểm / <paramref name="vndPerPoint"/> đồng doanh thu.</summary>
    public static async Task EnableLoyaltyAsync(TestPosDbContext db, decimal vndPerPoint, bool onGrandTotal = false)
    {
        var store = await db.Stores.FindAsync(StoreId);
        store!.LoyaltyVndPerPoint = vndPerPoint;
        store.LoyaltyEarnOnGrandTotal = onGrandTotal;
        await db.SaveChangesAsync();
    }
}
