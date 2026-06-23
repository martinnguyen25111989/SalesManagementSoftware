using Pos.Domain.Common;

namespace Pos.Application.Invoicing.Abstractions;

/// <summary>
/// NCC HĐĐT mặc định khi CHƯA cấu hình adapter thật (giai đoạn sau mới cắm EasyInvoice + đăng ký
/// máy tính tiền kết nối thuế). Coi như "offline": mọi yêu cầu trả TransientError → đơn ở hàng đợi
/// Pending (in phiếu tạm), chờ phát hành khi adapter thật sẵn sàng. KHÔNG cấp mã CQT giả.
/// </summary>
public sealed class NullEInvoiceProvider : IEInvoiceProvider
{
    private const string Msg = "Chưa cấu hình nhà cung cấp HĐĐT (máy tính tiền kết nối thuế cài ở giai đoạn sau).";

    public Task<EInvoiceResult> IssueAsync(EInvoiceRequest req, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceResult.Transient(Msg));

    public Task<EInvoiceResult> AdjustAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceResult.Transient(Msg));

    public Task<EInvoiceResult> ReplaceAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceResult.Transient(Msg));

    public Task<EInvoiceResult> CancelAsync(string invoiceKey, string reason, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceResult.Transient(Msg));

    public Task<EInvoiceStatus> QueryAsync(string invoiceKey, CancellationToken ct = default) =>
        Task.FromResult(EInvoiceStatus.Pending);
}
