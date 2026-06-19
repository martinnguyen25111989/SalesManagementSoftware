using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Operations;
using Pos.Domain.Organization;
using Pos.Domain.Returns;
using Pos.Domain.Sales;

namespace Pos.Application.Tests.Support;

/// <summary>
/// DbContext tối giản (EF InMemory) hiện thực <see cref="IPosDbContext"/> để test handler
/// mà không cần Infrastructure/PostgreSQL. Giữ tách tầng Application sạch.
/// </summary>
public sealed class TestPosDbContext : DbContext, IPosDbContext
{
    public TestPosDbContext(DbContextOptions<TestPosDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceItem> PriceItems => Set<PriceItem>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Register> Registers => Set<Register>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<GrnLine> GrnLines => Set<GrnLine>();
    public DbSet<ReturnOrder> ReturnOrders => Set<ReturnOrder>();
    public DbSet<ReturnLine> ReturnLines => Set<ReturnLine>();

    public static TestPosDbContext Create() =>
        new(new DbContextOptionsBuilder<TestPosDbContext>()
            .UseInMemoryDatabase($"pos-test-{Guid.NewGuid()}")
            .Options);
}
