using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Web_sayt.Data;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Controllers;

public class RegisterRequest
{
    [Required(ErrorMessage = "Ismingizni kiriting.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Ism 2 dan 80 gacha belgidan iborat bo'lsin.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Telefon raqamni kiriting.")]
    [StringLength(30, ErrorMessage = "Telefon raqam noto'g'ri.")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Parolni kiriting.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Parol kamida 6 belgidan iborat bo'lsin.")]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required(ErrorMessage = "Telefon raqam va parolni kiriting.")]
    [StringLength(30, ErrorMessage = "Telefon raqam noto'g'ri.")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Telefon raqam va parolni kiriting.")]
    [StringLength(100)]
    public string Password { get; set; } = "";
}

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuthController(AppDbContext db) => _db = db;

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var name = req.Name.Trim();
        if (name.Length < 2)
            return BadRequest(new { message = "Ismingizni kiriting." });

        var phone = PhoneHelper.Normalize(req.Phone);
        if (phone == null)
            return BadRequest(new { message = "Telefon raqam noto'g'ri formatda. Masalan: +998 90 123 45 67" });

        if (await _db.Users.AnyAsync(u => u.Phone == phone))
            return Conflict(new { message = "Ushbu telefon raqam allaqachon ro'yxatdan o'tgan!" });

        var user = new AppUser
        {
            Name = name,
            Phone = phone,
            PasswordHash = PasswordHasher.Hash(req.Password)
        };

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Bir vaqtda ikki marta bosilganda unique indeks ushlaydi
            return Conflict(new { message = "Ushbu telefon raqam allaqachon ro'yxatdan o'tgan!" });
        }

        await SignInAsync(user);
        return Ok(new { message = "Muvaffaqiyatli ro'yxatdan o'tdingiz!", name = user.Name, phone = user.Phone });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var phone = PhoneHelper.Normalize(req.Phone);
        var user = phone == null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Phone == phone);

        if (user == null)
        {
            PasswordHasher.BurnTime(req.Password);
            return Unauthorized(new { message = "Telefon raqam yoki parol noto'g'ri." });
        }

        if (!PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Telefon raqam yoki parol noto'g'ri." });

        await SignInAsync(user);
        return Ok(new { name = user.Name, phone = user.Phone });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Tizimdan chiqildi." });
    }

    // Frontend sahifa yuklanganda sessiya bor-yo'qligini shu orqali biladi.
    // 401 o'rniga 200 {authenticated:false} qaytadi - brauzer konsolida xato ko'rinmaydi.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole(Roles.User))
            return Ok(new { authenticated = false });

        var id = User.GetUserId();
        var user = id == null ? null : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { authenticated = false });
        }

        return Ok(new { authenticated = true, name = user.Name, phone = user.Phone });
    }

    private Task SignInAsync(AppUser user) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthHelper.CreatePrincipal(user.Id, user.Name, Roles.User),
            new AuthenticationProperties { IsPersistent = true });
}
