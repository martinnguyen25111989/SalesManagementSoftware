using Pos.Application.Pricing;
using Pos.Domain.Common;

namespace Pos.Application.Invoicing;

/// <summary>
/// Ánh xạ thuế suất nội bộ (B3) → nhãn thuế HĐĐT theo quy định VN (B11). Phần trăm dùng chung
/// <see cref="OrderCalculator.VatPercent"/> để KHÔNG lệch cách tính tiền với B5/B13.
/// </summary>
public static class EInvoiceTaxMap
{
    public static decimal Percent(VatRate rate) => OrderCalculator.VatPercent(rate);

    /// <summary>Nhãn thuế suất hiển thị/khai trên HĐĐT (KCT = không chịu thuế, KKKNT = không kê khai nộp thuế).</summary>
    public static string Label(VatRate rate) => rate switch
    {
        VatRate.Zero => "0%",
        VatRate.Five => "5%",
        VatRate.Eight => "8%",
        VatRate.Ten => "10%",
        VatRate.Exempt => "KCT",
        VatRate.NotDeclared => "KKKNT",
        _ => "KCT",
    };
}
