using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Infrastructure.Persistence;

/// <summary>
/// Factory cho design-time (dotnet ef migrations/update). Dùng connection string local
/// (Postgres host port 5433 — xem docs/Remember.md). Runtime dùng cấu hình ở Pos.Api.
/// </summary>
public class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5433;Database=posdb;Username=posuser;Password=pospass";

        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new PosDbContext(options);
    }
}
