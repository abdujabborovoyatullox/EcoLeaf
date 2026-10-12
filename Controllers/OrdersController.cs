using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web_sayt.Data;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Controllers;

public class CreateOrderRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Mahsulotni tanlang.")]
    public int ProductId { get; set; }

    [Range(1, 1000, ErrorMessage = "Miqdor 1 dan 1000 gacha bo'lishi kerak.")]
    public int Quantity { get; set; } = 1;

    [Required(ErrorMessage = "Iltimos, yetkazib berish manzilini kiriting!")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Manzil 5 dan 300 gacha belgidan iborat bo'lsin.")]
    public string Address { get; set; } = "";
}

[ApiController]
[Route("api/orders")]
[Authorize(Roles = Roles.User)]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITelegramService _telegram;

    public OrdersController(AppDbContext db, ITelegramService telegram)
    {
        _db = db;
        _telegram = telegram;
    }

    // Foydalanuvchining o'z buyurtmalari
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.GetUserId();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.ProductName,
                o.Quantity,
                o.Address,
                o.Status,
                o.UnitPrice,
                Total = o.UnitPrice * o.Quantity,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
    {
        var userId = User.GetUserId();
        var user = userId == null ? null : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Unauthorized(new { message = "Qayta tizimga kiring." });

        var address = req.Address.Trim();
        if (address.Length < 5)
            return BadRequest(new { message = "Iltimos, yetkazib berish manzilini kiriting!" });

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId && !p.IsDeleted);
        if (product == null)
            return NotFound(new { message = "Mahsulot topilmadi yoki sotuvdan olingan." });

        // Telegramga spam tushmasligi uchun: daqiqasiga ko'pi bilan 5 ta buyurtma
        var since = DateTime.UtcNow.AddMinutes(-1);
        if (await _db.Orders.CountAsync(o => o.UserId == user.Id && o.CreatedAt > since) >= 5)
            return StatusCode(429, new { message = "Juda ko'p buyurtma berildi. Biroz kutib turing." });

        var order = new Order
        {
            UserId = user.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPrice = product.Price,
            Quantity = req.Quantity,
            Address = address,
            Status = OrderStatus.Pending
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // Buyurtma bazaga yozildi. Telegram xatosi buyurtmani bekor qilmaydi.
        await _telegram.SendOrderAsync(user.Name, user.Phone, product.Name, order.Quantity,
            order.UnitPrice * order.Quantity, order.Address, order.Id);

        return Ok(new { message = "Buyurtmangiz muvaffaqiyatli qabul qilindi va operatorlarga yuborildi!", id = order.Id });
    }

    // Foydalanuvchi faqat o'zining va faqat hali ko'rib chiqilmagan buyurtmasini bekor qila oladi
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.GetUserId();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order == null)
            return NotFound(new { message = "Buyurtma topilmadi." });

        if (order.Status != OrderStatus.Pending)
            return BadRequest(new { message = "Faqat kutilayotgan buyurtmani bekor qilish mumkin." });

        order.Status = OrderStatus.Cancelled;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Buyurtma bekor qilindi." });
    }
}
