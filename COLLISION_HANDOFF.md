# Tsunami–Duvar Çarpışması Devir Notu

## Windows / Unity entegrasyon sonucu — 17 Ekim 2026

- `origin/collision` commit `eed4ee5` yerel `collision` dalına fast-forward alındı. Çalışma klasörü: `C:\Users\demir\OneDrive\Desktop\PojectIdletown\Collision`. Ana `BigEs` klasöründeki değişikliklere dokunulmadı.
- Unity 6000.6.6f1 gerçek C# derlemesi başarılı. Mevcut CS0108 DestroyObject uyarısı sürüyor.
- `SampleScene` yeniden oluşturulmadı; inşa ve su adaptörünün aynı World'ü kullanması, simulation/terrain/controller/splash referansları doğrulandı. 100 HP dayanıklılık, hasar eşikleri, 20 enkaz / 8 saniye ve 320 köpük parçacığı ayarları sahneye kaydedildi.
- Görsel Play kontrolünde tsunami panelinin ana menü üstünde görünmesi düzeltildi: Tsunami UI CanvasGroup'u mevcut `MainMenu.gameplayUI` listesine bağlandı. Menüde gizli/tıklanamaz, kamera giriş geçişi tamamlanınca diğer oyun panelleriyle birlikte görünür. Menü → Oyna → gerçek kasaba ve tsunami paneli geçişi ekranda doğrulandı.
- Grafik açık Unity Play kontrolünde 10 m dalga başlatıldı; gerçek kasabada su ilerlemesi, dağ/ev temasındaki sıçramalar ve köy flood/destruction sayaçlarının değişmesi görüldü. Test sonunda Sıfırla ile deniz ve köy başlangıca döndürüldü; Unity Collision/SampleScene projesinde Play modunda hazır bırakıldı. Gerçek dalga ile farklı duvar düzenlerinin koruma dengesi henüz kapsamlı test edilmedi. Unity Cloud Project ID için 403 uyarısı var; oynanışı engellemedi.
- `Tsunami > Collision > Validate and Save Team Map` menüsü eklendi. Aynı editör aracının `PrepareAndSmoke` giriş noktası gerçek haritada kontrollü bir Play Mode doğrulaması çalıştırır: iki segment inşa etme, su bariyerine kaydolma, gerçek yük örnekleme/hasar olay zinciriyle tek segmenti kırma, Rigidbody enkazı, bariyer kaldırma, komşuyu koruma ve aynı kenara yeniden inşa. Bu kontrol geçti. Test girdisi deterministik olarak çözücü hücresine yazılır; tam tsunami yayılımının veya görsel kalitenin doğrulaması değildir. Test kaynak deposu ve su girdisi yalnız kaydedilmeyen Play Mode durumunda kullanılır.
- Yerel ağaç bağımlılığı eksik olduğu için ilk geniş Unity koşumunda 37/40 geçti. Önceden indirilmiş FinottiGames unitypackage yerel önbellekten aktarıldı; paket Git dışında kalıyor. Yeniden koşum: **40/40 EditMode testi geçti**, 0 başarısız.
- Saf C# çarpışma testleri: **10/10 geçti**. Bilgisayarda .NET 10 olduğu için test projesinin hedefi komut satırında net10.0 olarak override edildi; proje dosyası değiştirilmedi.
- Kanıt dosyaları çalışma klasörünün üst dizininde: `collision-compile.log`, `collision-runtime.log`, `collision-editmode-verified.xml`, `collision-editmode-verified.log`.
- İlk headless Play koşumunda UnityEditor.Search.SearchDatabase kaynaklı editör indeksleme exception'ı görüldü; oyun kodu kaynaklı değildi ve kontrollü runtime kontrolü geçti. Bu, görsel oynanış doğrulamasının yerine geçmez.
- Commit, push veya başka dala merge yapılmadı. Aşağıdaki eski teslim notları OpenCode'un teslim anını anlatır; yukarıdaki sonuçlar bu makinedeki güncel entegrasyon kontrolleridir.

## Çalışma kapsamı

- Workspace: `/Users/egedemirturkanil/Documents/GitHub/Collision`
- Branch: `collision`
- Base HEAD: `f35fd06061c92f9dc48baf0319efc61ce38e6a71` (`f35fd06`, `origin/Terrain-1`)
- Commit/push/merge/rebase yapılmadı. Başka worktree'ye dokunulmadı.
- Scene, prefab, `Packages/`, package manifest ve ProjectSettings değiştirilmedi; yeni asset yoktur.

