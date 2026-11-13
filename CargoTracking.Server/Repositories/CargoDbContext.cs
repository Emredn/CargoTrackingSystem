using CargoTracking.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace CargoTracking.Server.Repositories
{
    /// <summary>
    /// SQLite veritabanı bağlantısı (Entity Framework Core). Dosya: cargos_enterprise.db (sunucunun çalıştığı klasörde).
    /// </summary>
    public class CargoDbContext : DbContext
    {
        public DbSet<CargoRecord> Cargos { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Proje klasöründe "cargos_enterprise.db" adında gerçek bir SQLite veritabanı dosyası oluşturur
            optionsBuilder.UseSqlite("Data Source=cargos_enterprise.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Başlangıç (Seed) Verileri: Veritabanı ilk kez oluştuğunda tabloya otomatik eklenecek kayıtlar.
            // Adres formatı: "İl / İlçe / Mahalle - Cadde/Sokak" (mevcut veritabanı dosyasını etkilemez)
            modelBuilder.Entity<CargoRecord>().HasData(
                new CargoRecord
                {
                    TrackingNumber = "TR-1001",
                    SenderName = "Emre Dindar",
                    ReceiverName = "Ceyda Gülmez",
                    CurrentStatus = "Yola Çıktı",
                    LastLocation = "Bursa / Nilüfer / Özlüce - Uludağ Üniversitesi",
                    EstimatedDelivery = "17.11.2026",
                    IsActive = true
                },
                new CargoRecord
                {
                    TrackingNumber = "TR-1002",
                    SenderName = "Trendyol Lojistik",
                    ReceiverName = "Fatih Şahinbaş",
                    CurrentStatus = "Hazırlanıyor",
                    LastLocation = "Bursa / Osmangazi / Hasanağa - Atatürk Caddesi",
                    EstimatedDelivery = "29.10.2026",
                    IsActive = true
                }
            );
        }
    }
}