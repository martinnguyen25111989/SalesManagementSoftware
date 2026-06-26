using Pos.Domain.Common;

namespace Pos.Client.UI.Services;

/// <summary>Nhãn tiếng Việt cho phương thức thanh toán (B6) — dùng chung cho báo cáo, ca &amp; đối soát.</summary>
public static class PaymentMethodNames
{
    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.Card => "Thẻ",
        PaymentMethod.VietQR => "VietQR",
        PaymentMethod.Wallet => "Ví điện tử",
        PaymentMethod.Debt => "Ghi nợ",
        PaymentMethod.Point => "Điểm",
        _ => method.ToString(),
    };
}
