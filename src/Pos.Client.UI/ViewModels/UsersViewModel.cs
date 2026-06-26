using System;
using System.Collections.ObjectModel;
using System.Linq;
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
/// Màn hình QUẢN TRỊ — Người dùng &amp; phân quyền (B2): liệt kê tài khoản kèm vai trò, tạo tài khoản mới với
/// 1 vai trò (Owner/Manager/Cashier/Warehouse/Accountant) và bật/tắt hoạt động. Quyền theo VAI TRÒ —
/// chọn vai trò là chọn tập quyền (RBAC). Chỉ người có quyền <c>user.manage</c> mới vào được màn này.
/// </summary>
public partial class UsersViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public UsersViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    /// <summary>Thiết kế-thời (previewer).</summary>
    public UsersViewModel() : this(null!, null!) { }

    public ObservableCollection<UserListItem> Users { get; } = new();
    public ObservableCollection<string> Roles { get; } = new();

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _selectedRole;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public async Task ActivateAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var users = await mediator.Send(new GetUsersQuery());
        Users.Clear();
        foreach (var u in users) Users.Add(u);

        if (Roles.Count == 0)
        {
            var roles = await mediator.Send(new GetRolesQuery());
            foreach (var r in roles) Roles.Add(r);
            SelectedRole ??= Roles.Contains(PosRoles.Cashier) ? PosRoles.Cashier : Roles.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedRole))
                throw new BusinessRuleException("Phải chọn vai trò.");

            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var auth = await mediator.Send(new RegisterUserCommand
            {
                FullName = FullName,
                Username = Username,
                Password = Password,
                RoleName = SelectedRole!,
                StoreId = _session.StoreId,
            });

            StatusMessage = $"✓ Đã tạo '{auth.FullName}' ({SelectedRole}).";
            ResetForm();
            await ReloadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleActive(UserListItem? user)
    {
        if (user is null || IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new SetUserActiveCommand(user.Id, !user.IsActive));
            await ReloadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ResetForm()
    {
        FullName = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        SelectedRole = Roles.Contains(PosRoles.Cashier) ? PosRoles.Cashier : Roles.FirstOrDefault();
    }
}
