using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pos.Client.UI.Converters;

/// <summary>Định dạng tiền VND: nhóm hàng nghìn bằng dấu chấm, không phần lẻ (B13). Vd 1.250.000.</summary>
public sealed class VndConverter : IValueConverter
{
    public static readonly VndConverter Instance = new();
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal amount = value switch
        {
            decimal d => d,
            int i => i,
            double db => (decimal)db,
            _ => 0m,
        };
        return amount.ToString("#,##0", Vi);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && decimal.TryParse(s.Replace(".", "").Replace(",", ""),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0m;
    }
}
