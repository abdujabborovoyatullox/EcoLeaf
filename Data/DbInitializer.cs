using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Data;

public static class ConnectionStringHelper
{
    /// <summary>
    /// Render beradigan DATABASE_URL (postgres://user:pass@host:port/db) ni Npgsql formatiga o'giradi.
    /// DATABASE_URL bo'lmasa, appsettings.json'dagi ConnectionStrings:Default ishlatiladi (lokal ishlab chiqish uchun).
    /// </summary>
    public static string Resolve(IConfiguration config)
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(url))
        {
            return config.GetConnectionString("Default")
                   ?? throw new InvalidOperationException(
                       "Baza sozlanmagan: DATABASE_URL environment variable yoki ConnectionStrings:Default kerak.");
        }

        if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Allaqachon Npgsql formatida ("Host=...;Database=...") berilgan
            return url;
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = uri.AbsolutePath.TrimStart('/'),
            // Render: ichki ulanishda SSL shart emas, tashqi ulanishda kerak. Prefer ikkalasida ham ishlaydi.
            SslMode = SslMode.Prefer
        };

        return builder.ConnectionString;
    }
}

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var log = services.GetRequiredService<ILogger<AppDbContext>>();

        // Render'da baza ilovadan sal kechroq ishga tushishi mumkin - bir necha marta urinib ko'ramiz
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Jadvallar yo'q bo'lsa yaratadi. Mavjud bo'lsa, tegmaydi.
                await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                log.LogWarning("Bazaga ulanib bo'lmadi ({Attempt}/{Max}): {Msg}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        await SeedProductsAsync(db);
        await SeedAdminAsync(db, config, log);
    }

    private static async Task SeedProductsAsync(AppDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        db.Products.AddRange(
            new Product { Name = "Eco Premium Zich", Description = "Uzoq vaqt yonuvchi premium mahsulot.", Price = 55000, PackageInfo = "20 kg" },
            new Product { Name = "Eco Standard Briket", Description = "Kundalik uy va kaminlar uchun.", Price = 25000, PackageInfo = "10 kg" });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(AppDbContext db, IConfiguration config, ILogger log)
    {
        var username = config["ADMIN_USERNAME"];
        var password = config["ADMIN_PASSWORD"];
        var forceReset = string.Equals(config["ADMIN_FORCE_RESET"], "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(username)) username = "admin";

        var existing = await db.Admins.FirstOrDefaultAsync();

        if (existing != null)
        {
            // Parolni unutib qo'ysangiz: ADMIN_FORCE_RESET=true va ADMIN_PASSWORD bering, qayta deploy qiling,
            // kirib bo'lgach ADMIN_FORCE_RESET'ni o'chirib qo'ying.
            if (forceReset && !string.IsNullOrWhiteSpace(password))
            {
                existing.Username = username.Trim();
                existing.PasswordHash = PasswordHasher.Hash(password);
                await db.SaveChangesAsync();
                log.LogWarning("Admin login/paroli ADMIN_FORCE_RESET orqali tiklandi. Endi ADMIN_FORCE_RESET'ni o'chiring!");
            }
            return;
        }

        var generated = false;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', 'A').Replace('/', 'B').TrimEnd('=');
            generated = true;
        }

        db.Admins.Add(new AdminAccount { Username = username.Trim(), PasswordHash = PasswordHasher.Hash(password) });
        await db.SaveChangesAsync();

        if (generated)
            log.LogWarning("ADMIN_PASSWORD berilmagani uchun vaqtinchalik parol yaratildi. Login: {User}  Parol: {Pass}  (kirgach darhol o'zgartiring)", username, password);
        else
            log.LogInformation("Admin hisobi yaratildi: {User}", username);
    }
}
