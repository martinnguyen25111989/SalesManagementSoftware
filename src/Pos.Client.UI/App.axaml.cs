using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.UI.Services;
using Pos.Client.UI.ViewModels;
using Pos.Client.UI.Views;

namespace Pos.Client.UI;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Build();
            var mainVm = services.GetRequiredService<MainWindowViewModel>();

            // Khởi tạo nền (migration/RBAC/chi nhánh — B2) rồi chọn màn cổng (đăng nhập / thiết lập lần đầu).
            // Chạy qua Task.Run để KHÔNG chặn luồng UI: ở đây SynchronizationContext của Avalonia đã được
            // cài, nếu .GetResult() trực tiếp thì continuation sau await (DB bất đồng bộ với Postgres) bị post
            // ngược về luồng UI đang bị chặn → deadlock (cửa sổ không bao giờ hiện). Bọc Task.Run cho continuation
            // resume trên thread pool.
            Task.Run(async () =>
            {
                await services.GetRequiredService<PosBootstrapper>().RunAsync();
                await mainVm.StartAsync();
            }).GetAwaiter().GetResult();

            desktop.MainWindow = new MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
