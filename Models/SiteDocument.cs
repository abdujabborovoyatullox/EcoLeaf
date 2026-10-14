namespace Web_sayt.Models;

// "Ma'lumotlar" sahifasidagi "Hujjatlar" bo'limi: PDF / Word / Excel fayllar.
// Fayl mazmuni bazaning o'zida saqlanadi (Render'da disk vaqtinchalik, deploy'da tozalanadi).
public class SiteDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string FileName { get; set; } = "";       // asl fayl nomi (masalan: shartnoma.pdf)
    public string Extension { get; set; } = "";      // .pdf, .docx, .xlsx ...
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}