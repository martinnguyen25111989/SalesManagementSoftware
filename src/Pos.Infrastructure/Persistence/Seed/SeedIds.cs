namespace Pos.Infrastructure.Persistence.Seed;

/// <summary>
/// GUID cố định cho dữ liệu seed (deterministic) — để seeder chạy idempotent,
/// re-run không tạo trùng và demo/test tham chiếu được ID ổn định.
/// Quy ước: PK là GUID sinh ở client (CLAUDE.md); ở đây cố định cho dữ liệu hệ thống/mẫu.
/// </summary>
public static class SeedIds
{
    // Roles (B2 — 5 role mặc định)
    public static readonly Guid RoleOwner = new("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid RoleManager = new("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid RoleCashier = new("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid RoleWarehouse = new("a1000000-0000-0000-0000-000000000004");
    public static readonly Guid RoleAccountant = new("a1000000-0000-0000-0000-000000000005");

    // Tổ chức mẫu
    public static readonly Guid Tenant = new("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid Store = new("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid Register = new("b1000000-0000-0000-0000-000000000003");
    public static readonly Guid UserOwner = new("b1000000-0000-0000-0000-000000000004");
    public static readonly Guid UserCashier = new("b1000000-0000-0000-0000-000000000005");

    // Catalog mẫu
    public static readonly Guid CategoryDrinks = new("c1000000-0000-0000-0000-000000000001");
    public static readonly Guid ProductCoke = new("c1000000-0000-0000-0000-000000000002");
    public static readonly Guid VariantCoke = new("c1000000-0000-0000-0000-000000000003");
    public static readonly Guid BarcodeCoke = new("c1000000-0000-0000-0000-000000000004");
    public static readonly Guid ProductWater = new("c1000000-0000-0000-0000-000000000005");
    public static readonly Guid VariantWater = new("c1000000-0000-0000-0000-000000000006");
    public static readonly Guid BarcodeWater = new("c1000000-0000-0000-0000-000000000007");

    public static readonly Guid PriceListDefault = new("d1000000-0000-0000-0000-000000000001");
    public static readonly Guid PriceItemCoke = new("d1000000-0000-0000-0000-000000000002");
    public static readonly Guid PriceItemWater = new("d1000000-0000-0000-0000-000000000003");
}
