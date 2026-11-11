namespace CargoTracking.Server.Models
{
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
    }
}