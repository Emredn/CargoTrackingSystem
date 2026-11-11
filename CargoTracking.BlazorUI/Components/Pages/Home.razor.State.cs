using CargoTracking.Grpc;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace CargoTracking.BlazorUI.Components.Pages
{
    public partial class Home : ComponentBase, IDisposable
    {
        [Inject]
        public IJSRuntime JSRuntime { get; set; } = default!;

        private readonly string _serverAddress = "http://localhost:5135";
        private static readonly HttpClient _apiHttpClient = new();

        public class ProvinceModel
        {
            public string Name { get; set; } = "";
            public List<string> Districts { get; set; } = new();
        }

        private static List<ProvinceModel> _provinces = new();
        private bool _isDarkMode = true;

        private class ToastMessage
        {
            public string Message { get; set; } = "";
            public bool IsSuccess { get; set; }
        }
        private readonly List<ToastMessage> _toasts = new();

        private string _activePage = "HOME";
        private string _breadcrumb = "Operasyon Merkezi  /  Ana Sayfa";
        private readonly Stack<(string Page, string Breadcrumb, string Filter)> _navHistory = new();

        private string _clockText = DateTime.Now.ToString("dd.MM.yyyy  |  HH:mm:ss");
        private bool _serverOnline = true;
        private System.Threading.Timer? _clockTimer;
        private int _tickCounter = 0;

        private int _totalCount;
        private int _activeCount;
        private int _passiveCount;
        private int _inTransitCount;

        protected override async Task OnInitializedAsync()
        {
            _clockTimer = new System.Threading.Timer(async _ =>
            {
                _clockText = DateTime.Now.ToString("dd.MM.yyyy  |  HH:mm:ss");
                _tickCounter++;

                if (_tickCounter % 4 == 0)
                {
                    await LoadSummaryCountsAsync();
                }

                await InvokeAsync(StateHasChanged);
            }, null, 1000, 1000);

            await LoadTurkeyProvincesFromApiAsync();
            await LoadSummaryCountsAsync();
        }

        private async Task ShowToast(string message, bool success)
        {
            var toast = new ToastMessage { Message = message, IsSuccess = success };
            _toasts.Add(toast);
            StateHasChanged();
            await Task.Delay(3500);
            _toasts.Remove(toast);
            StateHasChanged();
        }

        private int GetPercentage(int part) => _totalCount == 0 ? 0 : (int)Math.Round((double)part / _totalCount * 100);

        private string GetBadgeClass(string status) => status switch
        {
            "Teslim Edildi" => "badge-delivered",
            "İptal Edildi" => "badge-cancelled",
            "Yola Çıktı" => "badge-transit",
            _ => "badge-prep"
        };

        private void NavigateTo(string targetPage, string breadcrumbText)
        {
            if (_activePage != targetPage)
            {
                _navHistory.Push((_activePage, _breadcrumb, _currentFilter));
            }
            _activePage = targetPage;
            _breadcrumb = breadcrumbText;
            _queryFeedback = "";
            _liveMapFeedback = "";
        }

        private async Task GoBackAsync()
        {
            if (_navHistory.Count == 0) return;
            StopLiveTracking(clearMessage: true);
            var prev = _navHistory.Pop();
            _activePage = prev.Page;
            _breadcrumb = prev.Breadcrumb;

            if (_activePage == "HOME") await LoadSummaryCountsAsync();
            else if (_activePage == "LIST") await LoadCargoListAsync(prev.Filter);
        }

        private async Task LoadTurkeyProvincesFromApiAsync()
        {
            if (_provinces.Any()) return;

            try
            {
                string json = await _apiHttpClient.GetStringAsync("https://turkiyeapi.dev/api/v1/provinces");
                using var doc = JsonDocument.Parse(json);
                var dataArray = doc.RootElement.GetProperty("data");

                var tempList = new List<ProvinceModel>();
                foreach (var item in dataArray.EnumerateArray())
                {
                    string provName = item.GetProperty("name").GetString() ?? "";
                    var distList = new List<string>();

                    if (item.TryGetProperty("districts", out var districtsElement))
                    {
                        foreach (var d in districtsElement.EnumerateArray())
                        {
                            string dName = d.GetProperty("name").GetString() ?? "";
                            if (!string.IsNullOrEmpty(dName)) distList.Add(dName);
                        }
                    }

                    tempList.Add(new ProvinceModel
                    {
                        Name = provName,
                        Districts = distList.OrderBy(x => x).ToList()
                    });
                }

                _provinces = tempList.OrderBy(p => p.Name).ToList();
            }
            catch
            {
                _provinces = new List<ProvinceModel>
                {
                    new() { Name = "Bursa", Districts = new() { "Nilüfer", "Osmangazi", "Yıldırım", "İnegöl", "Mudanya" } },
                    new() { Name = "İstanbul", Districts = new() { "Kadıköy", "Üsküdar", "Beşiktaş", "Ataşehir", "Maltepe" } }
                };
            }
        }

        private IEnumerable<string> GetDistrictsForCity(string cityName)
        {
            var prov = _provinces.FirstOrDefault(p => p.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
            return prov != null ? prov.Districts : Enumerable.Empty<string>();
        }

        public void Dispose()
        {
            StopLiveTracking(clearMessage: true);
            _clockTimer?.Dispose();
        }
    }
}