using System.Net.Sockets;
using Pos.Hardware.Abstractions;

namespace Pos.Hardware.MacOS;

/// <summary>
/// Máy in hóa đơn ESC/POS qua mạng (TCP cổng 9100). Implementation HAL đầu tiên cho macOS —
/// in qua mạng nên chạy giống nhau mọi OS (Technical.md §10). ViewModel chỉ phụ thuộc
/// <see cref="IReceiptPrinter"/>, không gọi socket trực tiếp.
/// </summary>
public sealed class NetworkReceiptPrinter : IReceiptPrinter
{
    private readonly NetworkPrinterOptions _opt;

    public NetworkReceiptPrinter(NetworkPrinterOptions options) => _opt = options;

    public async Task PrintAsync(ReceiptDocument doc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var bytes = ReceiptRenderer.Render(doc, _opt.Encoding);
        await SendAsync(bytes, ct);
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(_opt.TimeoutMs);
            await client.ConnectAsync(_opt.Host, _opt.Port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Gửi xung mở két qua máy in (ESC p). Két thường nối sau máy in.</summary>
    internal Task KickDrawerAsync(CancellationToken ct = default)
        => SendAsync(new EscPosWriter().OpenDrawer().ToArray(), ct);

    private async Task SendAsync(byte[] payload, CancellationToken ct)
    {
        using var client = new TcpClient { SendTimeout = _opt.TimeoutMs };
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_opt.TimeoutMs);

        await client.ConnectAsync(_opt.Host, _opt.Port, connectCts.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }
}
