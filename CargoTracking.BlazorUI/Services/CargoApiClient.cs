using CargoTracking.Grpc;
using Grpc.Net.Client;

namespace CargoTracking.BlazorUI.Services
{
    public class CargoApiClient : IDisposable
    {
        private readonly GrpcChannel _channel;
        public CargoService.CargoServiceClient Client { get; }
        public bool IsServerOnline { get; private set; } = true;

        public CargoApiClient()
        {
            // Kanal uygulama boyunca sadece 1 kez oluşturulur ve tüm çağrılarda yeniden kullanılır
            _channel = GrpcChannel.ForAddress("http://localhost:5135");
            Client = new CargoService.CargoServiceClient(_channel);
        }

        public async Task<List<CargoStatusResponse>> GetAllCargosSafeAsync()
        {
            try
            {
                var res = await Client.GetAllCargosAsync(new EmptyRequest());
                IsServerOnline = true;
                return res.Cargos.ToList();
            }
            catch
            {
                IsServerOnline = false;
                return new List<CargoStatusResponse>();
            }
        }

        public void Dispose()
        {
            _channel.Dispose();
        }
    }
}