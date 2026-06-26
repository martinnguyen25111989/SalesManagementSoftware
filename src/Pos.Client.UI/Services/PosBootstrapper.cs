using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Organization;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Persistence.Seed;

namespace Pos.Client.UI.Services;

/// <summary>
/// Khởi tạo nền cục bộ (offline-first): áp schema/migration, nạp cấu hình RBAC hệ thống (5 role + quyền, B2)
/// và đảm bảo CẤU HÌNH tổ chức tối thiểu (chi nhánh / quầy) để hệ thống chạy được. Đặt <see cref="PosSession"/>.
///
/// KHÔNG seed hàng hóa/tồn kho và KHÔNG mở ca ở đây — danh mục nhập qua màn QUẢN TRỊ; ca làm việc (B9)
/// mở sau khi ĐĂNG NHẬP, gắn đúng người dùng. Tên chi nhánh lấy từ biến môi trường (KHÔNG hardcode "Demo").
/// </summary>
public sealed class PosBootstrapper
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public PosBootstrapper(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public async Task RunAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();

        // Postgres (online): áp migration chuẩn; SQLite (offline): tạo schema từ model.
        bool isPostgres = (db.Database.ProviderName ?? string.Empty).Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
        if (isPostgres)
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();
        _session.Backend = isPostgres ? "PostgreSQL (online)" : "SQLite (offline)";

        // RBAC hệ thống (B2) — cấu hình, không phải dữ liệu kinh doanh. Idempotent.
        await DbSeeder.EnsureRolesAndPermissionsAsync(db);

        var (store, register) = await EnsureOrganizationAsync(db);
        _session.StoreId = store.Id;
        _session.StoreName = store.Name;
        _session.RegisterId = register.Id;
    }

    /// <summary>Đảm bảo có 1 chi nhánh + quầy (cấu hình cài đặt). Idempotent. Tên lấy từ biến môi trường.</summary>
    private static async Task<(Store, Register)> EnsureOrganizationAsync(PosDbContext db)
    {
        var store = await db.Stores.FirstOrDefaultAsync();
        if (store is null)
        {
            string Env(string key, string fallback) =>
                Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

            var tenant = new Tenant
            {
                Name = Env("POS_TENANT_NAME", "Cửa hàng bán lẻ"),
                TaxCode = Environment.GetEnvironmentVariable("POS_TAXCODE"),
            };
            store = new Store
            {
                TenantId = tenant.Id,
                Name = Env("POS_STORE_NAME", "Chi nhánh trung tâm"),
                OrderPrefix = Env("POS_ORDER_PREFIX", "CH01"),
                TaxCode = Environment.GetEnvironmentVariable("POS_TAXCODE"),
                Address = Environment.GetEnvironmentVariable("POS_STORE_ADDRESS"),
                InvoiceTemplateCode = Env("POS_INVOICE_TEMPLATE", "1"),
                InvoiceSerial = Env("POS_INVOICE_SERIAL", "C25TAA"),
            };
            db.Tenants.Add(tenant);
            db.Stores.Add(store);
        }

        var register = await db.Registers.FirstOrDefaultAsync(r => r.StoreId == store.Id);
        if (register is null)
        {
            register = new Register { StoreId = store.Id, Name = "Quầy 1", Code = "R1" };
            db.Registers.Add(register);
        }

        await db.SaveChangesAsync();
        return (store, register);
    }
}
