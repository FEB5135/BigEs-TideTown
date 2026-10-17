# Wallfix

`main` dalındaki `aa8da56` sürümünden oluşturuldu.

## Kullanım

- Sol tık: başlangıç ve bitiş düğümü.
- Enter veya İnşa Et: geçerli ön izlemeyi onayla.
- Sağ tık / Esc: iptal.
- **↑ / ↓: katı yükselt / alçalt.** PageUp / PageDown kaldırıldı.
- 1 / 2 / 3: ahşap / taş / güçlendirilmiş duvar.
- B: inşa ile toplama arasında geçiş.

Geçerli yol yeşil; çakışma, yetersiz destek veya yetersiz kaynak kırmızı görünür. Ön izleme duvar panellerini ve köşe direklerini hacimli, yarı saydam olarak gösterir. Ön izleme hareketi kısa bir geçişle yumuşar; kurallar ve onay her zaman gerçek grid düğümlerine göre hesaplanır. Kalıcı duvarın yalnız görseli 0,2 saniyede yerine oturur; fiziksel engel hemen oluşur.

## Duvar varlıkları

Bu çalışma için özgün olarak üretildi; dışarıdan görsel veya model indirilmedi.

`Assets/Art/Walls` içinde:

- `Timber.prefab`: kademeli ahşap tahtalar, koyu destek kuşakları.
- `Stone.prefab`: şaşırtmalı, pahlanmış taş örgü ve üst kapak.
- `Reinforced.prefab`: koyu taş örgü ve metal takviye kuşakları.
- Her seviye için düzenlenebilir Mesh `.asset` ve üç malzeme `.mat`.
- `Joint.asset`: ortak köşe direği.

Varsayılan inşa, bu tasarımları `WallArt` üzerinden grid uzunluğuna uyarlar. Her düğüm ve katta tek bir ortak direk oluşturulur. Paneller direğin sınırında biter; düz, çapraz ve köşe birleşimlerinde üst üste binen paneller oluşturulmaz. Yükseltme eski duvarı değiştirir, ikinci bir duvar eklemez. Eski özel `wallPrefab` desteği korunur.

Varlıklar ilk derlemede oluşturulur. Menüden yeniden erişim: **Tools → Idle Town → Wallfix → Export Wall Assets**. Var olan varlıklar üzerine yazılmaz.

## Çakışma

Katı 3D collider'lar bütün katmanlarda kontrol edilir; `Obstacle Layers = Nothing` artık çakışma kontrolünü kapatmaz. Seçilen katın yüksekliği, duvar ve köşe direklerinin kapladığı alan kontrol edilir. Zeminle temas için küçük bir dikey tolerans bulunur. Aynı kat/kenarda tekrar inşa ve aynı hücre içindeki çapraz kesişimler model tarafından reddedilir. Yükseltme, komşu bağlantı ve alt kat desteği korunur. Başka bir inşa kontrolcüsünün duvarı da engel sayılır.

Toplama ve seçim trigger'ları varsayılan olarak engel değildir. `Obstacle Layers`, ayrıca engel sayılacak trigger katmanlarını seçer. Yeni yapı prefab'larında fiziksel hacmi temsil eden bir collider bulunmalıdır.

Yıkılan bir duvarın eski sabit collider'ı, görseli enkaz için tutulsa bile hemen devre dışı bırakılır. Böylece aynı yere yeniden inşa edilebilir.

## Deneme sahnesi ve doğrulama

**Tools → Idle Town → Wallfix → Open Workshop** veya `Assets/Scenes/WallfixWorkshop.unity`.

Sahnede 300 odun / 200 taş, normal katmanda katı collider'lı bir yapı ve sandıklar var.

**Tools → Idle Town → Wallfix → Run Wall Tests**: son koşuda **47 geçti, 0 başarısız, 0 atlandı**. Sonuç dosyası: `Logs/Wallfix-tests.xml`.

Mevcut duvar modeli, depo, yükseltme, kat desteği ve yıkım testlerine ek olarak yeni testler şunları doğrular:

- Varsayılan katmandaki yapı ve çok alçak temel inşayı kaynak harcamadan engeller.
- Başka bir kontrolcünün duvarının içine inşa edilemez.
- Köşe bağlantısı tek direk paylaşır; yükseltme kopya parça oluşturmaz.
- Engel sonradan oluşursa onay reddedilir ve gerçek malzeme rengi kırmızı olur.
- Kaynak kaybı ön izlemeyi kırmızı yapar.
- Ön izlemelerin fiziksel collider'ı yoktur ve iptalde gizlenir.
- Üç duvar tasarımı yerleşim hacminin içinde kalır.

Unity Play modunda ayrıca ahşap inşa, bina üzerinden geçen kırmızı yol, başarısız inşada bakiyenin değişmemesi ve ↑/↓ ile kat değişimi ekranda kontrol edildi. Üç seviyeli varlıklar üretildi. Ana oyun sahnesinin bütün tsunami ve köylü akışı bu oturumda baştan sona oynanmadı.

Silme: panelde SİLME MODUNU AÇ düğmesine basıp kaldırılacak duvara sol tıkla. Ona bağlı üst katlar da kaldırılır; kaynak iadesi yapılmaz. Aynı düğme inşa moduna döndürür.
Ok tuşları tek giriş yolundan işlenir; yeni kat geçişi için tuşun bırakılması gerekir. Son doğrulama: 49/49 test geçti; Unity içinde 1 → 2 → 3 → 2 → 1 ve tıklayarak silme doğrulandı.

N: normal oyun veya inşa modundan doğrudan silme moduna geçer; tekrar N veya Esc inşaya döndürür. Fare altındaki bir grid duvar parçası ve onunla birlikte silinecek üst katlar sarı çerçeveyle belirtilir. Başka duvara, boşluğa veya panele geçildiğinde seçim güncellenir; moddan çıkınca temizlenir. Ön izleme collider içermez ve duvar malzemelerini değiştirmez. Kısayol tekrarı ve ön izleme kapsamı/temizliği testleriyle 51/51 test geçti.
