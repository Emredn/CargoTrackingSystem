using System.Collections.Concurrent;
using System.Text.Json;

namespace CargoTracking.BlazorUI.Services
{
    public class DistrictModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class ProvinceModel
    {
        public string Name { get; set; } = "";
        public List<DistrictModel> Districts { get; set; } = new();
    }

    /// <summary>
    /// İl / ilçe / mahalle listelerini turkiyeapi.dev'den çeker ve önbellekte tutar (uygulama boyunca tek kopya).
    /// İnternet yoksa az sayıda örnek il/ilçe ile çalışır ve bir sonraki fırsatta gerçek listeyi tekrar dener.
    /// Ayrıca veritabanındaki "İl / İlçe / Mahalle - Sokak" adres metnini parçalara ayırır.
    /// </summary>
    public class LocationService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        // Gerçek liste alınamadıysa yedek liste kullanılır; yedek listedeki ilçelerin Id'si 0'dır
        private const int FallbackDistrictId = 0;
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        private bool _usingFallback;
        private DateTime _lastLoadAttemptUtc = DateTime.MinValue;
        private List<ProvinceModel> _provinces = new();
        private readonly ConcurrentDictionary<int, List<string>> _neighborhoodCache = new();

        public IReadOnlyList<ProvinceModel> Provinces => _provinces;

        public async Task EnsureLoadedAsync()
        {
            // Gerçek liste yüklüyse bitti. Yedek listedeysek en fazla 30 saniyede bir yeniden deneriz.
            if (_provinces.Any() && !_usingFallback) return;
            if (_usingFallback && DateTime.UtcNow - _lastLoadAttemptUtc < RetryInterval) return;

            _lastLoadAttemptUtc = DateTime.UtcNow;

            try
            {
                string json = await _http.GetStringAsync("https://turkiyeapi.dev/api/v1/provinces");
                using var doc = JsonDocument.Parse(json);
                var dataArray = doc.RootElement.GetProperty("data");

                var tempList = new List<ProvinceModel>();
                foreach (var item in dataArray.EnumerateArray())
                {
                    string provName = item.GetProperty("name").GetString() ?? "";
                    var distList = new List<DistrictModel>();

                    if (item.TryGetProperty("districts", out var districtsElement))
                    {
                        foreach (var d in districtsElement.EnumerateArray())
                        {
                            int dId = d.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                            string dName = d.GetProperty("name").GetString() ?? "";
                            if (!string.IsNullOrEmpty(dName))
                            {
                                distList.Add(new DistrictModel { Id = dId, Name = dName });
                            }
                        }
                    }

                    tempList.Add(new ProvinceModel
                    {
                        Name = provName,
                        Districts = distList.OrderBy(x => x.Name).ToList()
                    });
                }

                _provinces = tempList.OrderBy(p => p.Name).ToList();
                _usingFallback = false;
            }
            catch
            {
                _usingFallback = true;
                _provinces = new List<ProvinceModel>
                {
                    new() { Name = "Bursa", Districts = new() { new() { Id = FallbackDistrictId, Name = "Nilüfer" }, new() { Id = FallbackDistrictId, Name = "Osmangazi" }, new() { Id = FallbackDistrictId, Name = "Yıldırım" } } },
                    new() { Name = "İstanbul", Districts = new() { new() { Id = FallbackDistrictId, Name = "Kadıköy" }, new() { Id = FallbackDistrictId, Name = "Üsküdar" }, new() { Id = FallbackDistrictId, Name = "Beşiktaş" } } }
                };
            }
        }

        public IEnumerable<string> GetDistricts(string cityName)
        {
            var prov = _provinces.FirstOrDefault(p => p.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
            return prov != null ? prov.Districts.Select(d => d.Name) : Enumerable.Empty<string>();
        }

        public async Task<List<string>> GetNeighborhoodsAsync(string cityName, string districtName)
        {
            if (string.IsNullOrWhiteSpace(cityName) || string.IsNullOrWhiteSpace(districtName))
                return new List<string>();

            var prov = _provinces.FirstOrDefault(p => p.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
            var dist = prov?.Districts.FirstOrDefault(d => d.Name.Equals(districtName, StringComparison.OrdinalIgnoreCase));

            // İlçe listede yoksa mahalle de yoktur
            if (dist == null)
                return new List<string>();

            // Sadece internet yokken kullanılan yedek ilçeler için örnek mahalle listesi
            if (dist.Id == FallbackDistrictId)
                return new List<string> { "Merkez Mah.", "Cumhuriyet Mah.", "Atatürk Mah.", "Fatih Mah." };

            if (_neighborhoodCache.TryGetValue(dist.Id, out var cached))
                return cached;

            try
            {
                string json = await _http.GetStringAsync($"https://turkiyeapi.dev/api/v1/districts/{dist.Id}");
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");

                var list = new List<string>();

                if (data.TryGetProperty("neighborhoods", out var nArr))
                {
                    foreach (var n in nArr.EnumerateArray())
                    {
                        string? name = n.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                    }
                }

                if (data.TryGetProperty("villages", out var vArr))
                {
                    foreach (var v in vArr.EnumerateArray())
                    {
                        string? name = v.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                    }
                }

                var ordered = list.Distinct().OrderBy(x => x).ToList();
                _neighborhoodCache[dist.Id] = ordered;
                return ordered;
            }
            catch
            {
                return new List<string> { "Merkez Mah." };
            }
        }

        // Format: "İl / İlçe / Mahalle - Cadde/Sokak"
        // Ayırıcı olarak sadece " - " (boşluklu tire) kullanılır; "Yeni-Mahalle" gibi isimler bölünmez.
        // Listede bulunamayan parçalar boş bırakılır (eskiden sessizce başka bir ilçe seçiliyordu);
        // kullanıcı düzenleme ekranında doğru değeri kendisi seçer.
        public void ParseStructuredAddress(string rawLocation, out string city, out string district, out string neighborhood, out string detail)
        {
            city = "";
            district = "";
            neighborhood = "";
            detail = rawLocation ?? "";

            if (string.IsNullOrWhiteSpace(rawLocation)) return;

            string leftPart = rawLocation.Trim();
            int sepIdx = leftPart.IndexOf(" - ", StringComparison.Ordinal);
            if (sepIdx >= 0)
            {
                detail = leftPart.Substring(sepIdx + 3).Trim();
                leftPart = leftPart.Substring(0, sepIdx).Trim();
            }

            var slashParts = leftPart.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (slashParts.Length == 0) return;

            var matchedProv = _provinces.FirstOrDefault(p => p.Name.Equals(slashParts[0], StringComparison.OrdinalIgnoreCase));
            if (matchedProv == null) return;

            city = matchedProv.Name;

            if (slashParts.Length >= 2)
            {
                var matchedDist = matchedProv.Districts.FirstOrDefault(d => d.Name.Equals(slashParts[1], StringComparison.OrdinalIgnoreCase));
                district = matchedDist?.Name ?? "";
            }

            if (slashParts.Length >= 3)
            {
                neighborhood = slashParts[2];
            }
        }
    }
}
