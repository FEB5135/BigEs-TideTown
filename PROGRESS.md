# İlerleme Kaydı — Kaynak Ekibi

Yeni bir oturuma (session) başlarken önce bu dosyayı oku. Her iş bitince "Son durum" ve "Sıradaki işler" bölümlerini güncelle.

Son güncelleme: 2026-10-17

## Bağlam

- **Yarışma:** Civil Game Jam, 16–18 Ekim 2026, 48 saat. Son teslim **18 Ekim 18.00 (UTC+3)**. Kurallar: `CIVIL_GAME_JAM_Kural_Kitapcigi_v09.pdf` (repoda değil, proje klasöründe).
- **Oyun:** Köyü tsunamiden koru (bkz. `Plan.md`). Ortada kuyu, çevresinde kasaba. Ağaçlardan odun, madenden taş toplanıp kuyuya atılır; kaynaklarla duvar yapılır.
- **Bizim sorumluluğumuz:** Kaynak toplama, idle mekanik ve kaynak yönetimi (Plan.md'deki Kişi 2).
- **Unity:** 6000.6.6f1, URP 17.6.0, Input System (yalnız yeni sistem açık). TextMeshPro içe aktarılmadı; arayüzde UGUI `Text` kullanılıyor.
- **Repo:** https://github.com/FEB5135/BigEs (şu an private, teslimde public olmalı).

## Branch ve ana sahne

- Aktif dal: `ContructionMechanics`; proje konumu: `C:\Projects\BigEs`.
- Son birleştirilen main: `7afe46b` (Wave Collapse Function).
- **Ortak geliştirme sahnesi `Assets/Scenes/SampleScene.unity`**. Kaynak, ekonomi, inşa ve harita geliştirmeleri burada sürdürülür; Build Settings'te ilk sahnedir.
- `ResourceTest.unity` eski bağımsız test sahnesi olarak korunur. Oradaki değişiklikler SampleScene'e otomatik aktarılmaz.
- Asset Store ağaç paketi Git dışında kalır; diğer bilgisayarda aynı paket kurulmalıdır.

## Son durum (yapılanlar)

- **Construction güncellemesi (ff6d8c9):** GitHub ContructionMechanics dalındaki çok katlı duvar ve malzeme seviye yükseltmeleri çekildi; yerel SampleScene/yeşil zemin a5e25b5 ile korunup 10bb1a5 ile çakışmasız birleştirildi. Unity 48/48 test başarılı. Genişletilmiş SampleScene Play testi normal toplanan 20 odun/16 taşla dört duvar kurdu, Taş ve Güçlendirilmiş yükseltmeleri ile destekli üst katı ve kümülatif maliyet farklarını doğruladı. Push yapılmadı.

- **Yeşil zemin geri yüklendi:** SampleScene MapView, model paleti Flat.mat yerine ayrı TerrainGround.mat kullanır. Eski zemin rengi (0.35, 0.55, 0.3) korunur; kuyu/ev/maden malzemeleri değişmez. Play görünümünde yeşil arazi ve çalışan grid doğrulandı. Yeni sahne kurulumu da bu malzemeyi atar.

- **SampleScene ortak oynanış:** ResourceTest içindeki çalışan kuyu/depo, 40 ev, 34 ağaç, 5 oduncu, 3 maden ve vagon üreticileri, HUD, kamera ve navigasyon taşındı. Ayrı inşa World kaldırılıp controller mevcut WFC World bileşenine bağlandı. Grid 32x32, hücre 6 m, köken (-96,0,-96); modeller hücre boyutuna ölçeklenir ve arazi collider üretir. Köy/kaynaklar ve bağlantı yollarının altı başlangıç WFC hücreleriyle düz tutulur. Eski örnek platform ve görsel kuyu yerine çalışan kuyu kullanılır. Arazi WFC sonrasında, toplayıcılar başlamadan önce oluşturulur. Son kontrol: 38/38 test başarılı; Play testi normal toplamayla odun/taş kazanıp aynı kuyudan harcayarak dört duvar kurdu. SampleSceneSetup menüsü bir defalık taşıma içindir, kurulu sahneyi tekrar yazmaz.

- **Güncel main grid (7afe46b):** GitHub'dan yeniden fetch edildi ve `Wave Collapse Function` commiti birleştirildi. Grid/WFC kurulumu `Assets/Scenes/SampleScene.unity` içindedir (32x32 dünya, altı model); `ResourceTest` bu aşamada ayrıydı; sonraki SampleScene aktarımıyla oynanış birleştirildi. MapView ağırlıklı görsel varyant seçimini içerir. ProBuilder ayar çakışması main değerleri korunarak çözüldü. Yeni sahne bağlantı testi dahil 35/35 EditMode testi başarılı.

- **Uzak construction dalıyla uzlaştırma:** Pull sonrası 232 add/add çakışması çözüldü. 225 paket meta dosyasında Unity 6000.6.6f1 kurulumuyla eşleşen uzak kimlikler, 5 UI meta dosyasında özgün sprite ayarları korundu. ResourceTest sahnesindeki inşa/kuyu entegrasyonu ve main WFC kodu korundu. URP ayarlarındaki eski bir dosya kimliği doğru değere bağlandı. Yinelenen GUID yok; `MergedAssetIntegrationTests` dahil 34/34 EditMode testi geçti. Push yapılmadı.

- **Main birleştirmesi:** Yereldeki `origin/main` (`5002591`, WFC/grid ve zemin modelleri), `cbb3e6e` ile `ContructionMechanics` dalına çakışmasız birleştirildi. Yerel değişiklikler öncesinde `059eb1a` ile kaydedildi. Unity EditMode: `IdleTown.EditModeTests` derlemesinde 26/26 test başarılı. Sonraki başarılı fetch ile GitHub main sürümünün de 5002591 olduğu doğrulandı.

- **Maden/vagon prefab bağlantısı:** Yerelde yeniden oluşturulan FBX meta GUID'leri kaynak dalındaki özgün değerlere döndürüldü (Quarry: `de100f7e611584749bb32c5aff7ca2c6`, MineCart: `1e0776f0470e66e418ee02157ce9870e`). Prefab kaynak referansları artık mevcut model dosyalarını buluyor. Orman için Git'ten hariç tutulan `Assets/FinottiGames` paketi resmi Asset Store üzerinden bu makineye içe aktarıldı; sahnedeki 34 ağacın özgün prefab bağlantıları geri geldi. ResourceTest Play modunda orman ve üç maden görünür, Console hata ve uyarı sayıları 0.

- **Yeni proje konumu:** `C:\Projects\BigEs`. Gömülü URP 17.6.0 paketlerindeki eksik dosyalar Unity 6000.6.6f1 kurulumundaki aynı paketlerden tamamlandı; mevcut dosyalar korundu. Eksik `Textures/DebugFont.tga` ilk render oluşturulurken MissingReferenceException ve ardından Blitter yeniden başlatma hatasına neden oluyordu.

- **Unity derleme hatası (CS0246 MaterialReferenceChanger):** Unity 6000.6.6f1'in kendi hatası. URP core ve universal paketleri `Packages/` içine gömüldü, eksik `.meta` dosyaları eklendi. Yeni bir URP projesinde aynı hata çıkarsa aynı çözüm uygulanır.
- **Kaynak sistemi** (`Assets/Scripts/Economy/`):
  - `Well`: Kuyu ve depo. `Deposit`, `CanAfford`, `TrySpend` (ya hepsi harcanır ya hiçbiri), `Changed` olayı. `Throw` ile eğik atış: ilk hız analitik olarak çözülür, tek parabol, duvarın üstünden geçmesi kontrol edilir.
  - `ResourcePickup`: Atılan parça. Kuyudaki tetikleyiciye girince depoya eklenir; 6 sn içinde girmezse yine eklenir.
  - `WellIntake`: Kuyu içindeki tetikleyici.
  - `ResourceNode`: Ağaç/kaya. Kapasite, tükenince gizlenme ve yeniden çıkma, tıklayınca sallanma. Collider yoksa modelin sınırlarına göre kendisi ekler, bu yüzden model değişse de çalışır.
  - `Gatherer`: Idle köylü. En yakın boş kaynağa gider, toplar, kuyunun dışında durup atar.
- **Oynanış** (`Assets/Scripts/Gameplay/`):
  - `IsometricCamera`: 45/45 bakış, tekerlekle zoom, sağ tıkla kaydırma (yönü kullanıcı onayladı).
  - `ClickHarvester`: Ağaca sol tıklayınca odun kuyuya atılır.
- **Arayüz** (`Assets/Scripts/UI/ResourceHUD.cs`): Sol altta odun ve taş butonları (Kenney ahşap çerçeve, saint11 ikonları). Butonların tıklama işlevi henüz yok.
- **Test sahnesi:** `Assets/Scenes/ResourceTest.unity`. Hepsi `Assets/Scripts/Editor/ResourceTestSetup.cs` ile kuruluyor (menü: Tools > BigEs > Kaynak Test Sahnesi Kur).
  - **Ölçek:** Ortalama ev genişliği ~3.8 m, kuyu çapı 5 ev = 19 m (`well.obj` buna göre büyütülüyor; `Flat.mat` + `palette.png` ile boyanıyor).
  - Kuyunun etrafında 3 halkada 49 konsept ev (kasaba yarıçapı ~36 m). Orman yönü köylülerin yolu olarak boş.
  - Solda kasabanın dışında 34 ağaçlık orman (FinottiGames), 5 oduncu (hız 5).
  - Sağda dağ sırası (yükseklik 32–58 m). Kasabanın arkasında vadi: sağ duvarı dağ sırası, sol duvarı tek büyük dağ. **Tsunami bu vadiden gelecek.**
  - Zemin 300 m. Kamera zoom 15–160 (başlangıç 70), kaydırma ±120 m.
- **Maden ocağı modeli (v2, detaylı):** `Assets/Meshes/Quarry.fbx` (~15.8k üçgen, `palette.png` UV'leriyle, kuyu stilinde). Blender 4.2 arka planda scriptle kuruldu: `Art/Blender/quarry.py` (+ `common.py`, `quarry.blend`, `quarry_preview*.png`). Yeniden üretmek için: `QUARRY_FBX=<yol> blender -b --python Art/Blender/quarry.py`. İçerik: katmanlı kaya kütlesi, kuyunun tuğla dilinde taş kemerli giriş, kemerli ve derine doğru kararan tünel, ahşap iç destekler, raylar, paletli taş istifi, vinç ve kova, alet sundurması, el arabası, fenerli direkler, tabela, sarı kristaller, bayrak. Alt nesneler: Rocks, Tunnel, Portal, Frame, Rails, StonePile, Crane, Bucket, Shed, Props, Sacks, Barrow, Sign, Posts, Crystals ve Lantern/PostLantern0/PostLantern1/Bucket (pivotları kancada, sallanabilir). Giriş Unity'de +Z'ye bakmalı (sahnede henüz doğrulanmadı). Sahnedeki placeholder ocaklar henüz bununla değiştirilmedi.
  - **Sahnede:** `BuildQuarries` artık `Quarry.fbx`'i kullanıyor (yoksa eski placeholder). Model `Flat.mat` ile boyanıyor, kameraya dönsün diye -35° çevrili. Collider yalnız Rocks ve Portal'da (MeshCollider), raylar ve süsler tıklanmıyor. Fenerler ve kova `Sway` (`Assets/Scripts/Gameplay/Sway.cs`) ile hafifçe sallanıyor. Ocak tükenince yalnız taş istifi gizleniyor (`ResourceNode.depletedHide`, boşsa eskisi gibi tüm model). Ocak hizasındaki dağlar geri itildi (`BuildMountains`, `QuarryOffsets`), yoksa ortadaki ocağı kapatıyordu. Play'de vagonların taş taşıdığı görüldü (sayaç arttı). Sallanma Play'de gözle kontrol edilmedi.
- **Maden vagonu modeli:** `Assets/Meshes/MineCart.fbx` (~3.9k üçgen; `Art/Blender/minecart.py`, `MINECART_FBX=<yol>` ile çıkarılır). Konik ahşap kasa, metal kuşak ve çerçeve, şasi, tamponlar, kanca, kırmızı plaka, arka köşede fener. Ayrı alt nesneler: Load (taşırken görünür), WheelsFront/WheelsBack (pivot aksta), CartLantern (sallanır). Tekerlekler raylara oturur (ray aralığı 1.1 m). Blender yardımcıları `Art/Blender/shapes.py`'de ortak.
- **Vagonların maden döngüsü:** `ResourceNode.approachPath` verilirse `Gatherer` noktalardan sırayla geçip içeri girer, toplar, aynı yoldan çıkar. Ocaklarda yol: Model altında "Rail Entry" (ray başı) ve "Mine Inside" (tünelin 5 m içi). Vagon boş girer, dolu çıkar, kuyuya atıp geri döner. `RollingWheels` tekerlekleri gidilen yola göre döndürür. Ocak başına 1 vagon (`maxGatherers` 1), yoksa üçü aynı ocağa gidip üst üste yürüyordu. Play'de karelerle doğrulandı.
- **Rastgele harita (WFC) uyumu, vagon yol bulma:** Vagonlar artık sabit değil, ocaklarına bağlı.
  - `Assets/Prefabs/Economy/Quarry.prefab`: ocak modeli + `ResourceNode` + `GathererSpawner` + ray başı/tünel içi noktaları. Girişi prefab'ın +Z yönünde. Oyun başında kendi vagonunu (`MineCart.prefab`) ray başında üretir, vagonun ev ocağı (`Gatherer.home`) o ocaktır. Prefab'lar `Assets/Scripts/Editor/EconomyPrefabs.cs` ile FBX'lerden kurulur (menü: Tools > BigEs > Ekonomi Prefab'larını Kur; test sahnesi kurulumu da çağırır).
  - `Assets/Scripts/Navigation/NavigationBaker.cs`: NavMesh'i oyun sırasında kurar (paket yok, Unity çekirdeği `NavMeshBuilder`). Engeller her Renderer'ın kutusundan çıkarılır, model okunabilir olmasa da çalışır; `Gatherer` olanlar ve `NavIgnore` işaretliler hariç. Test haritasında 221 kaynak, 38 ms. Vagon yarıçapı 0.8, eğim 25°, basamak 0.4.
  - `Gatherer`: NavMesh varsa köşe köşe engellerin etrafından gider (yoksa düz çizgi), köşelerde yumuşak döner, kuyunun kendi tarafındaki kenarına gider. Ev ocağı tükenirse ray başında bekler. Oduncular ev olmadan en yakın ağacı arar, onlar da NavMesh kullanır.
  - Deneme: Quarry 2 köyün arkasına taşındı. Vagon evlerin arasındaki boşluklardan geçip ocağa gitti ve döngüyü tamamladı. Sonra sahne normal yerleşime geri kuruldu.
  - **WFC ekibine:** (1) Ocak için `Quarry.prefab`'ı koyun, istediğiniz yöne döndürün; ray başı girişin ~12 m önünde, orası boş/yürünebilir kalmalı. (2) Harita bitince `NavigationBaker.RequestRebuild()` çağırın (sahnede bir `NavigationBaker` olmalı; haritayı sonradan üretiyorsanız `buildOnStart`'ı kapatın). (3) Hareket eden ya da engel sayılmaması gereken objelere `NavIgnore` ekleyin.
