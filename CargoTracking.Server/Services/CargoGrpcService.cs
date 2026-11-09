using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CargoTracking.Grpc;
using CargoTracking.Server.Repositories;
using Grpc.Core;

namespace CargoTracking.Server.Services
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

    public class CargoGrpcService : CargoService.CargoServiceBase
    {
        private readonly CargoRepository _repository;
        private static readonly HttpClient _httpClient = new();

        private static readonly ConcurrentDictionary<string, ActiveDeliverySimulation> _activeDeliveries = new();
        private static readonly ConcurrentDictionary<string, (double Lat, double Lng)> _geoCache = new();
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        static CargoGrpcService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KargoPro_Enterprise_Logistics/2.4");
        }

        public CargoGrpcService(CargoRepository repository)
        {
            _repository = repository;
        }

        public override async Task<CargoStatusResponse> GetCargoStatus(CargoLookupRequest request, ServerCallContext context)
        {
            await EnsureAllInTransitCargosStartedAsync();
            SyncAllActiveSimulationsWithRepo();

            var record = _repository.GetByTrackingNumber(request.TrackingNumber);

            if (record == null)
                return new CargoStatusResponse { IsFound = false };

            return new CargoStatusResponse
            {
                IsFound = true,
                TrackingNumber = record.TrackingNumber,
                SenderName = record.SenderName,
                ReceiverName = record.ReceiverName,
                CurrentStatus = record.CurrentStatus,
                LastLocation = record.LastLocation,
                EstimatedDelivery = record.EstimatedDelivery,
                IsActive = record.IsActive
            };
        }

        public override Task<CreateCargoResponse> CreateCargo(CreateCargoRequest request, ServerCallContext context)
        {
            var created = _repository.AddCargo(
                request.SenderName,
                request.ReceiverName,
                request.LastLocation,
                request.EstimatedDelivery);

            return Task.FromResult(new CreateCargoResponse
            {
                Success = true,
                GeneratedTrackingNumber = created.TrackingNumber,
                Message = "Kargo başarıyla oluşturuldu ve Aktif listeye eklendi."
            });
        }

        public override async Task<CargoListResponse> GetAllCargos(EmptyRequest request, ServerCallContext context)
        {
            // Uygulama açılır açılmaz "Yola Çıktı" olan tüm kargoları arka planda harekete geçir!
            await EnsureAllInTransitCargosStartedAsync();
            SyncAllActiveSimulationsWithRepo();

            var response = new CargoListResponse();
            var allCargos = _repository.GetAll();

            foreach (var record in allCargos)
            {
                response.Cargos.Add(new CargoStatusResponse
                {
                    IsFound = true,
                    TrackingNumber = record.TrackingNumber,
                    SenderName = record.SenderName,
                    ReceiverName = record.ReceiverName,
                    CurrentStatus = record.CurrentStatus,
                    LastLocation = record.LastLocation,
                    EstimatedDelivery = record.EstimatedDelivery,
                    IsActive = record.IsActive
                });
            }

            return response;
        }

        public override async Task<UpdateStatusResponse> UpdateCargoStatus(UpdateStatusRequest request, ServerCallContext context)
        {
            bool result = _repository.UpdateCargoAndStatus(
                request.TrackingNumber,
                request.SenderName,
                request.ReceiverName,
                request.LastLocation,
                request.EstimatedDelivery,
                request.NewStatus,
                out string msg);

            if (result && request.NewStatus == "Yola Çıktı")
            {
                _activeDeliveries.TryRemove(request.TrackingNumber, out _);
                await EnsureSimulationStartedAsync(request.TrackingNumber, request.LastLocation);
            }
            else if (result && request.NewStatus != "Yola Çıktı")
            {
                _activeDeliveries.TryRemove(request.TrackingNumber, out _);
            }

            return new UpdateStatusResponse
            {
                Success = result,
                Message = msg
            };
        }

        public override async Task StreamCourierLocation(
            CargoLookupRequest request,
            IServerStreamWriter<LiveCourierLocation> responseStream,
            ServerCallContext context)
        {
            try
            {
                await EnsureAllInTransitCargosStartedAsync();
                SyncAllActiveSimulationsWithRepo();

                var record = _repository.GetByTrackingNumber(request.TrackingNumber);

                if (record == null)
                {
                    await responseStream.WriteAsync(new LiveCourierLocation
                    {
                        TrackingNumber = request.TrackingNumber,
                        CurrentStepText = $"{request.TrackingNumber} numaralı kargo sistemde bulunamadı."
                    });
                    return;
                }

                if (record.CurrentStatus != "Yola Çıktı")
                {
                    await responseStream.WriteAsync(new LiveCourierLocation
                    {
                        TrackingNumber = request.TrackingNumber,
                        CurrentStepText = $"Kargo şu anda '{record.CurrentStatus}' durumunda. Canlı harita sadece 'Yola Çıktı' durumundaki kargolar içindir.",
                        IsDelivered = (record.CurrentStatus == "Teslim Edildi")
                    });
                    return;
                }

                var sim = await EnsureSimulationStartedAsync(record.TrackingNumber, record.LastLocation);

                while (!context.CancellationToken.IsCancellationRequested)
                {
                    double elapsed = (DateTime.UtcNow - sim.StartedAt).TotalSeconds;
                    double progress = Math.Clamp(elapsed / sim.TotalDurationSeconds, 0.0, 1.0);

                    int totalPts = sim.RoutePoints.Count;
                    double exactIndex = progress * (totalPts - 1);
                    int idx = (int)Math.Floor(exactIndex);
                    int nextIdx = Math.Min(idx + 1, totalPts - 1);
                    double localFraction = exactIndex - idx;

                    double curLat = sim.RoutePoints[idx].Lat + (sim.RoutePoints[nextIdx].Lat - sim.RoutePoints[idx].Lat) * localFraction;
                    double curLng = sim.RoutePoints[idx].Lng + (sim.RoutePoints[nextIdx].Lng - sim.RoutePoints[idx].Lng) * localFraction;

                    bool isArrived = progress >= 1.0;

                    string statusText = progress switch
                    {
                        < 0.15 => $"{sim.HubName} noktasından çıkış yapıldı, kurye rotada.",
                        < 0.50 => $"Ana arter üzerinden '{record.LastLocation}' yönüne ilerleniyor (%{Math.Round(progress * 100)}).",
                        < 0.85 => $"Teslimat ilçesine girildi, hedefe yaklaşıldı (%{Math.Round(progress * 100)}).",
                        < 1.00 => "Kurye teslimat sokağında, alıcı adresine varmak üzere.",
                        _ => $"Kargo '{record.LastLocation}' adresine başarıyla teslim edildi!"
                    };

                    await responseStream.WriteAsync(new LiveCourierLocation
                    {
                        TrackingNumber = record.TrackingNumber,
                        Latitude = curLat,
                        Longitude = curLng,
                        CourierName = sim.CourierName,
                        CurrentStepText = statusText,
                        IsDelivered = isArrived,
                        StartLat = sim.StartLat,
                        StartLng = sim.StartLng,
                        EndLat = sim.EndLat,
                        EndLng = sim.EndLng
                    });

                    if (isArrived)
                    {
                        SyncAllActiveSimulationsWithRepo();
                        break;
                    }

                    await Task.Delay(1000, context.CancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı "Durdur" butonuna bastığında veya başka kargoya geçtiğinde hata vermeden çık
            }
            catch (Exception)
            {
                // İstemci bağlantıyı kapattığında oluşan stream istisnalarını yut
            }
        }

        // Sistemdeki tüm "Yola Çıktı" statüsündeki kargoları uygulama başlar başlamaz eşzamanlı yola çıkarır
        private async Task EnsureAllInTransitCargosStartedAsync()
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

        private void SyncAllActiveSimulationsWithRepo()
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

        private async Task<(double Lat, double Lng)?> GeocodeLockedAsync(string urlQuery)
        {
            if (_geoCache.TryGetValue(urlQuery, out var cached))
                return cached;

            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?format=json&{urlQuery}&countrycodes=tr&limit=1";
                var json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    double lat = double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                    double lng = double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    _geoCache[urlQuery] = (lat, lng);
                    return (lat, lng);
                }
            }
            catch { }

            return null;
        }

        private async Task<ActiveDeliverySimulation> EnsureSimulationStartedAsync(string trackingNo, string rawAddress)
        {
            if (_activeDeliveries.TryGetValue(trackingNo, out var existing))
                return existing;

            string city = "Bursa";
            string district = "Nilüfer";
            string detail = "";

            int dashIdx = rawAddress.IndexOf('-');
            string leftPart = dashIdx > 0 ? rawAddress.Substring(0, dashIdx).Trim() : rawAddress;
            detail = dashIdx > 0 ? rawAddress.Substring(dashIdx + 1).Trim() : "";

            var slashParts = leftPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (slashParts.Length >= 2)
            {
                city = slashParts[0].Trim();
                district = slashParts[1].Trim();
            }
            else if (slashParts.Length == 1)
            {
                city = slashParts[0].Trim();
            }

            string hubQuery = $"state={Uri.EscapeDataString(city)}&city={Uri.EscapeDataString(city)}";
            var hubCoords = await GeocodeLockedAsync(hubQuery)
                         ?? await GeocodeLockedAsync($"q={Uri.EscapeDataString(city + " Merkez, Türkiye")}")
                         ?? (40.1885, 29.0610);

            (double Lat, double Lng)? destCoords = null;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                string fullQuery = $"q={Uri.EscapeDataString($"{detail}, {district}, {city}, Türkiye")}";
                destCoords = await GeocodeLockedAsync(fullQuery);
            }

            if (destCoords == null)
            {
                string districtQuery = $"city={Uri.EscapeDataString(district)}&state={Uri.EscapeDataString(city)}";
                destCoords = await GeocodeLockedAsync(districtQuery)
                          ?? await GeocodeLockedAsync($"q={Uri.EscapeDataString($"{district}, {city}, Türkiye")}");
            }

            var finalDest = destCoords ?? (hubCoords.Lat + 0.025, hubCoords.Lng + 0.035);

            if (Math.Abs(hubCoords.Lat - finalDest.Lat) < 0.005 && Math.Abs(hubCoords.Lng - finalDest.Lng) < 0.005)
            {
                hubCoords = (finalDest.Lat - 0.022, finalDest.Lng - 0.032);
            }

            var routePoints = new List<(double Lat, double Lng)>();
            try
            {
                string osrmUrl = string.Create(CultureInfo.InvariantCulture,
                    $"https://router.project-osrm.org/route/v1/driving/{hubCoords.Lng},{hubCoords.Lat};{finalDest.Lng},{finalDest.Lat}?overview=full&geometries=geojson");

                var osrmJson = await _httpClient.GetStringAsync(osrmUrl);
                using var doc = JsonDocument.Parse(osrmJson);
                var routes = doc.RootElement.GetProperty("routes");
                if (routes.GetArrayLength() > 0)
                {
                    var coords = routes[0].GetProperty("geometry").GetProperty("coordinates");
                    foreach (var pt in coords.EnumerateArray())
                    {
                        routePoints.Add((pt[1].GetDouble(), pt[0].GetDouble()));
                    }
                }
            }
            catch { }

            if (routePoints.Count < 2)
            {
                for (int i = 0; i <= 20; i++)
                {
                    double t = i / 20.0;
                    routePoints.Add((hubCoords.Lat + (finalDest.Lat - hubCoords.Lat) * t, hubCoords.Lng + (finalDest.Lng - hubCoords.Lng) * t));
                }
            }

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