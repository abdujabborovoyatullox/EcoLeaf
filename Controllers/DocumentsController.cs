using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Web_sayt.Data;
using Web_sayt.Models;
using Web_sayt.Services;

namespace Web_sayt.Controllers;

public class DocumentForm
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public IFormFile? File { get; set; }
}

[ApiController]
public class DocumentsController : ControllerBase
{
    private const long MaxFileBytes = 20L * 1024 * 1024; // 20 MB

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private readonly AppDbContext _db;

    public DocumentsController(AppDbContext db) => _db = db;

    // ================= OMMAVIY (sayt) =================

    // Faqat ko'rinadigan hujjatlar; fayl baytlari ro'yxatda yuborilmaydi
    [HttpGet("api/documents")]
    public async Task<IActionResult> PublicList()
    {
        var items = await _db.SiteDocuments
            .AsNoTracking()
            .Where(d => d.IsVisible)
            .OrderBy(d => d.SortOrder).ThenByDescending(d => d.Id)
            .Select(d => new { d.Id, d.Title, d.Description, d.Extension, d.SizeBytes, d.CreatedAt, d.UpdatedAt })
            .ToListAsync();

        Response.Headers.CacheControl = "no-cache";
        return Ok(items);
    }

    [HttpGet("api/documents/{id:int}/file")]
    public Task<IActionResult> PublicFile(int id, [FromQuery] bool inline = false)
        => ServeAsync(id, onlyVisible: true, inline);

    // ================= ADMIN =================

    [HttpGet("api/admin/documents")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminList()
    {
        var items = await _db.SiteDocuments
            .AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.Description,
                d.FileName,
                d.Extension,
                d.SizeBytes,
                d.SortOrder,
                d.IsVisible,
                d.CreatedAt,
                d.UpdatedAt
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("api/admin/documents/{id:int}/file")]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> AdminFile(int id) => ServeAsync(id, onlyVisible: false, inline: false);

    [HttpPost("api/admin/documents")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] DocumentForm form)
    {
        if (form.File is not { Length: > 0 })
            return BadRequest(new { message = "Fayl tanlanmagan." });

        var title = (form.Title ?? "").Trim();
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(form.File.FileName);
        if (title.Length < 2 || title.Length > 120)
            return BadRequest(new { message = "Hujjat nomi 2 dan 120 gacha belgidan iborat bo'lsin." });

        var desc = (form.Description ?? "").Trim();
        if (desc.Length > 1000)
            return BadRequest(new { message = "Tavsif 1000 belgidan oshmasligi kerak." });

        var (error, ext, data) = await ReadAndValidateAsync(form.File);
        if (error != null) return BadRequest(new { message = error });

        var now = DateTime.UtcNow;
        var doc = new SiteDocument
        {
            Title = title,
            Description = desc,
            FileName = SafeFileName(form.File.FileName),
            Extension = ext!,
            ContentType = AllowedTypes[ext!],
            SizeBytes = data!.Length,
            Data = data,
            SortOrder = form.SortOrder,
            IsVisible = form.IsVisible,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.SiteDocuments.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Hujjat saytga joylandi!", id = doc.Id });
    }

    // Nomi/tavsifi/tartibi/holati o'zgaradi; yangi fayl yuborilsa, eskisi almashtiriladi
    [HttpPut("api/admin/documents/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Update(int id, [FromForm] DocumentForm form)
    {
        var doc = await _db.SiteDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "Hujjat topilmadi." });

        var title = (form.Title ?? "").Trim();
        if (title.Length < 2 || title.Length > 120)
            return BadRequest(new { message = "Hujjat nomi 2 dan 120 gacha belgidan iborat bo'lsin." });

        var desc = (form.Description ?? "").Trim();
        if (desc.Length > 1000)
            return BadRequest(new { message = "Tavsif 1000 belgidan oshmasligi kerak." });

        if (form.File is { Length: > 0 })
        {
            var (error, ext, data) = await ReadAndValidateAsync(form.File);
            if (error != null) return BadRequest(new { message = error });

            doc.FileName = SafeFileName(form.File.FileName);
            doc.Extension = ext!;
            doc.ContentType = AllowedTypes[ext!];
            doc.SizeBytes = data!.Length;
            doc.Data = data;
        }

        doc.Title = title;
        doc.Description = desc;
        doc.SortOrder = form.SortOrder;
        doc.IsVisible = form.IsVisible;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Hujjat yangilandi." });
    }

    [HttpDelete("api/admin/documents/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.SiteDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "Hujjat topilmadi." });

        _db.SiteDocuments.Remove(doc);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Hujjat o'chirildi." });
    }

    // ================= YORDAMCHI =================

    private async Task<IActionResult> ServeAsync(int id, bool onlyVisible, bool inline)
    {
        var doc = await _db.SiteDocuments.AsNoTracking()
            .Where(d => d.Id == id && (!onlyVisible || d.IsVisible))
            .Select(d => new { d.Data, d.ContentType, d.FileName, d.Extension })
            .FirstOrDefaultAsync();
        if (doc == null) return NotFound();

        var showInline = inline && doc.Extension == ".pdf";
        var cd = new ContentDispositionHeaderValue(showInline ? "inline" : "attachment");
        cd.SetHttpFileName(doc.FileName);

        Response.Headers.ContentDisposition = cd.ToString();
        Response.Headers.CacheControl = "no-cache"; // fayl almashtirilsa, eskisi qolib ketmasin
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(doc.Data, doc.ContentType);
    }

    private static async Task<(string? Error, string? Ext, byte[]? Data)> ReadAndValidateAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if (!AllowedTypes.ContainsKey(ext))
            return ("Faqat PDF, Word (.doc, .docx) va Excel (.xls, .xlsx) fayllar qabul qilinadi.", null, null);
        if (file.Length > MaxFileBytes)
            return ($"Fayl hajmi {MaxFileBytes / 1024 / 1024} MB dan oshmasligi kerak.", null, null);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        // Kengaytma soxta bo'lmasligi uchun fayl boshi (magic bytes) tekshiriladi
        var ok = ext switch
        {
            ".pdf" => StartsWith(data, 0x25, 0x50, 0x44, 0x46),            // %PDF
            ".docx" or ".xlsx" => StartsWith(data, 0x50, 0x4B),            // ZIP (PK)
            ".doc" or ".xls" => StartsWith(data, 0xD0, 0xCF, 0x11, 0xE0),  // OLE2
            _ => false
        };
        if (!ok) return ("Fayl mazmuni kengaytmasiga mos kelmaydi yoki buzilgan.", null, null);
        return (null, ext, data);
    }

    private static bool StartsWith(byte[] data, params byte[] sig)
    {
        if (data.Length < sig.Length) return false;
        for (var i = 0; i < sig.Length; i++)
            if (data[i] != sig[i]) return false;
        return true;
    }

    private static string SafeFileName(string? name)
    {
        var n = Path.GetFileName(name ?? "fayl");
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n.Length <= 200 ? n : n[..200];
    }
}