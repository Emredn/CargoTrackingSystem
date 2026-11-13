namespace CargoTracking.Server.Services
{
    /// <summary>
    /// Sunucu çalıştığı sürece her saniye arka planda iki iş yapar:
    ///  - Teslim süresi dolan kargoları "Teslim Edildi" yapar
    ///  - "Yola Çıktı" olup simülasyonu kurulmamış kargoların simülasyonunu kurar (örn. sunucu yeniden başladıysa)
    /// Böylece liste/sorgu gibi okuma istekleri bu işleri beklemek zorunda kalmaz.
    /// </summary>
    public class SimulationBackgroundService : BackgroundService
    {
        private readonly DeliverySimulationManager _simManager;
        private readonly ILogger<SimulationBackgroundService> _logger;

        public SimulationBackgroundService(DeliverySimulationManager simManager, ILogger<SimulationBackgroundService> logger)
        {
            _simManager = simManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        // Önce biten teslimatları kapat, sonra (yavaş olabilen) yeni simülasyon kurulumlarına geç
                        _simManager.SyncAllActiveSimulationsWithRepo();
                        await _simManager.EnsureAllInTransitCargosStartedAsync();
                    }
                    catch (Exception ex)
                    {
                        // Bir turdaki hata servisi durdurmasın
                        _logger.LogError(ex, "Simülasyon arka plan turunda hata oluştu.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Sunucu kapanıyor, normal durum
            }
        }
    }
}
