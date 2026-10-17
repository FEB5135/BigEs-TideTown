# Tsunami Simülasyonu

Ekibin oyun haritasında (`Assets/Scenes/SampleScene.unity`) çalışan, fizik tabanlı, etkileşimli tsunami simülasyonu.
Unity 6000.6.6f1, URP 17.6, yalnız yeni Input System. Ek paket gerekmez.

## Kurulum

1. `Assets/Scenes/SampleScene.unity`'yi aç.
2. **Tsunami > Add Tsunami To Open Scene (Team Map)** menüsünü çalıştır ve **Continue**'ya tıkla. Sahneye tek bir `Tsunami` nesnesi eklenir ve sahne kaydedilir; sahnedeki başka hiçbir şey değişmez. Yeniden çalıştırmak nesneyi baştan kurar. Kaldırmak için `Tsunami` nesnesini sil.
3. Play. Ana menüden oyuna geç. Sağ üstteki panelde:
   - **Dalga yüksekliği** kaydırıcısı: 1–25 m. Açık denizdeki ilk kabarmanın yüksekliği; bir sonraki başlatmada ya da yeniden başlatmada uygulanır.
   - **Tsunamiyi Başlat** (ilerlerken **Yeniden Başlat**), **Duraklat/Devam**, **Sıfırla**.
   - Varsayılan yükseklik: `Tsunami > Tsunami Controller > Scenarios > Element 0 > Amplitude` (10 m).

Ayrı test adası: **Tsunami > Build Demo Scene** (`Assets/Tsunami/Scenes/TsunamiDemo.unity`). Asıl kullanım ekip haritasıdır.

## Mimari

| Bileşen | Görev |
|---|---|
| `TsunamiPhysicsSolver` | Saf C# sığ su çözücüsü (Unity bağımlılığı yok, Unity dışında test edilir). Çok çekirdekli. |
| `WaterSimulation` | Çözücüyü sürer: CFL'li adımlar, oynatma hızı, duraklatma, kare başına zaman bütçesi, kaynak ekleme, duvarlar. |
| `BathymetryProfile` | Deniz tabanı profili: kıyı eğimi → kıta sahanlığı → kıta yamacı → derin deniz (+ isteğe bağlı sualtı sırtı). |
| `TeamMapTsunamiAdapter` | Simülasyon ızgarasını ekibin `World`'üne hizalar (2 m hücre). Vadinin açıldığı kuzeydoğuya okyanus ekler, dağları araziye çizer, oyuncunun duvarlarını ince engellere çevirir, deniz tabanını gösterir. |
| `TsunamiController` | Simülasyon yöneticisi: oynat, duraklat, devam, sıfırla, yeniden başlat, hız, dalga yüksekliği, senaryolar (Inspector), kaynak yerleşimi. |
| `TsunamiScenario` | Senaryo tanımı (kaynak + deniz tabanı). |
| `WaterMeshRenderer`, `OceanRing` | Su yüzeyini simülasyon yüksekliğinden çizer; ızgara kenarı çevredeki okyanusla dikişsiz birleşir. |
| `ImpactSplashEmitter`, `ImpactDetection` | Çarpma ve sıçrama: su kendinden yüksek bir şeye (dağ yamacı, dik kıyı, duvar, ev) çarptığında damlacık ve sis fırlar. Fırlama hızı çarpma hızıyla büyür. Tespit çözücünün hız, derinlik ve engel yüksekliği verisiyle yapılır. |
| `TsunamiVisualizationManager` | Inspector'dan: renk haritaları, köpük, sprey, su yüzeyi ve batimetri görünümü; duraklatınca süs animasyonları da durur. |
| `TsunamiUI` | Oyun içi panel: dalga yüksekliği, başlat/duraklat/sıfırla, durum, geri sayım. |
| `VillageFloodTracker` | 40 köy evinin su basma / yıkılma durumu. |

## Fizik modeli

Derinlik ortalamalı, doğrusal olmayan sığ su (Saint-Venant) denklemleri:

- ∂h/∂t + ∇·(h**u**) = 0
- ∂(h**u**)/∂t + ∇·(h**u**⊗**u** + ½ g h² **I**) = −g h ∇z_b − sürtünme

**Ayrıklaştırma:**

- Hücre merkezli sonlu hacim.
- 2. dereceden MUSCL yeniden yapılandırma (minmod sınırlayıcı). Kuru hücrelerin ve duvarların yanında 1. dereceye düşer.
- HLL yaklaşık Riemann çözücüsü.
- Audusse vd. hidrostatik yeniden yapılandırma. Göl durgunken durgun kalır, kıyıda negatif derinlik oluşmaz, ıslanma/kuruma kararlı.
- Heun (SSP-RK2) zaman adımı. Adım CFL koşulundan seçilir (Courant 0.3).
- Yarı-örtük Manning sürtünmesi (deniz n = 0.025, kara n = 0.06).

**Duvarlar:** Oyuncunun duvarları hücre kenarlarında ince engellerdir. Hidrostatik yeniden yapılandırmaya yükseltilmiş bir eşik olarak girer: su, duvarın tepesini aşınca savaktan taşar gibi geçer.

**Sınırlar:**

- Açık denizde bir sönümleme bandı dalgaları dışarı atar.
- Izgaranın kare kenarı çizimde durgun okyanusa karışır, görünmez.

