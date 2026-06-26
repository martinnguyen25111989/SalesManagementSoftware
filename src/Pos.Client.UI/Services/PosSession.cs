using System;
using System.Collections.Generic;
using System.Linq;
using Pos.Application.Auth;

namespace Pos.Client.UI.Services;

/// <summary>
/// Ngữ cảnh phiên hiện tại (chi nhánh / máy / ca / người đăng nhập + quyền) — thiết lập khi khởi động &amp;
/// sau đăng nhập, dùng cho mọi lệnh tạo đơn, chốt đơn (B4/B9) và phân quyền màn quản trị (B2).
/// </summary>
public sealed class PosSession
{
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "Chi nhánh";

    /// <summary>Nguồn dữ liệu đang dùng (PostgreSQL online / SQLite offline) để hiển thị &amp; chẩn đoán.</summary>
    public string Backend { get; set; } = "SQLite (offline)";

    public Guid RegisterId { get; set; }
    public Guid ShiftId { get; set; }
    public string DeviceId { get; set; } = Environment.MachineName;

    // ── Người đăng nhập (B2) ──────────────────────────────────────────────
    public Guid UserId { get; private set; }
    public string UserFullName { get; private set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; private set; } = Array.Empty<string>();

    public bool IsAuthenticated => UserId != Guid.Empty;

    /// <summary>Thu ngân = người đang đăng nhập (gắn vào Order/Shift).</summary>
    public Guid CashierId => UserId;
    public string CashierName => UserFullName;

    /// <summary>Có quyền vào khu QUẢN TRỊ (quản lý sản phẩm hoặc kho) — B2 phân quyền theo hành động.</summary>
    public bool CanManageAdmin =>
        Has(PosPermissions.ProductManage) || Has(PosPermissions.StockAdjust) || Has(PosPermissions.PriceManage);

    public bool Has(string permission) => Permissions.Contains(permission);

    public bool IsReady => StoreId != Guid.Empty && ShiftId != Guid.Empty && IsAuthenticated;

    public void SignIn(AuthResult auth)
    {
        UserId = auth.UserId;
        UserFullName = auth.FullName;
        Roles = auth.Roles;
        Permissions = auth.Permissions;
    }

    public void SignOut()
    {
        UserId = Guid.Empty;
        UserFullName = string.Empty;
        Roles = Array.Empty<string>();
        Permissions = Array.Empty<string>();
        ShiftId = Guid.Empty;
    }
}
