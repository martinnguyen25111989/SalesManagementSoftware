using Microsoft.EntityFrameworkCore;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Organization;

namespace Pos.Infrastructure.Persistence.Seed;

/// <summary>
/// Seed dữ liệu hệ thống (5 role + quyền theo B2) và dữ liệu mẫu cho dev/demo.
/// Idempotent: kiểm tra tồn tại trước khi thêm → chạy nhiều lần không tạo trùng.
/// </summary>
public static class DbSeeder
{
    /// <summary>Mã quyền theo hành động (B2 — phân quyền theo hành động, không theo màn hình).</summary>
    public static class Permissions
    {
        public const string OrderCreate = "order.create";
        public const string OrderCheckout = "order.checkout";
        public const string DiscountOverThreshold = "discount.over_threshold";
        public const string OrderVoidPaid = "order.void_paid";
        public const string ReturnRefund = "return.refund";
        public const string PriceOverride = "price.override";
        public const string DrawerOpenStandalone = "drawer.open_standalone";
        public const string ShiftCloseVariance = "shift.close_variance";
        public const string StockAdjust = "stock.adjust";
        public const string ProductManage = "product.manage";
        public const string PriceManage = "price.manage";
        public const string ReportView = "report.view";
        public const string UserManage = "user.manage";

        public static readonly string[] All =
        {
            OrderCreate, OrderCheckout, DiscountOverThreshold, OrderVoidPaid, ReturnRefund,
            PriceOverride, DrawerOpenStandalone, ShiftCloseVariance, StockAdjust,
            ProductManage, PriceManage, ReportView, UserManage,
        };
    }

    /// <summary>5 role mặc định (B2) → tập quyền.</summary>
    private static readonly Dictionary<Guid, (string Name, string[] Perms)> RoleMap = new()
    {
        [SeedIds.RoleOwner] = ("Owner", Permissions.All),
        [SeedIds.RoleManager] = ("Manager", new[]
        {
            Permissions.OrderCreate, Permissions.OrderCheckout, Permissions.DiscountOverThreshold,
            Permissions.OrderVoidPaid, Permissions.ReturnRefund, Permissions.PriceOverride,
            Permissions.DrawerOpenStandalone, Permissions.ShiftCloseVariance, Permissions.StockAdjust,
            Permissions.ProductManage, Permissions.PriceManage, Permissions.ReportView,
        }),
        [SeedIds.RoleCashier] = ("Cashier", new[]
        {
            Permissions.OrderCreate, Permissions.OrderCheckout,
        }),
        [SeedIds.RoleWarehouse] = ("Warehouse", new[]
        {
            Permissions.StockAdjust, Permissions.ProductManage, Permissions.ReportView,
        }),
        [SeedIds.RoleAccountant] = ("Accountant", new[]
        {
            Permissions.ReportView, Permissions.PriceManage,
        }),
    };

