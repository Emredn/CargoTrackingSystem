using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace CargoTracking.Server.Services
{
    public class GeoRoutingService
    {
        private static readonly HttpClient _httpClient = new();
        private readonly ConcurrentDictionary<string, (double Lat, double Lng)> _geoCache = new();

        static GeoRoutingService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KargoPro_Enterprise_Logistics/3.0");
        }

        public async Task<(double Lat, double Lng)?> GeocodeLockedAsync(string urlQuery)
        {
            if (_geoCache.TryGetValue(urlQuery, out var cached))
                return cached;

            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?format=json&{urlQuery}&countrycodes=tr&limit=1";
                var json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    double lat = double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                    double lng = double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    _geoCache[urlQuery] = (lat, lng);
                    return (lat, lng);
                }
            }
            catch { }

            return null;
        }

        public async Task<List<(double Lat, double Lng)>> GetRoadRouteAsync((double Lat, double Lng) start, (double Lat, double Lng) end)
        {
            var routePoints = new List<(double Lat, double Lng)>();
            try
            {
                string osrmUrl = string.Create(CultureInfo.InvariantCulture,
                    $"https://router.project-osrm.org/route/v1/driving/{start.Lng},{start.Lat};{end.Lng},{end.Lat}?overview=full&geometries=geojson");

                var osrmJson = await _httpClient.GetStringAsync(osrmUrl);
                using var doc = JsonDocument.Parse(osrmJson);
                var routes = doc.RootElement.GetProperty("routes");
                if (routes.GetArrayLength() > 0)
                {
                    var coords = routes[0].GetProperty("geometry").GetProperty("coordinates");
                    foreach (var pt in coords.EnumerateArray())
                    {
                        routePoints.Add((pt[1].GetDouble(), pt[0].GetDouble()));
                    }
                }
            }
            catch { }

            // OSRM yanıt vermezse yedek doğrusal rota üret
            if (routePoints.Count < 2)
            {
                for (int i = 0; i <= 20; i++)
                {
                    double t = i / 20.0;
                    routePoints.Add((start.Lat + (end.Lat - start.Lat) * t, start.Lng + (end.Lng - start.Lng) * t));
                }
            }

            return routePoints;
        }
    }
}