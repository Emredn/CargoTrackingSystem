using CargoTracking.Grpc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using System.Text;

namespace CargoTracking.BlazorUI.Components.Pages
{
    public partial class Home
    {
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
    }
}