- **Kural uyumu:** `THIRD_PARTY.md`'de tüm hazır kaynaklar ve AI kullanımı yazılı. FinottiGames ağaç paketi (Asset Store EULA) `.gitignore`'da ve geçmişten silindi.
- **Duvar/kaynak entegrasyonu:** `Well` artık duvar sisteminin `IWallResourceStore` uygulamasıdır; odun+taş harcaması tek atomik işlemde yapılır ve hatalı olay dinleyicileri commit'i bozamaz. Eksik/bozuk serialize edilmiş bakiye dizileri edit-time erişimde de güvenle normalize edilir. `ResourceTest` kurucusu 240×240 m, negatif dünya koordinatlarını kapsayan inşa gridini ve controller'ı hazırlar fakat mevcut bakiyeyi değiştirmez; kaynaklar normal oynanışla toplanır. Yeni ve yükseltilen sahneler Toplama modunda başlar; B/panel ile İnşa–Toplama modu değiştirilir ve UI tıklamaları iki sol-tık sistemine de aktarılmaz. Yalnız ayrı duvar demosu `Well` deposunu 60/30 ile başlatır. Başarı/yetersizlik/geçersiz yol/observer ve bozuk bakiye dizisi EditMode testleri yazıldı; statik kontroller geçti, Unity bulunmadığı için testlerin çalıştırılması bekliyor.
- **Duvar katları ve seviyeleri (kod tamamlandı, Unity doğrulaması bekliyor):** İnşada panel veya `PageUp/PageDown` ile kat, `1/2/3` ile Ahşap/Taş/Güçlendirilmiş seçilir. Kat 1 tabanı, duvar/kat yüksekliği ve azami kat sayısı ayarlanabilir. Üst kenarlar aynı kenarda eşit ya da daha güçlü doğrudan alt destek ister; doluluk ve çapraz kesişme kat bazındadır. Maliyetler kümülatif ve monotondur; yükseltme yalnız farkı öder, düşürme/aynı seviye reddedilir. Görsel önce hazırlanır, depo bir kez harcanır, sonra model commit edilir ve ancak bundan sonra eski yükseltme görseli kaldırılır. Ön izleme, collider/engel sorgusu ve oluşturulan geometri seçili kat yüksekliğini kullanır. `WallSegment` kenar/kat/seviye metadata'sı ile `WallTierBuilt` olayı eklendi; eski `TryBuild`, `Edges` ve `WallBuilt` korunuyor. Unity'den bağımsız `WallTierRules` için 5 senaryolu .NET çalıştırılabilir testi geçti. Kat/destek/yükseltme/rollback/cache/engel regresyonları EditMode testlerine yazıldı fakat kullanıcının isteğiyle Unity çalıştırılmadı.

