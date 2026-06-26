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
using Pos.Application.Common;
using Pos.Application.Customers.RecordDebtPayment;
using Pos.Application.Customers.UpsertCustomer;
using Pos.Client.UI.Services;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình Khách hàng, Công nợ &amp; Tích điểm (B10): liệt kê hồ sơ KH kèm điểm &amp; dư nợ; tạo/sửa hồ sơ
/// (SĐT là định danh chính, MST cho HĐĐT, hạn mức tín dụng); thu nợ (phân bổ vào công nợ, trả tiền mặt
/// cộng vào quỹ ca B9).
/// </summary>
public partial class CustomersViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public CustomersViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public CustomersViewModel() : this(null!, null!) { }

    public ObservableCollection<CustomerRow> Customers { get; } = new();
    private List<CustomerRow> _all = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Form tạo/sửa hồ sơ ─────────────────────────────────────────────────
    [ObservableProperty] private Guid? _editId;
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formPhone = string.Empty;
    [ObservableProperty] private string _formTaxCode = string.Empty;
    [ObservableProperty] private decimal _formCreditLimit;
    public string FormTitle => EditId is null ? "Thêm khách hàng mới" : "Sửa hồ sơ khách hàng";

    // ── Thu nợ ─────────────────────────────────────────────────────────────
    [ObservableProperty] private CustomerRow? _selected;
    [ObservableProperty] private decimal _debtAmount;
    [ObservableProperty] private bool _debtCash = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnEditIdChanged(Guid? value) => OnPropertyChanged(nameof(FormTitle));

    partial void OnSelectedChanged(CustomerRow? value)
    {
        if (value is null) return;
        EditId = value.Id;
        FormName = value.Name;
        FormPhone = value.Phone ?? string.Empty;
        FormTaxCode = value.TaxCode ?? string.Empty;
        FormCreditLimit = value.CreditLimit;
    }

    public async Task ActivateAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();

            var customers = await db.Customers.OrderBy(c => c.Name).ToListAsync();
            // Gom dư nợ phía client: SQLite (offline) KHÔNG dịch được SUM(decimal) phía server.
            var debt = (await db.Receivables
                    .Where(r => r.Outstanding != 0m)
                    .Select(r => new { r.CustomerId, r.Outstanding })
                    .ToListAsync())
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Outstanding));

            _all = customers.Select(c => new CustomerRow(
                c.Id, c.Name, c.Phone, c.TaxCode, c.CreditLimit, c.PointBalance,
                debt.GetValueOrDefault(c.Id))).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        var items = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.Phone ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
        Customers.Clear();
        foreach (var c in items) Customers.Add(c);
    }

    [RelayCommand]
    private void NewCustomer()
    {
        EditId = null;
        Selected = null;
        FormName = FormPhone = FormTaxCode = string.Empty;
        FormCreditLimit = 0m;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveCustomer()
    {
        StatusMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(FormName)) { StatusMessage = "⚠ Nhập tên khách hàng."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new UpsertCustomerCommand
            {
                CustomerId = EditId ?? Guid.NewGuid(),
                Name = FormName.Trim(),
                Phone = string.IsNullOrWhiteSpace(FormPhone) ? null : FormPhone.Trim(),
                TaxCode = string.IsNullOrWhiteSpace(FormTaxCode) ? null : FormTaxCode.Trim(),
                CreditLimit = FormCreditLimit,
            });
            StatusMessage = "✓ Đã lưu hồ sơ khách hàng.";
            NewCustomer();
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task RecordDebtPayment()
    {
        StatusMessage = string.Empty;
        if (Selected is null) { StatusMessage = "⚠ Chọn khách hàng cần thu nợ."; return; }
        if (DebtAmount <= 0m) { StatusMessage = "⚠ Số tiền thu nợ phải lớn hơn 0."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new RecordDebtPaymentCommand
            {
                CustomerId = Selected.Id,
                Amount = DebtAmount,
                Method = DebtCash ? PaymentMethod.Cash : PaymentMethod.VietQR,
                ShiftId = DebtCash ? _session.ShiftId : null,
                DeviceId = _session.DeviceId,
                StoreId = _session.StoreId,
            });
            StatusMessage = $"✓ Đã thu nợ {DebtAmount:N0} đ. Còn nợ: {result.OutstandingAfter:N0} đ.";
            DebtAmount = 0m;
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }
}

/// <summary>Dòng khách hàng kèm điểm &amp; dư nợ (B10).</summary>
public sealed record CustomerRow(
    Guid Id, string Name, string? Phone, string? TaxCode,
    decimal CreditLimit, decimal PointBalance, decimal Outstanding);
