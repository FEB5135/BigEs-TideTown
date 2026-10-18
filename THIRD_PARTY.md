# Hazır Kaynaklar ve Yapay Zekâ Kullanımı

Bu projede ekip dışından alınan hazır kaynaklar ve yapay zekâ kullanımı aşağıda listelenmiştir (Civil Game Jam kural kitapçığı, madde 3.4, 3.6 ve 4.2). Repodaki tüm aktif dallar (`main`, `music`, `feature/storm-warning`, `feature/upgrades`, `welcomeparty`, `Wallfix`, `collision`, `Terrain-1`, `ContructionMechanics`) taranarak belgelenmiştir.

---

## 1. Hazır Kaynaklar (Third-Party Assets)

### Görsel

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Resources 1 — saint11 | https://opengameart.org/content/resouces-pack-1 | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `Wood.png` ve `Stone.png`: sol alttaki kaynak panelinde odun ve taş ikonları (`Assets/UI/Icons/`). Değiştirilmedi. | Evet |
| UI Pack: RPG Expansion — Kenney | https://kenney.nl/assets/ui-pack-rpg-expansion | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `buttonLong_brown`, `buttonLong_brown_pressed`, `panelInset_beige`: kaynak panelindeki butonların çerçevesi ve ikon yuvası (`Assets/UI/Kenney/`). Değiştirilmedi, 9-slice ile ölçekleniyor. | Evet |
| UI Pack 2.0 — Kenney | https://kenney.nl/assets/ui-pack | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Ana menünün ilk sürümündeki butonlar, panel, kaydırıcı ve ayraç (`Assets/UI/Menu/Kenney/`; artık yalnız `ResourceTest` test sahnesindeki eski menüde). Paketin gri parçaları, oyunun renk paletine (ahşap, kırmızı çatı, krem) göre yeniden renklendirildi (`Art/UI/adapt_kenney.py`). | Evet (yalnız değiştirilmiş parçalar) |
| LowPoly Trees Game-Ready Free-Asset — FinottiGames | Unity Asset Store | [Unity Asset Store Standard EULA](https://unity.com/legal/as-terms) | Ormandaki ağaç modelleri (`Assets/FinottiGames/`). Oyunda ölçekleniyor ve döndürülüyor. | Hayır, EULA ham dosyaların yeniden dağıtımına izin vermiyor. Kurulum aşağıda. |

> **Not (Ekip Tarafından Üretilen Görseller - Hazır Değil):** `Assets/Meshes/WorldMeshes/` altındaki arazi karoları (`ground_*.obj`, `slope_*.obj`, `cornerslope_*.obj`, `tree_*.obj`, `sand*.obj`), ev modelleri (`house_0_0` - `house_3.obj`), kuyu (`well.obj`), köylü ana gövdesi (`villager.obj`), renk paleti (`palette.png`), yeni kaynak ikonları (`log.png`, `stone 1.png`) ve yeni imleçler (`cursor_default.png`, `cursor_open.png`, `cursor_clenched.png`) ekip üyesi (Eren) tarafından bizzat modellenmiş ve çizilmiştir; hazır kaynak değildir.

### Yazı Tipi

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Rubik (Bold, Black) — The Rubik Project Authors | https://fonts.google.com/specimen/Rubik | [SIL Open Font License 1.1](https://openfontlicense.org) (`Assets/UI/Fonts/Rubik-OFL.txt`) | Ana menünün yazı tipi: yazılar Bold, logo Black (`Assets/UI/Fonts/Rubik-Bold.ttf`, `Rubik-Black.ttf`). Değiştirilmedi. | Evet |
| Baloo 2 (ExtraBold) — Ek Type | https://fonts.google.com/specimen/Baloo+2 | [SIL Open Font License 1.1](https://openfontlicense.org) (`Assets/UI/Fonts/Baloo2-OFL.txt`) | Ana menünün ilk sürümünün yazı tipi (`Assets/UI/Fonts/Baloo2-ExtraBold.ttf`); artık yalnız `ResourceTest` test sahnesindeki eski menüde. Değiştirilmedi. | Evet |

### Müzik

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Townie Loop — Kevin MacLeod (Incompetech) | https://incompetech.com/music/royalty-free/index.php?search=Townie+Loop | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Köy kurma, odun/taş toplama ve duvar örme (Idle & Build) fazı fon müziği (`Assets/Audio/Music/Townie_Loop.mp3`). Değiştirilmedi. | Evet |
| Volatile Reaction — Kevin MacLeod (Incompetech) | https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400039 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Alternatif yüksek tempolu savunma / aksiyon müziği (`Assets/Audio/Music/Volatile_Reaction.mp3`). Değiştirilmedi. | Evet |

### Ses Efektleri (SFX)

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| UI Pack 2.0 — Kenney | https://kenney.nl/assets/ui-pack | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `click-a.ogg`: menü butonlarının tıklama sesi (`Assets/Audio/UI/Click.ogg`). Değiştirilmedi. | Evet |
| Heavy Rock Rolling — Dragon Studio (Pixabay) | https://pixabay.com/sound-effects/film-special-effects-heavy-rock-rolling-515254/ | [Pixabay Content License](https://pixabay.com/service/license-summary/) (ücretsiz, atıf zorunlu değil; tek başına dağıtımı yasak, ürün parçası olarak serbest) | Maden ocağına vurunca çıkan taş sesi (`Assets/Audio/SFX/RockBreak.mp3`). Ekip üyesi kaydın yaklaşık 0.6 sn'lik bir bölümünü Audacity ile kesti. | Evet (oyunun parçası olarak) |
| RPG Audio — Kenney | https://kenney.nl/assets/rpg-audio | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) (`Assets/Audio/SFX/Kenney-RPG-Audio-License.txt`) | `creak1-3.ogg` (ağacın kırılması) (`Assets/Audio/SFX/`). Değiştirilmedi; oyunda perdesi rastgele hafifçe değiştirilerek çalınır. | Evet |
| Impact Sounds — Kenney | https://kenney.nl/assets/impact-sounds | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) (`Assets/Audio/SFX/Kenney-Impact-Sounds-License.txt`) | `impactWood_medium_000` (ağaca vurma), `impactWood_heavy_000-002` (ağacın yere düşmesi), `impactMetal_light_000-002` (vagonun kalkış/duruş tıkırtısı) (`Assets/Audio/SFX/`). Değiştirilmedi. | Evet |
| CrunchingWaterbottle.wav - Jedimichael | https://freesound.org/people/Jedimichael/sounds/491213/ | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
### Kod ve Paketler

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Universal Render Pipeline 17.6.0 ve Render Pipelines Core 17.6.0 — Unity Technologies | Unity 6000.6.6f1 paketleri | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) | `Packages/` içine gömüldü. Unity 6000.6.6f1'deki bir derleme hatası için 4 eksik `.meta` dosyası eklendi, 2 `.meta` dosyası düzeltildi. | Evet |
| Input System 1.20.0 — Unity Technologies | Unity Package Manager | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) | Yeni girdi sistemi entegrasyonu. | Evet (manifest) |
| ProBuilder 6.1.3 — Unity Technologies | Unity Package Manager | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) | Sahne düzenleme ve prototipleme aracı. | Evet (manifest) |

