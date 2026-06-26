using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Catalog.CreateProduct;
using Pos.Application.Catalog.Queries;
using Pos.Application.Common;
using Pos.Client.UI.Services;
using Pos.Domain.Common;

namespace Pos.Client.UI.ViewModels;

/// <summary>Lựa chọn thuế suất VAT (B3) hiển thị tiếng Việt, ánh xạ về <see cref="VatRate"/>.</summary>
public sealed record VatOption(string Label, VatRate Value);

/// <summary>
/// Màn hình QUẢN TRỊ — Sản phẩm (B3): tạo/liệt kê hàng hóa và đẩy thẳng vào DB (PostgreSQL) qua
/// <see cref="CreateProductCommand"/>. Sau khi lưu, POS đọc lại danh mục là thấy ngay.
/// </summary>
public partial class ProductsViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public ProductsViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public ObservableCollection<AdminProductItem> Products { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public IReadOnlyList<VatOption> TaxRates { get; } = new[]
    {
        new VatOption("0%", VatRate.Zero),
        new VatOption("5%", VatRate.Five),
        new VatOption("8%", VatRate.Eight),
        new VatOption("10%", VatRate.Ten),
        new VatOption("Không chịu thuế", VatRate.Exempt),
        new VatOption("Không kê khai", VatRate.NotDeclared),
    };

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _sku = string.Empty;
    [ObservableProperty] private string _barcode = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _baseUnit = "Cái";
    [ObservableProperty] private VatOption _selectedTaxRate = new("8%", VatRate.Eight);
    [ObservableProperty] private decimal _price;
    [ObservableProperty] private decimal _initialStock;
    [ObservableProperty] private decimal _initialCost;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public async Task ActivateAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var products = await mediator.Send(new GetAdminProductsQuery(_session.StoreId));
        Products.Clear();
        foreach (var p in products) Products.Add(p);

        var cats = await mediator.Send(new GetCategoriesQuery());
        Categories.Clear();
        foreach (var c in cats) Categories.Add(c);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator.Send(new CreateProductCommand
            {
                Name = Name,
                Sku = Sku,
                Barcode = Barcode,
                Category = Category,
                BaseUnit = BaseUnit,
                TaxRate = SelectedTaxRate.Value,
                Price = Price,
                IsActive = IsActive,
                StoreId = _session.StoreId,
                DeviceId = _session.DeviceId,
                InitialStock = InitialStock,
                InitialCost = InitialCost,
            });

            StatusMessage = InitialStock > 0m
                ? $"✓ Đã lưu '{result.Name}' (SKU {result.Sku}) — tồn ban đầu {InitialStock:0.###}."
                : $"✓ Đã lưu '{result.Name}' (SKU {result.Sku}).";
            ResetForm();
            await ReloadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ResetForm()
    {
        Name = string.Empty;
        Sku = string.Empty;
        Barcode = string.Empty;
        Category = string.Empty;
        BaseUnit = "Cái";
        SelectedTaxRate = TaxRates.First(t => t.Value == VatRate.Eight);
        Price = 0m;
        InitialStock = 0m;
        InitialCost = 0m;
        IsActive = true;
    }
}
