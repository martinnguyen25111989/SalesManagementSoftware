using Pos.Domain.Common;

namespace Pos.Domain.Organization;

public class Tenant : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }

    public ICollection<Store> Stores { get; set; } = new List<Store>();
}

public class Store : EntityBase
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Prefix mã đơn riêng chi nhánh, vd HCM01.</summary>
    public string OrderPrefix { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Address { get; set; }

    /// <summary>B8 — xử lý khi bán làm tồn về âm (mặc định Allow cho offline-first).</summary>
    public NegativeStockPolicy NegativeStockPolicy { get; set; } = NegativeStockPolicy.Allow;

    /// <summary>B10 — số VND doanh thu cho 1 điểm tích lũy (0 = tắt tích điểm).</summary>
    public decimal LoyaltyVndPerPoint { get; set; }

    /// <summary>B10 — true = tích điểm trên tổng có thuế (GrandTotal); false = trên doanh thu sau CK trước thuế.</summary>
    public bool LoyaltyEarnOnGrandTotal { get; set; }

    public ICollection<Register> Registers { get; set; } = new List<Register>();
}

/// <summary>Máy POS (Register/Device).</summary>
public class Register : EntityBase
{
    public Guid StoreId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public class User : EntityBase
{
    public Guid? StoreId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role : EntityBase
{
    public string Name { get; set; } = string.Empty; // Owner/Manager/Cashier/Warehouse/Accountant
}

public class Permission : EntityBase
{
    /// <summary>vd discount.over_threshold, order.void_paid, return.refund, drawer.open, stock.adjust.</summary>
    public string Code { get; set; } = string.Empty;
}

public class UserRole : EntityBase
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

public class RolePermission : EntityBase
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
