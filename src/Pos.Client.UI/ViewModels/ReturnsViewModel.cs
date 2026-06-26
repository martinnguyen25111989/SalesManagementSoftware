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
using Pos.Application.Returns.CreateReturn;
using Pos.Client.UI.Services;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình Trả / Đổi hàng &amp; Hoàn tiền (B7): tra cứu hóa đơn gốc theo số đơn → chọn dòng &amp; số lượng trả,
/// (nhập lại tồn hay không), lý do + phương thức hoàn. Trả hàng cần Manager duyệt (B7). Hoàn tỷ lệ CK/thuế &amp;
/// thu hồi điểm do handler xử lý. HĐĐT điều chỉnh: làm ở màn HĐĐT (B11).
/// </summary>
public partial class ReturnsViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public ReturnsViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public ReturnsViewModel() : this(null!, null!) { }

    [ObservableProperty] private string _orderNumber = string.Empty;
    [ObservableProperty] private bool _orderLoaded;
    [ObservableProperty] private string _orderInfo = string.Empty;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private bool _refundCash = true;
    [ObservableProperty] private bool _managerApproved;
    [ObservableProperty] private string _statusMessage = string.Empty;

    private Guid _originalOrderId;

    public ObservableCollection<ReturnLineRow> Lines { get; } = new();

    public bool CanRefund => _session.Has(Pos.Application.Auth.PosPermissions.ReturnRefund);

    public async Task ActivateAsync()
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanRefund));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task FindOrder()
    {
        StatusMessage = string.Empty;
        OrderLoaded = false;
        Lines.Clear();
        var no = OrderNumber?.Trim();
        if (string.IsNullOrEmpty(no)) { StatusMessage = "⚠ Nhập số hóa đơn cần trả."; return; }

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();

            var order = await db.Orders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.OrderNumber == no);
            if (order is null) { StatusMessage = "⚠ Không tìm thấy hóa đơn."; return; }
            if (order.Status is not (OrderStatus.Completed or OrderStatus.PartiallyReturned))
            {
                StatusMessage = $"⚠ Chỉ trả hàng cho đơn đã hoàn tất (hiện {order.Status}).";
                return;
            }

            _originalOrderId = order.Id;

            // Số đã trả trước đó theo từng dòng (không cho trả vượt).
            var priorIds = await db.ReturnOrders.Where(r => r.OriginalOrderId == order.Id)
                .Select(r => r.Id).ToListAsync();
            var prior = await db.ReturnLines.Where(rl => priorIds.Contains(rl.ReturnOrderId))
                .GroupBy(rl => rl.OrderLineId)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);

            // Tên hàng để hiển thị (join Variant → Product).
            var variantIds = order.Lines.Select(l => l.VariantId).ToList();
            var names = await (from v in db.Variants
                               join p in db.Products on v.ProductId equals p.Id
                               where variantIds.Contains(v.Id)
                               select new { v.Id, p.Name, p.BaseUnit })
                .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.BaseUnit });

            foreach (var l in order.Lines)
            {
                decimal already = prior.GetValueOrDefault(l.Id);
                decimal returnable = l.Qty - already;
                if (returnable <= 0m) continue;
                var info = names.GetValueOrDefault(l.VariantId);
                Lines.Add(new ReturnLineRow
                {
                    OrderLineId = l.Id,
                    ProductName = info?.Name ?? l.VariantId.ToString(),
                    Unit = info?.BaseUnit ?? string.Empty,
                    PurchasedQty = l.Qty,
                    AlreadyReturned = already,
                    ReturnableQty = returnable,
                    UnitPrice = l.LineTotal / l.Qty,
                });
            }

            OrderInfo = $"{order.OrderNumber} · {order.GrandTotal:N0} đ · {order.Status}";
            OrderLoaded = true;
            if (Lines.Count == 0) StatusMessage = "Đơn này đã trả hết các dòng.";
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SubmitReturn()
    {
        StatusMessage = string.Empty;
        var inputs = Lines.Where(l => l.ReturnQty > 0m)
            .Select(l => new ReturnLineInput(l.OrderLineId, l.ReturnQty, l.RestockToInventory))
            .ToList();
        if (inputs.Count == 0) { StatusMessage = "⚠ Chưa nhập số lượng trả cho dòng nào."; return; }
        if (string.IsNullOrWhiteSpace(Reason)) { StatusMessage = "⚠ Nhập lý do trả hàng."; return; }
        foreach (var l in Lines.Where(l => l.ReturnQty > 0m))
            if (l.ReturnQty > l.ReturnableQty)
            { StatusMessage = $"⚠ '{l.ProductName}': trả vượt số còn lại ({l.ReturnableQty:N0})."; return; }

        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new CreateReturnCommand
            {
                OriginalOrderId = _originalOrderId,
                ShiftId = _session.ShiftId,
                ApprovedBy = _session.CashierId,
                DeviceId = _session.DeviceId,
                Reason = Reason.Trim(),
                RefundMethod = RefundCash ? PaymentMethod.Cash : PaymentMethod.VietQR,
                ManagerApproved = ManagerApproved,
                Lines = inputs,
            });
            StatusMessage = $"✓ Đã trả hàng. Hoàn {result.RefundAmount:N0} đ. Đơn gốc: {result.OriginalOrderStatus}. "
                + "Nếu đã phát hành HĐĐT, vào màn Hóa đơn để lập chứng từ điều chỉnh.";
            Reason = string.Empty;
            ManagerApproved = false;
            await FindOrder(); // tải lại số còn trả được
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (NotFoundException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }
}

/// <summary>Dòng trả hàng có thể chỉnh số lượng &amp; chọn nhập lại kho (B7).</summary>
public partial class ReturnLineRow : ObservableObject
{
    public Guid OrderLineId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal PurchasedQty { get; init; }
    public decimal AlreadyReturned { get; init; }
    public decimal ReturnableQty { get; init; }
    public decimal UnitPrice { get; init; }

    [ObservableProperty] private decimal _returnQty;
    [ObservableProperty] private bool _restockToInventory = true;
}
