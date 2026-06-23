using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Customers;

namespace Pos.Application.Customers.UpsertCustomer;

public sealed class UpsertCustomerHandler : IRequestHandler<UpsertCustomerCommand, CustomerResult>
{
    private readonly IPosDbContext _db;

    public UpsertCustomerHandler(IPosDbContext db) => _db = db;

    public async Task<CustomerResult> Handle(UpsertCustomerCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new BusinessRuleException("Khách hàng phải có tên.");
        if (cmd.CreditLimit < 0m)
            throw new BusinessRuleException("Hạn mức tín dụng không được âm.");

        var phone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : cmd.Phone.Trim();

        // SĐT là định danh chính ở VN → không cho trùng (trừ chính khách này).
        if (phone is not null)
        {
            bool dup = await _db.Customers.AnyAsync(c => c.Phone == phone && c.Id != cmd.CustomerId, ct);
            if (dup)
                throw new BusinessRuleException($"SĐT {phone} đã thuộc về khách hàng khác.");
        }

        if (cmd.TierId is { } tierId && !await _db.CustomerTiers.AnyAsync(t => t.Id == tierId, ct))
            throw new NotFoundException($"Không tìm thấy hạng thành viên {tierId}.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cmd.CustomerId, ct);
        if (customer is null)
        {
            customer = new Customer { Id = cmd.CustomerId };
            _db.Customers.Add(customer);
        }
        else
        {
            customer.MarkModified();
        }

        customer.Name = cmd.Name.Trim();
        customer.Phone = phone;
        customer.TaxCode = string.IsNullOrWhiteSpace(cmd.TaxCode) ? null : cmd.TaxCode.Trim();
        customer.TierId = cmd.TierId;
        customer.CreditLimit = cmd.CreditLimit;

        await _db.SaveChangesAsync(ct);

        decimal outstanding = await _db.Receivables
            .Where(r => r.CustomerId == customer.Id)
            .SumAsync(r => r.Outstanding, ct);

        return new CustomerResult(customer.Id, customer.Name, customer.Phone, customer.TaxCode,
            customer.TierId, customer.CreditLimit, customer.PointBalance, outstanding);
    }
}
