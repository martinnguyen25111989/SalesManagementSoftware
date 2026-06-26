using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pos.Application;
using Pos.Application.Common;
using Pos.Client.UI.ViewModels;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.Services;

/// <summary>
/// Thành phần DI của ứng dụng bán hàng. Chọn nguồn dữ liệu ĐỘNG: ưu tiên PostgreSQL (online) khi
/// kết nối được; nếu không (mất mạng / chưa bật DB) tự rơi về SQLite cục bộ (offline-first).
/// Chuỗi kết nối Postgres lấy từ biến môi trường <c>ConnectionStrings__Postgres</c>, mặc định cổng 5433
/// (khớp API &amp; deploy). KHÔNG hardcode đường dẫn / KHÔNG commit secret.
/// </summary>
public static class AppServices
{
    public const string DefaultPostgres =
        "Host=localhost;Port=5433;Database=posdb;Username=posuser;Password=pospass";

    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        var pgConn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? DefaultPostgres;
        if (TryConnectPostgres(pgConn))
        {
            services.AddDbContext<PosDbContext>(o => o.UseNpgsql(pgConn));
        }
        else
        {
            // Thư mục dữ liệu chuẩn theo OS (Technical.md 6.2) — KHÔNG hardcode đường dẫn.
            var dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PosSystem");
            Directory.CreateDirectory(dbDir);
            services.AddDbContext<PosDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dbDir, "pos_local.db")}"));
        }

        services.AddScoped<IPosDbContext>(sp => sp.GetRequiredService<PosDbContext>());

        // Handler nghiệp vụ B4–B13 (MediatR) — tái dùng nguyên vẹn ở client.
        services.AddApplication();

        services.AddSingleton<PosSession>();
        services.AddSingleton<PosBootstrapper>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<AccountSetupViewModel>();
        services.AddSingleton<SalesViewModel>();
        services.AddSingleton<ProductsViewModel>();
        services.AddSingleton<ReceiveStockViewModel>();
        services.AddSingleton<InventoryViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<UsersViewModel>();
        services.AddSingleton<ReturnsViewModel>();
        services.AddSingleton<ShiftViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddSingleton<InvoicesViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>Thử mở kết nối Postgres nhanh (timeout ngắn) để quyết định online/offline.</summary>
    private static bool TryConnectPostgres(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 3,
                CommandTimeout = 3,
            };
            using var conn = new NpgsqlConnection(builder.ConnectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
