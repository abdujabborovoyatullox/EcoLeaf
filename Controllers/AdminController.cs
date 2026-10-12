using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Web_sayt.Data;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Controllers;

// ---------- DTO'lar ----------
public class AdminLoginRequest
{
    [Required(ErrorMessage = "Login va parolni kiriting.")]
    [StringLength(50)]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Login va parolni kiriting.")]
    [StringLength(100)]
    public string Password { get; set; } = "";
}

public class ChangeCredentialsRequest
{
    [Required(ErrorMessage = "Joriy parolni kiriting.")]
    [StringLength(100)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Yangi loginni kiriting.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Login 3 dan 50 gacha belgidan iborat bo'lsin.")]
    public string NewUsername { get; set; } = "";

    [Required(ErrorMessage = "Yangi parolni kiriting.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Yangi parol kamida 8 belgidan iborat bo'lsin.")]
    public string NewPassword { get; set; } = "";
}

public class ProductRequest
{
    [Required(ErrorMessage = "Mahsulot nomini kiriting.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Mahsulot nomi 2 dan 120 gacha belgidan iborat bo'lsin.")]
    public string Name { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Tavsif 1000 belgidan oshmasligi kerak.")]
    public string Description { get; set; } = "";

    [Range(1, 1_000_000_000, ErrorMessage = "Narx 0 dan katta bo'lishi kerak.")]
    public decimal Price { get; set; }

    [StringLength(60, ErrorMessage = "Qadoq ma'lumoti 60 belgidan oshmasligi kerak.")]
    public string PackageInfo { get; set; } = "";
}

public class UpdateOrderStatusRequest
{
    [Required(ErrorMessage = "Holatni tanlang.")]
    public string Status { get; set; } = "";
}

public class UpdateAdRequest
{
    [StringLength(100, ErrorMessage = "Sarlavha 100 belgidan oshmasligi kerak.")]
    public string Title { get; set; } = "";

    [StringLength(400, ErrorMessage = "Matn 400 belgidan oshmasligi kerak.")]
    public string Description { get; set; } = "";

    [StringLength(500, ErrorMessage = "Rasm havolasi juda uzun.")]
    public string ImageUrl { get; set; } = "";

