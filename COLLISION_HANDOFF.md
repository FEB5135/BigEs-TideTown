# Tsunami–Duvar Çarpışması

## Güncel durum — 17 Ekim 2026

`collision`, `origin/ContructionMechanics` dalındaki katlı ve malzeme seviyeli inşa sistemiyle birleştirildi. Gerçek oyun sahnesi `Assets/Scenes/SampleScene.unity`; Unity 6000.6.6f1, URP 17.6, yeni Input System.

## Hasar ve parçalanma

- Duvar dayanıklılığı su çözücüsünün adımında hesaplanır; normal giriş hızı ve su yükü kullanılır. Render FPS'inden bağımsızdır, pause sırasında ilerlemez.
- Çok yüksek kuvvet bile tek normal solver adımında sağlam duvarı yok etmez. `minimumFailureSeconds` varsayılanı 3 simülasyon saniyesi; adım başına toplam hasar `durability * dt / minimumFailureSeconds` ile sınırlanır. Bu süre güçlü ve sürekli yük altında en erken kırılma süresidir; her duvarın üç saniyede kırılacağı anlamına gelmez.
- %15 hasarda çatlaklı parça görünümü başlar. %40 ve %70 seviyelerinde aralıklar ve parça açıları artar. Sağlam segmentin collider'ı ve su engeli yapısal kırılmaya kadar korunur.
- Dayanıklılık bitince tek parça duvarın devrilmesi yerine 12 küçük kutu/parça ayrılır; her parçanın güvenli BoxCollider ve Rigidbody'si vardır. Mevcut malzeme paylaşılır; yeni asset, texture veya her darbede mesh/material üretilmez. Bu, prefab için özgün topoloji kırma değil stilize blok parçalanmasıdır.
- Aynı anda en çok 20 fiziksel parça; ömür 8 s. Limit aşımında en eski parça hemen pasifleştirilir ve silinir. Pause hareketi ve ömrü dondurur.
- Ahşap temel dayanıklılık 100 HP, taş 200 HP, güçlendirilmiş 350 HP. Eşik ve HP dönüşümleri oyun ayarıdır; gerçek yapı mühendisliği modeli değildir.
- Darbe eşiği 6 kN, darbe hasarı 0.32 HP/(kN·s); sürekli yük eşiği 12 kN, sürekli hasar 0.055 HP/(kN·s). Değerler Inspector'dan ayarlanabilir.

## Katlar ve yükseltmeler

- Kayıt anahtarı `(WallEdge, floor)`; her katın kendi hasarı, instance'ı ve su handle'ı var. Adapter `WallTierBuilt/WallTierRemoved` olaylarını kullanır.
- Yükseltme eski su owner'ını kaldırır; yeni owner kaydedilir. Yeni malzemeli segment hasarsız kurulur. Aynı kayıt tekrarlanarak görünmez bariyer bırakılamaz.
- Yük, o katın gerçek alt/üst yüksekliği aralığına göre örneklenir. Su üst kata ulaşmadığında o kat hasar almaz.
- Üst kat kırılırsa alt katlar korunur. Alt destek kırılırsa aynı kenarın desteksiz kalan üst katları yukarıdan aşağıya kaldırılır ve parçalanır. Komşu kenarlar korunur.
- Kırılma model kaydını, orijinal collider/görseli ve su bariyeri owner'ını birlikte kaldırır. Kırık kenar normal maliyetle yeniden kurulabilir; kaynak iadesi yoktur.
- Su bariyeri mevcut sığ su çözücüsünde eşik yüksekliğiyle temsil edilir; tepeden taşma korunur. Çapraz segmentin sayısal izi mevcut iki yüzlü L yaklaşımıdır.
- Legacy `Edges`, `WallBuilt`, `WallRemoved`, `TryGetWallInstance(edge, out ...)` ve `TryDetachDestroyedWall(edge, out ...)` taban kat uyumluluğu için korunur. Yeni katlı lookup/removal overload'ları `floor` alır. İnşa maliyet/atomik harcama mekanikleri korunur.

