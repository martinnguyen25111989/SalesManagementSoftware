using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Common;
using Pos.Client.UI.Services;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Thiết lập lần đầu (B2): khi chưa có tài khoản nào, tạo tài khoản QUẢN TRỊ (vai trò Owner) với mật khẩu thật.
/// Tránh phải seed tài khoản/mật khẩu mặc định trong code. Tạo xong tự đăng nhập &amp; phát <see cref="Authenticated"/>.
/// </summary>
public partial class AccountSetupViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public AccountSetupViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public event Action? Authenticated;

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task Create()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Mật khẩu nhập lại không khớp.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var auth = await mediator.Send(new RegisterUserCommand
            {
                FullName = FullName,
                Username = Username,
                Password = Password,
                RoleName = PosRoles.Owner,
                StoreId = _session.StoreId,
            });
            _session.SignIn(auth);
            Password = string.Empty;
            ConfirmPassword = string.Empty;
            Authenticated?.Invoke();
        }
        catch (BusinessRuleException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex) { ErrorMessage = "Lỗi tạo tài khoản: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
