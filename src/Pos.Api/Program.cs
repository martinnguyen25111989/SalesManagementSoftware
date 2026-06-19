using Microsoft.EntityFrameworkCore;
using Pos.Application;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// EF Core / PostgreSQL. Connection string từ cấu hình (env: ConnectionStrings__Postgres),
// fallback localhost:5433 cho dev (xem docs/Remember.md).
var conn = builder.Configuration.GetConnectionString("Postgres")
           ?? "Host=localhost;Port=5433;Database=posdb;Username=posuser;Password=pospass";
builder.Services.AddDbContext<PosDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddScoped<IPosDbContext>(sp => sp.GetRequiredService<PosDbContext>());

// CQRS (MediatR) handlers của tầng Application.
builder.Services.AddApplication();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Seed role/permission mặc định (B2) + dữ liệu mẫu khi dev.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
