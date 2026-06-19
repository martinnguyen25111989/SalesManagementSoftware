using Pos.Hardware.Abstractions;

namespace Pos.Hardware.MacOS;

/// <summary>
/// Két tiền nối sau máy in mạng — mở bằng xung kick ESC/POS (ESC p) gửi qua chính máy in.
/// Mở két không gắn giao dịch là hành động nhạy cảm (B2) → kiểm tra quyền ở tầng Application.
/// </summary>
public sealed class NetworkCashDrawer : ICashDrawer
{
    private readonly NetworkReceiptPrinter _printer;

    public NetworkCashDrawer(NetworkReceiptPrinter printer) => _printer = printer;

    public Task OpenAsync(CancellationToken ct = default) => _printer.KickDrawerAsync(ct);
}
