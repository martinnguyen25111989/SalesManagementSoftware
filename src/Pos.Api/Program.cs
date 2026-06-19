using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// EF Core / PostgreSQL. Connection string từ cấu hình (env: ConnectionStrings__Postgres),
// fallback localhost:5433 cho dev (xem docs/Remember.md).
var conn = builder.Configuration.GetConnectionString("Postgres")
           ?? "Host=localhost;Port=5433;Database=posdb;Username=posuser;Password=pospass";
builder.Services.AddDbContext<PosDbContext>(o => o.UseNpgsql(conn));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
