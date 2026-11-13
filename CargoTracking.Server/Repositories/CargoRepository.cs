using CargoTracking.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace CargoTracking.Server.Repositories
{
    /// <summary>
    /// Kargo kayıtlarının veritabanı işlemleri (ekle / sorgula / listele / güncelle).
    /// Her çağrıda kısa ömürlü bir CargoDbContext açar.
    /// </summary>
    public class CargoRepository
    {
        public CargoRepository()
        {
            // Uygulama ayağa kalkarken SQLite veritabanı dosyası ve tablolar yoksa otomatik oluşturur!
            using var db = new CargoDbContext();
            db.Database.EnsureCreated();
        }

        // Takip numarasına göre SQLite'tan tekil kayıt sorgulama (SELECT * FROM Cargos WHERE TrackingNumber = ...)
        public CargoRecord? GetByTrackingNumber(string trackingNumber)
        {
            using var db = new CargoDbContext();
            return db.Cargos
                .AsNoTracking()
                .FirstOrDefault(c => c.TrackingNumber.ToUpper() == trackingNumber.ToUpper());
        }

        // Yeni kargo ekleme (Son TR-XXXX numarasını SQL'den bulup +1 artırarak INSERT INTO yapar)
        public CargoRecord AddCargo(string sender, string receiver, string location, string estimatedDelivery)
        {
            using var db = new CargoDbContext();

            // Mevcut en büyük takip numarasını bul (Örn: TR-1002 -> 1002)
            int maxId = 1000;
            var allNumbers = db.Cargos.Select(c => c.TrackingNumber).ToList();

            foreach (var tn in allNumbers)
            {
                if (tn.StartsWith("TR-") && int.TryParse(tn.Substring(3), out int parsedId))
                {
                    if (parsedId > maxId) maxId = parsedId;
                }
            }

            var newCargo = new CargoRecord
            {
                TrackingNumber = $"TR-{maxId + 1}",
                SenderName = sender,
                ReceiverName = receiver,
                CurrentStatus = "Hazırlanıyor",
                LastLocation = location,
                EstimatedDelivery = estimatedDelivery,
                IsActive = true
            };

            db.Cargos.Add(newCargo);
            db.SaveChanges(); // Değişiklikleri SQLite diskine yazar (COMMIT)
            return newCargo;
        }

        // Tüm kargoları getirme (SELECT * FROM Cargos)
        public List<CargoRecord> GetAll()
        {
            using var db = new CargoDbContext();
            return db.Cargos.AsNoTracking().ToList();
        }

        // Kargo bilgilerini ve durumunu güncelleme (UPDATE Cargos SET ...)
        public bool UpdateCargoAndStatus(string trackingNumber, string sender, string receiver, string location, string deliveryDate, string newStatus, out string message)
        {
            using var db = new CargoDbContext();
            var cargo = db.Cargos.FirstOrDefault(c => c.TrackingNumber.ToUpper() == trackingNumber.ToUpper());

            if (cargo == null)
            {
                message = "Bu takip numarasına ait kargo veritabanında bulunamadı.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sender)) cargo.SenderName = sender;
            if (!string.IsNullOrWhiteSpace(receiver)) cargo.ReceiverName = receiver;
            if (!string.IsNullOrWhiteSpace(location)) cargo.LastLocation = location;
            if (!string.IsNullOrWhiteSpace(deliveryDate)) cargo.EstimatedDelivery = deliveryDate;

            cargo.CurrentStatus = newStatus;

            if (newStatus == "Teslim Edildi" || newStatus == "İptal Edildi")
            {
                cargo.IsActive = false;
                message = $"Kargo bilgileri ve durumu ('{newStatus}') SQLite veritabanında güncellendi.\nKayıt PASİF (Arşiv) listesine taşındı.";
            }
            else
            {
                cargo.IsActive = true;
                message = $"Kargo bilgileri ve durumu ('{newStatus}') SQLite veritabanında başarıyla güncellendi.";
            }

            db.SaveChanges(); // UPDATE işlemini veritabanına kaydet
            return true;
        }
    }
}