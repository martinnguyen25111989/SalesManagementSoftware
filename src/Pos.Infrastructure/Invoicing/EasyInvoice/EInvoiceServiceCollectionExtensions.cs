using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pos.Application.Invoicing.Abstractions;

namespace Pos.Infrastructure.Invoicing.EasyInvoice;

public static class EInvoiceServiceCollectionExtensions
{
    /// <summary>
    /// Cắm adapter HĐĐT EasyInvoice (B11-A) khi đã cấu hình (section "EInvoice"). Ghi đè
    /// <c>NullEInvoiceProvider</c> mặc định. Nếu chưa cấu hình (chưa cài máy tính tiền kết nối thuế) →
    /// giữ Null provider (offline, đơn vào hàng đợi Pending).
    /// </summary>
    public static IServiceCollection AddEasyInvoice(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(EInvoiceOptions.SectionName);
        services.Configure<EInvoiceOptions>(section);

        var opt = section.Get<EInvoiceOptions>() ?? new EInvoiceOptions();
        if (!opt.IsConfigured)
            return services; // chưa cấu hình → dùng Null provider (giai đoạn sau mới bật)

        services.AddSingleton<EasyInvoiceTokenCache>();
        services.AddHttpClient<EasyInvoiceProvider>(c => c.BaseAddress = new Uri(opt.BaseUrl));

        // Ghi đè Null provider bằng adapter thật.
        services.RemoveAll<IEInvoiceProvider>();
        services.AddScoped<IEInvoiceProvider>(sp => sp.GetRequiredService<EasyInvoiceProvider>());
        return services;
    }
}
