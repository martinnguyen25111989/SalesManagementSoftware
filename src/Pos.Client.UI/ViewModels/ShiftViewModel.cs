using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Common;
using Pos.Application.Shifts.CashMovements;
using Pos.Application.Shifts.CloseShift;
using Pos.Application.Shifts.OpenShift;
using Pos.Application.Shifts.Reports;
using Pos.Client.UI.Services;
using Pos.Domain.Common;

namespace Pos.Client.UI.ViewModels;

/// <summary>
/// Màn hình Ca làm việc &amp; Quỹ tiền (B9): xem X-report (ca đang mở) / Z-report (đã đóng), ghi thu/chi
/// tiền mặt trong ca, và đóng ca (đếm quỹ cuối → Variance). Lệch quỹ vượt ngưỡng cần Manager duyệt (B2).
/// </summary>
public partial class ShiftViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosSession _session;

    public ShiftViewModel(IServiceScopeFactory scopes, PosSession session)
    {
        _scopes = scopes;
        _session = session;
    }

    public ShiftViewModel() : this(null!, null!) { }

    // ── Báo cáo ca hiện tại ────────────────────────────────────────────────
    [ObservableProperty] private bool _isClosed;
    [ObservableProperty] private DateTimeOffset _openAt;
    [ObservableProperty] private DateTimeOffset? _closeAt;
    [ObservableProperty] private decimal _openingFloat;
    [ObservableProperty] private decimal _cashSales;
    [ObservableProperty] private decimal _cashIn;
    [ObservableProperty] private decimal _cashOut;
    [ObservableProperty] private decimal _cashRefunds;
    [ObservableProperty] private decimal _expectedCash;
    [ObservableProperty] private decimal? _countedCashReport;
    [ObservableProperty] private decimal? _variance;
    [ObservableProperty] private int _orderCount;
    [ObservableProperty] private decimal _grandTotalSales;
    [ObservableProperty] private string _reportKind = "X-report";

    public ObservableCollection<PaymentRow> Payments { get; } = new();

    // ── Thu/chi tiền mặt trong ca ──────────────────────────────────────────
    [ObservableProperty] private bool _isCashIn = true;
    [ObservableProperty] private decimal _movementAmount;
    [ObservableProperty] private string _movementReason = string.Empty;

    // ── Đóng ca ────────────────────────────────────────────────────────────
    [ObservableProperty] private decimal _countedCash;
    [ObservableProperty] private bool _managerApproved;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool CanCloseVariance => _session.Has(PosPermissions.ShiftCloseVariance);

    public async Task ActivateAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_session.ShiftId == Guid.Empty) { StatusMessage = "Chưa có ca làm việc."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var r = await mediator.Send(new GetShiftReportQuery(_session.ShiftId));

            IsClosed = r.IsClosed;
            ReportKind = r.IsClosed ? "Z-report (ca đã chốt)" : "X-report (ca đang mở)";
            OpenAt = new DateTimeOffset(DateTime.SpecifyKind(r.OpenAt, DateTimeKind.Utc).ToLocalTime());
            CloseAt = r.CloseAt is null ? null
                : new DateTimeOffset(DateTime.SpecifyKind(r.CloseAt.Value, DateTimeKind.Utc).ToLocalTime());
            OpeningFloat = r.OpeningFloat;
            CashSales = r.CashSales;
            CashIn = r.CashIn;
            CashOut = r.CashOut;
            CashRefunds = r.CashRefunds;
            ExpectedCash = r.ExpectedCash;
            CountedCashReport = r.CountedCash;
            Variance = r.Variance;
            OrderCount = r.OrderCount;
            GrandTotalSales = r.GrandTotalSales;

            Payments.Clear();
            foreach (var kv in r.PaymentsByMethod)
                Payments.Add(new PaymentRow(PaymentMethodNames.Label(kv.Key), kv.Value, 0m, kv.Value));

            OnPropertyChanged(nameof(CanCloseVariance));
        }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task RecordMovement()
    {
        StatusMessage = string.Empty;
        if (MovementAmount <= 0m) { StatusMessage = "⚠ Số tiền phải lớn hơn 0."; return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new RecordCashMovementCommand
            {
                ShiftId = _session.ShiftId,
                Type = IsCashIn ? CashMovementType.In : CashMovementType.Out,
                Amount = MovementAmount,
                Reason = string.IsNullOrWhiteSpace(MovementReason) ? null : MovementReason.Trim(),
            });
            StatusMessage = $"✓ Đã ghi {(IsCashIn ? "thu" : "chi")} {MovementAmount:N0} đ.";
            MovementAmount = 0m;
            MovementReason = string.Empty;
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CloseShift()
    {
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var z = await mediator.Send(new CloseShiftCommand
            {
                ShiftId = _session.ShiftId,
                CountedCash = CountedCash,
                ManagerApproved = ManagerApproved,
            });
            StatusMessage = $"✓ Đã chốt ca. Lệch quỹ: {z.Variance:N0} đ.";
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task OpenNewShift()
    {
        StatusMessage = string.Empty;
        try
        {
            using var scope = _scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new OpenShiftCommand
            {
                StoreId = _session.StoreId,
                RegisterId = _session.RegisterId,
                CashierId = _session.CashierId,
                DeviceId = _session.DeviceId,
                OpeningFloat = OpeningFloat,
            });
            _session.ShiftId = result.Id;
            CountedCash = 0m;
            ManagerApproved = false;
            StatusMessage = "✓ Đã mở ca mới.";
            await LoadAsync();
        }
        catch (BusinessRuleException ex) { StatusMessage = "⚠ " + ex.Message; }
        catch (Exception ex) { StatusMessage = "Lỗi: " + ex.Message; }
    }

}
