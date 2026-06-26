using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Application.Catalog.Queries;
using Pos.Application.Pricing;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Một hóa đơn đang bán (tab). Giữ giỏ hàng, khách, thanh toán; tính tiền qua <see cref="OrderCalculator"/>
/// để khớp tuyệt đối quy tắc B5/B13 với backend.
/// </summary>
public partial class InvoiceViewModel : ObservableObject
{
    public InvoiceViewModel(string title)
    {
        Title = title;
        Lines.CollectionChanged += OnLinesChanged;
    }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _customerName = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private decimal _orderDiscount;
    [ObservableProperty] private decimal _amountTendered;
    [ObservableProperty] private string _externalRef = string.Empty;
    [ObservableProperty] private string _selectedPaymentMethod = "Tiền mặt";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<CartLineViewModel> Lines { get; } = new();

    public IReadOnlyList<string> PaymentMethods { get; } = new[] { "Tiền mặt", "Thẻ", "Chuyển khoản" };

    private OrderTotals _totals = OrderCalculator.Calculate(Array.Empty<OrderCalcLine>());

    public decimal ItemCount => Lines.Sum(l => l.Qty);
    public decimal Subtotal => _totals.Subtotal;
    public decimal DiscountTotal => _totals.DiscountTotal;
    public decimal TaxTotal => _totals.TaxTotal;
    public decimal GrandTotal => _totals.GrandTotal;
    public bool IsCash => SelectedPaymentMethod == "Tiền mặt";
    public decimal ChangeDue => IsCash && AmountTendered > GrandTotal ? AmountTendered - GrandTotal : 0m;
    public bool IsEmpty => Lines.Count == 0;

    public void AddProduct(SalesCatalogItem product)
    {
        var existing = Lines.FirstOrDefault(l => l.VariantId == product.VariantId);
        if (existing is not null)
            existing.Qty += 1m;
        else
            Lines.Add(new CartLineViewModel(product));
        StatusMessage = string.Empty;
        Recalculate();
    }

    [RelayCommand]
    private void Increase(CartLineViewModel line) { line.Qty += 1m; Recalculate(); }

    [RelayCommand]
    private void Decrease(CartLineViewModel line)
    {
        if (line.Qty <= 1m) Lines.Remove(line);
        else line.Qty -= 1m;
        Recalculate();
    }

    [RelayCommand]
    private void Remove(CartLineViewModel line) { Lines.Remove(line); Recalculate(); }

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        OrderDiscount = 0m;
        AmountTendered = 0m;
        ExternalRef = string.Empty;
        StatusMessage = string.Empty;
        Recalculate();
    }

    /// <summary>Dọn giỏ sau khi đã chốt đơn thành công (giữ nguyên StatusMessage để hiển thị kết quả).</summary>
    public void ResetForNextSale()
    {
        Lines.Clear();
        OrderDiscount = 0m;
        AmountTendered = 0m;
        ExternalRef = string.Empty;
        Recalculate();
    }

    [RelayCommand]
    private void ExactCash() { AmountTendered = GrandTotal; }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CartLineViewModel l in e.OldItems) l.PropertyChanged -= OnLinePropertyChanged;
        if (e.NewItems is not null)
            foreach (CartLineViewModel l in e.NewItems) l.PropertyChanged += OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartLineViewModel.Qty)
            or nameof(CartLineViewModel.UnitPrice)
            or nameof(CartLineViewModel.LineDiscount))
            Recalculate();
    }

    partial void OnOrderDiscountChanged(decimal value) => Recalculate();
    partial void OnAmountTenderedChanged(decimal value) => OnPropertyChanged(nameof(ChangeDue));
    partial void OnSelectedPaymentMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsCash));
        OnPropertyChanged(nameof(ChangeDue));
    }

    private void Recalculate()
    {
        _totals = OrderCalculator.Calculate(
            Lines.Select(l => new OrderCalcLine(l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate)).ToList(),
            OrderDiscount);

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(ChangeDue));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
