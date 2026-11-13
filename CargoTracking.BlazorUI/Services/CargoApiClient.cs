using CargoTracking.Grpc;
using Grpc.Net.Client;

namespace CargoTracking.BlazorUI.Services
{
    /// <summary>
    /// Sunucuyla (CargoTracking.Server) gRPC üzerinden konuşan tek nokta.
    /// Uygulama boyunca tek bir kanal açılır ve bütün sayfalar bunu paylaşır.
    /// Sunucu adresi appsettings.json içindeki "Grpc:ServerAddress" değerinden okunur.
    /// </summary>
    public class CargoApiClient : IDisposable
    {
        private readonly GrpcChannel _channel;
        public CargoService.CargoServiceClient Client { get; }

        /// <summary>Son çağrı başarılı olduysa true. Sunucu kapalıysa false olur.</summary>
        public bool IsServerOnline { get; private set; } = true;

        public CargoApiClient(IConfiguration configuration)
        {
            string address = configuration["Grpc:ServerAddress"] ?? "http://localhost:5135";
            _channel = GrpcChannel.ForAddress(address);
            Client = new CargoService.CargoServiceClient(_channel);
        }

        /// <summary>Tüm kargoları getirir. Sunucuya ulaşılamazsa hata fırlatmaz; boş liste döner ve IsServerOnline=false olur.</summary>
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
