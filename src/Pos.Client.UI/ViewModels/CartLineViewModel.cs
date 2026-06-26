using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Application.Catalog.Queries;
using Pos.Domain.Common;

namespace Pos.Client.UI.ViewModels;

/// <summary>Một dòng hàng trong hóa đơn đang bán (giỏ hàng), gắn với biến thể thật trong DB.</summary>
public partial class CartLineViewModel : ObservableObject
{
    public CartLineViewModel(SalesCatalogItem product)
    {
        VariantId = product.VariantId;
        Sku = product.Sku;
        Name = product.Name;
        UnitPrice = product.Price;
        TaxRate = product.TaxRate;
    }

    public Guid VariantId { get; }
    public string Sku { get; }
    public string Name { get; }
    public VatRate TaxRate { get; }

    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private decimal _qty = 1m;

    [ObservableProperty]
    private decimal _lineDiscount;

    /// <summary>Thành tiền hiển thị trên dòng = SL × đơn giá (trước CK tổng &amp; thuế).</summary>
    public decimal LineSubtotal => Qty * UnitPrice;

    partial void OnQtyChanged(decimal value) => OnPropertyChanged(nameof(LineSubtotal));
    partial void OnUnitPriceChanged(decimal value) => OnPropertyChanged(nameof(LineSubtotal));
}