## Değişen dosyalar

- `Assets/Tsunami/Scripts/Simulation/TsunamiPhysicsSolver.cs`
- `Assets/Tsunami/Scripts/Simulation/ImpactDetection.cs`
- `Assets/Tsunami/Scripts/Simulation/WaterSimulation.cs`
- `Assets/Tsunami/Scripts/Simulation/WallDamageModel.cs` + `.meta`
- `Assets/Tsunami/Scripts/Gameplay/TeamMapTsunamiAdapter.cs`
- `Assets/Tsunami/Scripts/Rendering/ImpactSplashEmitter.cs`
- `Assets/Scripts/Construction/WallConstructionModel.cs`
- `Assets/Scripts/Construction/WallConstructionController.cs`
- `Assets/Tests/EditMode/WallConstructionModelTests.cs`
- `Tests/TsunamiCollisionPure/TsunamiCollisionPure.csproj`
- `Tests/TsunamiCollisionPure/Program.cs`
- `.gitignore` (yalnız elle yazılan `Tests/**/*.csproj` ve test build çıktıları için kurallar)
- `Assets/Tsunami/README.md`, `PROGRESS.md`, `THIRD_PARTY.md`, `COLLISION_HANDOFF.md`

Sahne, prefab, paket ve ProjectSettings değiştirilmedi; yeni asset paketi yoktur.

## Kurulum / bağlantı

Mevcut **Tsunami > Add Tsunami To Open Scene (Team Map)** kurulumu yeterlidir. Kurucu `TeamMapTsunamiAdapter.wallController` ve `ImpactSplashEmitter.simulation` referanslarını bağlar. Adapter ve emitter, aynı `Tsunami` kökü altındaki `TsunamiController` reset olayını kendileri bulur. Sahne yeniden kurulmayacaksa Inspector'da adapter'ın World/Terrain/Simulation/Wall Controller alanlarının dolu olduğunu kontrol edin. Ek OnCollisionEnter bileşeni veya ikinci çözücü eklemeyin.

## Inspector varsayılanları

- Team Map Adapter: fallback wall height **1.25 m**.
- Dayanıklılık: **100 HP**.
- Darbe: eşik **6 kN**, **0.32 HP/(kN·s)**.
- Sürekli su yükü: eşik **12 kN**, **0.055 HP/(kN·s)**.
- Enkaz: en çok **20**, ömür **8 s**, impulse scale **0.18**.
- Impact Splash: minimum normal hız **2.5 m/s**, minimum derinlik **0.3 m**, kare başı en çok **24** duvar çarpması; damla/köpük/sis kapasitesi **900/320/160**.

## Fizik yaklaşımı ve sınırları

