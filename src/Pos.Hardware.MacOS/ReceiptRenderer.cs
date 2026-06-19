using System.Globalization;
using System.Text;
using Pos.Hardware.Abstractions;

namespace Pos.Hardware.MacOS;

/// <summary>Chuyển <see cref="ReceiptDocument"/> (độc lập driver) thành byte ESC/POS.</summary>
internal static class ReceiptRenderer
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static byte[] Render(ReceiptDocument doc, Encoding encoding)
    {
        int cols = doc.PaperWidthMm <= 58 ? 32 : 48;
        var w = new EscPosWriter(encoding);

        w.Initialize();

        // Header
        w.SetAlign(EscPosWriter.Align.Center).Bold(true).DoubleSize(true)
            .Line(doc.StoreName)
            .DoubleSize(false).Bold(false);
        if (!string.IsNullOrWhiteSpace(doc.Address)) w.Line(doc.Address);
        if (!string.IsNullOrWhiteSpace(doc.TaxCode)) w.Line($"MST: {doc.TaxCode}");

        w.SetAlign(EscPosWriter.Align.Left)
            .Line(new string('-', cols))
            .Line($"So: {doc.OrderNumber}")
            .Line($"Ngay: {doc.IssuedAt:dd/MM/yyyy HH:mm}")
            .Line(new string('-', cols));

        // Lines: tên ở dòng trên; "qty x đơn giá" trái, thành tiền phải ở dòng dưới
        foreach (var l in doc.Lines)
        {
            w.Line(l.Name);
            var left = $"  {Num(l.Qty)} x {Num(l.UnitPrice)}";
            w.Line(TwoCols(left, Num(l.LineTotal), cols));
        }

        w.Line(new string('-', cols));
        w.Line(TwoCols("Tam tinh", Num(doc.Subtotal), cols));
        if (doc.DiscountTotal != 0) w.Line(TwoCols("Chiet khau", "-" + Num(doc.DiscountTotal), cols));
        w.Line(TwoCols("Thue VAT", Num(doc.TaxTotal), cols));
        w.Bold(true).Line(TwoCols("TONG CONG", Num(doc.GrandTotal), cols)).Bold(false);

        // QR tra cứu HĐĐT / VietQR
        if (!string.IsNullOrEmpty(doc.QrContent))
        {
            w.Feed(1).SetAlign(EscPosWriter.Align.Center).Qr(doc.QrContent).Feed(1)
                .SetAlign(EscPosWriter.Align.Left);
        }

        if (!string.IsNullOrWhiteSpace(doc.Footer))
            w.SetAlign(EscPosWriter.Align.Center).Line(doc.Footer).SetAlign(EscPosWriter.Align.Left);

        w.Feed(3);
        if (doc.OpenDrawerAfterPrint) w.OpenDrawer();
        w.Cut();

        return w.ToArray();
    }

    private static string Num(decimal v) => v.ToString("#,##0", Vi);

    /// <summary>Căn 2 cột: nhãn trái, giá trị sát phải, đệm khoảng trắng ở giữa.</summary>
    private static string TwoCols(string left, string right, int cols)
    {
        if (left.Length + right.Length >= cols)
            return (left + " " + right);
        return left + new string(' ', cols - left.Length - right.Length) + right;
    }
}
