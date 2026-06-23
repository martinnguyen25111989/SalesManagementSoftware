using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Catalog;
using Pos.Domain.Customers;
using Pos.Domain.Inventory;
using Pos.Domain.Invoicing;
using Pos.Domain.Operations;
using Pos.Domain.Organization;
using Pos.Domain.Promotions;
using Pos.Domain.Returns;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Persistence;

public class PosDbContext : DbContext, IPosDbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<Barcode> Barcodes => Set<Barcode>();
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceItem> PriceItems => Set<PriceItem>();

    // Sales
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Operations
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    // Promotions
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();

    // Inventory
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<GrnLine> GrnLines => Set<GrnLine>();

    // Returns
    public DbSet<ReturnOrder> ReturnOrders => Set<ReturnOrder>();
    public DbSet<ReturnLine> ReturnLines => Set<ReturnLine>();

    // Organization
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Register> Registers => Set<Register>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Customers
    public DbSet<CustomerTier> CustomerTiers => Set<CustomerTier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<LoyaltyTxn> LoyaltyTxns => Set<LoyaltyTxn>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();

    // Invoicing
    public DbSet<EInvoice> EInvoices => Set<EInvoice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Tiền: decimal(18,2) mặc định (B13). Số lượng/giá vốn ghi đè 18,3 ở dưới.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Số lượng có thể lẻ (bán theo cân) → 18,3
        b.Entity<OrderLine>().Property(x => x.Qty).HasPrecision(18, 3);
        b.Entity<StockTransaction>().Property(x => x.QtyChange).HasPrecision(18, 3);
        b.Entity<StockBalance>().Property(x => x.Quantity).HasPrecision(18, 3);
        b.Entity<GrnLine>().Property(x => x.Qty).HasPrecision(18, 3);
        b.Entity<ReturnLine>().Property(x => x.Qty).HasPrecision(18, 3);
        b.Entity<UnitConversion>().Property(x => x.FactorToBase).HasPrecision(18, 3);

        // Index nghiệp vụ
        b.Entity<Barcode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Customer>().HasIndex(x => x.Phone).IsUnique();
        b.Entity<StockBalance>().HasIndex(x => new { x.VariantId, x.StoreId }).IsUnique();
        b.Entity<Order>().HasIndex(x => x.OrderNumber);
        b.Entity<Product>().HasIndex(x => x.Sku);
        b.Entity<EInvoice>().HasIndex(x => x.OrderId);

        // Tránh chuỗi cascade-delete vòng trong PostgreSQL → Restrict toàn bộ.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
