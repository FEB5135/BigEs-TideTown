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
| `TeamMapTsunamiAdapter` | Simülasyon ızgarasını ekibin `World`'üne hizalar (2 m hücre). Oyunda WFC haritası kurulunca eğim/tepe/dağ karolarını araziye çizer (ağaçları atlar), köye en açık koridoru bulup dalgayı oradan getirir ve okyanusu o tarafa ekler, oyuncunun duvarlarını ince engellere çevirir, deniz tabanını gösterir. |
| `TsunamiController` | Simülasyon yöneticisi: oynat, duraklat, devam, sıfırla, yeniden başlat, hız, dalga yüksekliği, senaryolar (Inspector), kaynak yerleşimi. |
| `TsunamiScenario` | Senaryo tanımı (kaynak + deniz tabanı). |
| `WaterMeshRenderer`, `OceanRing` | Su yüzeyini simülasyon yüksekliğinden çizer; ızgara kenarı çevredeki okyanusla dikişsiz birleşir. |
| `ImpactSplashEmitter`, `ImpactDetection` | Çarpma ve sıçrama: su kendinden yüksek bir şeye (dağ yamacı, dik kıyı, duvar, ev) çarptığında damlacık, köpük ve hafif sis fırlar. Duvar teması kayıtlı gerçek segment üzerine izdüşürülür; yalnız ıslak ve duvar normaline akan su efekt üretir. |
| `WallDamageModel` | Saf C# oyun dayanıklılığı: dinamik darbe ve sürekli hidrostatik yükü çözücü zaman adımıyla ayrı ayrı hasara çevirir. |
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

**Duvarlar:** Oyuncunun duvarları hücre kenarlarında ince engellerdir. Yatay/dikey segment bir yüzdür; çapraz segment mevcut ızgarada iki yüzlü L biçimli bir merdiven yaklaşımıdır. Tepe yüksekliği gerçek segment Renderer sınırından (yoksa `fallbackWallHeight=1.25 m`) dünya koordinatında okunur ve hidrostatik yeniden yapılandırmaya yükseltilmiş eşik olarak girer. Su tepeyi aşınca savaktan taşar gibi geçer. Solver her yüzün kalıcı/statik `SetBarrier` crest'ini ve oyuncu-segmenti sahiplerini ayrı tutar; bir segment kaldırılınca aynı yüzün diğer sahibi ve düşük/yüksek statik crest'i korunur, yalnız son owner ve statik katkı yoksa yüz açılır.

**Duvar yükü ve dayanıklılık (oyun modeli):** Su yoğunluğu 1000 kg/m³ kabul edilir. Her kayıtlı yüzün iki tarafında normal hızdan dinamik yük `½ρ h u_n² L` hesaplanır ve iki yöndeki girişler segment toplamına eklenir. Sürekli yük, iki tarafın `½ρ g h² L` hidrostatik kuvvetlerinin **mutlak net farkıdır**; eşit su seviyeleri birbirini götürür. Çapraz merdivenin iki yüzündeki net kuvvetler segment için toplanır. Hasar olayı segment başına her tamamlanmış solver adımında tam bir kez, gerçek `dt` ile kN·s üzerinden ilerler; render FPS'i ve pause sırasında geçen süre hasara girmez. Bu değerler yapısal mühendislik tahmini değil ayarlanabilir oyun ölçekleridir. Varsayılan: 100 HP, darbe eşiği 6 kN ve oranı 0.32 HP/(kN·s), sürekli yük eşiği 12 kN ve oranı 0.055 HP/(kN·s).

Kırılan tek kenar inşa modelinden ve sahipli sayısal bariyerden çıkar; görseli en çok 20 adet, 8 saniyelik hafif fizik enkazına dönüşür. Enkaz, custom prefab'lardaki non-convex MeshCollider riskini ve pahalı convex cooking'i önlemek için özgün collider'ları kapatıp Renderer bounds'larından konservatif tek root BoxCollider kullanır; sağlam duvarlar değişmez. Pause öncesi doğrusal/açısal hız cache'lenip resume'da geri yüklenir. Komşu/örtüşen owner ve statik bariyer katkıları korunur. Aynı kenar normal maliyetle yeniden yapılabilir; otomatik iade yoktur.

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
- Duvar çarpma damlası/köpüğü/sisi `ImpactSplashEmitter` içindeki üç sınırlı ParticleSystem'i kullanır (900/320/160); genel hücre taraması kayıtlı duvar yüzlerini atladığı için çift efekt yoktur.

