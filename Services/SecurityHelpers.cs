using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web_sayt.Services;

/// <summary>
/// PBKDF2-SHA256 (tuzli, 210 000 iteratsiya). Eski SHA-256 (tuzsiz) o'rniga.
/// Format: v1.{iteratsiya}.{salt-base64}.{hash-base64}
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    // Foydalanuvchi topilmaganda ham bir xil vaqt sarflash uchun (login enumeration'dan himoya)
    private static readonly string DummyHash = Hash("dummy-password-for-timing");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Hisob topilmaganda chaqiriladi: vaqt farqi orqali login mavjudligini bilib bo'lmasin.</summary>
    public static void BurnTime(string password) => Verify(password, DummyHash);
}

public static class PhoneHelper
{
    /// <summary>
    /// "+998 90 123-45-67", "998901234567", "901234567" -> "998901234567".
    /// Noto'g'ri bo'lsa null qaytaradi.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 9) digits = "998" + digits;
        if (digits.Length != 12 || !digits.StartsWith("998")) return null;

        return digits;
    }
}

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class AuthHelper
{
    public static ClaimsPrincipal CreatePrincipal(int id, string name, string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role)
        }, CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }
}