- Solver bariyer yüzü gerçek `WallEdge` ile handle üzerinden kayıtlıdır. Yatay/dikey kenar bir yüz; çapraz kenar mevcut grid yaklaşımı gereği iki yüzlü L merdivendir. Bu nedenle çapraz engelin sayısal izi gerçek ince diyagonalden daha geniştir.
- Crest dünya yüksekliği inşa edilen nesnenin Renderer üst sınırıdır; Renderer yoksa 1.25 m fallback kullanılır. Su yüzeyi crest'i aşınca mevcut hidrostatik taşma davranışı sürer.
- Her solver adımından sonra yalnız segmentin dokunduğu yüzler okunur. Her iki taraftaki `½ρhu_n²L` giriş yükleri toplanır. Sürekli yük iki taraftaki `½ρgh²L` basıncın mutlak net farkıdır (eşit seviyeler iptal); çapraz merdiven yüzlerinin netleri toplanır. Segment hasarı solver adımı başına tam bir kez uygulanır. Birimler fiziksel, HP dönüşümü oyun ayarıdır; yapı mühendisliği/afet tahmini değildir.
- Her owner için her dokunduğu grid yüzünün güçlü giriş yönü ayrı metadata event'i üretir; ortak yüzde sonraki owner artık bastırılmaz. Temas o yüz merkezinden ilgili gerçek segmente izdüşürülür. Kuvvet axis-aligned grid normaliyle hesaplanır; sıçrama normali bu girişle aynı tarafa çevrilen gerçek segment normalidir. Görsel dedup bundan sonra yalnız emitter'dadır: sample sequence + grid yüzü başına ilk uygun owner bir burst üretir, kare başı duvar burst sınırı 24'tür. Genel terrain taraması kayıtlı/statik bariyeri atlar.
- Solver bariyer bileşimi owner-aware'dir: `SetBarrier` kalıcı baseline, `SetOwnedBarrier` segment katkısıdır. Owner kaldırılması diğer owner'ı ve daha düşük/yüksek baseline'ı korur; owner-only yüz son owner'da açılır. Global `ClearBarriers` yalnız gerçekten bütün bariyerleri sıfırlamak içindir.
- Solver işi tamamlandıktan sonra event ana thread'de işlenir; worker'lardan Unity API çağrılmaz. Yük maliyeti O(segment yüzü), O(duvar×hücre) değildir. Kalıcı listeler kullanılır; normal karelerde allocation yoktur.
- Arbitrary prefab parçalama yoktur: yalnız kırılan segment kökü tek hafif Rigidbody enkaz olur. Kırık parçanın tüm özgün collider'ları (özellikle dinamik Rigidbody ile güvenli olmayan non-convex MeshCollider'lar) kapatılır ve Renderer dünya bounds'larından root-local hesaplanan tek konservatif BoxCollider proxy kullanılır. Mesh convex cooking yapılmaz; sağlam statik duvar collider'larına dokunulmaz. Bu proxy performans/güvenlik için görsel taşa tam oturmayabilir. Limit/ömür dolunca silinir.
- Enkaz itkisi kırılmayı doğuran aynı segment-adımı metadata'sını kullanır. Normal akış yoksa fakat sürekli net su yükü duvarı kırarsa temas noktası gerçek segment merkezi, yön en güçlü hidrostatik basınç farkının gerçek segment normali ve itki sürdürülen yükten sınırlı bir ölçektir; `(0,0,0)`/eski impact snapshot kullanılmaz.
- Pause, enkazın kalan ömrünü ve Rigidbody simülasyonunu durdurur; pause öncesi linear/angular velocity cache'lenip resume sonrasında geri yüklenir, böylece enkaz aynı hareket durumundan devam eder.

## Reset / restart

- `WaterSimulation.ResetWater`: suyu sıfırlar; yaşayan duvarları ve hasarını korur.
- `TsunamiController.ResetSimulation` ve `Restart`: yaşayan segmentlerin hasarını sıfırlar; parçacık/enkaz/pending break temizlenir.
- Yıkılan segment modelden silindiği için hiçbir reset onu diriltmez. Aynı kenar tekrar normal kaynak harcamasıyla yapılabilir; kaynak iadesi yoktur.
- Adapter disable olduğunda event'lerden ayrılır ve yalnız player-owner bariyerlerini kaldırır. Re-enable ve solver yeniden yaratılması, `WallConstructionController.Edges` modelini authoritative kabul ederek handle'ları yeniden kurar; artık modelde olmayan state silinir, yaşayan duvar hasarı korunur. Removal dinleyicisi yoksa controller detached görseli kendisi siler.

## API değişiklikleri

- `WaterSimulation.AddWallSegment(...)` artık `WallSegmentHandle` döndürür.
- `TsunamiPhysicsSolver.SetOwnedBarrier/RemoveBarrierOwner/ClearBarrierOwners` statik ve çok-owner crest bileşimini yönetir.
- `WaterSimulation.RemoveWallSegment(handle)` yalnız ilgili owner'ı kaldırır; statik ve diğer owner katkıları yerinde kalır.
- `WaterSimulation.ClearWalls()` artık yalnız player-owner kayıtlarını temizler; solver'ın kalıcı `SetBarrier` baseline'larına dokunmaz. Bütün bariyerleri silmek yalnız düşük seviye `TsunamiPhysicsSolver.ClearBarriers()` işidir.
- `WaterSimulation.WallLoadSampled` segment toplamını solver adımı başına bir kez; `WallFaceImpactSampled` yönlü yüz temasını efekt için yayınlar. `WallRegistryInvalidated` solver yeniden yaratılınca adapter'ı modelden reconcile eder.
- `ImpactDetection.Find(...)` duvar/terrain ayrımı için `out bool isWall` aldı; tüm çağrılar güncellendi.
- `WallConstructionModel.Remove(edge)` eklendi.
- `WallConstructionController.TryGetWallInstance`, `TryDetachDestroyedWall` ve claim döndüren `WallRemoved` eklendi. En az bir subscriber detached görselin sahipliğini `true` ile almazsa controller görseli siler. Ekonomi akışı ve `WallBuilt` korunmuştur.