---

### Repoda Olmayan Dosyaları Kurma

1. Unity Asset Store'da **LowPoly Trees Game-Ready Free-Asset** (FinottiGames) paketini hesabınıza ekleyin (ücretsiz).
2. Unity'de **Window > Package Manager > My Assets** içinde paketi bulun, **Download**, sonra **Import** deyin.
3. Paket `Assets/FinottiGames/` altına gelir, sahnedeki ağaçlar kendiliğinden görünür.

---

## 2. Yapay Zekâ Kullanımı (AI Usage Declaration)

Aşağıdaki tabloda projenin geliştirilmesinde kullanılan yapay zekâ araçları, kullanım kategorileri ve kullanım amaçları açıkça belirtilmiştir:

| Kategori | AI Kullanımı | Kullanılan Araç | Amaç ve Kullanım Yeri |
| --- | --- | --- | --- |
| **Kod (Müzik Sistemi)** | **Kullanıldı** | Gemini / Antigravity (Google DeepMind) | `Assets/Scripts/Audio/AudioManager.cs`: Müzik ve SFX yönetimini sağlayan Singleton ses yöneticisi; iki kanallı AudioSource mimarisi ile yumuşak geçiş (crossfade), oyun fazları (`IdleBuilding`, `TsunamiDefense`, `Victory`, `GameOver`), editörde otomatik AudioClip bağlama (Auto-assign) ve Input System kısayolları. `Assets/Audio/CREDITS.md` lisans belgelemesi ve `music` dalının `main`'e entegrasyonu. |
| **Kod (Oynanış & Menü)** | **Kullanıldı** | Claude (Anthropic) | `Assets/Scripts/` altındaki kaynak toplama, kuyu, kamera kontrolleri, tıklama mekanikleri, arayüz, ana menü (`MainMenu.cs`), kamera giriş geçişi (`CameraIntro.cs`), oyun başı ipucu kartları (tutorial, `TutorialHints.cs`; adım sırasında zamanı durdurma, ekranı karartma ve tıklanacak yerleri gösteren halkalar dahil) ve ayarlar; tsunami sonrası kazanma/kaybetme kartı (`Assets/Tsunami/Scripts/Gameplay/GameOutcome.cs`); sahne kurulum (editör) scriptleri (`SampleSceneSetup.cs`); Unity URP derleme hatasının çözümü. |
| **Kod (Tsunami Simülasyonu)** | **Kullanıldı** | Claude (Anthropic) | `Assets/Tsunami/` altındaki tsunami simülasyonu: sonlu hacim sığ su çözücüsü (`TsunamiPhysicsSolver`), batimetri profili (`BathymetryProfile.cs`), su/deniz tabanı/sprey shader'ları (`StylizedWater.shader`), su yüzeyi ve okyanus çizimi, köpük ve çarpma sıçraması efektleri (`ImpactSplashEmitter.cs`), oyun içi tsunami paneli ve sahne kurulum scriptleri. Çözücü Unity dışında sayısal testlerle doğrulandı. |
| **Kod ve Görsel (Fırtına & Uyarı)** | **Kullanıldı** | Claude (Anthropic) | Tsunami öncesi fırtına (`Assets/Tsunami/Scripts/Rendering/TsunamiStorm.cs`): kodla üretilen low-poly bulutlar, şimşekler ve ışığın kararması; açık denizden gelen dalga kabarması (`TsunamiSwell.cs`); tsunaminin oyun başladıktan 2-3 dakika sonra kendiliğinden gelmesi ve paneldeki "Anında Etkinleştir" butonu. Hazır model, doku veya ses kullanılmadı. |
| **Kod (Yapı Yükseltmeleri)** | **Kullanıldı** | Claude (Anthropic) | `feature/upgrades` dalındaki kuyu ve maden seviye yükseltmeleri (`UpgradeLevel.cs`), yükseltme arayüzü HUD'ı (`UpgradeHUD.cs`) ve kaynak karşılığı işçi üretme mekaniği. |
| **Kod (Harita Üretimi)** | **Kullanıldı** | Claude (Anthropic) | `Assets/Scripts/Map/WfcSolver.cs` içindeki ekleme ve değişiklikler: boyanmış hücrelere göre kıyı/kumsal bölgesi, kum çukurlarının doldurulması, köy (ev) bölgesi ve dağınıklığı, rastgele düz alanlar, kuyu çevresinde ağaçsız alan, çakışan orman sabitlemelerinde yeniden çözme; `GridForest` uyarlaması; işçilerin NavMesh üstünde kalması ve zemin yüksekliği (`Gatherer`, `NavigationBaker`). WFC çözücüsünün kendisi ve karo kuralları ekibe aittir. |
| **Kod (Animasyon & Köylü)** | **Kullanıldı** | Codex (OpenAI) | Köylülerin yürüme ve odun kesme animasyonu (`VillagerAnimation.cs`), mevcut ekip modelinin kemik ağırlıkları, animasyonlu oduncu prefab kurulumu, SampleScene aktarımı ve Unity entegrasyon testleri. |
| **Kod ve Metin (Rehber Turu)** | **Kullanıldı** | Codex (OpenAI) | `welcomeparty` dalında haritadaki orman, maden ve kuyu odak noktalarını kamerayla tanıtan karşılama rehberi (`WelcomeParty.cs`, `WelcomeMapTour.cs`), oyun başlangıç entegrasyonu, Türkçe karşılama metinleri ve Unity EditMode testleri. |
| **Kod ve Metin (Duvar Çarpışması)** | **Kullanıldı** | OpenCode & Codex (OpenAI) | Tsunami ile oyuncu duvarı arasındaki çözücü-yüzü çarpma örneklemesi, zaman adımlı dayanıklılık/kırılma fiziği, sınırlı sıçrama-köpük-sis ve enkaz entegrasyonu; katlı/malzeme seviyeli inşa ile tsunami çarpışmasının birleştirilmesi; aşamalı hasar ve çatlak görünümü, testler ve devir dokümantasyonu (`COLLISION_HANDOFF.md`). |
| **Kod (Duvar ve Eğim Geometrisi)** | **Kullanıldı** | Codex / Gemini / Claude | `Wallfix` dalındaki arazi eğimini takip eden duvar panelleri (`WallSlopeGeometry.cs`), sızdırmazlık kontrolleri (`WallWaterSealChecks.cs`, `WallBarrierRasterizer.cs`), kat yüksekliği sabitlemesi (2 m kat / 20 m sınır), `WallArt.cs` ile kodla üretilen duvar parçaları (`Joint.asset`, `Timber`, `Stone`, `Reinforced`) ve atölye regresyon testleri (`WALLFIX.md`). |
| **Görsel (Maden Modelleri)** | **Kullanıldı** | Claude (Anthropic) + Blender 4.2 | Maden ocağı ve maden vagonu 3B modelleri (`Assets/Meshes/Quarry.fbx`, `Assets/Meshes/MineCart.fbx`): Claude modelleri Blender Python scriptleriyle (`Art/Blender/`) kurdu; ekibin `palette.png` paleti ve kuyu stili kullanıldı. |
| **Görsel (Arayüz Sprite'ları)** | **Kullanıldı** | Claude (Anthropic) | Ana menünün ikonları, gölgesi, kenar karartması, dalga amblemi, butonları (kesik köşeli, alt kalınlıklı), paneli ve kaydırıcı parçaları (`Assets/UI/Menu/`) Claude'un yazdığı Python scriptiyle (`Art/UI/make_menu_sprites.py`) çizildi; Kenney parçalarını yeniden renklendiren script (`Art/UI/adapt_kenney.py`) de Claude'a aittir. |
| **Ses (Sentez Efekt)** | **Kullanıldı** | Claude (Anthropic) | Maden vagonunun ray üstünde yuvarlanma sesi (`Assets/Audio/SFX/CartRoll.wav`): Claude'un yazdığı Python scriptiyle (`Art/Audio/make_cart_roll.py`) gürültü ve sinüs dalgalarından sentezlendi; hazır kayıt kullanılmadı. |
| **Metin (Dokümantasyon)** | **Kullanıldı** | Claude (Anthropic), Codex (OpenAI), Gemini (Google DeepMind) | `THIRD_PARTY.md`, `PROGRESS.md`, `COLLISION_HANDOFF.md`, `WALLFIX.md`, `Assets/Audio/CREDITS.md` beyan ve devir belgeleri, oyun içi menü ve tutorial ipucu metinleri ve kod içi açıklamalar. |
| **Görsel & 3B Modeller (Harita, Evler, Karolar)** | **KULLANILMADI** | — | `Assets/Meshes/WorldMeshes/` altındaki tüm dünya karoları (`ground_*.obj`, `slope_*.obj`, `cornerslope_*.obj`, `tree_*.obj`, `sand*.obj`), ev modelleri (`house_0_0.obj` - `house_3.obj`) ve tsunamide yıkılan evlerin batık sürümleri (`house_*f.obj`), kuyu (`well.obj`), köylü ham modeli (`villager.obj`), renk paleti (`palette.png`), yeni kaynak ikonları (`log.png`, `stone 1.png`) ve yeni imleç dokuları (`cursor_default.png`, `cursor_open.png`, `cursor_clenched.png`) ekip üyeleri (Eren ve ekip) tarafından bizzat modellenmiş ve çizilmiştir. Yapay zekâ **kullanılmamıştır**. |
| **Müzik (Fon Müzikleri)** | **KULLANILMADI** | — | Oyunda yer alan müzik eserleri (*Glorious Morning*, *Townie Loop*, *Volatile Reaction*) insan besteciler (Waterflame, Kevin MacLeod) tarafından bestelenmiştir. Yapay zekâ müzik üretim araçları (Suno, Udio vb.) **kullanılmamıştır**. |
| **Diğer** | **Kullanılmadı** | — | Başka bir alanda yapay zekâ kullanılmamıştır. |

Tutorial güncellemesi (18 Ekim 2026): adımlarda zamanı durdurma, ekran karartma ve hedef halkaları Claude (Anthropic) desteğiyle kodlandı; yeni dış görsel kullanılmadı.
