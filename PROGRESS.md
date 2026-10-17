# İlerleme Kaydı — Kaynak Ekibi

Yeni bir oturuma (session) başlarken önce bu dosyayı oku. Her iş bitince "Son durum" ve "Sıradaki işler" bölümlerini güncelle.

Son güncelleme: 2026-10-17 (14:45)

## Bağlam

- **Yarışma:** Civil Game Jam, 16–18 Ekim 2026, 48 saat. Son teslim **18 Ekim 18.00 (UTC+3)**. Kurallar: `CIVIL_GAME_JAM_Kural_Kitapcigi_v09.pdf` (repoda değil, proje klasöründe).
- **Oyun:** Köyü tsunamiden koru (bkz. `Plan.md`). Ortada kuyu, çevresinde kasaba. Ağaçlardan odun, madenden taş toplanıp kuyuya atılır; kaynaklarla duvar yapılır.
- **Bizim sorumluluğumuz:** Kaynak toplama, idle mekanik ve kaynak yönetimi (Plan.md'deki Kişi 2).
- **Unity:** 6000.6.6f1, URP 17.6.0, Input System (yalnız yeni sistem açık). TextMeshPro içe aktarılmadı; arayüzde UGUI `Text` kullanılıyor.
- **Repo:** https://github.com/FEB5135/BigEs (şu an private, teslimde public olmalı).

## Branch'ler

- `main`: Ekibin ortak branch'i. `feature/main-menu` main'e merge edildi (2026-10-17, ana menü + ContructionMechanics + palet düzeltmesi). Son çekilen commit: `7afe46b Wave Collapse Function` (WFC ile grid harita: `Assets/Scripts/Map/WfcSolver.cs`, `MapView`, yeni zemin/yokuş modelleri, `villager.obj`; `SampleScene`'de). `feature/main-menu`'ye merge edildi (2026-10-17), çakışma yok; `Flat.mat`'te iki tarafın değişikliği birleşti. `testground*` modelleri main'de silindi, bizim sahne onları kullanmıyordu.
- `feature/resource-gathering`: Bizim çalışmamız (kaynak toplama). Push etmeden önce kullanıcıya sor.
- `feature/main-menu`: `feature/resource-gathering`'den açıldı. Ana menü burada. GitHub'a push edildi (2026-10-17).
- `backup/resource-gathering-before-rewrite`: Sadece yerelde. İçinde Asset Store ağaç paketi var, **asla push etme**. İş bitince silinebilir.

## ⚠ Yarım kalan iş (önce bunu bitir)

**Maden ocakları + taş toplayan vagonlar** kodlandı, derlendi (hata yok), sahne kuruldu (3 ocak + 3 vagon) ve commit edildi. **Play'de henüz test edilmedi.**
- İlgili dosyalar: `Assets/Scripts/Economy/ResourceNode.cs` (`maxGatherers`: bir kaynakta birden fazla toplayıcı), `Assets/Scripts/Editor/ResourceTestSetup.cs` (`BuildQuarries`, `BuildMineCarts`, köyde ocak yönüne ±24° yol boşluğu).
- Tasarım: Ekranın sağında, kasabanın dışında (TownRadius+9) 3 ocak, girişleri kasabaya bakıyor. Her ocak bir Stone `ResourceNode` (kapasite 200, yenilenme 15 sn, en fazla 3 vagon); tıklayınca taş kuyuya atılır. 3 vagon (`Gatherer`, Stone, 8 taş, hız 4, girişin 5 m önünde durur) taşı kuyuya götürüp atar.
- Yapılacaklar: Play'de vagonların ocağa gidip taş getirdiğini, kuyuya attığını ve ocağa tıklayınca taş sayısının arttığını test et. Sorun yoksa bu bölümü "Son durum"a taşı.

## Aktif çalışma

- Dal: `main`. `villageranimations` çalışması `133f068` ile kaydedilip `2a502c1` merge commit'iyle main'e birleştirildi (taban ve doğrulanan origin/main: `0a0d7c2`). Push yapılmadı.
- Kullanıcının son kararı: kaynak toplama, ana menü ve animasyonlu köylülerle **SampleScene** ana sahne. Kurulum: Tools > BigEs > SampleScene Kaynak ve Koylu Kurulumu.
- Mevcut 128x128 WFC/chunk yapısı ve zemin varyantları korunur; hücre 2 m, köken (-128,0,-128). Köy/kaynak yolları düz başlangıç hücrelerine ayrılır. Chunk collider ve üçgen bazlı arazi NavMesh kaynağı kullanılır. TerrainGround.mat eski yeşil zemin rengini (0.35,0.55,0.3) korur; Flat.mat ve model paleti değişmez. Eski demo nesneleri inaktif Legacy Demo Objects altında; ResourceTest dosyası özgün haliyle durur.

## Son durum (yapılanlar)

- **Köylüleri elle işe koyma (2026-10-17, `feature/villager-jobs`, `fix/tree-cell-ground` + güncel main'den; push edilmedi):** Tomodachi Life tarzı.
  - SampleScene'deki 5 oduncu artık işsiz köylü (`Gatherer.villager`): başta bulunduğu yerin 5 m çevresinde dolaşır.
  - `Assets/Scripts/Gameplay/VillagerPicker.cs` (Main Camera'da): imleç köylünün üzerine (ya da ekranda ~32 px yakınına; uzaktan küçük göründükleri için) gelince açık el, sol tıkla tutunca kapalı el (`Assets/UI/Cursor/HandOpen/HandClosed.png`, yazılımsal imleç). Tutulan köylü imleci 3 m yukarıda takip eder, sallanır. Bırakınca: bir maden ocağının (kaya ya da ray başı) 12 m yakınındaysa ve ocağın boşta vagonu varsa vagonu iter; bir ağacın 6 m yakınındaysa oduncu olur; yoksa orada dolaşır. Menü açıkken ve inşa modunda kapalı; köylü üzerindeyken tıklayarak toplama çalışmaz (`VillagerPicker.Busy`).
  - Vagonlar (`GathererSpawner.needsPusher`, varsayılan açık) iten köylü olmadan bekler; köylü vagonun arkasına yürür, vagonla birlikte hareket eder (kollar önde, `GathererActivity.Pushing`), vagon madene girip taşla kuyuya döner, sürekli. Köylü tutulursa vagon olduğu yerde durur. Bir vagonu aynı anda tek köylü sahiplenir.
  - Gatherer döngüsü yeniden başlatılabilir oldu: `Wander`, `WorkInForest`, `PushCart`, `Stop`.
  - Play'de doğrulandı: köylü tutulup bırakıldı, ormana bırakılan odun getirdi (5), ocağa bırakılan vagonu itti (8 taş), diğer vagonlar bekledi, konsol temiz. Vagon itme sahnesi yakından gözle incelenmedi.

- **Ocak/vagon renkleri (2026-10-17):** Ekip `29cf5f4 Mountains` ile `palette.png`'nin 5–7 ve bizim 57/59/61. sütunlarımızı (ahşap/sarı/kırmızı) beyaza boyadı; ocak ve vagonlar renksiz kaldı. Bizim modeller artık ekip paletinden bağımsız: `Assets/Textures/EconomyPalette.png` (paletin o sabahki hali) + `Assets/Materials/EconomyFlat.mat`; `Quarry.prefab`, `MineCart.prefab` ve `EconomyPrefabs.cs` bunu kullanır. Ekip paleti değiştirse de bozulmaz. (Not: Blender scriptleri `Art/Blender/shapes.py` hâlâ aynı sütun düzenini kullanıyor; EconomyPalette ile uyumlu.)

- **Ana menü yeniden tasarımı (2026-10-17, `feature/menu-redesign`, main'den; push edilmedi):** Kullanıcıyla konuşulan UI rehberine göre (Crossy Road tarzı). Yalnız `MainMenuSetup` ve sprite'lar değişti, `MainMenu`/`PressableButton` davranışı aynı. SampleScene'de menü yeniden kuruldu.
  - Renkler: Oyna mercan `#F2542D` (alt `#B83A1B`), ikincil (Ayarlar, Geri, ayar okları) okyanus `#1F5F8B` (alt `#154566`), Çıkış hayalet (yarı saydam beyaz, çerçeveli), panel lacivert `#0B2A3D`, vurgu su `#2EC4D6`, skor altın `#FFD23F`. Kahverengi/krem kalktı.
  - Butonlar: düz yüz, 8 px sert alt kalınlık, kesik (pah kırılmış) köşeler; `Art/UI/make_menu_sprites.py` çiziyor (`Button{Coral,Ocean,Ghost}`, `Square*`, `Panel`, `Bar`, `Handle`, `Scrim`). Kenney menü sprite'ları SampleScene'de artık kullanılmıyor.
  - Yerleşim (1920×1080): logo üst ortada (y 262), Oyna 440×112 (y -34), Ayarlar 360×80 (y -170), Çıkış 360×72 (y -266); boşluklar 88/40/20. Skor ortadan sağ üst köşeye, kenarlardan 64 px içeride bir çip. Alt yazı 16 px, %60.
  - İkon/yazı: ikon buton yüksekliğinin %42'si; Oyna'da ikon+yazı ortada grup, ikincillerde ikon soldan 32 px sabit sütunda. Dış çizgi yok, sert aşağı gölge var.
  - Okunabilirlik: lacivert vinyet (%55) + buton sütununun arkasında yumuşak koyuluk (%35) + mevcut bokeh. Logo: kalın çift dış çizgi yerine 3 px lacivert çizgi + 9 px sert aşağı gölge.
  - Yazı tipi Rubik (kullanıcı izniyle Google Fonts CSS API'den sabit dosyalar: `Rubik-Bold.ttf` yazılar, `Rubik-Black.ttf` logo; `Rubik-OFL.txt`). Ç, Ğ, İ, Ö, Ş, Ü, ı Pillow ve fontTools ile kontrol edildi. Ayarlar sayfası Rubik ile gözle kontrol edilmedi (aynı yazı tipi).
  - Play'de doğrulandı: ana sayfa, Ayarlar sayfası, Geri, Oyna → oyun başlıyor; konsol temiz.

- **WFC ağaçları ve zemin main'e (2026-10-17, `feature/wfc-forest`'tan yalnız ağaç/zemin görünümü):** Kullanıcı isteği: main'in SampleScene'i ve World'ü (128×128, 2 m) kullanılacak; ResourceTest ve inşa değişikliklerimiz alınmadı. `SampleSceneSetup.EnsureGridForest` (SampleScene kurulum menüsü de çağırır): MapView malzemesi TerrainGround yerine `Flat.mat` (çimenli karolar görünür), World'e `GridForest` eklenir, eski tek tek ağaçlı `Gameplay/Forest` kapatılır (silinmez), WFC'nin köy için ayırdığı başlangıç hücrelerinden orman alanına düşenler silinir, inşa grid'ine `ConstructionGridToggle` eklenir (çizgiler yalnız inşa modunda). `GridForest`: orman alanı (45° dönük, merkez kasabanın 6 m dışında solda, 30×55 m) dışını WFC'de zemine sabitler; alan içinde yalnız ağaç ve düz zemin kalır (yokuş/tepe karoları zemine çevrilir). Ağaçlar toplanabilir; kesilince zemin plakası olmadan, her ağaç ayrı, 8 yönden rastgele devrilir, 2 sn sonra `FlatFade.mat` ile saydamlaşıp kaybolur; tıklanınca ve oduncu vurunca (`Gatherer.hitInterval` 0.5 sn, yalnız odun) sallanır. `ResourceNode`'a `VisibilityChanged` ve `Hit` olayları eklendi. Play'de doğrulandı: orman köyün solunda, zemin çimenli ve grid'siz, oduncular odun getiriyor, konsol temiz. Ağaçlar ekibin karolarının kendisi, orijinal boyda ve WFC'nin koyduğu sıklıkta (`treeScale` 1, `treeKeep` 1, `treeSpacing` 0). Bir ara kullanıcı isteğiyle 2.5 kat büyütülmüş kopyalar (`*_big`) kullanıldı; ekip arkadaşı "ağaçlar o kadar büyük değildi" deyince kullanıcı orijinal boya döndürttü, kopyalar silindi (`SampleSceneSetup.TeamTreeTile` eski `*_big` referanslarını ekibin .obj'sine çevirir). Ekibin güncel ağaç modelinde (`29cf5f4 Mountains`) gövde altları kapalı ve karonun kendi zemin plakası var, hücrede delik kalmaz. Play'de doğrulandı (gri delik yok, konsol temiz).

- **Animasyon merge (2026-10-17):** Yürüyüş/odun kesme animasyonları, köylü prefabları ve SampleScene kaynak toplama düzeni main'e çakışmasız birleştirildi. Birleşmiş main sürümünde 37/37 Unity testi başarılı (0 hata); yürüyüş, ağaca yönelme, kol/mesh animasyonu ve odun/taş teslimi doğrulandı. Rapor: Logs/main-villager-merge-tests.xml.

- **villageranimations — köylü hareketleri:** Kullanıcının `Assets/Prefabs/villager.prefab` kaynağı iç içe prefab olarak kullanılır; `Woodcutter.prefab` ve `VillagerVisual.prefab` ayrı üretilir. Mevcut köylü Gatherer bileşenleri/tuning korunup görselleri yerinde yükseltilir. Yürüyüşte görsel kök ±15 derece sallanır, kollar/bacaklar hafifçe salınır; ağaca dönüp odun toplarken omuzlar 45–125 derece kaldırılıp indirilir. Pozlar durunca yumuşakça normale döner; animasyon yön bulma kökünü oynatmaz. Scriptler: VillagerAnimation, GathererActivity; kurulum: VillagerPrefabSetup. Son Unity kontrolü: 37/37 test başarılı; Play testinde yürüyüş, ağaç yönüne dönme, omuz/mesh hareketi, odun taşıma ve 20 odun/8 taş teslimi doğrulandı.

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
- **Ana menü** (`feature/main-menu`): Harita arka planda uzaktan ve bulanık; ortada logo (şimdilik yazı: "BigEs" + "KÖYÜ TSUNAMİDEN KORU", dalga amblemi), en yüksek skor rozeti, Oyna / Ayarlar / Çıkış. "Nasıl Oynanır" kullanıcı isteğiyle kaldırıldı. Play'de kontrol edildi.
  - `Assets/Scripts/UI/MainMenu.cs`: Sayfalar, açılışta öğelerin sırayla kayarak gelmesi, Esc ile geri. Oyna deyince menü kaybolur, `CameraIntro` geçişi oynar, sonra kaynak paneli belirir, kuyudaki kaynaklar sıfırlanır (menüde arka planda toplananlar) ve `MainMenu.GameStarted` olayı çağrılır. Diğer ekipler oyunu bu olayla başlatabilir; `MainMenu.IsOpen` menü açık mı söyler.
  - `Assets/Scripts/Gameplay/CameraIntro.cs` (Main Camera'da): Menü duruşu (mesafe 190, açı oyunla aynı 45/45, odak yavaşça sağa sola kayar). Geçiş 3.2 sn, smootherstep (yavaş-hızlı-yavaş), uzaklık logaritmik yaklaşır. Menü açısı (`menuPitch`/`menuYaw`) farklı verilirse geçişte açı da döner: ileride eğimli giriş için hazır. Bulanıklık: oyun sırasında oluşturulan bir Volume'da Bokeh alan derinliği; geçişin ilk %55'inde kaybolur, sonra post-processing sahnenin kendi ayarına döner. Menüdeyken `IsometricCamera.InputEnabled` ve `ClickHarvester` kapalı.
  - `HighScore` (PlayerPrefs): Oyun sonunda `HighScore.Submit(skor)` çağrılmalı. Skorun ne olacağı henüz belli değil.
  - `GameSettings` + `SettingsPanel`: Ses (AudioListener), tam ekran, grafik kalitesi (Düşük=Mobile, Yüksek=PC). PlayerPrefs'te saklanır, açılışta uygulanır.
  - Görünüm (ikinci sürüm, kullanıcı ilkini beğenmedi): Kenney UI Pack 2.0'ın gri parçaları oyunun paletine göre yeniden renklendirildi (`Art/UI/adapt_kenney.py <kenney klasörü>` → `Assets/UI/Menu/Kenney/`). Kullanıcı "olduğu gibi alınmasın, uyarlansın" dedi. Butonlar kalınlıklı; üzerine gelince büyür, basınca düz sprite'a geçip içerik iner, Kenney tık sesi çalar (`PressableButton`). İkon, gölge, perde ve dalga sprite'ları `Art/UI/make_menu_sprites.py` ile üretilir.
  - Yazı tipi: Baloo 2 ExtraBold (OFL, `Assets/UI/Fonts/`). **Lilita One, Fredoka ve Kenney Future'da Ğ, Ş, İ yok**; yeni font seçilirse Türkçe harfler önce Pillow ile çizilip kontrol edilmeli. Unity eski `Text` değişken fontta yalnız varsayılan ağırlığı kullanır; ExtraBold sabit dosyası Google Fonts CSS API'den alındı.
  - Kurulum: `Assets/Scripts/Editor/MainMenuSetup.cs` (Tools > BigEs > Ana Menüyü Kur, varsa silip yeniden kurar). `ResourceTestSetup` sahnede menü yoksa kendisi kurar.
- **ContructionMechanics merge'i (2026-10-17, `feature/main-menu`):** Kullanıcı SampleScene'deki WFC haritasını beğenmedi ("dağlar yok"); tek-sahne kurulumu geri alındı (yedek yerel branch: `backup/single-scene-setup`, push edilmedi) ve `origin/ContructionMechanics` merge edildi. Oynanan harita yine `ResourceTest` (dağlar, köy, orman, ocaklar + 240×240 m inşa grid'i ve duvar inşası).
  - Arkadaşın asmdef'lerinde `Unity.ugui` diye olmayan bir assembly vardı (doğrusu `UnityEngine.UI`) ve URP referansı yoktu; düzeltildi (`IdleTown.Runtime`, `IdleTown.ResourceEditor`). Unity hatasız derliyor.
  - `MainMenu.gameplayBehaviours`: menü açıkken `WallConstructionController` kapalı (yoksa OnGUI paneli menünün üstünde çiziliyor ve B tuşu çalışıyordu). `MainMenuSetup` bunu kendisi bağlar.
  - Play'de doğrulandı: menü → Oyna → kamera iner, kaynak paneli ve inşa paneli (Toplama modu) gelir, kaynaklar artar.
  - Kalan uyarı: `WallConstructionController.DestroyObject` (CS0108), arkadaşın kodu, dokunulmadı.
- **Palet değişikliği (main, "Mesh Additions"):** Ekip `palette.png`'nin 2–4. sütunlarını harita renklerine çevirdi (yeşil, kiremit, gri-mavi). Bizim ocak ve vagon modellerinin ahşap/sarı/kırmızısı bu sütunlardaydı; eski renkler paletin boş 57/59/61. sütunlarına kopyalandı, `Art/Blender/shapes.py` sabitleri değişti ve `Quarry.fbx`, `MineCart.fbx` Blender'da yeniden üretildi (geometri aynı). Ekibe: 57/59/61 sütunlarını boyamayın.
- **Kural uyumu:** `THIRD_PARTY.md`'de tüm hazır kaynaklar ve AI kullanımı yazılı; dosya public repoya ve jüriye uygun dille yazıldı (iç notlar `PROGRESS.md`'de). FinottiGames ağaç paketi (Asset Store EULA) `.gitignore`'da ve geçmişten silindi.
- **Duvar/kaynak entegrasyonu:** `Well` artık duvar sisteminin `IWallResourceStore` uygulamasıdır; odun+taş harcaması tek atomik işlemde yapılır ve hatalı olay dinleyicileri commit'i bozamaz. Eksik/bozuk serialize edilmiş bakiye dizileri edit-time erişimde de güvenle normalize edilir. `ResourceTest` kurucusu 240×240 m, negatif dünya koordinatlarını kapsayan inşa gridini ve controller'ı hazırlar fakat mevcut bakiyeyi değiştirmez; kaynaklar normal oynanışla toplanır. Yeni ve yükseltilen sahneler Toplama modunda başlar; B/panel ile İnşa–Toplama modu değiştirilir ve UI tıklamaları iki sol-tık sistemine de aktarılmaz. Yalnız ayrı duvar demosu `Well` deposunu 60/30 ile başlatır. Başarı/yetersizlik/geçersiz yol/observer ve bozuk bakiye dizisi EditMode testleri yazıldı; statik kontroller geçti, Unity bulunmadığı için testlerin çalıştırılması bekliyor.

## Sıradaki işler / açık konular

- [x] Köylü animasyonlarını ve SampleScene düzenini main'e birleştir.
- [ ] Yerel main değişikliklerini paylaşmak için push (henüz yapılmadı).

- [ ] Ana menü: oyunun adı/logosu belli olunca `Main Menu/Main Page/Logo` değiştirilecek (yazı yerine görsel konabilir).
- [ ] Ana menü: skorun tanımı (kurtarılan ev sayısı, dayanılan dalga...) belirlenince oyun sonunda `HighScore.Submit` çağrılacak.
- [ ] Ana menü: eğimli kamera girişi (CameraIntro'da `menuPitch`/`menuYaw` değiştirmek yeterli).
- [x] Tek sahne kararı: kullanıcı kaynak toplama düzeninin SampleScene içine taşınmasını istedi; animasyonlu köylüler, menü ve kaynak sistemleri burada kuruldu.

- [ ] Maden modelini arkadaşa gösterip stil onayı almak. İsteğe bağlı: taş alınınca toz efekti; ocaklar 16 m aralıkla birbirine değiyor, gerekirse aralık açılabilir.
- [ ] Sol alttaki odun/taş butonlarına işlev vermek (ne yapacakları henüz kararlaştırılmadı).
- [x] Duvar sistemini `Well.TrySpend` ile bağlamak; ResourceHUD aynı bakiye olaylarını izliyor.
- [ ] Toplama hızı ile duvar maliyetini dengelemek.
- [ ] Arkadaşa iletilecek: `.obj` modellerin `.mtl` dosyaları repoda yok, modeller beyaz görünüyor. Malzemeleri `Flat.mat`'e eşlemeli.
- [ ] Teslimden önce: Repo public olmadan eski commit'lerdeki Asset Store dosyaları için GitHub Support'a başvurmak ya da temiz bir geçmişle yeniden oluşturmak.
- [ ] `ProjectSettings/Packages/com.unity.probuilder/Settings.json` Unity tarafından değişti, commit edilmedi.
- [ ] URP gölge mesafesi (`Assets/Settings/PC_RPAsset.asset`, `m_ShadowDistance: 50`) büyük haritada uzaktan bakınca gölgeleri kesiyor; ekip isterse artırılabilir (ortak ayar, dokunulmadı).

## Çalışma notları (oturumlar arası)

- **Sahneyi değiştirmek:** `ResourceTestSetup.cs` düzenlenir. Mevcut sahneye uygulamak için tek seferlik bir `Assets/Scripts/Editor/RebuildResourceTestOnce.cs` yazılır: `[InitializeOnLoad]` + `delayCall` kökü siler, menüyü çalıştırır, sahneyi kaydeder ve kendini siler.
- **Derleme:** Unity ancak penceresi öne gelince derler. Play modundayken derlemez. Sonuç `Logs/Editor.log`'dan (`Tundra build success`, `error CS`) okunur.
- **Ekran kontrolü:** Kullanıcının Unity düzeni değişebiliyor (Game görünümü üstte küçük). Fare/klavye ile test etmeden önce tam pencere görüntüsü alınmalı. Unity önde değilse kullanıcı bilgisayarı kullanıyordur; dokunulmaz.
- **İndirme:** Her dosya indirmesi için kullanıcıdan izin alınır. Yalnız CC0 ya da repoya konabilir lisanslar repoya girer; Asset Store paketleri `.gitignore`'a eklenir ve `THIRD_PARTY.md`'ye yazılır.
- **Model tasarımcısı var:** Görsellerde fazladan iş yapılmaz, placeholder'lar yeterli.
- Kullanıcıyla Türkçe konuşulur.
