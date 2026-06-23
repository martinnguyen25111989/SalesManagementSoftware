using MediatR;

namespace Pos.Application.Customers.UpsertCustomer;

/// <summary>
/// Tạo / cập nhật hồ sơ khách hàng (B10). Idempotent &amp; upsert theo <see cref="CustomerId"/>.
/// SĐT là định danh chính ở VN → không trùng giữa các khách. Khách lẻ không cần hồ sơ (bán bình thường).
/// </summary>
public sealed record UpsertCustomerCommand : IRequest<CustomerResult>
{
    public Guid CustomerId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? TaxCode { get; init; }
    public Guid? TierId { get; init; }
    public decimal CreditLimit { get; init; }
}

public sealed record CustomerResult(
    Guid Id, string Name, string? Phone, string? TaxCode, Guid? TierId,
    decimal CreditLimit, decimal PointBalance, decimal Outstanding);
