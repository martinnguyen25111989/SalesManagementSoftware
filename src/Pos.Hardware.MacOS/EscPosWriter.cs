using System.Text;

namespace Pos.Hardware.MacOS;

/// <summary>
/// Bộ ghi lệnh ESC/POS (fluent) — tạo byte stream gửi thẳng tới máy in nhiệt.
/// Tham chiếu tập lệnh Epson ESC/POS; tương thích đa số máy in tương thích (Xprinter, Gprinter…).
/// </summary>
public sealed class EscPosWriter
{
    private const byte ESC = 0x1B;
    private const byte GS = 0x1D;

    private readonly List<byte> _buf = new();
    private readonly Encoding _encoding;

    /// <param name="encoding">
    /// Bảng mã ký tự cho máy in. Tiếng Việt cần đúng code page của máy (vd CP1258/TCVN);
    /// mặc định Latin1 để an toàn. Map dấu tiếng Việt là việc làm tiếp theo.
    /// </param>
    public EscPosWriter(Encoding? encoding = null) => _encoding = encoding ?? Encoding.Latin1;

    public enum Align { Left = 0, Center = 1, Right = 2 }

    /// <summary>ESC @ — reset máy in về mặc định.</summary>
    public EscPosWriter Initialize() => Raw(ESC, (byte)'@');

    public EscPosWriter SetAlign(Align a) => Raw(ESC, (byte)'a', (byte)a);

    /// <summary>ESC E n — in đậm.</summary>
    public EscPosWriter Bold(bool on) => Raw(ESC, (byte)'E', (byte)(on ? 1 : 0));

    /// <summary>GS ! n — nhân đôi chiều rộng/cao (n=0x11 cho cả 2).</summary>
    public EscPosWriter DoubleSize(bool on) => Raw(GS, (byte)'!', (byte)(on ? 0x11 : 0x00));

    public EscPosWriter Text(string text)
    {
        _buf.AddRange(_encoding.GetBytes(text));
        return this;
    }

    public EscPosWriter Line(string text = "") => Text(text).Raw((byte)'\n');

    /// <summary>ESC d n — đẩy giấy n dòng.</summary>
    public EscPosWriter Feed(byte lines = 1) => Raw(ESC, (byte)'d', lines);

    /// <summary>GS V 66 0 — cắt giấy một phần (kèm đẩy giấy).</summary>
    public EscPosWriter Cut() => Raw(GS, (byte)'V', 66, 0);

    /// <summary>ESC p m t1 t2 — gửi xung mở két (cash drawer kick), chân 2 (0x00).</summary>
    public EscPosWriter OpenDrawer() => Raw(ESC, (byte)'p', 0x00, 0x19, 0xFA);

    /// <summary>In mã QR bằng tập lệnh GS ( k (Model 2). Bỏ qua nếu nội dung rỗng.</summary>
    public EscPosWriter Qr(string? content, byte moduleSize = 6)
    {
        if (string.IsNullOrEmpty(content)) return this;

        // Model: GS ( k pL pH cn fn n1 n2  (fn=65, chọn model 2)
        Raw(GS, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0);
        // Kích thước module: GS ( k 3 0 49 67 n
        Raw(GS, (byte)'(', (byte)'k', 3, 0, 49, 67, moduleSize);
        // Mức sửa lỗi: GS ( k 3 0 49 69 48 (L)
        Raw(GS, (byte)'(', (byte)'k', 3, 0, 49, 69, 48);

        // Nạp dữ liệu: GS ( k pL pH 49 80 48 d...
        var data = _encoding.GetBytes(content);
        int len = data.Length + 3;
        Raw(GS, (byte)'(', (byte)'k', (byte)(len & 0xFF), (byte)((len >> 8) & 0xFF), 49, 80, 48);
        _buf.AddRange(data);

        // In: GS ( k 3 0 49 81 48
        return Raw(GS, (byte)'(', (byte)'k', 3, 0, 49, 81, 48);
    }

    public EscPosWriter Raw(params byte[] bytes)
    {
        _buf.AddRange(bytes);
        return this;
    }

    public byte[] ToArray() => _buf.ToArray();
}
