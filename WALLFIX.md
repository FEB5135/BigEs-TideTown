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

## Arazi atölyesi
Tools → Idle Town → Wallfix → Open Terrain Workshop: Assets/Scenes/WallfixTerrainWorkshop.unity sahnesini açar. Main ile aynı SampleScene içindeki Ground ve ilk iki Mountain geometrisi/malzemesi ayrı varlıklara kopyalandı; test alanına sığacak ölçekte yerleştirildi. Ana sahne değiştirilmedi.
Bu sahnede 32×32 grid, çimen ve iki dağ, 5000 odun/taş bulunur. Fare arazi collider'ına vurur; grid, ön izleme, duvar ve silme çerçevesi aynı arazi yüksekliğini kullanır. Duvar panelleri iki ucun yüksekliğine göre eğilir, dikmeler dik kalır; collider da aynı eğimli hacmi izler. Sadece açıkça kayıtlı arazi collider'ları zemin sayılır, yapılar engel olmaya devam eder. Arazi dışına inşa reddedilir.
Yeni varsayılan ve iki Wallfix atölyesinde azami yükseklik 10 kat × 2 m = zeminden 20 m. Üst katlar aynı duvarın alt kat desteğini gerektirir.
Doğrulama: 53/53 EditMode testi geçti. Eğimli collider'ın iki uçta zemine oturması, 10 kat desteği, 11. katın reddi, arazi dışına inşanın ve bina içinden geçişin reddi test edildi. Unity'de çimenden dağa 9 parçalı duvar, onun ikinci katı, 5 parçalı çapraz eğimli duvar ve eğimde sarı silme çerçevesi denendi. Tsunami akışı bu yeni sahnede çalıştırılmadı.

## 2 m kat / 20 m sınır ve main entegrasyonu
Kat yüksekliği 2 m sabitlendi; azami kat sayısı en çok 10 olacak şekilde doğrulanıyor (20 m). Mevcut duvar sahneleri ve üç görsel prefab güncellendi. 100 katlık yanlış yapılandırmanın da 10'a kısıtlandığı test ediliyor.
origin/main 3d00740, Wallfix içine b746224 ile birleştirildi. Ana haritanın yeni ev/kum varlıkları ve WFC düzenlemeleri alındı. Paletin çalışma dosyası önceki LFS içeriğiyle aynı olduğu doğrulanıp Git durumu yenilendi; kullanıcı dosyaları stash/reset yapılmadı.
Birleşmiş sürümde genel entegrasyon koşusu: 64 test, 61 geçti / 3 başarısız. ResourceTest eski ağaç prefab GUID'leri eksik (main'de de mevcut); SampleScene köylü animasyonu/yürüyüş kontrolü başarısız; eğimli duvar testine ana sahnenin collider'ı karıştı. Sonuncusu fixture içinde dış collider'ları geçici kapatıp sonunda geri açarak düzeltildi. Son duvar koşusu: 53/53 geçti. Genel koşunun raporu Logs/Wallfix-integration-tests.xml, son duvar koşusu Logs/Wallfix-tests.xml. Diğer iki entegrasyon sorunu çözülmediği için Wallfix → main son birleştirmesi yapılmadı. Unity'de 2 m duvar inşası ve ikinci katın 2 m'de başlaması doğrulandı.