## Sıradaki işler / açık konular

- [x] ff6d8c9 kat/seviye güncellemesini ortak SampleScene kaynak akışıyla Unity Play modunda doğrulamak.

- [ ] Yeni oynanış ve harita düzenlemelerini SampleScene üzerinde sürdür; ResourceTest kurucusunu ana sahneyi yenilemek için kullanma.

- [ ] Maden modelini arkadaşa gösterip stil onayı almak. İsteğe bağlı: taş alınınca toz efekti; ocaklar 16 m aralıkla birbirine değiyor, gerekirse aralık açılabilir.
- [ ] Sol alttaki odun/taş butonlarına işlev vermek (ne yapacakları henüz kararlaştırılmadı).
- [x] Duvar sistemini `Well.TrySpend` ile bağlamak; ResourceHUD aynı bakiye olaylarını izliyor.
- [ ] Toplama hızı ile duvar maliyetini dengelemek.
- [ ] Unity'de yeni duvar EditMode regresyonlarını ve Play modunda kat/seviye paneli, yükseltilmiş ön izleme ile materyal ayrımını doğrulamak.
- [ ] Arkadaşa iletilecek: `.obj` modellerin `.mtl` dosyaları repoda yok, modeller beyaz görünüyor. Malzemeleri `Flat.mat`'e eşlemeli.
- [ ] Teslimden önce: Repo public olmadan eski commit'lerdeki Asset Store dosyaları için GitHub Support'a başvurmak ya da temiz bir geçmişle yeniden oluşturmak.
- [ ] `ProjectSettings/Packages/com.unity.probuilder/Settings.json` Unity tarafından değişti, commit edilmedi.
- [ ] URP gölge mesafesi (`Assets/Settings/PC_RPAsset.asset`, `m_ShadowDistance: 50`) büyük haritada uzaktan bakınca gölgeleri kesiyor; ekip isterse artırılabilir (ortak ayar, dokunulmadı).

