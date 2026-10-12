using Microsoft.EntityFrameworkCore;
using Web_sayt.Data;
using Web_sayt.Models;

namespace Web_sayt.Services;

public record AdSettings(string Title, string Description, string ImageUrl, bool IsActive);

public class SettingsService
{
    private const string TitleKey = "ad.title";
    private const string DescKey = "ad.description";
    private const string ImageKey = "ad.imageUrl";
    private const string ActiveKey = "ad.active";

    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<AdSettings> GetAdAsync()
    {
        var rows = await _db.Settings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("ad."))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return new AdSettings(
            rows.GetValueOrDefault(TitleKey, ""),
            rows.GetValueOrDefault(DescKey, ""),
            rows.GetValueOrDefault(ImageKey, ""),
            rows.GetValueOrDefault(ActiveKey, "false") == "true");
    }

    public async Task SaveAdAsync(AdSettings ad)
    {
        await UpsertAsync(TitleKey, ad.Title);
        await UpsertAsync(DescKey, ad.Description);
        await UpsertAsync(ImageKey, ad.ImageUrl);
        await UpsertAsync(ActiveKey, ad.IsActive ? "true" : "false");
        await _db.SaveChangesAsync();
    }

    private async Task UpsertAsync(string key, string value)
    {
        var row = await _db.Settings.FindAsync(key);
        if (row == null) _db.Settings.Add(new SiteSetting { Key = key, Value = value });
        else row.Value = value;
    }
}
