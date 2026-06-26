using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Catalog.Queries;
using Pos.Application.Common;
using Pos.Application.Orders.CheckoutOrder;
using Pos.Application.Orders.CreateOrder;
using Pos.Client.UI.Services;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình bán hàng động: nạp danh mục từ DB, dựng hóa đơn và chốt qua handler nghiệp vụ
/// (CreateOrder B4/B5/B13 → CheckoutOrder B6/B8/B9). Mỗi thao tác DB chạy trong 1 scope riêng.
/// </summary>
public partial class SalesViewModel : ViewModelBase
{
    private const string AllCategory = "Tất cả";

    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;
    private List<SalesCatalogItem> _catalog = new();
    private int _tabSeq;

    public SalesViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
        AddTab();
    }

    public string StoreName { get; private set; } = "Chi nhánh";
    public string CashierName { get; private set; } = "Thu ngân";

    /// <summary>Nguồn dữ liệu đang hoạt động (PostgreSQL online / SQLite offline).</summary>
    public string Backend { get; private set; } = "—";

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<SalesCatalogItem> FilteredProducts { get; } = new();
    public ObservableCollection<InvoiceViewModel> Tabs { get; } = new();

    /// <summary>Hồ sơ khách hàng để gợi ý nhanh tại quầy (B10) — tìm theo tên/SĐT khi gõ.</summary>
    public ObservableCollection<CustomerSuggestion> Customers { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = AllCategory;
    [ObservableProperty] private InvoiceViewModel? _selectedTab;
    [ObservableProperty] private bool _isBusy;

    partial void OnSearchTextChanged(string value) => RefreshProducts();
    partial void OnSelectedCategoryChanged(string value) => RefreshProducts();

    /// <summary>Nạp danh mục hàng từ DB (gọi sau khi đã bootstrap phiên).</summary>
    public async Task InitializeAsync()
    {
        StoreName = _session.StoreName;
        CashierName = "Thu ngân: " + _session.CashierName;
        Backend = _session.Backend;
        OnPropertyChanged(nameof(StoreName));
        OnPropertyChanged(nameof(CashierName));
        OnPropertyChanged(nameof(Backend));
        await RefreshCatalogAsync();
    }

    /// <summary>Nạp danh sách khách hàng để gợi ý tại ô "Tìm khách hàng" (B10).</summary>
    public async Task RefreshCustomersAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var list = await db.Customers
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSuggestion(c.Id, c.Name, c.Phone, c.TierId))
            .ToListAsync();

        Customers.Clear();
        foreach (var c in list) Customers.Add(c);
    }

    /// <summary>
    /// Nạp lại danh mục bán hàng từ DB — gọi mỗi khi quay về màn POS để thấy ngay hàng hóa vừa
    /// thêm/nhập ở màn QUẢN TRỊ (giữ nguyên giỏ hàng đang mở &amp; bộ lọc đang chọn).
    /// </summary>
    public async Task RefreshCatalogAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        _catalog = (await mediator.Send(new GetSalesCatalogQuery())).ToList();

        var previouslySelected = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategory);
        foreach (var c in _catalog.Select(p => p.Category).Distinct())
            Categories.Add(c);

        // Giữ lại ngành hàng đang lọc nếu vẫn còn, ngược lại về "Tất cả".
        if (!Categories.Contains(previouslySelected))
            SelectedCategory = AllCategory;

        RefreshProducts();
        await RefreshCustomersAsync();
    }

    [RelayCommand]
    private void AddProduct(SalesCatalogItem? product)
    {
        if (product is null) return;
        SelectedTab ??= Tabs.FirstOrDefault();
        SelectedTab?.AddProduct(product);
    }

    [RelayCommand]
    private void AddTab()
    {
        var tab = new InvoiceViewModel($"Hóa đơn {++_tabSeq}");
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(InvoiceViewModel? tab)
    {
        if (tab is null) return;
        Tabs.Remove(tab);
        if (Tabs.Count == 0) AddTab();
        SelectedTab = Tabs.LastOrDefault();
    }

    [RelayCommand]
    private void SelectTab(InvoiceViewModel? tab)
    {
        if (tab is not null) SelectedTab = tab;
    }

    [RelayCommand]
    private void SelectCategory(string category) => SelectedCategory = category;

    /// <summary>Chốt đơn: tạo Order (B4/B5) rồi thu tiền (B6) trừ tồn (B8) cập nhật quỹ (B9) qua handler.</summary>
    [RelayCommand]
    private async Task Checkout()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        if (tab.IsEmpty) { tab.StatusMessage = "Hóa đơn trống — thêm hàng trước khi thanh toán."; return; }
        if (!_session.IsReady) { tab.StatusMessage = "Phiên bán hàng chưa sẵn sàng."; return; }
        if (tab.IsCash && tab.AmountTendered < tab.GrandTotal)
        {
            tab.StatusMessage = $"Khách đưa thiếu {tab.GrandTotal - tab.AmountTendered:#,##0} đ.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var order = await mediator.Send(new CreateOrderCommand
            {
                StoreId = _session.StoreId,
                ShiftId = _session.ShiftId,
                CashierId = _session.CashierId,
                DeviceId = _session.DeviceId,
                CustomerId = tab.CustomerId,
                CustomerTierId = tab.CustomerTierId,
                OrderDiscount = tab.OrderDiscount,
                Lines = tab.Lines
                    .Select(l => new CreateOrderLine(l.VariantId, l.Qty, l.LineDiscount))
                    .ToList(),
            });

            var method = MapMethod(tab.SelectedPaymentMethod);
            // Thẻ/Chuyển khoản: mã giao dịch (mã chuẩn chi/QR) do thu ngân nhập từ máy POS/sao kê.
            string? externalRef = method is PaymentMethod.Card or PaymentMethod.VietQR
                ? (string.IsNullOrWhiteSpace(tab.ExternalRef) ? null : tab.ExternalRef.Trim())
                : null;

            var result = await mediator.Send(new CheckoutOrderCommand
            {
                OrderId = order.Id,
                Payments = new[] { new PaymentInput(method, order.GrandTotal, externalRef) },
                CashTendered = tab.IsCash ? tab.AmountTendered : null,
            });

            tab.StatusMessage = $"✓ {result.OrderNumber}: đã thu {result.GrandTotal:#,##0} đ qua {tab.SelectedPaymentMethod}."
                + (result.ChangeDue > 0 ? $" Tiền thừa {result.ChangeDue:#,##0} đ." : string.Empty);
            tab.ResetForNextSale();
        }
        catch (BusinessRuleException ex) { tab.StatusMessage = "⚠ " + ex.Message; }
        catch (NotFoundException ex) { tab.StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { tab.StatusMessage = "Lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private static PaymentMethod MapMethod(string label) => label switch
    {
        "Thẻ" => PaymentMethod.Card,
        "Chuyển khoản" => PaymentMethod.VietQR,
        _ => PaymentMethod.Cash,
    };

    private void RefreshProducts()
    {
        IEnumerable<SalesCatalogItem> items = _catalog;

        if (SelectedCategory != AllCategory)
            items = items.Where(p => p.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            items = items.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredProducts.Clear();
        foreach (var p in items) FilteredProducts.Add(p);
    }
}

/// <summary>Gợi ý khách hàng tại quầy (B10) — khớp theo tên hoặc SĐT.</summary>
public sealed record CustomerSuggestion(Guid Id, string Name, string? Phone, Guid? TierId);
