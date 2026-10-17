# Hazır Kaynaklar ve Yapay Zekâ Kullanımı

Bu projede ekip dışından alınan hazır kaynaklar ve yapay zekâ kullanımı aşağıda listelenmiştir (Civil Game Jam kural kitapçığı, madde 3.4, 3.6 ve 4.2).

## Hazır kaynaklar

### Görsel

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Resources 1 — saint11 | https://opengameart.org/content/resouces-pack-1 | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `Wood.png` ve `Stone.png`: sol alttaki kaynak panelinde odun ve taş ikonları (`Assets/UI/Icons/`). Değiştirilmedi. | Evet |
| UI Pack: RPG Expansion — Kenney | https://kenney.nl/assets/ui-pack-rpg-expansion | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `buttonLong_brown`, `buttonLong_brown_pressed`, `panelInset_beige`: kaynak panelindeki butonların çerçevesi ve ikon yuvası (`Assets/UI/Kenney/`). Değiştirilmedi, 9-slice ile ölçekleniyor. | Evet |
| UI Pack 2.0 — Kenney | https://kenney.nl/assets/ui-pack | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Ana menüdeki butonlar, panel, kaydırıcı ve ayraç (`Assets/UI/Menu/Kenney/`). Paketin gri parçaları, oyunun renk paletine (ahşap, kırmızı çatı, krem) göre yeniden renklendirildi (`Art/UI/adapt_kenney.py`). | Evet (yalnız değiştirilmiş parçalar) |
| LowPoly Trees Game-Ready Free-Asset — FinottiGames | Unity Asset Store | [Unity Asset Store Standard EULA](https://unity.com/legal/as-terms) | Ormandaki ağaç modelleri (`Assets/FinottiGames/`). Oyunda ölçekleniyor ve döndürülüyor. | Hayır, EULA ham dosyaların yeniden dağıtımına izin vermiyor. Kurulum aşağıda. |

### Yazı tipi

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Baloo 2 (ExtraBold) — Ek Type | https://fonts.google.com/specimen/Baloo+2 | [SIL Open Font License 1.1](https://openfontlicense.org) (`Assets/UI/Fonts/Baloo2-OFL.txt`) | Ana menünün yazı tipi (`Assets/UI/Fonts/Baloo2-ExtraBold.ttf`). Değiştirilmedi. | Evet |

### Ses

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| UI Pack 2.0 — Kenney | https://kenney.nl/assets/ui-pack | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `click-a.ogg`: menü butonlarının tıklama sesi (`Assets/Audio/UI/Click.ogg`). Değiştirilmedi. | Evet |

### Kod

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Universal Render Pipeline 17.6.0 ve Render Pipelines Core 17.6.0 — Unity Technologies | Unity 6000.6.6f1 ile gelen paketler | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) | `Packages/` içine gömüldü. Unity 6000.6.6f1'deki bir derleme hatası için 4 eksik `.meta` dosyası eklendi, 2 `.meta` dosyası düzeltildi. | Evet |

Metin ve diğer kategorilerde hazır kaynak kullanılmadı.

### Repoda olmayan dosyaları kurma

1. Unity Asset Store'da **LowPoly Trees Game-Ready Free-Asset** (FinottiGames) paketini hesabınıza ekleyin (ücretsiz).
2. Unity'de **Window > Package Manager > My Assets** içinde paketi bulun, **Download**, sonra **Import** deyin.
3. Paket `Assets/FinottiGames/` altına gelir, sahnedeki ağaçlar kendiliğinden görünür.

## Yapay zekâ kullanımı

| Kategori | Kullanım | Araç | Amaç ve kullanım yeri |
| --- | --- | --- | --- |
| Kod | Kullanıldı | Claude (Anthropic) | `Assets/Scripts/` altındaki kaynak toplama, kuyu, kamera, tıklama, arayüz, ana menü, kamera giriş geçişi ve ayar scriptleri; sahne kurulum (editör) scriptleri; Unity paket derleme hatasının çözümü. |
| Kod | Kullanıldı | Claude (Anthropic) | `Assets/Tsunami/` altındaki tsunami simülasyonu: sonlu hacim sığ su çözücüsü (`TsunamiPhysicsSolver`), batimetri, su/deniz tabanı/sprey shader'ları, su yüzeyi ve okyanus çizimi, köpük ve çarpma sıçraması efektleri, oyun içi tsunami paneli ve sahne kurulum (editör) scriptleri. Çözücü Unity dışında sayısal testlerle (durgun göl, dalga hızı √(g·h), kütle korunumu, baraj yıkılması) doğrulandı. |
| Kod | Kullanıldı | Codex (OpenAI) | Köylülerin yürüme/odun kesme animasyonu, mevcut ekip modelinin kemik ağırlıkları, animasyonlu oduncu prefab kurulumu, SampleScene aktarımı ve Unity entegrasyon testleri. |
| Kod ve metin | Kullanıldı | OpenCode (OpenAI) | Tsunami ile oyuncu duvarı arasındaki çözücü-yüzü çarpma örneklemesi, zaman adımlı dayanıklılık/kırılma, sınırlı sıçrama-köpük-sis ve enkaz entegrasyonu; saf C# testleri ve devir dokümantasyonu. Yeni dış kaynak veya hazır asset kullanılmadı. |
| Kod ve metin | Kullanıldı | Codex (OpenAI) | Collision kodunun Unity 6000.6.6f1 entegrasyonu; mevcut SampleScene bağlantılarının ve yeni dayanıklılık/efekt ayarlarının kaydedilmesi; editör kurulum ve çalışma zamanı kontrol aracı, testler ve entegrasyon notları. Daha önce beyan edilen FinottiGames ağaç paketi yerel önbellekten kuruldu; yeni dış kaynak eklenmedi. |
| Görsel | Kullanıldı | Claude (Anthropic) + Blender 4.2 | Maden ocağı ve maden vagonu 3B modelleri (`Assets/Meshes/Quarry.fbx`, `Assets/Meshes/MineCart.fbx`): Claude, modelleri Blender Python scriptleriyle (`Art/Blender/`) kurdu; ekibin `palette.png` paleti ve kuyu stili kullanıldı. Ana menünün ikonları, gölgesi, kenar karartması ve dalga amblemi (`Assets/UI/Menu/`) Claude'un yazdığı Python scriptiyle (`Art/UI/make_menu_sprites.py`) çizildi; Kenney parçalarını yeniden renklendiren script (`Art/UI/adapt_kenney.py`) de Claude'a aittir. |
| Ses | Kullanılmadı | — | — |
| Metin | Kullanıldı | Claude (Anthropic) | Bu beyan dosyası, menü metinleri ve kod içi açıklamalar. |
| Diğer | Kullanılmadı | — | — |