**Kaynak:** Açık denizde Gauss ya da N-dalgası yüzey bozulması.

- **Işınsal:** su durgun başlar, bozulma her yöne yayılır.
- **Yönlü:** u = η·√(g/h) uzun dalga bağıntısıyla akış verilir; dalga tek bir ana yönde ilerler.
- **N-dalgası:** önde çukur vardır, kıyıda deniz önce çekilir.

**Varsayımlar ve sınırlar:**

- Bu bir **tahmin modeli değildir**, eğitim amaçlı bir yaklaşımdır.
- Dispersiyon, dalga kırılması fiziği ve dikey akış yoktur.
- Sürtünme basitleştirilmiştir.
- 2 m çözünürlük, alan yaklaşık 416 × 416 m.
- Açık sınır yaklaşıktır.
- Evler ve ağaçlar suya engel değildir; evlerin yalnız su basma ya da yıkılma durumu izlenir.

**Fizik ile görsel ayrımı:**

- Su yüzeyinin büyük ölçekli şekli doğrudan çözücüden gelir.
- Dalgacıklar, köpük, sprey, çarpma sıçramaları, ışık ve yüzey yumuşatması yalnız görseldir, fiziği değiştirmez.
- Köpük nereden çıkar: kırılan akış (Froude > 1.3, tamamı 2.8'de), kıyı kenarı ve dik dalga yüzü.
- Köpük dokusu, simülasyonun hız alanıyla akar (iki fazlı flow-map). Bu yüzden yerinde durup beneklenmez.

## Ayarlar

- **Oyun içinde:** yalnız dalga yüksekliği (sağ üst panel).
- **Inspector'da, `Tsunami` nesnesi altında:**
  - `Tsunami Controller`: senaryolar (kaynak genliği, boyutu, kıyıdan uzaklığı, yönü, Gauss / N-dalgası, ışınsal / yönlü, deniz tabanı profili), seçili senaryo, uyarı geri sayımı.
  - `Water Simulation`: oynatma hızı, Manning pürüzlülükleri, Courant sayısı, kare bütçesi.
  - `Visualization`: renk haritası modu ve aralıkları, köpük, sprey, batimetri görünümü.
- **Dalga hızı ayarı bilerek yok:** hız denklemlerden gelir (c ≈ √(g·h)).

Senaryolar (örnektir, tahmin değildir):

1. **Vadi Tsunamisi (Oyun):** varsayılan, 10 m kabarma, vadiden köye.
2. **Küçük Açık Deniz Bozulması.**
3. **Büyük Açık Deniz Tsunamisi.**
4. **Sığ Kıyıya Yaklaşma.**
5. **Kıyı Taşkını.**
6. **Özel.**

Seçili senaryo `Tsunami Controller > Scenario Index` ile değişir.

## Doğrulama (Unity dışında, saf C# çözücüyle)

| Test | Sonuç |
|---|---|
| Durgun göl, 60 sn (engebeli taban, kuru ada, duvarlar) | En büyük hız 1.5·10⁻⁵ m/s, yüzey sapması 4·10⁻⁶ m |
| Dalga hızı (5 / 10 / 40 m derinlik) | √(g·h)'den sapma +%3.5 / +%1.0 / +%0.9 |
| Kapalı havzada kütle (kıyıya tırmanma dahil) | Sapma 10⁻⁷, hiç negatif derinlik yok |
| Kuru yatağa baraj yıkılması | Cephe 10.9 m/s, Ritter çözümü 12.5 m/s (ince uç sayısal yayılmayla yavaşlıyor) |
| Zaman adımı yarıya | Tepe 1.005 → 1.006 m (aynı) |
| Izgara inceltme (2 → 1 m) | Tepe 1.005 → 1.129 m, varış zamanı aynı (2 m'nin sayısal yayılması) |
| Ekip haritası düzeninde, açık deniz kaynağı | 4 m: 9 ev sular altında. 8 m: 38 sular altında, 2 yıkık. 12 m: vadide 9 m su, 40 ev yıkık. Kararlı, negatif derinlik yok. |
| Çarpma tespiti ve köpük (oyun senaryosu, 60 sn) | Çarpmalar yalnız dalga gelirken (10–30 sn): dağ yamacında en çok 22 hücre, duvar halkasında 4; düz taşkında hiç yok. Islak karada köpük: dalga cephesinde %29–63 (eski eşikle %85–100), su yatışınca %0. |
| Hız (.NET 10, çok çekirdek) | 208×208: adım başına yaklaşık 1.3–1.9 ms, CFL adımı ~0.03 sn |

**Unity'de henüz test edilmedi:** shader derlemesi, sahne kurulumu, panel yerleşimi, FPS. Unity'nin Mono'su .NET'ten yavaştır; `Water Simulation` bileşeninde sağ tık > Log Water Volume çözücü süresini yazdırır.

## Performans ayarları

- **`WaterSimulation`:** `timeScale`, `maxStepsPerFrame` (8), `frameBudgetMs` (10 ms; aşılırsa simülasyon yavaşlar ama kararsızlaşmaz), `secondOrder`, `multithreaded`.
- **`TeamMapTsunamiAdapter.oceanCells`:** (72) okyanus genişliği; ızgara boyutunu ve maliyeti belirler.
- **Grafik:** Tsunami > Graphics > Use Fast Settings (SSAO, HDR, gölge kapalı kopya ayarlar; orijinaller değişmez).
