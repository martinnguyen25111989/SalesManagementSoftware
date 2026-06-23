using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pos.Application.Invoicing.Abstractions;

namespace Pos.Application;

public static class DependencyInjection
{
    /// <summary>Đăng ký MediatR (CQRS handlers) của tầng Application.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        // B11: NCC HĐĐT mặc định = Null (offline) — adapter thật (EasyInvoice) đăng ký ở Infrastructure
        // giai đoạn sau sẽ ghi đè qua DI. TryAdd để không đè nếu đã có adapter thật.
        services.TryAddScoped<IEInvoiceProvider, NullEInvoiceProvider>();
        return services;
    }
}
