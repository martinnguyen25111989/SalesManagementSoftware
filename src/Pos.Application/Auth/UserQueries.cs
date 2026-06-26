using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Application.Auth;

/// <summary>Một dòng người dùng cho màn quản trị (B2): danh tính + vai trò + trạng thái.</summary>
public sealed record UserListItem(
    Guid Id,
    string Username,
    string FullName,
    string Roles,
    bool IsActive);

/// <summary>Danh sách người dùng (B2) — kèm vai trò để hiển thị phân quyền theo cấp.</summary>
public sealed record GetUsersQuery : IRequest<IReadOnlyList<UserListItem>>;

public sealed class GetUsersHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<UserListItem>>
{
    private readonly IPosDbContext _db;

    public GetUsersHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserListItem>> Handle(GetUsersQuery q, CancellationToken ct)
    {
        var users = await _db.Users.OrderBy(u => u.Username).ToListAsync(ct);

        // Gom vai trò theo người dùng (UserRole → Role.Name) trong bộ nhớ để chạy trên mọi provider.
        var userRoles = await _db.UserRoles.ToListAsync(ct);
        var roleNames = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var rolesByUser = userRoles
            .GroupBy(ur => ur.UserId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g
                    .Select(ur => roleNames.GetValueOrDefault(ur.RoleId, "?"))
                    .OrderBy(n => n)));

        return users
            .Select(u => new UserListItem(
                u.Id, u.Username, u.FullName,
                rolesByUser.GetValueOrDefault(u.Id, "(chưa gán)"),
                u.IsActive))
            .ToList();
    }
}

/// <summary>Danh sách tên vai trò (B2) cho ô chọn khi tạo người dùng.</summary>
public sealed record GetRolesQuery : IRequest<IReadOnlyList<string>>;

public sealed class GetRolesHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<string>>
{
    private readonly IPosDbContext _db;

    public GetRolesHandler(IPosDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> Handle(GetRolesQuery q, CancellationToken ct) =>
        await _db.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(ct);
}

/// <summary>Bật/tắt hoạt động của 1 tài khoản (B2) — không xóa cứng để giữ lịch sử &amp; ràng buộc giao dịch.</summary>
public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : IRequest;

public sealed class SetUserActiveHandler : IRequestHandler<SetUserActiveCommand>
{
    private readonly IPosDbContext _db;

    public SetUserActiveHandler(IPosDbContext db) => _db = db;

    public async Task Handle(SetUserActiveCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct)
            ?? throw new NotFoundException($"Không tìm thấy người dùng {cmd.UserId}.");

        // Chặn tự khóa tài khoản đăng nhập cuối cùng còn hoạt động (tránh khóa mình ra ngoài).
        if (!cmd.IsActive && user.IsActive)
        {
            int activeCount = await _db.Users.CountAsync(u => u.IsActive, ct);
            if (activeCount <= 1)
                throw new BusinessRuleException("Không thể khóa tài khoản hoạt động cuối cùng.");
        }

        user.IsActive = cmd.IsActive;
        user.MarkModified();
        await _db.SaveChangesAsync(ct);
    }
}
