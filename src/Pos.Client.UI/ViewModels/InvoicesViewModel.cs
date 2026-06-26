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
using Pos.Application.Invoicing.IssueEInvoice;
using Pos.Application.Invoicing.ProcessPending;
using Pos.Application.Invoicing.Revise;
using Pos.Client.UI.Services;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình Hóa đơn điện tử (B11/B11-A): bảng kê chứng từ HĐĐT (gốc/điều chỉnh/thay thế/hủy) kèm trạng thái
/// &amp; mã CQT; phát hành cho đơn đã thanh toán (idempotent theo Order.Id); drain hàng đợi offline; lập chứng
/// từ điều chỉnh/thay thế/hủy gắn B7. Không sửa/xóa HĐ đã phát hành.
/// </summary>
public partial class InvoicesViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public InvoicesViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public InvoicesViewModel() : this(null!, null!) { }

    public ObservableCollection<InvoiceRow> Invoices { get; } = new();

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _issuedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Phát hành cho 1 đơn ────────────────────────────────────────────────
    [ObservableProperty] private string _issueOrderNumber = string.Empty;
    [ObservableProperty] private string _buyerName = string.Empty;
    [ObservableProperty] private string _buyerTaxCode = string.Empty;

    // ── Điều chỉnh / thay thế / hủy ────────────────────────────────────────
    [ObservableProperty] private InvoiceRow? _selected;
    [ObservableProperty] private string _reviseReason = string.Empty;

    public async Task ActivateAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();

            var invoices = await db.EInvoices
                .OrderByDescending(e => e.CreatedUtc)
                .Take(200)
                .ToListAsync();

            var orderIds = invoices.Select(e => e.OrderId).Distinct().ToList();
            var orderNos = await db.Orders.Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber);

            Invoices.Clear();
            foreach (var e in invoices)
                Invoices.Add(new InvoiceRow(
                    e.Id, e.OrderId, orderNos.GetValueOrDefault(e.OrderId, "—"),
                    TypeLabel(e.Type), StatusLabel(e.Status), e.Status,
                    e.CqtCode, e.InvoiceNo, e.Serial,
                    e.IssuedAt, e.ErrorMessage));

            PendingCount = invoices.Count(e => e.Status is EInvoiceStatus.Pending);
            IssuedCount = invoices.Count(e => e.Status is EInvoiceStatus.Issued or EInvoiceStatus.Sent);
            RejectedCount = invoices.Count(e => e.Status is EInvoiceStatus.Rejected);
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task IssueForOrder()
    {
        StatusMessage = string.Empty;
        var no = IssueOrderNumber?.Trim();
        if (string.IsNullOrEmpty(no)) { StatusMessage = "⚠ Nhập số hóa đơn cần phát hành."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == no);
            if (order is null) { StatusMessage = "⚠ Không tìm thấy đơn."; return; }

            var result = await mediator.Send(new IssueEInvoiceCommand
            {
                OrderId = order.Id,
                BuyerName = string.IsNullOrWhiteSpace(BuyerName) ? null : BuyerName.Trim(),
                BuyerTaxCode = string.IsNullOrWhiteSpace(BuyerTaxCode) ? null : BuyerTaxCode.Trim(),
            });
            StatusMessage = result.Status switch
            {
                EInvoiceStatus.Issued or EInvoiceStatus.Sent =>
                    $"✓ Đã phát hành. Mã CQT: {result.CqtCode}, số HĐ: {result.InvoiceNo}.",
                EInvoiceStatus.Pending =>
                    "⏳ Chưa có mạng/NCC — đã đưa vào hàng đợi, phát hành lại khi online.",
                EInvoiceStatus.Rejected => "⚠ Bị từ chối: " + result.ErrorMessage,
                _ => $"Trạng thái: {result.Status}.",
            };
            IssueOrderNumber = BuyerName = BuyerTaxCode = string.Empty;
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (NotFoundException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ProcessPending()
    {
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var r = await mediator.Send(new ProcessPendingEInvoicesCommand());
            StatusMessage = $"Đã xử lý {r.Processed}: phát hành {r.Issued}, từ chối {r.Rejected}, còn chờ {r.StillPending}.";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Revise(string type)
    {
        StatusMessage = string.Empty;
        if (Selected is null) { StatusMessage = "⚠ Chọn hóa đơn cần xử lý."; return; }
        if (string.IsNullOrWhiteSpace(ReviseReason)) { StatusMessage = "⚠ Nhập lý do điều chỉnh/thay thế/hủy."; return; }
        if (!Enum.TryParse<EInvoiceType>(type, out var t)) { StatusMessage = "⚠ Loại chứng từ không hợp lệ."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var r = await mediator.Send(new ReviseEInvoiceCommand
            {
                OriginalInvoiceId = Selected.Id,
                Type = t,
                Reason = ReviseReason.Trim(),
            });
            StatusMessage = r.Status is EInvoiceStatus.Rejected
                ? "⚠ Bị từ chối: " + r.ErrorMessage
                : $"✓ Đã lập chứng từ {TypeLabel(t)}. Trạng thái: {StatusLabel(r.Status)}.";
            ReviseReason = string.Empty;
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (NotFoundException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    private static string TypeLabel(EInvoiceType t) => t switch
    {
        EInvoiceType.Original => "Gốc",
        EInvoiceType.Adjust => "Điều chỉnh",
        EInvoiceType.Replace => "Thay thế",
        EInvoiceType.Cancel => "Hủy",
        _ => t.ToString(),
    };

    private static string StatusLabel(EInvoiceStatus s) => s switch
    {
        EInvoiceStatus.Pending => "Chờ phát hành",
        EInvoiceStatus.Issued => "Đã cấp mã",
        EInvoiceStatus.Sent => "Đã gửi CQT",
        EInvoiceStatus.Rejected => "Bị từ chối",
        EInvoiceStatus.Canceled => "Đã hủy",
        _ => s.ToString(),
    };
}

/// <summary>Dòng bảng kê HĐĐT (B11) — đã giải số đơn &amp; nhãn tiếng Việt.</summary>
public sealed record InvoiceRow(
    Guid Id, Guid OrderId, string OrderNumber, string TypeText, string StatusText,
    EInvoiceStatus Status, string? CqtCode, string? InvoiceNo, string? Serial,
    DateTime? IssuedAt, string? ErrorMessage);
