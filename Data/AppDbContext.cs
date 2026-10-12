using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Web_sayt.Models;

namespace Web_sayt.Data;

// IDataProtectionKeyContext: login cookie shifrlash kalitlari bazada saqlanadi,
// shuning uchun Render'da qayta deploy qilinganda foydalanuvchilar tizimdan chiqib ketmaydi.
public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AdminAccount> Admins => Set<AdminAccount>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<SiteSetting> Settings => Set<SiteSetting>();
    public DbSet<InfoPost> InfoPosts => Set<InfoPost>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        });

        b.Entity<AdminAccount>(e =>
        {
            e.ToTable("admins");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.PackageInfo).HasMaxLength(60);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasIndex(x => x.IsDeleted);
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(x => x.ProductName).HasMaxLength(120);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasOne(x => x.User).WithMany(u => u.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<Comment>(e =>
        {
            e.ToTable("comments");
            e.Property(x => x.UserName).HasMaxLength(80);
            e.Property(x => x.ProductName).HasMaxLength(120);
            e.Property(x => x.Text).HasMaxLength(600);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<InfoPost>(e =>
        {
            e.ToTable("info_posts");
            e.Property(x => x.Kind).HasMaxLength(10).IsRequired();
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Body).HasMaxLength(3000);
            e.Property(x => x.ImageContentType).HasMaxLength(50);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<SiteSetting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
            e.Property(x => x.Value).HasMaxLength(2000);
        });
    }
}