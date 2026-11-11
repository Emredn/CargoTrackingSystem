using CargoTracking.Grpc;
using CargoTracking.Server.Repositories;
using Grpc.Core;

namespace CargoTracking.Server.Services
{
    public class CargoGrpcService : CargoService.CargoServiceBase
    {
        private readonly CargoRepository _repository;
        private readonly DeliverySimulationManager _simManager;

        public CargoGrpcService(CargoRepository repository, DeliverySimulationManager simManager)
        {
            _repository = repository;
            _simManager = simManager;
        }

        public override async Task<CargoStatusResponse> GetCargoStatus(CargoLookupRequest request, ServerCallContext context)
        {
            await _simManager.EnsureAllInTransitCargosStartedAsync();
            _simManager.SyncAllActiveSimulationsWithRepo();

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
            await _simManager.EnsureAllInTransitCargosStartedAsync();
            _simManager.SyncAllActiveSimulationsWithRepo();

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
                _simManager.RemoveSimulation(request.TrackingNumber);
                await _simManager.EnsureSimulationStartedAsync(request.TrackingNumber, request.LastLocation);
            }
            else if (result && request.NewStatus != "Yola Çıktı")
            {
                _simManager.RemoveSimulation(request.TrackingNumber);
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
                await _simManager.EnsureAllInTransitCargosStartedAsync();
                _simManager.SyncAllActiveSimulationsWithRepo();

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

                var sim = await _simManager.EnsureSimulationStartedAsync(record.TrackingNumber, record.LastLocation);

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
                        _simManager.SyncAllActiveSimulationsWithRepo();
                        break;
                    }

                    await Task.Delay(1000, context.CancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
    }
}