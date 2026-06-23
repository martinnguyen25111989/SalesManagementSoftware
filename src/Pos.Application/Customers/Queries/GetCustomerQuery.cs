using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Customers.UpsertCustomer;

namespace Pos.Application.Customers.Queries;

/// <summary>
/// Tra cứu khách hàng theo SĐT (định danh chính khi bán) — kèm điểm tích lũy &amp; công nợ hiện tại (B10).
/// Trả null nếu không có hồ sơ (khách lẻ).
/// </summary>
public sealed record GetCustomerByPhoneQuery(string Phone) : IRequest<CustomerResult?>;

public sealed class GetCustomerByPhoneHandler : IRequestHandler<GetCustomerByPhoneQuery, CustomerResult?>
{
    private readonly IPosDbContext _db;

    public GetCustomerByPhoneHandler(IPosDbContext db) => _db = db;

    public async Task<CustomerResult?> Handle(GetCustomerByPhoneQuery query, CancellationToken ct)
    {
        var phone = query.Phone?.Trim();
        if (string.IsNullOrEmpty(phone))
            return null;

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == phone, ct);
        if (customer is null)
            return null;

        decimal outstanding = await _db.Receivables
            .Where(r => r.CustomerId == customer.Id)
            .SumAsync(r => r.Outstanding, ct);

        return new CustomerResult(customer.Id, customer.Name, customer.Phone, customer.TaxCode,
            customer.TierId, customer.CreditLimit, customer.PointBalance, outstanding);
    }
}
