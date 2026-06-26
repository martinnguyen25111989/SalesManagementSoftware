using MediatR;

namespace Pos.Application.Auth;

/// <summary>Mã quyền theo hành động (B2) — khớp với mã seed ở Infrastructure. Dùng để gate màn hình/thao tác.</summary>
public static class PosPermissions
{
    public const string ProductManage = "product.manage";
    public const string PriceManage = "price.manage";
    public const string StockAdjust = "stock.adjust";
    public const string ReportView = "report.view";
    public const string UserManage = "user.manage";
    public const string ReturnRefund = "return.refund";
    public const string ShiftCloseVariance = "shift.close_variance";
}

/// <summary>Tên 5 role mặc định (B2).</summary>
public static class PosRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Cashier = "Cashier";
    public const string Warehouse = "Warehouse";
    public const string Accountant = "Accountant";
}

/// <summary>Kết quả xác thực — danh tính + role + quyền (để client gate khu quản trị, không gọi lại DB).</summary>
public sealed record AuthResult(
    Guid UserId,
    string Username,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>Đăng nhập POS (B2): xác minh username + mật khẩu (PBKDF2), trả về role &amp; quyền.</summary>
public sealed record LoginCommand(string Username, string Password) : IRequest<AuthResult>;

/// <summary>
/// Tạo tài khoản người dùng (B2) với mật khẩu băm + 1 role. Dùng cho thiết lập tài khoản quản trị lần đầu
/// và quản lý người dùng sau này. Username là duy nhất.
/// </summary>
public sealed record RegisterUserCommand : IRequest<AuthResult>
{
    public string FullName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string RoleName { get; init; } = PosRoles.Cashier;
    public Guid? StoreId { get; init; }
}

/// <summary>Đã có tài khoản đăng nhập được nào chưa (quyết định màn thiết lập lần đầu vs đăng nhập).</summary>
public sealed record HasLoginAccountQuery : IRequest<bool>;