    public bool IsActive { get; set; }
}

public class InfoPostForm
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public IFormFile? Image { get; set; }
    public IFormFile? Video { get; set; }
}

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController : ControllerBase
{
    // O'zbekiston UTC+5 (yozgi/qishki vaqt yo'q). Hisobotlar mahalliy kun/oy bo'yicha hisoblanadi.
    private const int LocalOffsetHours = 5;

    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public AdminController(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ================= Autentifikatsiya =================

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest req)
    {
        var username = req.Username.Trim();
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Username == username);

        if (admin == null)
        {
            PasswordHasher.BurnTime(req.Password);
            return Unauthorized(new { message = "Login yoki parol xato!" });
        }

        if (!PasswordHasher.Verify(req.Password, admin.PasswordHash))
            return Unauthorized(new { message = "Login yoki parol xato!" });

        await SignInAdminAsync(admin);
        return Ok(new { message = "Muvaffaqiyatli kirildi", username = admin.Username });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Tizimdan chiqildi." });
    }

    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole(Roles.Admin))
            return Ok(new { authenticated = false });

        var id = User.GetUserId();
        var admin = id == null ? null : await _db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (admin == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { authenticated = false });
        }

        return Ok(new { authenticated = true, username = admin.Username });
    }

    [HttpPost("change-credentials")]
    public async Task<IActionResult> ChangeCredentials([FromBody] ChangeCredentialsRequest req)
    {
        var id = User.GetUserId();
        var admin = id == null ? null : await _db.Admins.FirstOrDefaultAsync(a => a.Id == id);
        if (admin == null) return Unauthorized(new { message = "Qayta tizimga kiring." });

        if (!PasswordHasher.Verify(req.CurrentPassword, admin.PasswordHash))
            return BadRequest(new { message = "Joriy parol noto'g'ri!" });

        var newUsername = req.NewUsername.Trim();
        if (await _db.Admins.AnyAsync(a => a.Username == newUsername && a.Id != admin.Id))
            return BadRequest(new { message = "Bu login band." });

        admin.Username = newUsername;
        admin.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await _db.SaveChangesAsync();

        await SignInAdminAsync(admin);
        return Ok(new { message = "Login va parol muvaffaqiyatli o'zgartirildi", username = admin.Username });
    }

    // ================= Mahsulotlar =================

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        var items = await _db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Description, p.Price, p.PackageInfo, p.CreatedAt })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("products/deleted")]
    public async Task<IActionResult> GetDeletedProducts()
    {
        var items = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsDeleted)
            .OrderByDescending(p => p.DeletedAt)
            .Select(p => new { p.Id, p.Name, p.Price, p.PackageInfo, p.DeletedAt })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("products")]
    public async Task<IActionResult> AddProduct([FromBody] ProductRequest req)
    {
        var product = new Product
        {
            Name = req.Name.Trim(),
            Description = req.Description.Trim(),
            Price = req.Price,
            PackageInfo = req.PackageInfo.Trim()
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Mahsulot muvaffaqiyatli yaratildi", id = product.Id });
    }

    [HttpPut("products/{id:int}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductRequest req)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (product == null) return NotFound(new { message = "Mahsulot topilmadi." });

        product.Name = req.Name.Trim();
        product.Description = req.Description.Trim();
        product.Price = req.Price;
        product.PackageInfo = req.PackageInfo.Trim();
        await _db.SaveChangesAsync();

        return Ok(new { message = "Mahsulot yangilandi" });
    }

    // Soft delete: mahsulot bazadan o'chmaydi, "O'chirilganlar" bo'limiga o'tadi
    [HttpDelete("products/{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (product == null) return NotFound(new { message = "Mahsulot topilmadi." });

        product.IsDeleted = true;
        product.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Mahsulot o'chirildi" });
    }

    [HttpPost("products/{id:int}/restore")]
    public async Task<IActionResult> RestoreProduct(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted);
        if (product == null) return NotFound(new { message = "Mahsulot topilmadi." });

        product.IsDeleted = false;
        product.DeletedAt = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Mahsulot tiklandi" });
    }

    // ================= Foydalanuvchilar =================
    // Parol hash'i hech qachon qaytarilmaydi.

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new { u.Id, u.Name, u.Phone, u.CreatedAt, OrdersCount = u.Orders.Count })
            .ToListAsync();

        return Ok(new { count = users.Count, users });
    }

    // ================= Buyurtmalar =================

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] string? status = null)
    {
        var query = _db.Orders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .Select(o => new
            {
                o.Id,
                UserName = o.User!.Name,
                Phone = o.User!.Phone,
                o.ProductName,
                o.Quantity,
                o.UnitPrice,
                Total = o.UnitPrice * o.Quantity,
                o.Address,
                o.Status,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPut("orders/{id:int}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequest req)
    {
        if (!OrderStatus.All.Contains(req.Status))
            return BadRequest(new { message = "Noto'g'ri holat." });

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound(new { message = "Buyurtma topilmadi." });

        order.Status = req.Status;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Buyurtma holati yangilandi." });
    }

    // ================= Izohlar =================

    [HttpGet("comments")]
    public async Task<IActionResult> GetComments()
    {
        var comments = await _db.Comments
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(200)
            .Select(c => new { c.Id, c.UserName, c.ProductName, c.Text, c.CreatedAt })
            .ToListAsync();

        return Ok(comments);
    }

    [HttpDelete("comments/{id:int}")]
    public async Task<IActionResult> DeleteComment(int id)
    {
        var comment = await _db.Comments.FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null) return NotFound(new { message = "Izoh topilmadi." });

        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Izoh o'chirildi." });
    }

    // ================= Hisobotlar =================

    [HttpGet("reports")]
    public async Task<IActionResult> GetReport(
        [FromQuery] string type = "monthly",
        [FromQuery] int year = 0,
        [FromQuery] int month = 0,
        [FromQuery] int? day = null)
    {
        var nowLocal = DateTime.UtcNow.AddHours(LocalOffsetHours);
        if (year == 0) year = nowLocal.Year;
        if (month == 0) month = nowLocal.Month;

        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { message = "Yil yoki oy noto'g'ri." });

        DateTime startLocal, endLocal;
        if (type == "daily")
        {
            var d = day ?? nowLocal.Day;
            if (d < 1 || d > DateTime.DaysInMonth(year, month))
                return BadRequest(new { message = "Kun noto'g'ri." });

            startLocal = new DateTime(year, month, d);
            endLocal = startLocal.AddDays(1);
        }
        else if (type == "monthly")
        {
            startLocal = new DateTime(year, month, 1);
            endLocal = startLocal.AddMonths(1);
        }
        else
        {
            return BadRequest(new { message = "Hisobot turi 'daily' yoki 'monthly' bo'lishi kerak." });
        }

        // Bazada vaqt UTC'da saqlanadi
        var fromUtc = DateTime.SpecifyKind(startLocal.AddHours(-LocalOffsetHours), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(endLocal.AddHours(-LocalOffsetHours), DateTimeKind.Utc);

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc)
            .ToListAsync();

        var valid = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var topProducts = valid
            .GroupBy(o => o.ProductName)
            .Select(g => new { Name = g.Key, Quantity = g.Sum(o => o.Quantity), Revenue = g.Sum(o => o.UnitPrice * o.Quantity) })
            .OrderByDescending(x => x.Quantity)
            .Take(5)
            .ToList();

        var newUsers = await _db.Users.CountAsync(u => u.CreatedAt >= fromUtc && u.CreatedAt < toUtc);
        var productsCreated = await _db.Products.CountAsync(p => p.CreatedAt >= fromUtc && p.CreatedAt < toUtc);
        var productsDeleted = await _db.Products.CountAsync(p => p.DeletedAt != null && p.DeletedAt >= fromUtc && p.DeletedAt < toUtc);
        var activeProducts = await _db.Products.CountAsync(p => !p.IsDeleted);

        return Ok(new
        {
            period = type,
            fromDate = startLocal.ToString("yyyy-MM-dd"),
            toDate = endLocal.AddDays(-1).ToString("yyyy-MM-dd"),
            ordersTotal = orders.Count,
            ordersPending = orders.Count(o => o.Status == OrderStatus.Pending),
            ordersProcessing = orders.Count(o => o.Status == OrderStatus.Processing),
            ordersDone = orders.Count(o => o.Status == OrderStatus.Done),
            ordersCancelled = orders.Count(o => o.Status == OrderStatus.Cancelled),
            revenue = valid.Sum(o => o.UnitPrice * o.Quantity),
            newUsers,
            productsCreated,
            productsDeleted,
            activeProducts,
            topProducts
        });
    }

    // ================= Reklama =================

    [HttpGet("settings/ads")]
    public async Task<IActionResult> GetAd() => Ok(await _settings.GetAdAsync());

    [HttpPut("settings/ads")]
    public async Task<IActionResult> UpdateAd([FromBody] UpdateAdRequest req)
    {
        var title = req.Title.Trim();
        var description = req.Description.Trim();
        var imageUrl = req.ImageUrl.Trim();

        if (req.IsActive && title.Length == 0)
            return BadRequest(new { message = "Reklamani faollashtirish uchun sarlavha kiriting!" });

        // Faqat http/https rasm havolalariga ruxsat (javascript: va boshqa sxemalarga yo'q)
        if (imageUrl.Length > 0 &&
            !(Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
              (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            return BadRequest(new { message = "Rasm havolasi http:// yoki https:// bilan boshlanishi kerak." });

        await _settings.SaveAdAsync(new AdSettings(title, description, imageUrl, req.IsActive));
        return Ok(new { message = req.IsActive ? "Reklama saytga joylandi!" : "Reklama saqlandi (faol emas)." });
    }

    // ================= Ma'lumotlar bo'limi (rasm / reklama) =================

    private const long MaxImageBytes = 5 * 1024 * 1024; // 5 MB

    [HttpGet("info-posts")]
    public async Task<IActionResult> GetInfoPosts()
    {
        var posts = await _db.InfoPosts
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Kind, p.Title, p.Body, HasImage = p.ImageData != null, HasVideo = p.VideoContentType != null, p.CreatedAt })
            .ToListAsync();

        return Ok(posts);
    }

    [HttpPost("info-posts")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> CreateInfoPost([FromForm] InfoPostForm form)
    {
        var title = (form.Title ?? "").Trim();
        var body = (form.Body ?? "").Trim();
        var kind = form.Kind == InfoPostKind.Ad ? InfoPostKind.Ad : InfoPostKind.Info;

        if (title.Length < 2 || title.Length > 120)
            return BadRequest(new { message = "Sarlavha 2 dan 120 gacha belgidan iborat bo'lsin." });
        if (body.Length > 3000)
            return BadRequest(new { message = "Matn 3000 belgidan oshmasligi kerak." });

        byte[]? data = null;
        string? contentType = null;

        if (form.Image is { Length: > 0 })
        {
            if (form.Image.Length > MaxImageBytes)
                return BadRequest(new { message = "Rasm hajmi 5 MB dan oshmasligi kerak." });

            using var ms = new MemoryStream();
            await form.Image.CopyToAsync(ms);
            data = ms.ToArray();

            // Fayl turi brauzer aytgan nom bo'yicha emas, haqiqiy mazmuni (magic bytes) bo'yicha aniqlanadi
            contentType = DetectImageType(data);
            if (contentType == null)
                return BadRequest(new { message = "Faqat JPG, PNG, WEBP yoki GIF rasm yuklash mumkin." });
        }

        // Video: birinchi bo'lak o'qilib, fayl haqiqatan video ekani tekshiriladi
        Stream? videoStream = null;
        byte[]? buf = null;
        var firstLen = 0;
        string? videoType = null;

        try
        {
            if (form.Video is { Length: > 0 })
            {
                videoStream = form.Video.OpenReadStream();
                buf = new byte[InfoPostChunk.Size];
                firstLen = await ReadFullAsync(videoStream, buf);
                videoType = DetectVideoType(buf, firstLen);
                if (videoType == null)
                    return BadRequest(new { message = "Faqat MP4, MOV yoki WEBM video yuklash mumkin." });
            }

            if (data == null && videoType == null && body.Length == 0)
                return BadRequest(new { message = "Rasm yoki video yuklang, yoki matn yozing." });

            var post = new InfoPost
            {
                Kind = kind,
                Title = title,
                Body = body,
                ImageData = data,
                ImageContentType = contentType,
                VideoContentType = videoType,
                VideoSize = videoType != null ? form.Video!.Length : 0
            };
            _db.InfoPosts.Add(post);
            await _db.SaveChangesAsync();
            var postId = post.Id;

            if (videoType != null)
            {
                try
                {
                    var seq = 0;
                    var len = firstLen;
                    while (len > 0)
                    {
                        _db.InfoPostChunks.Add(new InfoPostChunk
                        {
                            PostId = postId,
                            Seq = seq++,
                            Data = len == buf!.Length ? buf : buf[..len]
                        });
                        await _db.SaveChangesAsync();
                        _db.ChangeTracker.Clear();
                        len = await ReadFullAsync(videoStream!, buf!);
                    }
                }
                catch
                {
                    // Yarim yuklangan video qolib ketmasin
                    _db.ChangeTracker.Clear();
                    await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM info_posts WHERE \"Id\" = {postId}");
                    throw;
                }
            }

            return Ok(new { message = "Saytga joylandi! Foydalanuvchilar hozir ko'ra oladi.", id = postId });
        }
        finally
        {
            if (videoStream != null) await videoStream.DisposeAsync();
        }
    }

    private static async Task<int> ReadFullAsync(Stream s, byte[] buf)
    {
        var total = 0;
        while (total < buf.Length)
        {
            var r = await s.ReadAsync(buf.AsMemory(total));
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private static string? DetectVideoType(byte[] d, int len)
    {
        if (len < 12) return null;
        // MP4 / MOV: 5-8 baytlarda "ftyp"
        if (d[4] == 0x66 && d[5] == 0x74 && d[6] == 0x79 && d[7] == 0x70) return "video/mp4";
        // WEBM / MKV (EBML sarlavhasi)
        if (d[0] == 0x1A && d[1] == 0x45 && d[2] == 0xDF && d[3] == 0xA3) return "video/webm";
        return null;
    }

    [HttpDelete("info-posts/{id:int}")]
    public async Task<IActionResult> DeleteInfoPost(int id)
    {
        var post = await _db.InfoPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null) return NotFound(new { message = "Yozuv topilmadi." });

        _db.InfoPosts.Remove(post);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Yozuv o'chirildi." });
    }

    private static string? DetectImageType(byte[] d)
    {
        if (d.Length < 12) return null;
        if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
        if (d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46 &&
            d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return "image/webp";
        return null;
    }

    // ================= Eksport =================

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var products = await _db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Name, u.Phone, u.CreatedAt }).ToListAsync();
        var orders = await _db.Orders.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
        var comments = await _db.Comments.AsNoTracking().OrderBy(c => c.Id).ToListAsync();

        return Ok(new
        {
            exportedAt = DateTime.UtcNow,
            products,
            users,
            orders = orders.Select(o => new { o.Id, o.UserId, o.ProductId, o.ProductName, o.UnitPrice, o.Quantity, o.Address, o.Status, o.CreatedAt }),
            comments
        });
    }

    private Task SignInAdminAsync(AdminAccount admin) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthHelper.CreatePrincipal(admin.Id, admin.Username, Roles.Admin),
            new AuthenticationProperties { IsPersistent = false });
}