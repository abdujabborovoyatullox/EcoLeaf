namespace Web_sayt.Models;

public class AppUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Faqat raqamlar, masalan: 998901234567</summary>
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Order> Orders { get; set; } = new();
}

public class AdminAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PackageInfo { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    // Mahsulot keyinchalik o'zgarsa yoki o'chsa ham buyurtma tarixi buzilmasligi uchun
    // nom va narx buyurtma vaqtidagi holatda saqlanadi.
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Comment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>"Ma'lumotlar" bo'limidagi rasm / reklama yozuvi. Rasm bazada saqlanadi (Render diski vaqtinchalik).</summary>
public class InfoPost
{
    public int Id { get; set; }
    /// <summary>"info" (oddiy ma'lumot) yoki "ad" (reklama)</summary>
    public string Kind { get; set; } = InfoPostKind.Info;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }
    /// <summary>Video bo'lsa: turi va hajmi. Video baytlari InfoPostChunk jadvalida bo'laklab saqlanadi.</summary>
    public string? VideoContentType { get; set; }
    public long VideoSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Video fayl bo'lagi (1 MB). Katta videolar xotirani to'ldirmasligi uchun bo'laklab yoziladi va o'qiladi.</summary>
public class InfoPostChunk
{
    public const int Size = 1024 * 1024;

    public long Id { get; set; }
    public int PostId { get; set; }
    public int Seq { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public static class InfoPostKind
{
    public const string Info = "info";
    public const string Ad = "ad";
}

public class SiteSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public static class OrderStatus
{
    public const string Pending = "Kutilmoqda";
    public const string Processing = "Jarayonda";
    public const string Done = "Bajarildi";
    public const string Cancelled = "Bekor qilindi";

    public static readonly string[] All = { Pending, Processing, Done, Cancelled };
}