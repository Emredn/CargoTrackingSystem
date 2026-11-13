using System.Text;
using System.Text.RegularExpressions;

namespace CargoTracking.Server.Services
{
    /// <summary>Adres metninden çıkarılan parçalar.</summary>
    public record ParsedAddress(string City, string District, string Neighborhood, string Street);

    /// <summary>
    /// Veritabanında tek metin olarak saklanan adresi ("İl / İlçe / Mahalle - Cadde/Sokak") parçalar
    /// ve harita aramasına gitmeden önce metni temizler.
    /// Kapı/daire numarası gerekmez: yazılmışsa aramadan önce atılır (OpenStreetMap'te numara verisi zaten eksik).
    /// </summary>
    public static class AddressParser
    {
        // Ayırıcı olarak sadece " - " (boşluklu tire) kullanılır; böylece "Yeni-Mahalle" gibi isimler bölünmez.
        private const string DetailSeparator = " - ";

        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // "No:56", "no 12", "Numara: 4/A" gibi kapı numaralarını yakalar
        private static readonly Regex DoorNumber = new(@"\b(?:no|numara)\s*[:.]?\s*\d+[\w/\-]*", Opts);

        // "Daire: 4", "Kat 3", "Blok B", "D:4", "K:2" gibi iç kapı bilgilerini yakalar
        private static readonly Regex FlatInfo = new(@"\b(?:daire|kat|blok)\b\s*[:.]?\s*(?:\d+|[a-z])\b|\b[dk]\s*:\s*\d+", Opts);

        private static readonly Regex MultiSpace = new(@"\s{2,}", Opts);

        // Mahalle adının sonundaki "Mahallesi" / "Mah." ekini yakalar
        private static readonly Regex NeighborhoodSuffix = new(@"\b(?:mahallesi|mah)\b\.?", Opts);

        // Kısaltmaları açar: Nominatim "Cad." yerine "Caddesi" yazınca çok daha iyi sonuç verir
        private static readonly (Regex Pattern, string Replacement)[] Abbreviations =
        {
            (new Regex(@"\b(?:cad|cd)\.?(?=\s|$)", Opts), "Caddesi"),
            (new Regex(@"\b(?:sok|sk)\.?(?=\s|$)", Opts), "Sokak"),
            (new Regex(@"\b(?:bul|blv)\.?(?=\s|$)", Opts), "Bulvarı"),
        };

        /// <summary>"İl / İlçe / Mahalle - Sokak" metnini parçalara ayırır. Eksik parçalar boş döner.</summary>
        public static ParsedAddress Parse(string? rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
                return new ParsedAddress("", "", "", "");

            string left = rawAddress.Trim();
            string detail = "";

            int sepIdx = left.IndexOf(DetailSeparator, StringComparison.Ordinal);
            if (sepIdx >= 0)
            {
                detail = left.Substring(sepIdx + DetailSeparator.Length).Trim();
                left = left.Substring(0, sepIdx).Trim();
            }

            var parts = left.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string city = parts.Length > 0 ? parts[0] : "";
            string district = parts.Length > 1 ? parts[1] : "";
            string neighborhood = parts.Length > 2 ? parts[2] : "";

            return new ParsedAddress(city, district, neighborhood, CleanStreet(detail));
        }

        /// <summary>Sokak metninden kapı/daire bilgisini atar, kısaltmaları açar.</summary>
        public static string CleanStreet(string? street)
        {
            if (string.IsNullOrWhiteSpace(street)) return "";

            string s = DoorNumber.Replace(street, " ");
            s = FlatInfo.Replace(s, " ");

            foreach (var (pattern, replacement) in Abbreviations)
                s = pattern.Replace(s, replacement);

            s = MultiSpace.Replace(s, " ");
            return s.Trim(' ', ',', '-', '/', '.');
        }

        /// <summary>"Hasanağa Mahallesi" / "Hasanağa Mah." → "Hasanağa"</summary>
        public static string StripNeighborhoodSuffix(string? neighborhood)
        {
            if (string.IsNullOrWhiteSpace(neighborhood)) return "";
            string s = NeighborhoodSuffix.Replace(neighborhood, " ");
            return MultiSpace.Replace(s, " ").Trim();
        }

        /// <summary>
        /// Karşılaştırma için metni sadeleştirir: küçük harf + Türkçe karakterleri ASCII'ye çevirir
        /// (ı→i, ğ→g, ü→u, ş→s, ö→o, ç→c). Böylece "Nilüfer" ile "NILUFER" aynı sayılır.
        /// </summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // Kültüre bağımlı olmamak için büyük İ / I / ı harflerini elle çeviriyoruz
            string s = text.Trim().Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                // Harfin üzerindeki aksan/çengel işaretlerini at (ğ → g, ş → s ...)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
