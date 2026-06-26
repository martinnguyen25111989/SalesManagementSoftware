using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Catalog.Queries;
using Pos.Client.UI.Services;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình QUẢN TRỊ — Tồn kho (B8): xem tồn hiện có theo chi nhánh (đọc snapshot StockBalance qua
/// <see cref="GetAdminProductsQuery"/>). Chỉ đọc; thay đổi tồn đi qua Nhập hàng / bán hàng / kiểm kê.
/// </summary>
public partial class InventoryViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public InventoryViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public ObservableCollection<AdminProductItem> Items { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;

    private System.Collections.Generic.List<AdminProductItem> _all = new();

    public decimal TotalStockValue => _all.Sum(i => i.OnHand * i.Price);
    public int LineCount => _all.Count;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task ActivateAsync()
    {
        using var scope = _scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        _all = (await mediator.Send(new GetAdminProductsQuery(_session.StoreId))).ToList();
        ApplyFilter();
        OnPropertyChanged(nameof(TotalStockValue));
        OnPropertyChanged(nameof(LineCount));
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        var items = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(i =>
                i.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                || i.Sku.Contains(q, System.StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var i in items) Items.Add(i);
    }
}
