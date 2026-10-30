using CargoTracking.Grpc;
using CargoTracking.Server.Repositories;
using Grpc.Core;

namespace CargoTracking.Server.Services
{
    public class CargoGrpcService : CargoService.CargoServiceBase
    {
        private readonly CargoRepository _repository;

        public CargoGrpcService(CargoRepository repository)
        {
            _repository = repository;
        }

        public override Task<CargoStatusResponse> GetCargoStatus(CargoLookupRequest request, ServerCallContext context)
        {
            var record = _repository.GetByTrackingNumber(request.TrackingNumber);

            if (record == null)
            {
                return Task.FromResult(new CargoStatusResponse { IsFound = false });
            }

            return Task.FromResult(new CargoStatusResponse
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

            return Task.FromResult(response);
        }

        public override Task<UpdateStatusResponse> UpdateCargoStatus(UpdateStatusRequest request, ServerCallContext context)
        {
            bool result = _repository.UpdateCargoAndStatus(
                request.TrackingNumber,
                request.SenderName,
                request.ReceiverName,
                request.LastLocation,
                request.EstimatedDelivery,
                request.NewStatus,
                out string msg);

            return Task.FromResult(new UpdateStatusResponse
            {
                Success = result,
                Message = msg
            });
        }
    }
}