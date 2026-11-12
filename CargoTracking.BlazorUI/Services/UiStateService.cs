using CargoTracking.Grpc;
using Microsoft.JSInterop;

namespace CargoTracking.BlazorUI.Services
{
    public class ToastItem
    {
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }
    }

    public class UiStateService
    {
        public bool IsDarkMode { get; private set; } = true;
        public string Breadcrumb { get; private set; } = "Operasyon Merkezi  /  Ana Sayfa";
        public int InTransitCount { get; private set; }
        public bool ServerOnline { get; private set; } = true;

        public List<ToastItem> Toasts { get; } = new();

        // Tüm sayfaların anlık erişebileceği merkezi kargo listesi
        public List<CargoStatusResponse> CachedCargos { get; private set; } = new();
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

        // Arka plan tarayıcısı her çalıştığında yeni durumu kontrol eder
        public async Task SyncCargosAsync(List<CargoStatusResponse> freshList, bool serverOnline)
        {
            if (!_isFirstLoad)
            {
                foreach (var cargo in freshList)
                {
                    if (_lastKnownStatuses.TryGetValue(cargo.TrackingNumber, out string? oldStatus))
                    {
                        // Kargo "Yola Çıktı" durumundan "Teslim Edildi" durumuna geçtiği an bildirim patlat!
                        if (oldStatus == "Yola Çıktı" && cargo.CurrentStatus == "Teslim Edildi")
                        {
                            _ = ShowToastAsync($"🎉 {cargo.TrackingNumber} ({cargo.ReceiverName}) adresine başarıyla teslim edildi!", true);
                        }
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
            InTransitCount = freshList.Count(c => c.CurrentStatus == "Yola Çıktı");
            ServerOnline = serverOnline;

            NotifyStateChanged();
        }

        public void UpdateMetrics(int inTransitCount, bool serverOnline)
        {
            InTransitCount = inTransitCount;
            ServerOnline = serverOnline;
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