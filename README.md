# KARGO PRO | Kurumsal Lojistik ve Operasyon Yönetim Sistemi

**KARGO PRO**, şubeler arası gönderi takibini, gerçek zamanlı durum güncellemelerini ve kapsamlı kargo yönetimini tek çatı altında toplatan yeni nesil bir **Dağıtık Kurumsal Lojistik Otomasyonudur**. 

Bu proje; istemci ve sunucu haberleşmesinde **gRPC**, modern web arayüzünde **Blazor Web App**, kalıcı veri saklama katmanında ise **SQLite ve Entity Framework Core** teknolojilerini kullanan katmanlı bir mimariyle geliştirilmiştir.

---

## Proje Mimarisi & Teknolojiler

* **Backend / Sunucu (`CargoTracking.Server`):** gRPC protokolü üzerinden istemci isteklerini karşılayan, iş mantığını ve veritabanı operasyonlarını yöneten merkezi servis.
* **Frontend / Web Arayüz (`CargoTracking.BlazorUI`):** Tarayıcı üzerinde çalışan, sol yan menü, canlı saat, ekmek kırıntısı (breadcrumb), hızlı arama ve dinamik tablo özellikleriyle donatılmış modern Blazor web paneli.
* **Protokol Sözleşmesi (`cargo.proto`):** İstemci ve sunucu arasındaki veri iletişim kurallarını belirleyen Protocol Buffers şeması.
* **Veritabanı (`SQLite & EF Core`):** Eşzamanlı okuma/yazma kilitlerini yöneten, güvenli ve ilişkisel kurumsal veri deposu.
* **Hatıra Arayüzü (`CargoTracking.DesktopUI`):** Projenin ilk aşamasında geliştirilen Windows Forms masaüstü prototipi (arşiv amaçlı tutulmaktadır).

---

## Temel Özellikler

1. **gRPC Tabanlı İletişim:** HTTP/2 protokolü üzerinden ışık hızında ve güvenli veri aktarımı.
2. **Kapsamlı Kargo Yönetimi:** Yeni kargo kaydı oluşturma, 4 haneli `TR-` kodlarıyla akıllı sorgulama ve operasyon durumu güncelleme (Hazırlanıyor, Yola Çıktı, Teslim Edildi, İptal Edildi).
3. **Dinamik Liste ve Filtreleme:** Tüm kargoları, aktif operasyonları ve pasif (arşivlenmiş) kayıtları ayrı sekmelerde görüntüleme ve canlı arama.
4. **Hızlı Özet ve Panoya Kopyalama:** Listeden seçilen kargonun detaylarını anında sağ panelde görme ve tek tıkla takip numarasını panoya kopyalama.
5. **Güvenli Veri Saklama:** Otomatik tablo oluşturan Code-First SQLite veritabanı altyapısı.

---

## Kurulum ve Çalıştırma

Projeyi yerel ortamınızda çalıştırmak için şu adımları izleyebilirsiniz:

1. Depoyu klonlayın:
   ```bash
   git clone [https://github.com/emredn/CargoTrackingSystem.git](https://github.com/emredn/CargoTrackingSystem.git)
