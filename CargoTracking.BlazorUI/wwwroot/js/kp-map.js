// KARGO PRO | Harita ve yardımcı JavaScript fonksiyonları
// Blazor sayfaları bu fonksiyonları IJSRuntime ile çağırır.
//   initCargoTrackingMap / updateCourierLocationOnMap / setMapTheme  -> Canlı Kargo Takibi sayfasındaki Leaflet haritası
//   kpDownloadFile                                                  -> Kargo Listeleri sayfasındaki CSV indirme
// Not: Leaflet kütüphanesi (L) bu dosyadan önce yüklenir (bkz. App.razor).

let cargoMap = null;
let courierMarker = null;
let routePolyline = null;
let currentTileLayer = null;

window.initCargoTrackingMap = async (elementId, startLat, startLng, destLat, destLng, currentLat, currentLng, isDarkMode) => {
    const container = document.getElementById(elementId);
    if (!container) return;

    if (cargoMap) {
        cargoMap.remove();
        cargoMap = null;
    }

    cargoMap = L.map(elementId).setView([currentLat, currentLng], 13);

    // Her zaman ücretsiz olan resmi OpenStreetMap katmanı
    currentTileLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors',
        maxZoom: 19,
        className: isDarkMode ? 'kp-dark-tiles' : ''
    }).addTo(cargoMap);

    L.marker([startLat, startLng]).addTo(cargoMap).bindPopup("<b>Çıkış Transfer Merkezi</b>");
    L.marker([destLat, destLng]).addTo(cargoMap).bindPopup("<b>Teslimat Adresi</b>").openPopup();
    courierMarker = L.marker([currentLat, currentLng]).addTo(cargoMap).bindPopup("<b>Kurye Sahada</b>");

    try {
        const url = `https://router.project-osrm.org/route/v1/driving/${startLng},${startLat};${destLng},${destLat}?overview=full&geometries=geojson`;
        const response = await fetch(url);
        const data = await response.json();

        if (data.routes && data.routes.length > 0) {
            const coords = data.routes[0].geometry.coordinates.map(c => [c[1], c[0]]);
            let lineColor = isDarkMode ? '#38bdf8' : '#2563eb';
            routePolyline = L.polyline(coords, { color: lineColor, weight: 5 }).addTo(cargoMap);
            cargoMap.fitBounds(routePolyline.getBounds(), { padding: [40, 40] });
            return;
        }
    } catch (e) {
        console.warn("OSRM rota hatası:", e);
    }

    routePolyline = L.polyline([[startLat, startLng], [destLat, destLng]], { color: '#2563eb', weight: 4 }).addTo(cargoMap);
    cargoMap.fitBounds(routePolyline.getBounds(), { padding: [40, 40] });
};

window.setMapTheme = (isDarkMode) => {
    if (!cargoMap || !currentTileLayer) return;

    const tileContainer = currentTileLayer.getContainer();
    if (tileContainer) {
        if (isDarkMode) {
            tileContainer.classList.add('kp-dark-tiles');
        } else {
            tileContainer.classList.remove('kp-dark-tiles');
        }
    }

    if (routePolyline) {
        routePolyline.setStyle({ color: isDarkMode ? '#38bdf8' : '#2563eb' });
    }
};

window.updateCourierLocationOnMap = (lat, lng) => {
    if (courierMarker && cargoMap) {
        courierMarker.setLatLng([lat, lng]);
    }
};


// Verilen base64 içeriği dosya olarak indirtir (CSV raporu için)
window.kpDownloadFile = (fileName, base64Content, mimeType) => {
    const link = document.createElement('a');
    link.download = fileName;
    link.href = `data:${mimeType};base64,${base64Content}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