## Reset ve yaşam döngüsü

- `WaterSimulation.ResetWater`: suyu sıfırlar, yaşayan duvarların hasarı ve çatlak görünümü korunur.
- `TsunamiController.ResetSimulation/Restart`: yaşayan duvarların hasarı/çatlak görünümü temizlenir; enkaz ve parçacıklar temizlenir. Yıkılan duvarlar kendiliğinden dirilmez.
- Adapter re-enable/solver rebuild duvar modelinden kayıtları yeniden kurar. Hasarlı duvarın sınırları çatlak parçalarından değil orijinal Renderer'lardan okunur.
- Hasar ve efekt olayları solver işi tamamlandıktan sonra ana thread'de çalışır. Maliyet O(segmentin dokunduğu yüz); duvar × bütün hücre taraması yoktur.

## Unity kurulumu

- SampleScene hazırdır: yeni malzeme maliyetleri, üç kat sınırı, hasar/enkaz/köpük ayarları kaydedildi.
- `Tsunami > Collision > Validate and Save Team Map`: mevcut sahneyi yeniden oluşturmadan World, inşa, su, efekt referanslarını doğrular/kaydeder.
- Tsunami paneli `MainMenu.gameplayUI` CanvasGroup listesine bağlıdır; menü açıkken gizlidir, kamera giriş geçişi bitince açılır.
- FinottiGames ağaç paketi yerel bağımlılıktır. Bu bilgisayarda önceden indirilmiş önbellekten kuruldu; `Assets/FinottiGames` ve meta Git dışında kalır. Başka bilgisayarda kurulum için THIRD_PARTY.md'ye bakın.

## Doğrulama

- Unity 6000.6.6f1 gerçek C# derlemesi: başarılı.
- EditMode: **52/52 geçti**, 0 hata. Yeni testler üst katın kırılmasında alt/komşuyu koruma, desteği kırılan katların temizlenmesi ve yeniden inşayı kapsar.
- TsunamiCollisionPure: **10/10 geçti**; güçlü tek adımın anında yıkmaması da kontrol edilir.
- WallTierRules saf testleri: **5/5 geçti**.
- Gerçek SampleScene'de kontrollü Play Mode: çatlakların kırılmadan önce görünmesi, gerçek yük örnekleme → hasar → fiziksel parçalanma → su bariyerinin kaldırılması → yeniden inşa; üç malzeme, kat kaydı, yükseltmede owner sızmaması, kuru üst katın korunması, destek çökmesi, komşu koruma, su resetinin hasarı koruması ve tam resetin görünümü onarması; pause sırasında parçaların kinematik olması: geçti.
- Kontrollü test su hücresine deterministik hız/derinlik yazar; tam dalga yayılımı testi değildir. Test kaynakları yalnız kaydedilmeyen Play Mode durumundadır.
- Önceki grafik açık Play kontrolünde gerçek 10 m tsunami, kasabada su/sıçrama ve evlerin su basma/yıkılma durumu görüldü. Yeni parçalanmanın grafik açık uzun süreli dalga/duvar denge ve performans ayarı ayrıca yapılabilir.
- Kanıtlar proje üst klasöründe: `collision-fracture-runtime.log`, `collision-fracture-editmode.xml`, `collision-fracture-editmode.log`. Yerel .NET 10 kullanıldığından saf testlerin hedefi komutta net10.0 olarak override edilir; csproj dosyaları değiştirilmedi.
- Mevcut DestroyObject CS0108 uyarısı ve Unity Cloud Project ID 403 bağlantı uyarısı collision kaynaklı değildir.

## Değişiklikler

İnşa controller/model ve testleri, WallSegment/WallTierRules, WaterSimulation kat yüksekliği örneklemesi, TeamMapTsunamiAdapter kat/malzeme/enkaz entegrasyonu, WallDamageModel hasar sınırı, yeni WallDamageVisual, CollisionIntegrationSetup ve SampleScene bağlantıları. Kaynakça ve AI kaydı THIRD_PARTY.md içinde.
