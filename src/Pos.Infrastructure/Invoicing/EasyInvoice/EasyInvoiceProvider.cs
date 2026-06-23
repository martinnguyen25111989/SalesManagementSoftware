using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Pos.Application.Invoicing.Abstractions;
using Pos.Domain.Common;

namespace Pos.Infrastructure.Invoicing.EasyInvoice;

/// <summary>
/// Adapter HĐĐT EasyInvoice/SoftDreams (B11-A) — hiện thực <see cref="IEInvoiceProvider"/>:
/// quản lý token Bearer (A.2), gọi API phát hành/điều chỉnh/thay thế/hủy/tra cứu (A.3/A.5/A.8),
/// phân loại lỗi nghiệp vụ ≠ lỗi mạng và xử lý 401/timeout/trùng transactionId (A.7).
/// ⚠️ Endpoint &amp; field theo MẪU BusinessRules.md — xác nhận tài liệu SoftDreams trước go-live.
/// </summary>
public sealed class EasyInvoiceProvider : IEInvoiceProvider
{
    private readonly HttpClient _http;
    private readonly EInvoiceOptions _opt;
    private readonly EasyInvoiceTokenCache _tokens;

    public EasyInvoiceProvider(HttpClient http, IOptions<EInvoiceOptions> opt, EasyInvoiceTokenCache tokens)
    {
        _http = http;
        _opt = opt.Value;
        _tokens = tokens;
    }

    public Task<EInvoiceResult> IssueAsync(EInvoiceRequest req, CancellationToken ct = default) =>
        PostInvoiceAsync(_opt.CreateInvoicePath, req, originalKey: null, ct);

