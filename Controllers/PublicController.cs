using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using Web_sayt.Data;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Controllers;

public class CreateCommentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Mahsulotni tanlang.")]
    public int ProductId { get; set; }

    [Required(ErrorMessage = "Izoh matnini kiriting.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Izoh 3 dan 500 gacha belgidan iborat bo'lsin.")]
    public string Text { get; set; } = "";
}

[ApiController]
[Route("api")]
public class PublicController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public PublicController(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Description, p.Price, p.PackageInfo })
            .ToListAsync();

        return Ok(products);
    }

    // Bosh sahifadagi statistika (hammaga ko'rinadi)
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var users = await _db.Users.CountAsync();
        var orders = await _db.Orders.CountAsync(o => o.Status != OrderStatus.Cancelled);
        return Ok(new { users, orders });
    }

    [HttpGet("ad")]
    public async Task<IActionResult> GetAd()
    {
        var ad = await _settings.GetAdAsync();
        if (!ad.IsActive || string.IsNullOrWhiteSpace(ad.Title))
            return Ok(new { active = false });

        return Ok(new { active = true, title = ad.Title, description = ad.Description, imageUrl = ad.ImageUrl });
    }

    // "Ma'lumotlar" bo'limi: hamma ko'radi (rasm baytlari ro'yxatda yuborilmaydi, alohida URL orqali olinadi)
    [HttpGet("info-posts")]
    public async Task<IActionResult> GetInfoPosts()
    {
        var posts = await _db.InfoPosts
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .Select(p => new
            {
                p.Id,
                p.Kind,
                p.Title,
                p.Body,
                HasImage = p.ImageData != null,
                HasVideo = p.VideoContentType != null,
                p.CreatedAt
            })
            .ToListAsync();

        Response.Headers.CacheControl = "no-cache";
        return Ok(posts);
    }

    [HttpGet("info-posts/{id:int}/image")]
    public async Task<IActionResult> GetInfoPostImage(int id)
    {
        var img = await _db.InfoPosts
            .AsNoTracking()
            .Where(p => p.Id == id && p.ImageData != null)
            .Select(p => new { p.ImageData, p.ImageContentType })
            .FirstOrDefaultAsync();

        if (img?.ImageData == null) return NotFound();

        // Rasm hech qachon o'zgarmaydi (yangi yuklansa yangi Id bo'ladi), shuning uchun uzoq keshlanadi
        Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(img.ImageData, img.ImageContentType ?? "application/octet-stream");
    }

    // Video: Range so'rovlarini qo'llaydi (brauzer videoni oldinga-orqaga surishi va birdan oqim bilan ijro etishi uchun)
    [HttpGet("info-posts/{id:int}/video")]
    public async Task<IActionResult> GetInfoPostVideo(int id)
    {
        var meta = await _db.InfoPosts
            .AsNoTracking()
            .Where(p => p.Id == id && p.VideoContentType != null)
            .Select(p => new { p.VideoContentType, p.VideoSize })
            .FirstOrDefaultAsync();

        if (meta == null || meta.VideoSize <= 0) return NotFound();

        long total = meta.VideoSize;
        long start = 0, end = total - 1;
        var status = StatusCodes.Status200OK;

        if (RangeHeaderValue.TryParse(Request.Headers.Range.ToString(), out var range)
            && range.Unit == "bytes" && range.Ranges.Count == 1)
        {
            var r = range.Ranges.First();
            if (r.From.HasValue)
            {
                start = r.From.Value;
                end = r.To.HasValue ? Math.Min(r.To.Value, total - 1) : total - 1;
            }
            else if (r.To.HasValue)
            {
                start = Math.Max(0, total - r.To.Value);
            }

            if (start > end || start >= total)
            {
                Response.Headers["Content-Range"] = $"bytes */{total}";
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }
            status = StatusCodes.Status206PartialContent;
        }

        Response.StatusCode = status;
        Response.ContentType = meta.VideoContentType!;
        Response.ContentLength = end - start + 1;
        Response.Headers["Accept-Ranges"] = "bytes";
        Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (status == StatusCodes.Status206PartialContent)
            Response.Headers["Content-Range"] = $"bytes {start}-{end}/{total}";

        var ct = HttpContext.RequestAborted;
        try
        {
            var seq = (int)(start / InfoPostChunk.Size);
            long pos = (long)seq * InfoPostChunk.Size;

            while (pos <= end)
            {
                var data = await _db.InfoPostChunks
                    .AsNoTracking()
                    .Where(c => c.PostId == id && c.Seq == seq)
                    .Select(c => c.Data)
                    .FirstOrDefaultAsync(ct);
                if (data == null) break;

                var from = (int)Math.Max(0, start - pos);
                var to = (int)Math.Min(data.Length - 1, end - pos);
                if (to >= from)
                    await Response.Body.WriteAsync(data.AsMemory(from, to - from + 1), ct);

                pos += data.Length;
                seq++;
            }
        }
        catch (OperationCanceledException) { /* foydalanuvchi videoni yopdi */ }

        return new EmptyResult();
    }

    [HttpGet("comments")]
    public async Task<IActionResult> GetComments()
    {
        var comments = await _db.Comments
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new { c.Id, c.UserName, c.ProductName, c.Text, c.CreatedAt })
            .ToListAsync();

        return Ok(comments);
    }

    [HttpPost("comments")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> CreateComment([FromBody] CreateCommentRequest req)
    {
        var userId = User.GetUserId();
        var user = userId == null ? null : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Unauthorized(new { message = "Qayta tizimga kiring." });

        var text = req.Text.Trim();
        if (text.Length < 3)
            return BadRequest(new { message = "Izoh matnini kiriting." });

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProductId);
        if (product == null)
            return NotFound(new { message = "Mahsulot topilmadi." });

        // Oddiy spamdan himoya: bir foydalanuvchi daqiqasiga 3 tadan ko'p izoh qoldira olmaydi
        var since = DateTime.UtcNow.AddMinutes(-1);
        if (await _db.Comments.CountAsync(c => c.UserId == user.Id && c.CreatedAt > since) >= 3)
            return StatusCode(429, new { message = "Juda tez-tez izoh yozyapsiz. Biroz kuting." });

        _db.Comments.Add(new Comment
        {
            UserId = user.Id,
            UserName = user.Name,
            ProductId = product.Id,
            ProductName = product.Name,
            Text = text
        });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Izohingiz qo'shildi!" });
    }
}