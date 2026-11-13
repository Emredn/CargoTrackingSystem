using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace CargoTracking.Server.Services
{
    /// <summary>
    /// Nominatim (adres → koordinat) geçici olarak kullanılamadığında fırlatılır.
    /// "Adres bulunamadı" durumundan farklıdır: burada sorun adreste değil, servise ulaşmaktadır
    /// (internet yok, kota aşıldı, zaman aşımı vb.). Çağıran taraf bu durumda daha kaba bir sonuca
    /// DÜŞMEMELİ, bir süre sonra yeniden denemelidir.
    /// </summary>
    public class GeocodingUnavailableException : Exception
    {
        public GeocodingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Harita servisleriyle konuşan sınıf:
    ///  - Nominatim (OpenStreetMap): adres metnini koordinata çevirir (geocoding)
    ///  - OSRM: iki nokta arasındaki karayolu rotasını verir
    /// Nominatim kullanım kuralı gereği saniyede en fazla 1 istek atılır.
    /// </summary>
    public class GeoRoutingService
    {
        // Nominatim kullanım politikası: en fazla saniyede 1 istek
        private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1100);

        // Mahalle bilgisi taşıyan Nominatim adres alanları (city_district bilerek yok: Türkiye'de ilçeyi gösterebiliyor)
        private static readonly string[] NeighborhoodKeys = { "suburb", "neighbourhood", "quarter", "hamlet", "village" };

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly ILogger<GeoRoutingService> _logger;

        // Aynı sorgu tekrar gelirse internete gitmeden buradan cevaplanır (bulunamayanlar da saklanır)
        private readonly ConcurrentDictionary<string, (double Lat, double Lng)?> _geoCache = new();

        private readonly SemaphoreSlim _rateLimiter = new(1, 1);
        private DateTime _lastRequestUtc = DateTime.MinValue;

        public GeoRoutingService(ILogger<GeoRoutingService> logger)
        {
            _logger = logger;
        }

        private static HttpClient CreateHttpClient()
        {
            // Varsayılan zaman aşımı 100 sn; takılan bir istek bütün sistemi bekletmesin diye 10 sn yapıyoruz
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "KargoPro_Enterprise_Logistics/3.0");
            return client;
        }

        /// <summary>
        /// Adres metnini koordinata çevirir.
        /// Nominatim'in döndürdüğü sonuçlar, beklenen il / ilçe / mahalle ile KARŞILAŞTIRILIR;
        /// başka bir ilçedeki aynı isimli cadde gibi yanlış sonuçlar elenir.
        /// </summary>
        /// <returns>Koordinat; doğru yerde sonuç bulunamazsa null.</returns>
        /// <exception cref="GeocodingUnavailableException">Servise ulaşılamazsa.</exception>
        public async Task<(double Lat, double Lng)?> GeocodeAsync(
            string queryText, string expectedCity, string expectedDistrict = "", string expectedNeighborhood = "",
            CancellationToken ct = default)
        {
            string cacheKey = $"{queryText}|{expectedCity}|{expectedDistrict}|{expectedNeighborhood}";
            if (_geoCache.TryGetValue(cacheKey, out var cached))
                return cached;

            string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=5" +
                         "&countrycodes=tr&accept-language=tr&q=" + Uri.EscapeDataString(queryText);

            string json;
            try
            {
                await WaitForRequestSlotAsync(ct);
                using var response = await _httpClient.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                    throw new GeocodingUnavailableException($"Nominatim HTTP {(int)response.StatusCode} döndürdü.");

                json = await response.Content.ReadAsStringAsync(ct);
            }
            catch (GeocodingUnavailableException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Zaman aşımı da buraya düşer
                throw new GeocodingUnavailableException($"Nominatim'e ulaşılamadı: {ex.Message}", ex);
            }

            (double Lat, double Lng)? result = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!IsExpectedPlace(item, expectedCity, expectedDistrict, expectedNeighborhood))
                        continue;

                    double lat = double.Parse(item.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                    double lng = double.Parse(item.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    result = (lat, lng);

                    _logger.LogInformation("Geocode eşleşti: '{Query}' → {Display}", queryText,
                        item.TryGetProperty("display_name", out var dn) ? dn.GetString() : "?");
                    break;
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
            {
                throw new GeocodingUnavailableException("Nominatim yanıtı okunamadı.", ex);
            }

            if (result == null)
                _logger.LogInformation("Geocode sonuç yok / yanlış bölge: '{Query}'", queryText);

            _geoCache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// Sonuç gerçekten istenen yerde mi? İl ve ilçe adı sonucun açık adresinde geçmeli;
        /// sonuç bir mahalle bilgisi taşıyorsa o da seçilen mahalleyle uyuşmalı.
        /// </summary>
        private static bool IsExpectedPlace(JsonElement item, string city, string district, string neighborhood)
        {
            string display = item.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
            string normDisplay = AddressParser.Normalize(display);

            if (!normDisplay.Contains(AddressParser.Normalize(city)))
                return false;

            // "Merkez" ilçesi OSM'de çoğunlukla ayrı bir isimle geçmez, o yüzden kontrol dışı bırakılır
            string normDistrict = AddressParser.Normalize(district);
            if (normDistrict.Length > 0 && normDistrict != "merkez" && !normDisplay.Contains(normDistrict))
                return false;

            string normNeighborhood = AddressParser.Normalize(AddressParser.StripNeighborhoodSuffix(neighborhood));
            if (normNeighborhood.Length > 0 &&
                item.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
            {
                bool hasNeighborhoodInfo = false;
                bool matches = false;

                foreach (string key in NeighborhoodKeys)
                {
                    if (!address.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
                        continue;

                    string found = AddressParser.Normalize(AddressParser.StripNeighborhoodSuffix(value.GetString()));
                    if (found.Length == 0) continue;

                    hasNeighborhoodInfo = true;
                    if (found.Contains(normNeighborhood) || normNeighborhood.Contains(found))
                        matches = true;
                }

                // Sonuç başka bir mahalleye aitse (aynı isimli sokak başka mahallede) reddet.
                // Sonucun mahalle bilgisi hiç yoksa karar verilemez, kabul edilir.
                if (hasNeighborhoodInfo && !matches)
                    return false;
            }

            return true;
        }

        /// <summary>İstekler arasında en az MinRequestInterval kadar bekler (Nominatim kotası).</summary>
        private async Task WaitForRequestSlotAsync(CancellationToken ct)
        {
            await _rateLimiter.WaitAsync(ct);
            try
            {
                var wait = _lastRequestUtc + MinRequestInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        /// <summary>İki nokta arasındaki karayolu rotasını OSRM'den alır. Alamazsa düz çizgi üretir.</summary>
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSRM rotası alınamadı, düz çizgi kullanılacak.");
            }

            // OSRM yanıt vermezse yedek doğrusal rota üret
            if (routePoints.Count < 2)
            {
                routePoints.Clear();
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
