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
using Pos.Application.Reports;
using Pos.Application.Reports.DebtAging;
using Pos.Application.Reports.InventoryValuation;
using Pos.Application.Reports.PaymentReconciliation;
using Pos.Application.Reports.ProductSales;
using Pos.Application.Reports.SalesSummary;
using Pos.Application.Reports.TaxInvoice;
using Pos.Client.UI.Services;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình QUẢN TRỊ — Báo cáo (B12): chọn kỳ + chi nhánh hiện tại rồi chạy 6 báo cáo (bán hàng,
/// sản phẩm/lợi nhuận, đối soát thanh toán, thuế/HĐĐT, giá trị tồn, tuổi nợ). Chỉ đọc — không sửa nghiệp vụ.
/// </summary>
public partial class ReportsViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public ReportsViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;

        var today = DateTime.Today;
        _fromDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        _toDate = new DateTimeOffset(today);
    }

    // ── Bộ lọc kỳ ─────────────────────────────────────────────────────────
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private bool _isLoading;

    // ── Tab báo cáo đang xem ───────────────────────────────────────────────
    [ObservableProperty] private string _tab = "sales";
    public bool IsSales => Tab == "sales";
    public bool IsProducts => Tab == "products";
    public bool IsPayments => Tab == "payments";
    public bool IsTax => Tab == "tax";
    public bool IsInventory => Tab == "inventory";
    public bool IsDebt => Tab == "debt";

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsSales));
        OnPropertyChanged(nameof(IsProducts));
        OnPropertyChanged(nameof(IsPayments));
        OnPropertyChanged(nameof(IsTax));
        OnPropertyChanged(nameof(IsInventory));
        OnPropertyChanged(nameof(IsDebt));
    }

    [RelayCommand]
    private void SelectTab(string key) => Tab = key;

    // ── KPI bán hàng ───────────────────────────────────────────────────────
    [ObservableProperty] private int _orderCount;
    [ObservableProperty] private decimal _grossSales;
    [ObservableProperty] private decimal _refunds;
    [ObservableProperty] private decimal _netSales;
    [ObservableProperty] private decimal _averageOrderValue;

    public ObservableCollection<DailySales> Daily { get; } = new();
    public ObservableCollection<CashierSalesRow> ByCashier { get; } = new();
    public ObservableCollection<ProductSalesRow> Products { get; } = new();
    public ObservableCollection<PaymentRow> Payments { get; } = new();
    public ObservableCollection<TaxByRate> TaxRows { get; } = new();
    public ObservableCollection<InventoryValueRow> Inventory { get; } = new();
    public ObservableCollection<CustomerAging> Debt { get; } = new();

    [ObservableProperty] private decimal _inventoryTotal;
    [ObservableProperty] private decimal _debtTotal;
    [ObservableProperty] private int _invoicesIssued;
    [ObservableProperty] private int _invoicesPending;
    [ObservableProperty] private int _invoicesRejected;
    [ObservableProperty] private decimal _totalTax;

    public async Task ActivateAsync() => await LoadAsync();

    [RelayCommand]
    private async Task Load() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var fromLocal = (FromDate ?? DateTimeOffset.Now).Date;
            var toLocal = (ToDate ?? DateTimeOffset.Now).Date.AddDays(1); // nửa mở [from, to)
            var filter = new ReportFilter(
                DateTime.SpecifyKind(fromLocal, DateTimeKind.Local).ToUniversalTime(),
                DateTime.SpecifyKind(toLocal, DateTimeKind.Local).ToUniversalTime(),
                StoreId: _session.StoreId);

            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();

            var sales = await mediator.Send(new SalesSummaryQuery(filter));
            var products = await mediator.Send(new ProductSalesQuery(filter));
            var payments = await mediator.Send(new PaymentReconciliationQuery(filter));
            var tax = await mediator.Send(new TaxInvoiceQuery(filter));
            var inventory = await mediator.Send(new InventoryValuationQuery(_session.StoreId));
            var debt = await mediator.Send(new DebtAgingQuery(DateTime.UtcNow));

            OrderCount = sales.OrderCount;
            GrossSales = sales.GrossSales;
            Refunds = sales.Refunds;
            NetSales = sales.NetSales;
            AverageOrderValue = sales.AverageOrderValue;

            Daily.Clear();
            foreach (var d in sales.Daily) Daily.Add(d);

            // Tên thu ngân để hiển thị thân thiện thay vì GUID.
            var cashierIds = sales.ByCashier.Select(c => c.CashierId).ToList();
            var names = await db.Users
                .Where(u => cashierIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);
            ByCashier.Clear();
            foreach (var c in sales.ByCashier)
                ByCashier.Add(new CashierSalesRow(
                    names.GetValueOrDefault(c.CashierId, "—"), c.OrderCount, c.GrossSales));

            Products.Clear();
            foreach (var p in products) Products.Add(p);

            Payments.Clear();
            foreach (var p in payments)
                Payments.Add(new PaymentRow(PaymentMethodNames.Label(p.Method), p.Gross, p.Refunds, p.Net));

            TaxRows.Clear();
            foreach (var t in tax.TaxByRate) TaxRows.Add(t);
            InvoicesIssued = tax.Issued;
            InvoicesPending = tax.Pending;
            InvoicesRejected = tax.Rejected;
            TotalTax = tax.TotalTax;

            Inventory.Clear();
            foreach (var i in inventory.Items) Inventory.Add(i);
            InventoryTotal = inventory.TotalValue;

            Debt.Clear();
            foreach (var c in debt.Customers) Debt.Add(c);
            DebtTotal = debt.TotalOutstanding;
        }
        finally
        {
            IsLoading = false;
        }
    }

}

/// <summary>Dòng doanh thu theo thu ngân đã giải tên (B12).</summary>
public sealed record CashierSalesRow(string CashierName, int OrderCount, decimal GrossSales);

/// <summary>Dòng đối soát thanh toán đã gắn nhãn phương thức tiếng Việt (B12).</summary>
public sealed record PaymentRow(string Method, decimal Gross, decimal Refunds, decimal Net);
