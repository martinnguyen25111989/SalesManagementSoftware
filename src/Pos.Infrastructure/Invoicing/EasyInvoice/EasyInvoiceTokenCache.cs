namespace Pos.Infrastructure.Invoicing.EasyInvoice;

/// <summary>
/// Cache access token (Bearer) cho EasyInvoice (B11-A.2) — singleton, chia sẻ giữa các request để
/// không login lại mỗi lần. Có cửa (semaphore) tránh login đồng thời và trừ hao 30s tránh dùng token
/// sát hạn.
/// </summary>
public sealed class EasyInvoiceTokenCache
{
    private const int SkewSeconds = 30;

    public string? Token { get; private set; }
    public DateTime ExpiresUtc { get; private set; }

    /// <summary>Cửa serialize việc làm mới token.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public bool IsValid => Token is not null && DateTime.UtcNow < ExpiresUtc;

    public void Set(string token, TimeSpan ttl)
    {
        Token = token;
        ExpiresUtc = DateTime.UtcNow.Add(ttl).AddSeconds(-SkewSeconds);
    }

    public void Clear()
    {
        Token = null;
        ExpiresUtc = default;
    }
}
