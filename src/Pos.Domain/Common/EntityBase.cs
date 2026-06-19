namespace Pos.Domain.Common;

/// <summary>
/// Base cho mọi entity. PK = GUID sinh ở client (B14.6 — tránh trùng khi 2 máy offline).
/// Kèm cột kỹ thuật phục vụ offline-sync.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency / thứ tự ghi cho sync.</summary>
    public int Version { get; set; }

    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
}

/// <summary>
/// Bảng giao dịch: thêm StoreId + DeviceId (máy nào tạo record) phục vụ sync (B14.6).
/// </summary>
public abstract class TransactionEntity : EntityBase
{
    public Guid StoreId { get; set; }
    public string? DeviceId { get; set; }
}
