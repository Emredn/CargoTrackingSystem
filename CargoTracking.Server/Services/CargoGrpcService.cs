using CargoTracking.Grpc;
using CargoTracking.Server.Models;
using CargoTracking.Server.Repositories;
using Grpc.Core;

namespace CargoTracking.Server.Services
{
    /// <summary>
    /// gRPC uç noktaları: BlazorUI'ın sunucuyla konuştuğu yer (kargo sorgula / oluştur / listele / güncelle / canlı konum akışı).
    /// Okuma istekleri (GetCargoStatus, GetAllCargos) sadece veritabanını okur; simülasyonları
    /// SimulationBackgroundService yürütür.
    /// </summary>
    public class CargoGrpcService : CargoService.CargoServiceBase
    {
        private readonly CargoRepository _repository;
        private readonly DeliverySimulationManager _simManager;
        private readonly ILogger<CargoGrpcService> _logger;

        public CargoGrpcService(
            CargoRepository repository,
            DeliverySimulationManager simManager,
            ILogger<CargoGrpcService> logger)
        {
            _repository = repository;
            _simManager = simManager;
            _logger = logger;
        }

        public override Task<CargoStatusResponse> GetCargoStatus(CargoLookupRequest request, ServerCallContext context)
        {
            var record = _repository.GetByTrackingNumber(request.TrackingNumber);

            return Task.FromResult(record == null
                ? new CargoStatusResponse { IsFound = false }
                : ToResponse(record));
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

        public override Task<CargoListResponse> GetAllCargos(EmptyRequest request, ServerCallContext context)
        {
            var response = new CargoListResponse();
            foreach (var record in _repository.GetAll())
            {
                response.Cargos.Add(ToResponse(record));
            }

            return Task.FromResult(response);
        }

        public override Task<UpdateStatusResponse> UpdateCargoStatus(UpdateStatusRequest request, ServerCallContext context)
        {
            var existingRecord = _repository.GetByTrackingNumber(request.TrackingNumber);
            string oldStatus = existingRecord?.CurrentStatus ?? "";

            bool result = _repository.UpdateCargoAndStatus(
                request.TrackingNumber,
                request.SenderName,
                request.ReceiverName,
                request.LastLocation,
                request.EstimatedDelivery,
                request.NewStatus,
                out string msg);

            if (result)
            {
                // Yeni yola çıkıyorsa ya da artık yolda değilse eski simülasyon silinir.
                // Zaten yoldaysa kuryenin konumu bozulmaz. Yeni simülasyonu arka plan servisi kurar
                // (adres aramasını beklemeden kaydetme hızlı döner).
                if (request.NewStatus != "Yola Çıktı" || oldStatus != "Yola Çıktı")
                {
                    _simManager.RemoveSimulation(request.TrackingNumber);
                }
            }

            return Task.FromResult(new UpdateStatusResponse
            {
                Success = result,
                Message = msg
            });
        }

        /// <summary>Kuryenin konumunu saniyede bir istemciye akıtır (server streaming).</summary>
        public override async Task StreamCourierLocation(
            CargoLookupRequest request,
            IServerStreamWriter<LiveCourierLocation> responseStream,
            ServerCallContext context)
        {
            try
            {
                var record = _repository.GetByTrackingNumber(request.TrackingNumber);

                if (record == null)
                {
                    await WriteMessageOnlyAsync(responseStream, request.TrackingNumber,
                        $"{request.TrackingNumber} numaralı kargo sistemde bulunamadı.");
                    return;
                }

                if (record.CurrentStatus != "Yola Çıktı")
                {
                    await WriteMessageOnlyAsync(responseStream, request.TrackingNumber,
                        $"Kargo şu anda '{record.CurrentStatus}' durumunda. Canlı harita sadece 'Yola Çıktı' durumundaki kargolar içindir.",
                        isDelivered: record.CurrentStatus == "Teslim Edildi");
                    return;
                }

                var sim = await _simManager.EnsureSimulationStartedAsync(record.TrackingNumber, record.LastLocation);

                if (sim == null)
                {
                    // Koordinat yok (latitude/longitude = 0): istemci bunu hata mesajı olarak gösterir
                    await WriteMessageOnlyAsync(responseStream, request.TrackingNumber,
                        "Teslimat adresi haritada çözümlenemedi (harita servisine ulaşılamıyor olabilir). Birkaç saniye sonra tekrar deneyin.");
                    return;
                }

                // Adres tam bulunamadıysa (örn. sadece mahalle merkezi) kullanıcıya her mesajda not düşülür
                string note = string.IsNullOrEmpty(sim.PrecisionNote) ? "" : $" ⚠ {sim.PrecisionNote}";

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
                        CurrentStepText = statusText + note,
                        IsDelivered = isArrived,
                        StartLat = sim.StartLat,
                        StartLng = sim.StartLng,
                        EndLat = sim.EndLat,
                        EndLng = sim.EndLng
                    });

                    if (isArrived)
                    {
                        // Durumu hemen "Teslim Edildi" yap (arka plan servisini beklemeden)
                        _simManager.SyncAllActiveSimulationsWithRepo();
                        break;
                    }

                    await Task.Delay(1000, context.CancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // İstemci akışı kapattı (sayfadan çıktı / durdurdu), normal durum
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{TrackingNo} canlı konum akışı hata verdi.", request.TrackingNumber);
            }
        }

        // Veritabanı kaydını gRPC cevabına çevirir (3 yerde tekrar eden kod tek yerde toplandı)
        private static CargoStatusResponse ToResponse(CargoRecord record) => new()
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

        // Sadece mesaj taşıyan (koordinatsız) akış paketi gönderir; istemci bunu uyarı olarak gösterir
        private static Task WriteMessageOnlyAsync(
            IServerStreamWriter<LiveCourierLocation> stream, string trackingNumber, string message, bool isDelivered = false) =>
            stream.WriteAsync(new LiveCourierLocation
            {
                TrackingNumber = trackingNumber,
                CurrentStepText = message,
                IsDelivered = isDelivered
            });
    }
}
