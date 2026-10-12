using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