    public static async Task SeedAsync(PosDbContext db, CancellationToken ct = default)
    {
        await SeedRolesAndPermissionsAsync(db, ct);
        await SeedSampleDataAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Chỉ nạp cấu hình RBAC hệ thống (5 role + quyền theo B2) — KHÔNG kèm dữ liệu mẫu. Idempotent.
    /// Dùng cho POS client: phân quyền là cấu hình hệ thống, không phải dữ liệu kinh doanh.
    /// </summary>
    public static async Task EnsureRolesAndPermissionsAsync(PosDbContext db, CancellationToken ct = default)
    {
        await SeedRolesAndPermissionsAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAndPermissionsAsync(PosDbContext db, CancellationToken ct)
    {
        // Permissions
        var permByCode = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        foreach (var code in Permissions.All)
        {
            if (permByCode.ContainsKey(code)) continue;
            var perm = new Permission { Code = code };
            db.Permissions.Add(perm);
            permByCode[code] = perm;
        }

        // Roles
        var existingRoleIds = await db.Roles.Select(r => r.Id).ToListAsync(ct);
        foreach (var (roleId, def) in RoleMap)
        {
            if (existingRoleIds.Contains(roleId)) continue;
            db.Roles.Add(new Role { Id = roleId, Name = def.Name });
        }

        // RolePermission mapping (chỉ thêm cặp còn thiếu)
        var existingPairs = await db.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);

        foreach (var (roleId, def) in RoleMap)
        {
            foreach (var code in def.Perms)
            {
                var permId = permByCode[code].Id;
                if (existingPairs.Any(x => x.RoleId == roleId && x.PermissionId == permId)) continue;
                db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permId });
            }
        }
    }

    private static async Task SeedSampleDataAsync(PosDbContext db, CancellationToken ct)
    {
        if (await db.Tenants.AnyAsync(t => t.Id == SeedIds.Tenant, ct))
            return; // dữ liệu mẫu đã có

        db.Tenants.Add(new Tenant
        {
            Id = SeedIds.Tenant,
            Name = "Cửa hàng Demo",
            TaxCode = "0312345678",
        });

        db.Stores.Add(new Store
        {
            Id = SeedIds.Store,
            TenantId = SeedIds.Tenant,
            Name = "Chi nhánh HCM01",
            OrderPrefix = "HCM01",
            TaxCode = "0312345678",
            Address = "123 Lê Lợi, Q1, TP.HCM",
        });

        db.Registers.Add(new Register
        {
            Id = SeedIds.Register,
            StoreId = SeedIds.Store,
            Name = "Quầy 1",
            Code = "REG01",
        });

        // Mật khẩu mẫu: "demo" — chỉ dùng cho dev. KHÔNG dùng plaintext ở thật.
        db.Users.Add(new User
        {
            Id = SeedIds.UserOwner,
            StoreId = SeedIds.Store,
            Username = "owner",
            FullName = "Chủ cửa hàng",
            PasswordHash = DevPasswordHash,
        });
        db.Users.Add(new User
        {
            Id = SeedIds.UserCashier,
            StoreId = SeedIds.Store,
            Username = "cashier",
            FullName = "Thu ngân 1",
            PasswordHash = DevPasswordHash,
        });
        db.UserRoles.Add(new UserRole { UserId = SeedIds.UserOwner, RoleId = SeedIds.RoleOwner });
        db.UserRoles.Add(new UserRole { UserId = SeedIds.UserCashier, RoleId = SeedIds.RoleCashier });

        db.Categories.Add(new Category { Id = SeedIds.CategoryDrinks, Name = "Nước giải khát" });

        AddProduct(db, SeedIds.ProductCoke, SeedIds.VariantCoke, SeedIds.BarcodeCoke,
            "Coca-Cola lon 330ml", "COKE-330", "8934588063017", "Lon");
        AddProduct(db, SeedIds.ProductWater, SeedIds.VariantWater, SeedIds.BarcodeWater,
            "Nước suối Lavie 500ml", "LAVIE-500", "8935049510016", "Chai");

        db.PriceLists.Add(new PriceList
        {
            Id = SeedIds.PriceListDefault,
            Name = "Bảng giá bán lẻ",
            StoreId = null, // áp toàn hệ thống
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.PriceItems.Add(new PriceItem
        {
            Id = SeedIds.PriceItemCoke,
            PriceListId = SeedIds.PriceListDefault,
            VariantId = SeedIds.VariantCoke,
            Price = 12000m,
        });
        db.PriceItems.Add(new PriceItem
        {
            Id = SeedIds.PriceItemWater,
            PriceListId = SeedIds.PriceListDefault,
            VariantId = SeedIds.VariantWater,
            Price = 5000m,
        });
    }

    private static void AddProduct(PosDbContext db, Guid productId, Guid variantId, Guid barcodeId,
        string name, string sku, string barcode, string unit)
    {
        db.Products.Add(new Product
        {
            Id = productId,
            Name = name,
            Sku = sku,
            CategoryId = SeedIds.CategoryDrinks,
            BaseUnit = unit,
            TaxRate = VatRate.Eight, // hàng hóa được giảm còn 8% (cấu hình theo kỳ)
        });
        db.Variants.Add(new Variant
        {
            Id = variantId,
            ProductId = productId,
            VariantSku = sku,
        });
        db.Barcodes.Add(new Barcode
        {
            Id = barcodeId,
            VariantId = variantId,
            Code = barcode,
            Type = BarcodeType.Ean13,
        });
    }

    /// <summary>Hash mẫu cho dev (không phải mật khẩu thật). Thay bằng hash thực khi có Auth.</summary>
    private const string DevPasswordHash = "DEV-ONLY-NOT-A-REAL-HASH";
}