    public Task<EInvoiceResult> AdjustAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default) =>
        PostInvoiceAsync(_opt.AdjustInvoicePath, req, originalKey, ct);

    public Task<EInvoiceResult> ReplaceAsync(string originalKey, EInvoiceRequest req, CancellationToken ct = default) =>
        PostInvoiceAsync(_opt.ReplaceInvoicePath, req, originalKey, ct);

    public async Task<EInvoiceResult> CancelAsync(string invoiceKey, string reason, CancellationToken ct = default)
    {
        var body = new { invoiceKey, reason };
        try
        {
            using var resp = await SendWithAuthAsync(HttpMethod.Post, _opt.CancelInvoicePath, body, ct);
            if (resp.IsSuccessStatusCode)
                return EInvoiceResult.Issued(string.Empty, string.Empty, string.Empty, invoiceKey);
            return await ClassifyErrorAsync(resp, ct);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return EInvoiceResult.Transient(ex.Message);
        }
    }

    public async Task<EInvoiceStatus> QueryAsync(string invoiceKey, CancellationToken ct = default)
    {
        var path = $"{_opt.GetStatusPath}?key={Uri.EscapeDataString(invoiceKey)}";
        using var resp = await SendWithAuthAsync(HttpMethod.Get, path, body: null, ct);
        if (!resp.IsSuccessStatusCode) return EInvoiceStatus.Pending;
        var status = await resp.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: ct);
        return MapStatus(status?.status);
    }

    private async Task<EInvoiceResult> PostInvoiceAsync(
        string path, EInvoiceRequest req, string? originalKey, CancellationToken ct)
    {
        var payload = EasyInvoiceMapper.ToPayload(req, _opt, originalKey);
        try
        {
            using var resp = await SendWithAuthAsync(HttpMethod.Post, path, payload, ct);

            if (resp.IsSuccessStatusCode)
                return await ParseIssuedAsync(resp, req, ct);

            // A.7: trùng transactionId (đã phát hành) → tra cứu lấy lại mã CQT, KHÔNG tạo mới.
            if (resp.StatusCode == HttpStatusCode.Conflict)
                return await ReconcileDuplicateAsync(req, ct);

            return await ClassifyErrorAsync(resp, ct);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            // A.7: timeout/5xx/mạng → transient, giữ hàng đợi để retry.
            return EInvoiceResult.Transient(ex.Message);
        }
    }

    private async Task<EInvoiceResult> ParseIssuedAsync(
        HttpResponseMessage resp, EInvoiceRequest req, CancellationToken ct)
    {
        var body = await resp.Content.ReadFromJsonAsync<CreateInvoiceResponse>(cancellationToken: ct);
        var cqt = body?.ResolvedCqt;
        if (string.IsNullOrEmpty(cqt))
            // 2xx nhưng không có mã CQT → coi là lỗi nghiệp vụ (không lưu hóa đơn "treo").
            return EInvoiceResult.Business(body?.message ?? "Phản hồi không có mã CQT.");

        return EInvoiceResult.Issued(
            cqtCode: cqt,
            invoiceNo: body!.ResolvedInvoiceNo ?? string.Empty,
            serial: body.serial ?? req.Serial,
            providerRef: body.providerKey ?? body.transactionId ?? req.TransactionId.ToString());
    }

    /// <summary>A.7: đã gửi trước đó → QueryAsync; nếu CQT có thì coi như Issued, ngược lại giữ transient.</summary>
    private async Task<EInvoiceResult> ReconcileDuplicateAsync(EInvoiceRequest req, CancellationToken ct)
    {
        var key = req.TransactionId.ToString();
        var path = $"{_opt.GetStatusPath}?key={Uri.EscapeDataString(key)}";
        using var resp = await SendWithAuthAsync(HttpMethod.Get, path, body: null, ct);
        if (resp.IsSuccessStatusCode)
        {
            var st = await resp.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: ct);
            var cqt = st?.cqtCode ?? st?.taxAuthorityCode;
            if (!string.IsNullOrEmpty(cqt))
                return EInvoiceResult.Issued(cqt, string.Empty, req.Serial, key);
        }
        return EInvoiceResult.Transient("Trùng transactionId nhưng chưa lấy được mã CQT — sẽ thử lại.");
    }

    /// <summary>Gửi request kèm Bearer; nếu 401 thì login lại 1 lần rồi gửi lại (A.7).</summary>
    private async Task<HttpResponseMessage> SendWithAuthAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await GetTokenAsync(forceRefresh: false, ct);
        var resp = await SendAsync(method, path, body, token, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            resp.Dispose();
            token = await GetTokenAsync(forceRefresh: true, ct);
            resp = await SendAsync(method, path, body, token, ct);
        }
        return resp;
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            msg.Content = JsonContent.Create(body, body.GetType());
        return _http.SendAsync(msg, ct);
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _tokens.IsValid)
            return _tokens.Token!;

        await _tokens.Gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _tokens.IsValid)
                return _tokens.Token!;

            using var resp = await _http.PostAsJsonAsync(
                _opt.LoginPath, new LoginPayload(_opt.Username, _opt.Password), ct);
            if (!resp.IsSuccessStatusCode)
                throw new EInvoiceAuthException($"Đăng nhập EasyInvoice thất bại ({(int)resp.StatusCode}).");

            var login = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            var value = login?.Value
                ?? throw new EInvoiceAuthException("Phản hồi đăng nhập không có token.");

            var ttl = TimeSpan.FromSeconds(login!.expires_in ?? _opt.DefaultTokenTtlSeconds);
            _tokens.Set(value, ttl);
            return value;
        }
        finally
        {
            _tokens.Gate.Release();
        }
    }

    private static async Task<EInvoiceResult> ClassifyErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string detail = await SafeReadAsync(resp, ct);
        // A.7: 4xx (sai thuế/MST/field) = lỗi nghiệp vụ, KHÔNG retry mù; 5xx = transient.
        return (int)resp.StatusCode >= 500
            ? EInvoiceResult.Transient($"Lỗi NCC {(int)resp.StatusCode}: {detail}")
            : EInvoiceResult.Business($"Bị từ chối {(int)resp.StatusCode}: {detail}");
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return resp.ReasonPhrase ?? string.Empty; }
    }

    /// <summary>Lỗi mạng/timeout/đăng nhập (không phải lỗi nghiệp vụ) → transient, retry được.</summary>
    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException or EInvoiceAuthException;

    private static EInvoiceStatus MapStatus(string? s) => s?.ToLowerInvariant() switch
    {
        "issued" or "signed" or "approved" => EInvoiceStatus.Issued,
        "sent" => EInvoiceStatus.Sent,
        "rejected" or "error" => EInvoiceStatus.Rejected,
        "canceled" or "cancelled" => EInvoiceStatus.Canceled,
        _ => EInvoiceStatus.Pending,
    };
}

/// <summary>Lỗi xác thực với NCC HĐĐT (token) — coi là transient ở tầng gọi.</summary>
public sealed class EInvoiceAuthException : Exception
{
    public EInvoiceAuthException(string message) : base(message) { }
}
