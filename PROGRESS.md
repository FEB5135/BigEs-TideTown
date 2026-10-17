# İlerleme Kaydı — Kaynak Ekibi

Yeni bir oturuma (session) başlarken önce bu dosyayı oku. Her iş bitince "Son durum" ve "Sıradaki işler" bölümlerini güncelle.

Son güncelleme: 2026-10-17 (04:05)

## Bağlam

- **Yarışma:** Civil Game Jam, 16–18 Ekim 2026, 48 saat. Son teslim **18 Ekim 18.00 (UTC+3)**. Kurallar: `CIVIL_GAME_JAM_Kural_Kitapcigi_v09.pdf` (repoda değil, proje klasöründe).
- **Oyun:** Köyü tsunamiden koru (bkz. `Plan.md`). Ortada kuyu, çevresinde kasaba. Ağaçlardan odun, madenden taş toplanıp kuyuya atılır; kaynaklarla duvar yapılır.
- **Bizim sorumluluğumuz:** Kaynak toplama, idle mekanik ve kaynak yönetimi (Plan.md'deki Kişi 2).
- **Unity:** 6000.6.6f1, URP 17.6.0, Input System (yalnız yeni sistem açık). TextMeshPro içe aktarılmadı; arayüzde UGUI `Text` kullanılıyor.
- **Repo:** https://github.com/FEB5135/BigEs (şu an private, teslimde public olmalı).

## Branch'ler

- `main`: Ekibin ortak branch'i. Son merge edilen commit: `22f7139 Simple Grid Worldmodel` (harita grid sistemi, `well.obj`, ProBuilder).
- `feature/resource-gathering`: Bizim çalışmamız. Push edilmemiş commit'ler var; push etmeden önce kullanıcıya sor.
- `backup/resource-gathering-before-rewrite`: Sadece yerelde. İçinde Asset Store ağaç paketi var, **asla push etme**. İş bitince silinebilir.

## ⚠ Yarım kalan iş (önce bunu bitir)

**Maden ocakları + taş toplayan vagonlar** kodlandı, derlendi (hata yok), sahne kuruldu (3 ocak + 3 vagon) ve commit edildi. **Play'de henüz test edilmedi.**
- İlgili dosyalar: `Assets/Scripts/Economy/ResourceNode.cs` (`maxGatherers`: bir kaynakta birden fazla toplayıcı), `Assets/Scripts/Editor/ResourceTestSetup.cs` (`BuildQuarries`, `BuildMineCarts`, köyde ocak yönüne ±24° yol boşluğu).
- Tasarım: Ekranın sağında, kasabanın dışında (TownRadius+9) 3 ocak, girişleri kasabaya bakıyor. Her ocak bir Stone `ResourceNode` (kapasite 200, yenilenme 15 sn, en fazla 3 vagon); tıklayınca taş kuyuya atılır. 3 vagon (`Gatherer`, Stone, 8 taş, hız 4, girişin 5 m önünde durur) taşı kuyuya götürüp atar.
- Yapılacaklar: Play'de vagonların ocağa gidip taş getirdiğini, kuyuya attığını ve ocağa tıklayınca taş sayısının arttığını test et. Sorun yoksa bu bölümü "Son durum"a taşı.

## Son durum (yapılanlar)

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

## Sıradaki işler / açık konular

- [ ] Maden modelini arkadaşa gösterip stil onayı almak. İsteğe bağlı: taş alınınca toz efekti; ocaklar 16 m aralıkla birbirine değiyor, gerekirse aralık açılabilir.
- [ ] Sol alttaki odun/taş butonlarına işlev vermek (ne yapacakları henüz kararlaştırılmadı).
- [ ] Duvar ekibine `Well.TrySpend` / `ResourceCost` kullanımını anlatmak.
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
