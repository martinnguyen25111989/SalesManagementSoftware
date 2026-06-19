using Microsoft.Extensions.DependencyInjection;

namespace Pos.Application;

public static class DependencyInjection
{
    /// <summary>Đăng ký MediatR (CQRS handlers) của tầng Application.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        return services;
    }
}
