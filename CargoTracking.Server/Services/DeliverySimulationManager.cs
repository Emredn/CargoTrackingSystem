using System.Collections.Concurrent;
using CargoTracking.Server.Models;
using CargoTracking.Server.Repositories;

namespace CargoTracking.Server.Services
{
    /// <summary>
    /// "Yola Çıktı" durumundaki kargolar için canlı kurye simülasyonlarını kurar ve yönetir:
    ///  1. Adresi koordinata çevirir (sokak → mahalle → ilçe → il sırasıyla dener)
    ///  2. Çıkış merkezi ile teslimat noktası arasında karayolu rotası alır
    ///  3. Süre dolunca kargoyu "Teslim Edildi" yapar
    /// Bu sınıfı periyodik olarak SimulationBackgroundService çalıştırır; okuma isteklerinde (liste/sorgu) çağrılmaz.
    /// </summary>
    public class DeliverySimulationManager
    {
        // Harita servisine ulaşılamazsa aynı kargo için bu süre dolmadan tekrar denenmez
        private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(15);

        private readonly CargoRepository _repository;
        private readonly GeoRoutingService _geoService;
        private readonly ILogger<DeliverySimulationManager> _logger;

        private readonly ConcurrentDictionary<string, ActiveDeliverySimulation> _activeDeliveries = new();
        private readonly ConcurrentDictionary<string, DateTime> _retryAfterUtc = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public DeliverySimulationManager(
            CargoRepository repository,
            GeoRoutingService geoService,
            ILogger<DeliverySimulationManager> logger)
        {
            _repository = repository;
            _geoService = geoService;
            _logger = logger;
        }

        public void RemoveSimulation(string trackingNo)
        {
            _activeDeliveries.TryRemove(trackingNo, out _);
            _retryAfterUtc.TryRemove(trackingNo, out _);
        }

        /// <summary>Veritabanında "Yola Çıktı" olup simülasyonu henüz kurulmamış kargoların simülasyonunu kurar.</summary>
        public async Task EnsureAllInTransitCargosStartedAsync()
        {
            var pending = _repository.GetAll()
                .Where(c => c.CurrentStatus == "Yola Çıktı" && !_activeDeliveries.ContainsKey(c.TrackingNumber))
                .ToList();

            foreach (var cargo in pending)
            {
                await EnsureSimulationStartedAsync(cargo.TrackingNumber, cargo.LastLocation);
            }
        }

        /// <summary>Süresi dolan simülasyonları bitirir ve kargoyu "Teslim Edildi" durumuna alır.</summary>
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

