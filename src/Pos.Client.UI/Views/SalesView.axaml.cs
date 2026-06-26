using System;
using Avalonia.Controls;
using Pos.Client.UI.ViewModels;

namespace Pos.Client.UI.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();

        // Gợi ý khách hàng khớp theo TÊN hoặc SĐT (B10). ItemFilter nhận trực tiếp item nên
        // so khớp được cả Phone — FilterMode mặc định chỉ so theo chuỗi hiển thị (tên).
        var box = this.FindControl<AutoCompleteBox>("CustomerSearch");
        if (box is not null)
            box.ItemFilter = (search, item) =>
            {
                if (string.IsNullOrWhiteSpace(search)) return true;
                if (item is not CustomerSuggestion c) return false;
                var s = search.Trim();
                return (c.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.Phone?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
            };
    }
}
