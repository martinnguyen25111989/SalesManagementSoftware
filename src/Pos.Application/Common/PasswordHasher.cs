using System.Security.Cryptography;

namespace Pos.Application.Common;

/// <summary>
/// Băm &amp; xác minh mật khẩu bằng PBKDF2 (SHA-256, có salt ngẫu nhiên mỗi tài khoản). Không phụ thuộc thư viện
/// ngoài. Chuỗi lưu trong <c>User.PasswordHash</c> có dạng <c>pbkdf2.sha256.{iters}.{saltB64}.{hashB64}</c>.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2.sha256";

    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Mật khẩu trống.", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Xác minh mật khẩu. Trả về false nếu hash trống/không đúng định dạng (vd tài khoản chưa đặt mật khẩu).</summary>
    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
            return false;
        if (!int.TryParse(parts[2], out int iters))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Tài khoản có mật khẩu đăng nhập được hay không (đã đặt PBKDF2 hợp lệ).</summary>
    public static bool IsLoginable(string? storedHash) =>
        !string.IsNullOrWhiteSpace(storedHash) && storedHash.StartsWith(Prefix + ".", StringComparison.Ordinal);
}