## Çalışma notları (oturumlar arası)

- **Sahneyi değiştirmek:** Doğrudan `Assets/Scenes/SampleScene.unity` içinde düzenle ve kaydet. `ResourceTestSetup` eski test sahnesini kurar; ana sahne için yeniden çalıştırılmaz. `SampleSceneSetup` tek seferlik aktarım menüsüdür ve kurulu sahneyi yeniden oluşturmaz.
- **Derleme:** Unity ancak penceresi öne gelince derler. Play modundayken derlemez. Sonuç `Logs/Editor.log`'dan (`Tundra build success`, `error CS`) okunur.
- **Ekran kontrolü:** Kullanıcının Unity düzeni değişebiliyor (Game görünümü üstte küçük). Fare/klavye ile test etmeden önce tam pencere görüntüsü alınmalı. Unity önde değilse kullanıcı bilgisayarı kullanıyordur; dokunulmaz.
- **İndirme:** Her dosya indirmesi için kullanıcıdan izin alınır. Yalnız CC0 ya da repoya konabilir lisanslar repoya girer; Asset Store paketleri `.gitignore`'a eklenir ve `THIRD_PARTY.md`'ye yazılır.
- **Model tasarımcısı var:** Görsellerde fazladan iş yapılmaz, placeholder'lar yeterli.
- Kullanıcıyla Türkçe konuşulur.
