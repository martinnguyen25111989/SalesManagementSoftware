using System.Text;

namespace Pos.Application.Invoicing;

/// <summary>
/// Đọc số tiền VND thành chữ (B11 — HĐĐT phải có "tổng thanh toán bằng chữ"). VND không có phần lẻ
/// → làm tròn về đồng. Hỗ trợ tới hàng tỷ tỷ.
/// </summary>
public static class VietnameseMoney
{
    private static readonly string[] Digits =
        { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };

    private static readonly string[] Scales =
        { "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ", "tỷ tỷ" };

    public static string ToWords(decimal amount)
    {
        long n = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (n == 0) return "Không đồng";

        var sb = new StringBuilder();
        if (n < 0) { sb.Append("âm "); n = Math.Abs(n); }

        // Tách thành các nhóm 3 chữ số (từ thấp lên cao).
        var groups = new List<int>();
        while (n > 0) { groups.Add((int)(n % 1000)); n /= 1000; }

        bool firstSpoken = true;
        for (int g = groups.Count - 1; g >= 0; g--)
        {
            if (groups[g] == 0) continue;
            string words = ReadGroup(groups[g], full: !firstSpoken);
            if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            sb.Append(words);
            if (!string.IsNullOrEmpty(Scales[g])) sb.Append(' ').Append(Scales[g]);
            firstSpoken = false;
        }

        sb.Append(" đồng");
        var result = sb.ToString().Trim();
        return char.ToUpper(result[0]) + result[1..];
    }

    /// <param name="full">true = nhóm phía sau nhóm cao nhất → đọc đủ "không trăm/lẻ" để nối liền mạch.</param>
    private static string ReadGroup(int g, bool full)
    {
        int hundreds = g / 100, tens = (g % 100) / 10, units = g % 10;
        var sb = new StringBuilder();

        if (hundreds > 0 || full)
            sb.Append(Digits[hundreds]).Append(" trăm");

        if (tens == 0)
        {
            if (units > 0)
                sb.Append(sb.Length > 0 ? " lẻ " : "").Append(Digits[units]);
        }
        else if (tens == 1)
        {
            sb.Append(sb.Length > 0 ? " " : "").Append("mười");
            if (units == 5) sb.Append(" lăm");
            else if (units > 0) sb.Append(' ').Append(Digits[units]);
        }
        else
        {
            sb.Append(sb.Length > 0 ? " " : "").Append(Digits[tens]).Append(" mươi");
            if (units == 1) sb.Append(" mốt");
            else if (units == 5) sb.Append(" lăm");
            else if (units > 0) sb.Append(' ').Append(Digits[units]);
        }

        return sb.ToString().Trim();
    }
}
