using System.Collections.Concurrent;
using CargoTracking.Server.Models;
using CargoTracking.Server.Repositories;

namespace CargoTracking.Server.Services
{
    public class DeliverySimulationManager
    {
        private readonly CargoRepository _repository;
        private readonly GeoRoutingService _geoService;

        private readonly ConcurrentDictionary<string, ActiveDeliverySimulation> _activeDeliveries = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public DeliverySimulationManager(CargoRepository repository, GeoRoutingService geoService)
        {
            _repository = repository;
            _geoService = geoService;
        }

        public void RemoveSimulation(string trackingNo)
        {
            _activeDeliveries.TryRemove(trackingNo, out _);
        }

        public async Task EnsureAllInTransitCargosStartedAsync()
        {
            var inTransitList = _repository.GetAll()
                .Where(c => c.CurrentStatus == "Yola Çıktı" && !_activeDeliveries.ContainsKey(c.TrackingNumber))
                .ToList();

            if (inTransitList.Count == 0) return;

            await _initLock.WaitAsync();
            try
            {
                foreach (var cargo in inTransitList)
                {
                    if (!_activeDeliveries.ContainsKey(cargo.TrackingNumber))
                    {
                        await EnsureSimulationStartedAsync(cargo.TrackingNumber, cargo.LastLocation);
                    }
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        public void SyncAllActiveSimulationsWithRepo()
        {
            foreach (var kvp in _activeDeliveries.ToArray())
            {
                var sim = kvp.Value;
                if ((DateTime.UtcNow - sim.StartedAt).TotalSeconds >= sim.TotalDurationSeconds)
                {
                    var rec = _repository.GetByTrackingNumber(sim.TrackingNumber);
                    if (rec != null && rec.CurrentStatus == "Yola Çıktı")
                    {
                        _repository.UpdateCargoAndStatus(
                            rec.TrackingNumber,
                            rec.SenderName,
                            rec.ReceiverName,
                            rec.LastLocation,
                            rec.EstimatedDelivery,
                            "Teslim Edildi",
                            out _);
                    }
                    _activeDeliveries.TryRemove(kvp.Key, out _);
                }
            }
        }

        public async Task<ActiveDeliverySimulation> EnsureSimulationStartedAsync(string trackingNo, string rawAddress)
        {
            if (_activeDeliveries.TryGetValue(trackingNo, out var existing))
                return existing;

            string city = "Bursa";
            string district = "Nilüfer";
            string neighborhood = "";
            string detail = "";

            int dashIdx = rawAddress.IndexOf('-');
            string leftPart = dashIdx > 0 ? rawAddress.Substring(0, dashIdx).Trim() : rawAddress;
            detail = dashIdx > 0 ? rawAddress.Substring(dashIdx + 1).Trim() : "";

            // Format: "İl / İlçe / Mahalle - Açık Adres"
            var slashParts = leftPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (slashParts.Length >= 3)
            {
                city = slashParts[0].Trim();
                district = slashParts[1].Trim();
                neighborhood = slashParts[2].Trim();
            }
            else if (slashParts.Length == 2)
            {
                city = slashParts[0].Trim();
                district = slashParts[1].Trim();
            }
            else if (slashParts.Length == 1)
            {
                city = slashParts[0].Trim();
            }

            // Nominatim'in daha kolay bulması için "Mah." kısaltmasını temizle
            string cleanNeighborhood = neighborhood
                .Replace("Mahallesi", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Mah.", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            // 1. ÇIKIŞ NOKTASI (İl Ana Transfer Merkezi)
            string hubQuery = $"state={Uri.EscapeDataString(city)}&city={Uri.EscapeDataString(city)}";
            var hubCoords = await _geoService.GeocodeLockedAsync(hubQuery)
                         ?? await _geoService.GeocodeLockedAsync($"q={Uri.EscapeDataString(city + " Merkez, Türkiye")}")
                         ?? (40.1885, 29.0610);

            (double Lat, double Lng)? destCoords = null;

            // AŞAMA 1: Cadde/Sokak + Mahalle + İlçe + İl dene
            if (!string.IsNullOrWhiteSpace(detail))
            {
                string q1 = !string.IsNullOrWhiteSpace(cleanNeighborhood)
                    ? $"{detail}, {cleanNeighborhood}, {district}, {city}, Türkiye"
                    : $"{detail}, {district}, {city}, Türkiye";

                destCoords = await _geoService.GeocodeLockedAsync($"q={Uri.EscapeDataString(q1)}");
            }

            // AŞAMA 2 (MAHALLE GARANTİSİ): Açık adresteki bina/kapı no yüzünden bulamadıysa direkt MAHALLEYE git!
            if (destCoords == null && !string.IsNullOrWhiteSpace(cleanNeighborhood))
            {
                destCoords = await _geoService.GeocodeLockedAsync($"q={Uri.EscapeDataString($"{cleanNeighborhood} Mahallesi, {district}, {city}, Türkiye")}")
                          ?? await _geoService.GeocodeLockedAsync($"q={Uri.EscapeDataString($"{cleanNeighborhood}, {district}, {city}, Türkiye")}");
            }

            // AŞAMA 3: Mahalle de bulunamazsa İlçe merkezine git
            if (destCoords == null)
            {
                string districtQuery = $"city={Uri.EscapeDataString(district)}&state={Uri.EscapeDataString(city)}";
                destCoords = await _geoService.GeocodeLockedAsync(districtQuery)
                          ?? await _geoService.GeocodeLockedAsync($"q={Uri.EscapeDataString($"{district}, {city}, Türkiye")}");
            }

            var finalDest = destCoords ?? (hubCoords.Lat + 0.025, hubCoords.Lng + 0.035);

            if (Math.Abs(hubCoords.Lat - finalDest.Lat) < 0.005 && Math.Abs(hubCoords.Lng - finalDest.Lng) < 0.005)
            {
                hubCoords = (finalDest.Lat - 0.022, finalDest.Lng - 0.032);
            }

            var routePoints = await _geoService.GetRoadRouteAsync(hubCoords, finalDest);

            var newSim = new ActiveDeliverySimulation
            {
                TrackingNumber = trackingNo,
                CourierName = $"Kurye ({trackingNo})",
                HubName = $"{city} Ana Transfer Merkezi",
                StartLat = hubCoords.Lat,
                StartLng = hubCoords.Lng,
                EndLat = finalDest.Lat,
                EndLng = finalDest.Lng,
                RoutePoints = routePoints,
                StartedAt = DateTime.UtcNow,
                TotalDurationSeconds = 60
            };

            _activeDeliveries[trackingNo] = newSim;
            return newSim;
        }
    }
}