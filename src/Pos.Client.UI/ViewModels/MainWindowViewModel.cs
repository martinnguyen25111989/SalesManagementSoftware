using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Shifts.OpenShift;
using Pos.Client.UI.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Vỏ ứng dụng có CỔNG ĐĂNG NHẬP (B2): trước khi xác thực chỉ hiện màn Đăng nhập / Thiết lập tài khoản;
/// sau khi đăng nhập mới mở ca (B9) và hiện khu làm việc. Nav khu QUẢN TRỊ chỉ hiện khi người dùng có quyền.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public MainWindowViewModel(
        IServiceScopeFactory scopes,
        PosSession session,
        LoginViewModel login,
        AccountSetupViewModel accountSetup,
        SalesViewModel sales,
        ProductsViewModel products,
        ReceiveStockViewModel receive,
        InventoryViewModel inventory,
        ReportsViewModel reports,
        UsersViewModel users,
        ReturnsViewModel returns,
        ShiftViewModel shift,
        CustomersViewModel customers,
        InvoicesViewModel invoices)
    {
        _scopes = scopes;
        _session = session;
        Login = login;
        AccountSetup = accountSetup;
        Sales = sales;
        Products = products;
        Receive = receive;
        Inventory = inventory;
        Reports = reports;
        Users = users;
        Returns = returns;
        Shift = shift;
        Customers = customers;
        Invoices = invoices;

        Login.Authenticated += OnAuthenticated;
        AccountSetup.Authenticated += OnAuthenticated;
        _currentView = login;
    }

    /// <summary>Thiết kế-thời (previewer) — không dùng lúc chạy.</summary>
    public MainWindowViewModel() : this(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!) { }

    public LoginViewModel Login { get; }
    public AccountSetupViewModel AccountSetup { get; }
    public SalesViewModel Sales { get; }
    public ProductsViewModel Products { get; }
    public ReceiveStockViewModel Receive { get; }
    public InventoryViewModel Inventory { get; }
    public ReportsViewModel Reports { get; }
    public UsersViewModel Users { get; }
    public ReturnsViewModel Returns { get; }
    public ShiftViewModel Shift { get; }
    public CustomersViewModel Customers { get; }
    public InvoicesViewModel Invoices { get; }

    [ObservableProperty] private ViewModelBase _currentView;
    [ObservableProperty] private string _currentKey = "login";
    [ObservableProperty] private bool _isAuthenticated;

    public bool CanManage => _session.CanManageAdmin;
    public bool CanViewReports => _session.Has(PosPermissions.ReportView);
    public bool CanManageUsers => _session.Has(PosPermissions.UserManage);
    public bool CanReturn => _session.Has(PosPermissions.ReturnRefund);
    public bool CanManageInvoices => _session.Has(PosPermissions.ReportView);
    public string UserName => _session.UserFullName;
    public string StoreName => _session.StoreName;

    public bool IsPos => CurrentKey == "pos";
    public bool IsProducts => CurrentKey == "products";
    public bool IsReceive => CurrentKey == "receive";
    public bool IsInventory => CurrentKey == "inventory";
    public bool IsReports => CurrentKey == "reports";
    public bool IsUsers => CurrentKey == "users";
    public bool IsReturns => CurrentKey == "returns";
    public bool IsShift => CurrentKey == "shift";
    public bool IsCustomers => CurrentKey == "customers";
    public bool IsInvoices => CurrentKey == "invoices";

    partial void OnCurrentKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsPos));
        OnPropertyChanged(nameof(IsProducts));
        OnPropertyChanged(nameof(IsReceive));
        OnPropertyChanged(nameof(IsInventory));
        OnPropertyChanged(nameof(IsReports));
        OnPropertyChanged(nameof(IsUsers));
        OnPropertyChanged(nameof(IsReturns));
        OnPropertyChanged(nameof(IsShift));
        OnPropertyChanged(nameof(IsCustomers));
        OnPropertyChanged(nameof(IsInvoices));
    }

    /// <summary>Quyết định màn cổng: chưa có tài khoản nào → thiết lập lần đầu; ngược lại → đăng nhập.</summary>
    public async Task StartAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        bool hasAccount = await mediator.Send(new HasLoginAccountQuery());

        if (hasAccount)
        {
            CurrentView = Login;
            CurrentKey = "login";
        }
        else
        {
            CurrentView = AccountSetup;
            CurrentKey = "setup";
        }
    }

    private async void OnAuthenticated()
    {
        // Mở ca (B9) gắn người vừa đăng nhập nếu chưa có ca mở trên quầy này.
        await EnsureShiftAsync();
        await Sales.InitializeAsync();

        IsAuthenticated = true;
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanViewReports));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanReturn));
        OnPropertyChanged(nameof(CanManageInvoices));
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(StoreName));

        CurrentView = Sales;
        CurrentKey = "pos";
    }

    private async Task EnsureShiftAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var open = await db.Shifts.FirstOrDefaultAsync(
            s => s.RegisterId == _session.RegisterId && s.CloseAt == null);
        if (open is not null)
        {
            _session.ShiftId = open.Id;
            return;
        }

        var result = await mediator.Send(new OpenShiftCommand
        {
            StoreId = _session.StoreId,
            RegisterId = _session.RegisterId,
            CashierId = _session.CashierId,
            DeviceId = _session.DeviceId,
            OpeningFloat = 0m,
        });
        _session.ShiftId = result.Id;
    }

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        IsAuthenticated = false;
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanViewReports));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanReturn));
        OnPropertyChanged(nameof(CanManageInvoices));
        OnPropertyChanged(nameof(UserName));
        Login.Username = string.Empty;
        Login.Password = string.Empty;
        Login.ErrorMessage = string.Empty;
        CurrentView = Login;
        CurrentKey = "login";
    }

    [RelayCommand]
    private async Task Navigate(string key)
    {
        if (!IsAuthenticated) return;

        // Gate khu quản trị theo quyền (B2).
        if ((key is "products" or "receive" or "inventory") && !_session.CanManageAdmin)
            return;
        if (key == "reports" && !_session.Has(PosPermissions.ReportView))
            return;
        if (key == "users" && !_session.Has(PosPermissions.UserManage))
            return;
        if (key == "returns" && !_session.Has(PosPermissions.ReturnRefund))
            return;
        if (key == "invoices" && !_session.Has(PosPermissions.ReportView))
            return;

        switch (key)
        {
            case "products":
                CurrentView = Products;
                await Products.ActivateAsync();
                break;
            case "receive":
                CurrentView = Receive;
                await Receive.ActivateAsync();
                break;
            case "inventory":
                CurrentView = Inventory;
                await Inventory.ActivateAsync();
                break;
            case "reports":
                CurrentView = Reports;
                await Reports.ActivateAsync();
                break;
            case "users":
                CurrentView = Users;
                await Users.ActivateAsync();
                break;
            case "returns":
                CurrentView = Returns;
                await Returns.ActivateAsync();
                break;
            case "shift":
                CurrentView = Shift;
                await Shift.ActivateAsync();
                break;
            case "customers":
                CurrentView = Customers;
                await Customers.ActivateAsync();
                break;
            case "invoices":
                CurrentView = Invoices;
                await Invoices.ActivateAsync();
                break;
            default:
                key = "pos";
                CurrentView = Sales;
                await Sales.RefreshCatalogAsync(); // thấy ngay hàng vừa thêm/nhập ở màn quản trị
                break;
        }
        CurrentKey = key;
    }
}
