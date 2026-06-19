namespace Pos.Hardware.Abstractions;

/// <summary>
/// Két đựng tiền. Thường mở bằng lệnh kick gửi qua máy in (ESC/POS: 0x1B 0x70 0x00 ...).
/// </summary>
public interface ICashDrawer
{
    Task OpenAsync(CancellationToken ct = default);
}
