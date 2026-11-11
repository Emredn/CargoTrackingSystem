using CargoTracking.Grpc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CargoTracking.BlazorUI.Components.Pages
{
    public partial class Home
    {
        private string _liveMapDigits = "";
        private string _liveMapFeedback = "";
        private bool _liveMapSuccess;
        private bool _isStreamRunning;
        private bool _isMapVisible;
        private string _courierName = "Kurye";
        private string _liveStepMessage = "";
        private CancellationTokenSource? _streamCts;

        private async Task ToggleThemeAsync()
        {
            _isDarkMode = !_isDarkMode;
            if (_isMapVisible)
            {
                try { await JSRuntime.InvokeVoidAsync("setMapTheme", _isDarkMode); } catch { }
            }
        }

        private async Task JumpToLiveMapAsync(string fullTrackingNo)
        {
            string digits = fullTrackingNo.Replace("TR-", "");
            NavigateTo("LIVE_MAP", "Operasyon Merkezi  /  Canlı Kargo Takibi");
            _liveMapDigits = digits;
            await StartLiveTrackingAsync();
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
    }
}