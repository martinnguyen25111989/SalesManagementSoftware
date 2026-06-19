namespace Pos.Hardware.Abstractions;

/// <summary>
/// Máy quét mã vạch. Khuyến nghị loại HID keyboard-wedge (đa nền tảng tự nhiên).
/// Loại serial/USB-COM cần implementation riêng theo OS.
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>Phát ra khi quét được một mã (giá trị mã vạch).</summary>
    event EventHandler<string> BarcodeScanned;

    void Start();
    void Stop();
}
