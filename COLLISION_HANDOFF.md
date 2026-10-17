# Tsunami–Duvar Çarpışması Devir Notu

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
