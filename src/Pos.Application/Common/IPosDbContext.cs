using Microsoft.EntityFrameworkCore;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Operations;
using Pos.Domain.Organization;
using Pos.Domain.Returns;
using Pos.Domain.Sales;

namespace Pos.Application.Common;

/// <summary>
/// Cổng truy cập dữ liệu cho tầng Application (Clean Architecture) — Infrastructure (PosDbContext)
/// hiện thực. Cho phép handler thao tác EF Core mà không phụ thuộc ngược vào Infrastructure.
/// </summary>
public interface IPosDbContext
{
    DbSet<Order> Orders { get; }
    DbSet<OrderLine> OrderLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Variant> Variants { get; }
    DbSet<PriceItem> PriceItems { get; }
    DbSet<Shift> Shifts { get; }
    DbSet<CashMovement> CashMovements { get; }
    DbSet<Register> Registers { get; }
    DbSet<Store> Stores { get; }
    DbSet<StockTransaction> StockTransactions { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<GrnLine> GrnLines { get; }
    DbSet<ReturnOrder> ReturnOrders { get; }
    DbSet<ReturnLine> ReturnLines { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