## Yapılan kontroller

- `dotnet run --project "Tests/TsunamiCollisionPure/TsunamiCollisionPure.csproj"` — **10/10 geçti**.
- `dotnet build "Tests/TsunamiCollisionPure/TsunamiCollisionPure.csproj" --no-restore` — **0 warning, 0 error**.
- `git diff --check` — geçti.
- Bağımsız reviewer/tester raporu: final review **PASS**; Roslyn syntax taraması `Assets` altındaki C# dosyalarında **63/63**; yeni `.meta` GUID benzersiz; diff check geçti.
- Roslyn sonucu yalnız syntax kontrolüdür. Unity assembly/type resolution, Unity API typecheck, serialization ve runtime davranışı doğrulaması değildir.
- Kapsam: yüksüz hasar yok, timestep parçalanmasından/FPS'ten bağımsız hasar, `dt=0` pause, tek kırılma; statik + iki örtüşen owner'ın iki kaldırma sırası; owner-only yüzün yalnız son owner'da açılması; owner'dan sonra yazılan yüksek statik crest; owner toplu temizliğinde statik koruma; eşit iki taraf hidro iptali + iki yön dinamik toplamı; ortak yüzde iki owner metadata teslimi görsel gate'den önce yapılırken burst dedup'u; wall-vs-terrain sınıflaması.
- Unity EditMode'a model remove/rebuild testlerine ek olarak subscriber varken enkaz sahipliği ve subscriber yokken detached görselin controller tarafından silinmesi testleri yazıldı; Unity olmadığı için çalıştırılmadı.
- Unity kurulu olmadığı için gerçek Unity compile/typecheck, EditMode ve PlayMode çalıştırılmadı.

## Gelecekteki tier-wall merge notu

Codex ileride bu çalışmayı tier'lı duvar branch'iyle birleştirirken `WallConstructionModel.Remove(WallEdge)` ve `WallConstructionController.TryDetachDestroyedWall(WallEdge, ...)` API'lerinin bu base'de **yalnız edge** bildiğini dikkate almalıdır. Bu taban yalnız `floor 0` duvar modeline sahiptir. Tier/floor kimliğini edge registry, instance lookup, removal claim event'i ve tsunami handle eşlemesine birlikte taşıyıp çakışmayı bilinçli çözün. Bu çalışma kapsamında başka branch'ten tier kodu alınmadı ve tier davranışı eklenmedi.

## Codex Play Mode kontrol listesi

- [ ] Düşük dalga: duvar suyu tutuyor/yönlendiriyor; kuru/durgun duvarda efekt ve hasar yok.
- [ ] Yüksek dalga: crest üstünden taşma var; duvar sonsuz set gibi davranmıyor.
- [ ] Güçlü normal akış: temas gerçek segment üzerinde, sıçrama yukarı/geriye; köpük ve hafif sis var, çift efekt yok.
- [ ] Kırılma: yalnız hasarlı segment kopuyor; görsel/statik collider/model edge/nümerik bariyer birlikte kalkıyor ve su boşluktan akıyor.
- [ ] Komşu sağlam segment yerinde ve bariyer olarak etkin kalıyor; kırık kenar normal maliyetle yeniden yapılabiliyor, iade oluşmuyor.
- [ ] Yatay, dikey ve iki çapraz yönde konum/normal/grid merdiveni kontrolü.
- [ ] Pause sırasında su, hasar, parçacık ve enkaz zamanı ilerlemiyor; resume aynı durumdan sürüyor.
- [ ] `ResetWater` hasarı koruyor. UI Reset/Restart yaşayan hasarı ve efekt/enkazı temizliyor; yıkık duvar geri gelmiyor.
- [ ] Uzun koşu: ParticleSystem sayıları 900/320/160 altında, enkaz ≤20 ve ~8 s sonra temiz; profiler'da normal frame allocation yok.
- [ ] Unity Console temiz; mevcut inşa maliyeti/kaynak atomik harcaması ve diğer EditMode testleri geçiyor.
