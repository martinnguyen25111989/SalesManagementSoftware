using Pos.Domain.Common;

namespace Pos.Application.Common;

public static class EntityExtensions
{
    /// <summary>
    /// Đánh dấu entity vừa bị sửa: cập nhật mốc thời gian, tăng Version (optimistic concurrency)
    /// và đặt SyncStatus = Pending để engine offline-sync đẩy lên server.
    /// </summary>
    public static void MarkModified(this EntityBase e)
    {
        e.LastModifiedUtc = DateTime.UtcNow;
        e.Version++;
        e.SyncStatus = SyncStatus.Pending;
    }
}
