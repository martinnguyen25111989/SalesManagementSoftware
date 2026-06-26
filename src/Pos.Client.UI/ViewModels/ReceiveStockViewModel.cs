using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Catalog.Queries;
using Pos.Application.Common;
using Pos.Application.Inventory.ReceiveStock;
using Pos.Application.Inventory.Suppliers;
using Pos.Client.UI.Services;

namespace Pos.Client.UI.ViewModels;

/// <summary>Một dòng trên phiếu nhập đang lập (chọn hàng + số lượng + giá vốn).</summary>
public partial class ReceiveLineRow : ObservableObject
{
    public ReceiveLineRow(AdminProductItem product)
    {
        Product = product;
        UnitCost = product.Price; // gợi ý giá vốn = giá bán hiện tại; người dùng sửa lại.
    }

    public AdminProductItem Product { get; }
    public string Sku => Product.Sku;
    public string Name => Product.Name;

    [ObservableProperty] private decimal _qty = 1m;
    [ObservableProperty] private decimal _unitCost;

    public decimal LineTotal => Qty * UnitCost;

    partial void OnQtyChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
    partial void OnUnitCostChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
}

/// <summary>
/// Màn hình QUẢN TRỊ — Nhập hàng (B8 / GRN): chọn nhà cung cấp, thêm dòng hàng + giá vốn, rồi đẩy vào DB
/// qua <see cref="ReceiveStockCommand"/> (cộng tồn +, cập nhật giá vốn bình quân). Idempotent theo phiếu.
/// </summary>
public partial class ReceiveStockViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public ReceiveStockViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public ObservableCollection<SupplierItem> Suppliers { get; } = new();
    public ObservableCollection<AdminProductItem> Products { get; } = new();
    public ObservableCollection<ReceiveLineRow> Lines { get; } = new();

    [ObservableProperty] private SupplierItem? _selectedSupplier;
    [ObservableProperty] private string _newSupplierName = string.Empty;
    [ObservableProperty] private AdminProductItem? _selectedProduct;
    [ObservableProperty] private decimal _addQty = 1m;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public decimal GrandTotal => Lines.Sum(l => l.LineTotal);
    public bool HasLines => Lines.Count > 0;

    public async Task ActivateAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var suppliers = await mediator.Send(new GetSuppliersQuery());
        Suppliers.Clear();
        foreach (var s in suppliers) Suppliers.Add(s);
        SelectedSupplier ??= Suppliers.FirstOrDefault();

        var products = await mediator.Send(new GetAdminProductsQuery(_session.StoreId));
        Products.Clear();
        foreach (var p in products) Products.Add(p);
    }

    [RelayCommand]
    private async Task AddSupplier()
    {
        if (string.IsNullOrWhiteSpace(NewSupplierName)) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var supplier = await mediator.Send(new UpsertSupplierCommand { Name = NewSupplierName });
            Suppliers.Add(supplier);
            SelectedSupplier = supplier;
            NewSupplierName = string.Empty;
            StatusMessage = $"✓ Đã thêm NCC '{supplier.Name}'.";
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private void AddLine()
    {
        if (SelectedProduct is null) { StatusMessage = "Chọn hàng hóa để thêm dòng."; return; }
        var existing = Lines.FirstOrDefault(l => l.Product.VariantId == SelectedProduct.VariantId);
        if (existing is not null)
        {
            existing.Qty += AddQty <= 0 ? 1m : AddQty;
        }
        else
        {
            var row = new ReceiveLineRow(SelectedProduct) { Qty = AddQty <= 0 ? 1m : AddQty };
            row.PropertyChanged += OnLineChanged; // sửa SL/giá vốn trên dòng → cập nhật tổng
            Lines.Add(row);
        }
        RaiseTotals();
    }

    [RelayCommand]
    private void RemoveLine(ReceiveLineRow? row)
    {
        if (row is null) return;
        row.PropertyChanged -= OnLineChanged;
        Lines.Remove(row);
        RaiseTotals();
    }

    private void OnLineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReceiveLineRow.LineTotal)) RaiseTotals();
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (IsBusy) return;
        if (SelectedSupplier is null) { StatusMessage = "Chọn nhà cung cấp."; return; }
        if (Lines.Count == 0) { StatusMessage = "Phiếu nhập phải có ít nhất 1 dòng."; return; }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator.Send(new ReceiveStockCommand
            {
                StoreId = _session.StoreId,
                SupplierId = SelectedSupplier.Id,
                DeviceId = _session.DeviceId,
                Lines = Lines.Select(l => new ReceiveLine(l.Product.VariantId, l.Qty, l.UnitCost)).ToList(),
            });

            StatusMessage = $"✓ Đã nhập kho {result.Lines.Count} mặt hàng, tổng giá vốn {result.Total:#,##0} đ.";
            Lines.Clear();
            RaiseTotals();
            await ActivateAsync(); // làm mới tồn hiển thị
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (NotFoundException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(HasLines));
    }
}
