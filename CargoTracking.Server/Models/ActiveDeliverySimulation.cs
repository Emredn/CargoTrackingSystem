namespace CargoTracking.Server.Models
{
    /// <summary>
    /// "Yola Çıktı" durumundaki bir kargo için sunucu hafızasında tutulan canlı kurye simülasyonu.
    /// Veritabanına yazılmaz; sunucu yeniden başlarsa simülasyon baştan kurulur.
    /// </summary>
    public class ActiveDeliverySimulation
    {
        public string TrackingNumber { get; set; } = "";
        public string CourierName { get; set; } = "";
        public string HubName { get; set; } = "";
        public double StartLat { get; set; }
        public double StartLng { get; set; }
        public double EndLat { get; set; }
        public double EndLng { get; set; }
        public List<(double Lat, double Lng)> RoutePoints { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public int TotalDurationSeconds { get; set; } = 60; // 60 saniyelik gerçekçi teslimat süresi

        /// <summary>Adres haritada ne kadar kesin bulundu (sokak / mahalle / ilçe / il).</summary>
        public AddressPrecision Precision { get; set; } = AddressPrecision.Street;

        /// <summary>Adres tam bulunamadıysa kullanıcıya gösterilecek uyarı. Tam bulunduysa boştur.</summary>
        public string PrecisionNote { get; set; } = "";
    }
}
