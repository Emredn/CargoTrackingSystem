using CargoTracking.Grpc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CargoTracking.BlazorUI.Components.Pages // Proje namespace'ine göre gerekirse düzelt
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

        private string _newSender = "";
        private string _newReceiver = "";
        private string _newCity = "";
        private string _newDistrict = "";
        private string _newStreetDetail = "";
        private DateTime _newDeliveryDate = DateTime.Today;

        private string _trackingDigits = "";
        private bool _showDetailEditCard;
        private string _editTrackingNo = "";
        private bool _editIsActive;
        private string _editSender = "";
        private string _editReceiver = "";
        private string _editCity = "";
        private string _editDistrict = "";
        private string _editStreetDetail = "";
        private DateTime _editDeliveryDate = DateTime.Today;
        private string _editStatus = "Hazırlanıyor";
        private string _queryFeedback = "";
        private bool _querySuccess;

        private string _currentFilter = "ALL";
        private string _liveSearch = "";
        private List<CargoStatusResponse> _cachedCargos = new();
        private CargoStatusResponse? _selectedCargo;
        private bool _copiedJustNow;

        private string _liveMapDigits = "";
        private string _liveMapFeedback = "";
        private bool _liveMapSuccess;
        private bool _isStreamRunning;
        private bool _isMapVisible;
        private string _courierName = "Kurye";
        private string _liveStepMessage = "";
        private CancellationTokenSource? _streamCts;

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

        private async Task ToggleThemeAsync()
        {
            _isDarkMode = !_isDarkMode;
            if (_isMapVisible)
            {
                try { await JSRuntime.InvokeVoidAsync("setMapTheme", _isDarkMode); } catch { }
            }
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

        private async Task JumpToLiveMapAsync(string fullTrackingNo)
        {
            string digits = fullTrackingNo.Replace("TR-", "");
            NavigateTo("LIVE_MAP", "Operasyon Merkezi  /  Canlı Kargo Takibi");
            _liveMapDigits = digits;
            await StartLiveTrackingAsync();
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

        private void OnNewCityChanged(ChangeEventArgs e)
        {
            _newCity = e.Value?.ToString() ?? "";
            _newDistrict = "";
        }

        private void OnEditCityChanged(ChangeEventArgs e)
        {
            _editCity = e.Value?.ToString() ?? "";
            _editDistrict = "";
        }

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

        private void ResetQueryScreen()
        {
            _trackingDigits = "";
            _showDetailEditCard = false;
            _queryFeedback = "";
        }

        private void OnTrackingCodeInput(ChangeEventArgs e)
        {
            string raw = e.Value?.ToString() ?? "";
            _trackingDigits = new string(raw.Where(char.IsDigit).Take(4).ToArray());
        }

        private async Task OnTrackingCodeKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await SearchCargoByCodeAsync();
        }

        private void OnLiveMapInput(ChangeEventArgs e)
        {
            string raw = e.Value?.ToString() ?? "";
            _liveMapDigits = new string(raw.Where(char.IsDigit).Take(4).ToArray());
        }

        private async Task OnLiveMapKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await StartLiveTrackingAsync();
        }

        private async Task LoadSummaryCountsAsync()
        {
            try
            {
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(_serverAddress);
                var client = new CargoService.CargoServiceClient(channel);
                var response = await client.GetAllCargosAsync(new EmptyRequest());

                _cachedCargos = response.Cargos.ToList();
                _totalCount = _cachedCargos.Count;
                _activeCount = _cachedCargos.Count(c => c.IsActive);
                _passiveCount = _cachedCargos.Count(c => !c.IsActive);
                _inTransitCount = _cachedCargos.Count(c => c.CurrentStatus == "Yola Çıktı");
                _serverOnline = true;
            }
            catch
            {
                _serverOnline = false;
            }
        }

        private async Task CreateCargoAsync()
        {
            if (string.IsNullOrWhiteSpace(_newSender) || string.IsNullOrWhiteSpace(_newReceiver))
            {
                await ShowToast("Gönderici ve alıcı bilgisi zorunludur.", false);
                return;
            }

            if (string.IsNullOrWhiteSpace(_newCity) || string.IsNullOrWhiteSpace(_newDistrict) || string.IsNullOrWhiteSpace(_newStreetDetail))
            {
                await ShowToast("İl, ilçe ve açık adres seçilmelidir.", false);
                return;
            }

            string structuredAddress = $"{_newCity.Trim()} / {_newDistrict.Trim()} - {_newStreetDetail.Trim()}";

            try
            {
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(_serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var response = await client.CreateCargoAsync(new CreateCargoRequest
                {
                    SenderName = _newSender.Trim(),
                    ReceiverName = _newReceiver.Trim(),
                    LastLocation = structuredAddress,
                    EstimatedDelivery = _newDeliveryDate.ToString("dd.MM.yyyy")
                });

                if (response.Success)
                {
                    await ShowToast($"Sevkiyat Oluşturuldu! Barkod: {response.GeneratedTrackingNumber}", true);
                    _newSender = "";
                    _newReceiver = "";
                    _newCity = "";
                    _newDistrict = "";
                    _newStreetDetail = "";
                    _newDeliveryDate = DateTime.Today;
                    await LoadSummaryCountsAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowToast($"Sunucu hatası: {ex.Message}", false);
            }
        }

        private async Task SearchCargoByCodeAsync()
        {
            if (_trackingDigits.Length != 4)
            {
                _querySuccess = false;
                _queryFeedback = "Lütfen 4 haneli kargo takip kodunu girin.";
                return;
            }

            await FetchCargoForEditAsync($"TR-{_trackingDigits}");
        }

        private async Task FetchCargoForEditAsync(string fullTrackNo)
        {
            try
            {
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(_serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var res = await client.GetCargoStatusAsync(new CargoLookupRequest { TrackingNumber = fullTrackNo });
                if (res.IsFound)
                {
                    _editTrackingNo = res.TrackingNumber;
                    _editIsActive = res.IsActive;
                    _editSender = res.SenderName;
                    _editReceiver = res.ReceiverName;
                    _editStatus = res.CurrentStatus;

                    ParseStructuredAddress(res.LastLocation, out _editCity, out _editDistrict, out _editStreetDetail);

                    if (DateTime.TryParseExact(res.EstimatedDelivery, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                        _editDeliveryDate = dt < DateTime.Today ? DateTime.Today : dt;
                    else
                        _editDeliveryDate = DateTime.Today;

                    _showDetailEditCard = true;
                    _queryFeedback = "";
                }
                else
                {
                    _showDetailEditCard = false;
                    _querySuccess = false;
                    _queryFeedback = $"{fullTrackNo} numaralı kargo sistemde bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                _querySuccess = false;
                _queryFeedback = $"Sorgulama hatası: {ex.Message}";
            }
        }

        private void ParseStructuredAddress(string rawLocation, out string city, out string district, out string detail)
        {
            city = "Bursa";
            district = "Nilüfer";
            detail = rawLocation;

            if (string.IsNullOrWhiteSpace(rawLocation)) return;

            int dashIdx = rawLocation.IndexOf('-');
            string leftPart = dashIdx > 0 ? rawLocation.Substring(0, dashIdx).Trim() : rawLocation;
            detail = dashIdx > 0 ? rawLocation.Substring(dashIdx + 1).Trim() : rawLocation;

            var slashParts = leftPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (slashParts.Length >= 2)
            {
                string candidateCity = slashParts[0].Trim();
                string candidateDist = slashParts[1].Trim();

                var matchedProv = _provinces.FirstOrDefault(p => p.Name.Equals(candidateCity, StringComparison.OrdinalIgnoreCase));
                if (matchedProv != null)
                {
                    city = matchedProv.Name;
                    var matchedDist = matchedProv.Districts.FirstOrDefault(d => d.Equals(candidateDist, StringComparison.OrdinalIgnoreCase));
                    district = matchedDist ?? matchedProv.Districts.FirstOrDefault() ?? "";
                }
            }
        }

        private async Task SaveEditedCargoAsync()
        {
            if (string.IsNullOrWhiteSpace(_editSender) || string.IsNullOrWhiteSpace(_editReceiver))
            {
                await ShowToast("Gönderici ve alıcı boş olamaz.", false);
                return;
            }

            string structuredAddress = $"{_editCity.Trim()} / {_editDistrict.Trim()} - {_editStreetDetail.Trim()}";

            try
            {
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(_serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var res = await client.UpdateCargoStatusAsync(new UpdateStatusRequest
                {
                    TrackingNumber = _editTrackingNo,
                    SenderName = _editSender.Trim(),
                    ReceiverName = _editReceiver.Trim(),
                    LastLocation = structuredAddress,
                    EstimatedDelivery = _editDeliveryDate.ToString("dd.MM.yyyy"),
                    NewStatus = _editStatus
                });

                if (res.Success)
                {
                    ResetQueryScreen();
                    await ShowToast($"{_editTrackingNo} kaydı ve durumu güncellendi.", true);
                    await LoadSummaryCountsAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowToast($"Güncelleme hatası: {ex.Message}", false);
            }
        }

        private async Task LoadCargoListAsync(string filterMode)
        {
            _currentFilter = filterMode;
            await LoadSummaryCountsAsync();
            _selectedCargo = FilteredCargos.FirstOrDefault();
        }

        private IEnumerable<CargoStatusResponse> FilteredCargos
        {
            get
            {
                IEnumerable<CargoStatusResponse> q = _cachedCargos;
                if (_currentFilter == "ACTIVE") q = q.Where(c => c.IsActive);
                else if (_currentFilter == "PASSIVE") q = q.Where(c => !c.IsActive);

                if (!string.IsNullOrWhiteSpace(_liveSearch))
                {
                    string s = _liveSearch.Trim().ToLower();
                    q = q.Where(c =>
                        c.TrackingNumber.ToLower().Contains(s) ||
                        c.SenderName.ToLower().Contains(s) ||
                        c.ReceiverName.ToLower().Contains(s) ||
                        c.LastLocation.ToLower().Contains(s));
                }
                return q;
            }
        }

        private void SelectCargoForPreview(CargoStatusResponse cargo)
        {
            _selectedCargo = cargo;
            _copiedJustNow = false;
        }

        private async Task CopySelectedTrackNoAsync()
        {
            if (_selectedCargo == null) return;
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", _selectedCargo.TrackingNumber);
            _copiedJustNow = true;
            await ShowToast($"{_selectedCargo.TrackingNumber} panoya kopyalandı.", true);
            await Task.Delay(1200);
            _copiedJustNow = false;
        }

        private async Task OpenSelectedCargoInEditAsync()
        {
            if (_selectedCargo == null) return;
            string digits = _selectedCargo.TrackingNumber.Replace("TR-", "");
            NavigateTo("QUERY", $"Operasyon Merkezi  /  Düzenle ({_selectedCargo.TrackingNumber})");
            _trackingDigits = digits;
            await FetchCargoForEditAsync(_selectedCargo.TrackingNumber);
        }

        private async Task StartLiveTrackingAsync()
        {
            if (_liveMapDigits.Length != 4)
            {
                _liveMapSuccess = false;
                _liveMapFeedback = "Lütfen 4 haneli kargo takip kodunu girin.";
                _isMapVisible = false;
                return;
            }

            StopLiveTracking(clearMessage: true);
            await Task.Delay(40);

            var localCts = new CancellationTokenSource();
            _streamCts = localCts;

            string targetTrackNo = $"TR-{_liveMapDigits}";
            _isStreamRunning = true;
            _liveMapFeedback = $"{targetTrackNo} için canlı GPS uydusuna bağlanılıyor...";
            _liveMapSuccess = true;
            StateHasChanged();

            try
            {
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(_serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                using var call = client.StreamCourierLocation(
                    new CargoLookupRequest { TrackingNumber = targetTrackNo },
                    cancellationToken: localCts.Token);

                bool isFirstCoordinate = true;

                while (await call.ResponseStream.MoveNext(localCts.Token))
                {
                    var update = call.ResponseStream.Current;

                    if (update.Latitude == 0 && update.Longitude == 0)
                    {
                        _liveMapSuccess = false;
                        _liveMapFeedback = update.CurrentStepText;
                        _isMapVisible = false;
                        _isStreamRunning = false;
                        StateHasChanged();
                        return;
                    }

                    _isMapVisible = true;
                    _courierName = update.CourierName;
                    _liveStepMessage = update.CurrentStepText;
                    _liveMapFeedback = $"Aktif Telemetri Bağlantısı: {targetTrackNo}";
                    _liveMapSuccess = true;
                    StateHasChanged();

                    if (isFirstCoordinate)
                    {
                        await Task.Delay(120);
                        await JSRuntime.InvokeVoidAsync(
                            "initCargoTrackingMap",
                            "cargoMapBox",
                            update.StartLat,
                            update.StartLng,
                            update.EndLat,
                            update.EndLng,
                            update.Latitude,
                            update.Longitude,
                            _isDarkMode);
                        isFirstCoordinate = false;
                    }
                    else
                    {
                        await JSRuntime.InvokeVoidAsync("updateCourierLocationOnMap", update.Latitude, update.Longitude);
                    }

                    if (update.IsDelivered)
                    {
                        _liveMapSuccess = true;
                        _liveMapFeedback = $"{targetTrackNo} teslimat noktasına ulaştı!";
                        await ShowToast($"{targetTrackNo} başarıyla teslim edildi!", true);
                        await LoadSummaryCountsAsync();
                        break;
                    }
                }
            }
            catch (Exception)
            {
                if (localCts.IsCancellationRequested) return;
            }
            finally
            {
                if (_streamCts == localCts)
                {
                    _isStreamRunning = false;
                    StateHasChanged();
                }
            }
        }

        private void StopLiveTracking(bool clearMessage = false)
        {
            if (_streamCts != null)
            {
                try { _streamCts.Cancel(); } catch { }
                _streamCts.Dispose();
                _streamCts = null;
            }
            _isStreamRunning = false;

            if (clearMessage)
            {
                _liveMapFeedback = "";
            }
            else
            {
                _liveMapSuccess = true;
                _liveMapFeedback = "Canlı izleme durduruldu. Kuryeler arka planda ilerlemeye devam ediyor.";
            }
        }

        private async Task ExportFilteredListToExcelAsync()
        {
            var list = FilteredCargos.ToList();
            if (!list.Any()) return;

            var sb = new StringBuilder();
            sb.AppendLine("Takip No;Gönderici;Alıcı;Güncel Durum;Teslimat Adresi;Teslim Tarihi");
            foreach (var item in list)
            {
                sb.AppendLine($"{item.TrackingNumber};{item.SenderName};{item.ReceiverName};{item.CurrentStatus};{item.LastLocation};{item.EstimatedDelivery}");
            }

            var utf8WithBom = new UTF8Encoding(true);
            var bytes = utf8WithBom.GetBytes(sb.ToString());
            var base64 = Convert.ToBase64String(bytes);

            string fileName = $"KargoPro_Rapor_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            await JSRuntime.InvokeVoidAsync("eval", $@"
                (() => {{
                    const link = document.createElement('a');
                    link.download = '{fileName}';
                    link.href = 'data:text/csv;charset=utf-8;base64,{base64}';
                    document.body.appendChild(link);
                    link.click();
                    document.body.removeChild(link);
                }})()
            ");
            await ShowToast("Excel (CSV) raporu indirildi.", true);
        }

        private async Task ExportToPdfAsync()
        {
            await JSRuntime.InvokeVoidAsync("window.print");
        }

        private string GetBadgeClass(string status) => status switch
        {
            "Teslim Edildi" => "badge-delivered",
            "İptal Edildi" => "badge-cancelled",
            "Yola Çıktı" => "badge-transit",
            _ => "badge-prep"
        };

        public void Dispose()
        {
            StopLiveTracking(clearMessage: true);
            _clockTimer?.Dispose();
        }
    }
}