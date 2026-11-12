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

    public class LocationService
    {
        private static readonly HttpClient _http = new();
        private List<ProvinceModel> _provinces = new();
        private readonly ConcurrentDictionary<int, List<string>> _neighborhoodCache = new();

        public IReadOnlyList<ProvinceModel> Provinces => _provinces;

        public async Task EnsureLoadedAsync()
        {
            if (_provinces.Any()) return;

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
            }
            catch
            {
                _provinces = new List<ProvinceModel>
                {
                    new() { Name = "Bursa", Districts = new() { new() { Id = 1, Name = "Nilüfer" }, new() { Id = 2, Name = "Osmangazi" }, new() { Id = 3, Name = "Yıldırım" } } },
                    new() { Name = "İstanbul", Districts = new() { new() { Id = 4, Name = "Kadıköy" }, new() { Id = 5, Name = "Üsküdar" }, new() { Id = 6, Name = "Beşiktaş" } } }
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

            if (dist == null || dist.Id <= 10)
            {
                return new List<string> { "Merkez Mah.", "Cumhuriyet Mah.", "Atatürk Mah.", "Fatih Mah." };
            }

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

        // Format: "İl / İlçe / Mahalle - Cadde Sokak No"
        public void ParseStructuredAddress(string rawLocation, out string city, out string district, out string neighborhood, out string detail)
        {
            city = "Bursa";
            district = "Nilüfer";
            neighborhood = "";
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
                neighborhood = slashParts.Length >= 3 ? slashParts[2].Trim() : "";

                var matchedProv = _provinces.FirstOrDefault(p => p.Name.Equals(candidateCity, StringComparison.OrdinalIgnoreCase));
                if (matchedProv != null)
                {
                    city = matchedProv.Name;
                    var matchedDist = matchedProv.Districts.FirstOrDefault(d => d.Name.Equals(candidateDist, StringComparison.OrdinalIgnoreCase));
                    district = matchedDist?.Name ?? matchedProv.Districts.FirstOrDefault()?.Name ?? "";
                }
            }
        }
    }
}