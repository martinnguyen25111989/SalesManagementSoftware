using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory.Suppliers;

/// <summary>Tạo / cập nhật nhà cung cấp (B8 — nhập hàng cần NCC). Idempotent theo <see cref="SupplierId"/>.</summary>
public sealed record UpsertSupplierCommand : IRequest<SupplierItem>
{
    public Guid SupplierId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? TaxCode { get; init; }
    public string? Address { get; init; }
}

public sealed record SupplierItem(Guid Id, string Name, string? Phone, string? TaxCode, string? Address);

public sealed class UpsertSupplierHandler : IRequestHandler<UpsertSupplierCommand, SupplierItem>
{
    private readonly IPosDbContext _db;

    public UpsertSupplierHandler(IPosDbContext db) => _db = db;

    public async Task<SupplierItem> Handle(UpsertSupplierCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new BusinessRuleException("Nhà cung cấp phải có tên.");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == cmd.SupplierId, ct);
        if (supplier is null)
        {
            supplier = new Supplier { Id = cmd.SupplierId };
            _db.Suppliers.Add(supplier);
        }
        else
        {
            supplier.MarkModified();
        }

        supplier.Name = cmd.Name.Trim();
        supplier.Phone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : cmd.Phone.Trim();
        supplier.TaxCode = string.IsNullOrWhiteSpace(cmd.TaxCode) ? null : cmd.TaxCode.Trim();
        supplier.Address = string.IsNullOrWhiteSpace(cmd.Address) ? null : cmd.Address.Trim();

        await _db.SaveChangesAsync(ct);
        return new SupplierItem(supplier.Id, supplier.Name, supplier.Phone, supplier.TaxCode, supplier.Address);
    }
}

/// <summary>Danh sách nhà cung cấp (cho ô chọn ở màn hình nhập hàng).</summary>
public sealed record GetSuppliersQuery : IRequest<IReadOnlyList<SupplierItem>>;

public sealed class GetSuppliersHandler : IRequestHandler<GetSuppliersQuery, IReadOnlyList<SupplierItem>>
{
    private readonly IPosDbContext _db;

    public GetSuppliersHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupplierItem>> Handle(GetSuppliersQuery q, CancellationToken ct) =>
        await _db.Suppliers.OrderBy(s => s.Name)
            .Select(s => new SupplierItem(s.Id, s.Name, s.Phone, s.TaxCode, s.Address))
            .ToListAsync(ct);
}