        /// <summary>
        /// Kargonun simülasyonunu döndürür; yoksa kurar.
        /// Adres çözümlenemez ya da harita servisine ulaşılamazsa null döner (bir süre sonra yeniden denenir).
        /// </summary>
        public async Task<ActiveDeliverySimulation?> EnsureSimulationStartedAsync(string trackingNo, string rawAddress)
        {
            if (_activeDeliveries.TryGetValue(trackingNo, out var existing))
                return existing;

            if (IsInCooldown(trackingNo))
                return null;

            // Aynı anda iki çağrı aynı kargo için iki kere adres aramasın diye sıraya sokuyoruz
            await _initLock.WaitAsync();
            try
            {
                // Kilidi beklerken başka bir çağrı simülasyonu kurmuş olabilir
                if (_activeDeliveries.TryGetValue(trackingNo, out existing))
                    return existing;

                if (IsInCooldown(trackingNo))
                    return null;

                try
                {
                    var sim = await BuildSimulationAsync(trackingNo, rawAddress);
                    if (sim == null)
                    {
                        _retryAfterUtc[trackingNo] = DateTime.UtcNow + RetryCooldown;
                        return null;
                    }

                    _activeDeliveries[trackingNo] = sim;
                    _retryAfterUtc.TryRemove(trackingNo, out _);
                    return sim;
                }
                catch (GeocodingUnavailableException ex)
                {
                    _logger.LogWarning(ex, "{TrackingNo} için harita servisine ulaşılamadı, {Seconds} sn sonra yeniden denenecek.",
                        trackingNo, (int)RetryCooldown.TotalSeconds);
                    _retryAfterUtc[trackingNo] = DateTime.UtcNow + RetryCooldown;
                    return null;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        private bool IsInCooldown(string trackingNo) =>
            _retryAfterUtc.TryGetValue(trackingNo, out var until) && DateTime.UtcNow < until;

        private async Task<ActiveDeliverySimulation?> BuildSimulationAsync(string trackingNo, string rawAddress)
        {
            var address = AddressParser.Parse(rawAddress);

            if (string.IsNullOrWhiteSpace(address.City))
            {
                _logger.LogWarning("{TrackingNo}: adreste il bilgisi yok ('{Address}'), simülasyon kurulamadı.", trackingNo, rawAddress);
                return null;
            }

            var destination = await ResolveDestinationAsync(address);
            if (destination == null)
            {
                _logger.LogWarning("{TrackingNo}: adres haritada bulunamadı ('{Address}').", trackingNo, rawAddress);
                return null;
            }

            // Çıkış noktası: ilin merkezi ("Ana Transfer Merkezi"). İl sorgusu genellikle önbellekten gelir.
            var hub = await _geoService.GeocodeAsync(address.City, address.City);
            if (hub == null)
            {
                _logger.LogWarning("{TrackingNo}: '{City}' ili haritada bulunamadı.", trackingNo, address.City);
                return null;
            }

            var hubCoords = hub.Value;
            var dest = destination.Value;

            // Teslimat noktası il merkeziyle neredeyse aynıysa, haritada iki nokta ayrı görünsün diye çıkışı biraz kaydır
            if (Math.Abs(hubCoords.Lat - dest.Lat) < 0.005 && Math.Abs(hubCoords.Lng - dest.Lng) < 0.005)
            {
                hubCoords = (dest.Lat - 0.022, dest.Lng - 0.032);
            }

            var routePoints = await _geoService.GetRoadRouteAsync(hubCoords, (dest.Lat, dest.Lng));

            return new ActiveDeliverySimulation
            {
                TrackingNumber = trackingNo,
                CourierName = $"Kurye ({trackingNo})",
                HubName = $"{address.City} Ana Transfer Merkezi",
                StartLat = hubCoords.Lat,
                StartLng = hubCoords.Lng,
                EndLat = dest.Lat,
                EndLng = dest.Lng,
                RoutePoints = routePoints,
                StartedAt = DateTime.UtcNow,
                TotalDurationSeconds = 60,
                Precision = dest.Precision,
                PrecisionNote = dest.Precision switch
                {
                    AddressPrecision.Street => "",
                    AddressPrecision.Neighborhood => "Sokak haritada bulunamadı, mahalle merkezine yönlendirildi.",
                    AddressPrecision.District => "Mahalle haritada bulunamadı, ilçe merkezine yönlendirildi.",
                    _ => "İlçe haritada bulunamadı, il merkezine yönlendirildi."
                }
            };
        }

        /// <summary>
        /// Adresi en kesin seviyeden başlayarak arar: sokak → mahalle → ilçe → il.
        /// Her aşamada sonuç, seçilen il/ilçe/mahalleyle karşılaştırılır (yanlış yerdeki sonuç kabul edilmez).
        /// Hiçbir şey uydurulmaz: bulunamazsa null döner.
        /// </summary>
        private async Task<(double Lat, double Lng, AddressPrecision Precision)?> ResolveDestinationAsync(ParsedAddress a)
        {
            string neighborhood = AddressParser.StripNeighborhoodSuffix(a.Neighborhood);

            // 1) Cadde / sokak (kapı numarası aranmaz)
            if (!string.IsNullOrWhiteSpace(a.Street))
            {
                var p = await _geoService.GeocodeAsync(
                    JoinParts(a.Street, neighborhood, a.District, a.City), a.City, a.District, a.Neighborhood);
                if (p != null) return (p.Value.Lat, p.Value.Lng, AddressPrecision.Street);
            }

            // 2) Mahalle / köy ("Zafer Mahallesi" ve yalnızca "Zafer" diye iki şekilde dener)
            if (!string.IsNullOrWhiteSpace(neighborhood))
            {
                string[] queries =
                {
                    JoinParts($"{neighborhood} Mahallesi", a.District, a.City),
                    JoinParts(neighborhood, a.District, a.City)
                };

                foreach (var q in queries)
                {
                    var p = await _geoService.GeocodeAsync(q, a.City, a.District, a.Neighborhood);
                    if (p != null) return (p.Value.Lat, p.Value.Lng, AddressPrecision.Neighborhood);
                }
            }

            // 3) İlçe merkezi ("Merkez" ilçesi aranmaz, doğrudan ile geçilir)
            if (!string.IsNullOrWhiteSpace(a.District) &&
                !a.District.Equals("Merkez", StringComparison.OrdinalIgnoreCase))
            {
                var p = await _geoService.GeocodeAsync(JoinParts(a.District, a.City), a.City, a.District);
                if (p != null) return (p.Value.Lat, p.Value.Lng, AddressPrecision.District);
            }

            // 4) İl merkezi
            var city = await _geoService.GeocodeAsync(a.City, a.City);
            if (city != null) return (city.Value.Lat, city.Value.Lng, AddressPrecision.City);

            return null;
        }

        private static string JoinParts(params string[] parts) =>
            string.Join(", ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
