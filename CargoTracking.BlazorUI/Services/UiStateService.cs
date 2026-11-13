using CargoTracking.Grpc;
using Microsoft.JSInterop;

namespace CargoTracking.BlazorUI.Services
{
    public class ToastItem
    {
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }
    }

    /// <summary>
    /// Bütün sayfaların ortak kullandığı arayüz durumu (kullanıcı oturumu başına bir tane):
    /// tema, ekmek kırıntısı (breadcrumb), bildirimler (toast), sunucu durumu ve kargo listesi önbelleği.
    /// Durum değişince OnChange olayı tetiklenir; ilgili bileşenler kendini yeniler.
    /// </summary>
    public class UiStateService
    {
        private readonly CargoApiClient _api;

        public UiStateService(CargoApiClient api)
        {
            _api = api;
        }

        public bool IsDarkMode { get; private set; } = true;
        public string Breadcrumb { get; private set; } = "Operasyon Merkezi  /  Ana Sayfa";
        public bool ServerOnline { get; private set; } = true;

        public List<ToastItem> Toasts { get; } = new();

        /// <summary>Sunucudan en son alınan kargo listesi. Arka plan taraması ve sayfalar bunu günceller.</summary>
        public List<CargoStatusResponse> CachedCargos { get; private set; } = new();

        /// <summary>Şu an "Yola Çıktı" durumundaki kargo sayısı (menüdeki rozet için).</summary>
        public int InTransitCount => CachedCargos.Count(c => c.CurrentStatus == "Yola Çıktı");

        private readonly Dictionary<string, string> _lastKnownStatuses = new();
        private bool _isFirstLoad = true;

        public event Action? OnChange;

        public void SetBreadcrumb(string text)
        {
            if (Breadcrumb != text)
            {
                Breadcrumb = text;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Kargo listesini sunucudan yeniler, önbelleği günceller ve abonelere haber verir.
        /// Sunucuya ulaşılamazsa son bilinen liste korunur, sadece "çevrimdışı" işaretlenir.
        /// </summary>
        public async Task<List<CargoStatusResponse>> RefreshCargosAsync()
        {
            var fresh = await _api.GetAllCargosSafeAsync();

            if (!_api.IsServerOnline)
            {
                ServerOnline = false;
                NotifyStateChanged();
                return CachedCargos;
            }

            ApplyFreshCargos(fresh);
            return fresh;
        }

        private void ApplyFreshCargos(List<CargoStatusResponse> freshList)
        {
            if (!_isFirstLoad)
            {
                foreach (var cargo in freshList)
                {
                    // Kargo "Yola Çıktı" durumundan "Teslim Edildi" durumuna geçtiği an bildirim göster
                    if (_lastKnownStatuses.TryGetValue(cargo.TrackingNumber, out string? oldStatus) &&
                        oldStatus == "Yola Çıktı" && cargo.CurrentStatus == "Teslim Edildi")
                    {
                        _ = ShowToastAsync($"🎉 {cargo.TrackingNumber} ({cargo.ReceiverName}) adresine başarıyla teslim edildi!", true);
                    }
                }
            }

            _lastKnownStatuses.Clear();
            foreach (var c in freshList)
            {
                _lastKnownStatuses[c.TrackingNumber] = c.CurrentStatus;
            }
            _isFirstLoad = false;

            CachedCargos = freshList;
            ServerOnline = true;

            NotifyStateChanged();
        }

        public async Task ToggleThemeAsync(IJSRuntime js)
        {
            IsDarkMode = !IsDarkMode;
            NotifyStateChanged();
            try
            {
                await js.InvokeVoidAsync("setMapTheme", IsDarkMode);
            }
            catch { }
        }

        public async Task ShowToastAsync(string message, bool isSuccess = true)
        {
            var item = new ToastItem { Message = message, IsSuccess = isSuccess };
            Toasts.Add(item);
            NotifyStateChanged();

            await Task.Delay(4000);
            Toasts.Remove(item);
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
