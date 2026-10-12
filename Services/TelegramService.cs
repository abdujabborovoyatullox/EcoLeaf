using System.Net;
using System.Net.Http.Json;

namespace Web_sayt.Services;

public interface ITelegramService
{
    /// <summary>Yangi buyurtma haqida xabar yuboradi. Xato bo'lsa ham buyurtmani to'xtatmaydi.</summary>
    Task SendOrderAsync(string customerName, string phone, string product, int quantity, decimal total, string address, int orderId);
}

public class TelegramService : ITelegramService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramService> _log;

    public TelegramService(HttpClient http, IConfiguration config, ILogger<TelegramService> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task SendOrderAsync(string customerName, string phone, string product, int quantity, decimal total, string address, int orderId)
    {
        // Render'da: Telegram__BotToken va Telegram__ChatId environment variable'lari
        var token = _config["Telegram:BotToken"];
        var chatId = _config["Telegram:ChatId"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
        {
            _log.LogWarning("Telegram sozlanmagan (Telegram__BotToken / Telegram__ChatId). Buyurtma #{Id} xabarsiz saqlandi.", orderId);
            return;
        }

        static string E(string s) => WebUtility.HtmlEncode(s);

        var text =
            $"🛒 <b>Yangi buyurtma #{orderId}</b>\n\n" +
            $"👤 <b>Mijoz:</b> {E(customerName)}\n" +
            $"📞 <b>Telefon:</b> +{E(phone)}\n" +
            $"📦 <b>Mahsulot:</b> {E(product)}\n" +
            $"🔢 <b>Miqdori:</b> {quantity} ta\n" +
            $"💰 <b>Jami:</b> {total:N0} so'm\n" +
            $"📍 <b>Manzil:</b> {E(address)}";

        try
        {
            var response = await _http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId, text, parse_mode = "HTML" });

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _log.LogError("Telegram xatosi ({Status}): {Body}", (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            // Token URL ichida bo'lgani uchun to'liq exception'ni (URL bilan) logga yozmaymiz
            _log.LogError("Telegramga yuborishda xato: {Message}", ex.GetType().Name);
        }
    }
}
