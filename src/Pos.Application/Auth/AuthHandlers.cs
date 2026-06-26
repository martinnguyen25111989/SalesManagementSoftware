using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Organization;

namespace Pos.Application.Auth;

public sealed class LoginHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IPosDbContext _db;

    public LoginHandler(IPosDbContext db) => _db = db;

    public async Task<AuthResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var username = (cmd.Username ?? string.Empty).Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        // Thông báo gộp (không lộ username tồn tại hay không).
        if (user is null || !user.IsActive || !PasswordHasher.Verify(cmd.Password, user.PasswordHash))
            throw new BusinessRuleException("Sai tên đăng nhập hoặc mật khẩu.");

        var (roles, permissions) = await LoadRolesAndPermissionsAsync(_db, user.Id, ct);
        return new AuthResult(user.Id, user.Username, user.FullName, roles, permissions);
    }

    internal static async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)>
        LoadRolesAndPermissionsAsync(IPosDbContext db, Guid userId, CancellationToken ct)
    {
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId).ToListAsync(ct);

        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name).ToListAsync(ct);

        var permIds = await db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId).Distinct().ToListAsync(ct);

        var permissions = await db.Permissions.Where(p => permIds.Contains(p.Id))
            .Select(p => p.Code).ToListAsync(ct);

        return (roles, permissions);
    }
}

public sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand, AuthResult>
{
    private readonly IPosDbContext _db;

    public RegisterUserHandler(IPosDbContext db) => _db = db;

    public async Task<AuthResult> Handle(RegisterUserCommand cmd, CancellationToken ct)
    {
        var fullName = (cmd.FullName ?? string.Empty).Trim();
        var username = (cmd.Username ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(fullName))
            throw new BusinessRuleException("Phải nhập họ tên.");
        if (string.IsNullOrWhiteSpace(username))
            throw new BusinessRuleException("Phải nhập tên đăng nhập.");
        if ((cmd.Password ?? string.Empty).Length < 6)
            throw new BusinessRuleException("Mật khẩu phải từ 6 ký tự trở lên.");

        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            throw new BusinessRuleException($"Tên đăng nhập '{username}' đã tồn tại.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == cmd.RoleName, ct)
            ?? throw new NotFoundException($"Không tìm thấy vai trò '{cmd.RoleName}'.");

        var user = new User
        {
            StoreId = cmd.StoreId,
            Username = username,
            FullName = fullName,
            PasswordHash = PasswordHasher.Hash(cmd.Password!),
            IsActive = true,
        };
        _db.Users.Add(user);
        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(ct);

        var (roles, permissions) = await LoginHandler.LoadRolesAndPermissionsAsync(_db, user.Id, ct);
        return new AuthResult(user.Id, user.Username, user.FullName, roles, permissions);
    }
}

public sealed class HasLoginAccountHandler : IRequestHandler<HasLoginAccountQuery, bool>
{
    private readonly IPosDbContext _db;

    public HasLoginAccountHandler(IPosDbContext db) => _db = db;

    public async Task<bool> Handle(HasLoginAccountQuery query, CancellationToken ct) =>
        // Tài khoản đăng nhập được = đã đặt mật khẩu PBKDF2 (bỏ qua user nền chưa có mật khẩu).
        await _db.Users.AnyAsync(u => u.IsActive && u.PasswordHash.StartsWith("pbkdf2."), ct);
}
