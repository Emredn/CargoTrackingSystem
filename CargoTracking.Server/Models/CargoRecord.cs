using System.ComponentModel.DataAnnotations;

namespace CargoTracking.Server.Models
{
    public class CargoRecord
    {
        [Key] // SQLite tablosundaki Primary Key (Birincil Anahtar)
        public string TrackingNumber { get; set; } = string.Empty;

        public string SenderName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string LastLocation { get; set; } = string.Empty;
        public string EstimatedDelivery { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}