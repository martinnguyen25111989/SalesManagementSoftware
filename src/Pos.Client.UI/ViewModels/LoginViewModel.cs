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
/// Màn đăng nhập (B2): xác thực username + mật khẩu qua <see cref="LoginCommand"/>, đặt phiên &amp; phát sự kiện
/// <see cref="Authenticated"/> để vỏ ứng dụng mở khu làm việc theo quyền.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public LoginViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    /// <summary>Phát khi đăng nhập thành công (phiên đã được đặt).</summary>
    public event Action? Authenticated;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public string StoreName => _session.StoreName;

    [RelayCommand]
    private async Task Login()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Nhập tên đăng nhập và mật khẩu.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var auth = await mediator.Send(new LoginCommand(Username, Password));
            _session.SignIn(auth);
            Password = string.Empty;
            Authenticated?.Invoke();
        }
        catch (BusinessRuleException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex) { ErrorMessage = "Lỗi đăng nhập: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
