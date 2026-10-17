# Hazır Kaynak ve AI Beyanı

Kural kitapçığı madde 3.4, 3.6 ve 4.2 için tutulan liste. Teslim formundaki "Hazır kaynaklar" ve "AI" alanları buradan doldurulur. Projeye yeni bir hazır kaynak eklenince buraya da eklenmeli.

## Hazır kaynaklar

### Görsel

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Resources 1 — saint11 | https://opengameart.org/content/resouces-pack-1 | CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/) | `Wood.png` ve `Stone.png`, sol alttaki kaynak panelinde ikon olarak (`Assets/UI/Icons/`). Değiştirilmedi. | Evet |
| UI Pack: RPG Expansion — Kenney (kenney.nl) | https://kenney.nl/assets/ui-pack-rpg-expansion | CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/) | `buttonLong_brown`, `buttonLong_brown_pressed` ve `panelInset_beige`: sol alttaki odun/taş butonlarının çerçevesi ve ikon yuvası (`Assets/UI/Kenney/`). Değiştirilmedi, 9-slice ile ölçekleniyor. | Evet |
| LowPoly Trees Game-Ready Free-Asset — FinottiGames | Unity Asset Store (Package Manager > My Assets) | Unity Asset Store Standard EULA (https://unity.com/legal/as-terms) | Ormandaki ağaç modelleri (`Assets/FinottiGames/`). Oyunda yeniden ölçekleniyor ve döndürülüyor. | **Hayır**, EULA ham dosyaların yeniden dağıtımına izin vermiyor. |

### Kod

| Kaynak ve sağlayıcı | Bağlantı | Lisans | Kullanım ve değişiklik | Repoda mı? |
| --- | --- | --- | --- | --- |
| Universal Render Pipeline 17.6.0 ve Render Pipelines Core 17.6.0 — Unity Technologies | Unity 6000.6.6f1 ile gelen paketler | Unity Companion License (https://unity.com/legal/licenses/unity-companion-license) | `Packages/` içine gömüldü. Unity 6000.6.6f1'deki derleme hatası için 4 eksik `.meta` dosyası eklendi ve 2 `.meta` dosyası düzeltildi. | Evet |

Ses, metin, diğer: kullanılmadı (şimdilik).

### Repoda olmayan dosyaları kurma

1. Unity Asset Store'da **LowPoly Trees Game-Ready Free-Asset** (FinottiGames) paketini hesabınıza ekleyin.
2. Unity'de **Window > Package Manager > My Assets** içinde paketi bulun, **Download**, sonra **Import** deyin.
3. Paket `Assets/FinottiGames/` altına gelir ve sahnedeki ağaçlar kendiliğinden görünür.

## AI kullanımı

| Kategori | Kullanım | Araç | Amaç ve kullanım yeri |
| --- | --- | --- | --- |
| Kod | Kullanıldı | Claude (Anthropic) | `Assets/Scripts/` altındaki kaynak toplama, kuyu, kamera, tıklama ve arayüz scriptleri; ana menü, kamera giriş geçişi ve ayarlar; test sahnesi kurulum scripti; Unity paket derleme hatasının çözümü. |
| Görsel | Kullanıldı | Claude (Anthropic) + Blender 4.2 | Maden ocağı ve maden vagonu 3B modelleri (`Assets/Meshes/Quarry.fbx`, `Assets/Meshes/MineCart.fbx`): Claude, modelleri Blender Python scriptleriyle (`Art/Blender/quarry.py`, `minecart.py`) kurdu; ekibin `palette.png` paleti ve kuyu stili kullanıldı. İlk sürümdeki AI tarafından çizilen ikonlar ise saint11 CC0 ikonlarıyla değiştirildi. Ana menünün sprite'ları (yuvarlak panel, gölge, oyna/ayarlar/kupa/kitap/çıkış ikonları, dalga amblemi; `Assets/UI/Menu/`) Claude'un yazdığı bir Python scriptiyle (`Art/UI/make_menu_sprites.py`) çizildi. |
| Ses | Kullanılmadı | — | — |
| Metin | Kullanıldı | Claude (Anthropic) | Bu beyan dosyası ve kod içi açıklamalar. |
| Diğer | Kullanılmadı | — | — |
