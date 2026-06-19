using System.Text;

namespace Pos.Hardware.MacOS;

/// <summary>Cấu hình máy in mạng (ESC/POS qua TCP, cổng RAW 9100).</summary>
public sealed class NetworkPrinterOptions
{
    /// <summary>IP/hostname của máy in hoặc print server.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Cổng RAW/JetDirect tiêu chuẩn cho máy in nhiệt.</summary>
    public int Port { get; set; } = 9100;

    /// <summary>Timeout kết nối/gửi (ms).</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Bảng mã ký tự gửi tới máy in. Mặc định Latin1 (an toàn, chưa map dấu VN).</summary>
    public Encoding Encoding { get; set; } = Encoding.Latin1;
}