## Ayarlar

- **Oyun içinde:** yalnız dalga yüksekliği (sağ üst panel).
- **Inspector'da, `Tsunami` nesnesi altında:**
  - `Tsunami Controller`: senaryolar (kaynak genliği, boyutu, kıyıdan uzaklığı, yönü, Gauss / N-dalgası, ışınsal / yönlü, deniz tabanı profili), seçili senaryo, uyarı geri sayımı.
  - `Water Simulation`: oynatma hızı, Manning pürüzlülükleri, Courant sayısı, kare bütçesi.
  - `Team Map Adapter`: WFC karolarını araziye çizme ve atlanacak karo numaraları (ağaç = 5), otomatik dalga yönü, koridor genişliği ve denenecek yön sayısı; duvar yüksekliği fallback'i; dayanıklılık, darbe/sürekli yük eşikleri ve hasar oranları; enkaz sınırı/ömrü/itkisi.
  - `Impact Splash`: darbe ve derinlik eşikleri, kare başı çarpma sınırı, damla/köpük/sis kapasiteleri ve görünüm.
  - `Visualization`: renk haritası modu ve aralıkları, köpük, sprey, batimetri görünümü.
- **Dalga hızı ayarı bilerek yok:** hız denklemlerden gelir (c ≈ √(g·h)).

Senaryolar (örnektir, tahmin değildir):

1. **Vadi Tsunamisi (Oyun):** varsayılan, 10 m kabarma, rastgele haritada köye en açık koridordan.
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
| Çarpışma saf C# testleri (`dotnet run --project Tests/TsunamiCollisionPure/TsunamiCollisionPure.csproj`) | 10/10: yüksüz hasar yok, FPS bağımsızlığı, pause/`dt=0`, tek kırılma, statik + örtüşen owner kaldırma sıraları, son owner'da açılma, owner sonrası statik yazım, iki taraflı net hidro/dinamik toplam, ortak yüzde her owner'a metadata + yalnız görsel dedup, duvar çarpma sınıflaması. |

**Unity'de henüz test edilmedi:** shader derlemesi, sahne kurulumu, panel yerleşimi, FPS. Unity'nin Mono'su .NET'ten yavaştır; `Water Simulation` bileşeninde sağ tık > Log Water Volume çözücü süresini yazdırır.

## Performans ayarları

- **`WaterSimulation`:** `timeScale`, `maxStepsPerFrame` (8), `frameBudgetMs` (10 ms; aşılırsa simülasyon yavaşlar ama kararsızlaşmaz), `secondOrder`, `multithreaded`.
- **`TeamMapTsunamiAdapter.oceanCells`:** (72) okyanus genişliği; ızgara boyutunu ve maliyeti belirler.
- Duvar yükü yalnız kayıtlı segmentlerin dokunduğu 1–2 ızgara yüzünde örneklenir; duvar×tüm hücre taraması ve kare başı allocation yoktur. Her owner kendi yüz metadata event'ini mutlaka alır; böylece hasar ve kırılma yönü ortak yüzde de doğru segmente aittir. Görsel tekrar engeli bundan sonra `ImpactSplashEmitter` içindedir: solver örneği + sayısal yüz başına ilk uygun owner en çok bir burst üretir, ayrıca kare başı 24 duvar burst'ü sınırı vardır. Kuvvet hesabı axis-aligned grid normalini, efekt ise bu yönle aynı tarafa çevrilmiş gerçek segment normalini kullanır. Unity nesnelerine yalnız çözücü adımı tamamlandıktan sonra ana thread'de dokunulur.
- **Grafik:** Tsunami > Graphics > Use Fast Settings (SSAO, HDR, gölge kapalı kopya ayarlar; orijinaller değişmez).

## Reset anlamı

- `WaterSimulation.ResetWater()`: yalnız suyu durgun deniz seviyesine döndürür; yaşayan duvarları, mevcut hasarlarını ve bariyerlerini korur.
- `TsunamiController.ResetSimulation()` / `Restart()`: tam senaryo sıfırlamasıdır; yaşayan duvarların hasarını sıfırlar, parçacıkları ve enkazı temizler. Daha önce yıkılmış kenarlar inşa modelinde artık olmadığı için geri gelmez.
