# Game Jam Planı — Köyü Tsunamiden Koru

## 1. Genel Konsept

Dört kişilik ekibin geliştireceği oyun, bir köyü tsunamiden koruma üzerine kurulu. Köyün tek savunma yolu duvar inşa etmek. Köy, idle mekanik ile kaynak toplarken oyuncu bu kaynakları yönetir ve savunma duvarının şeklini kendisi oluşturur.

Köyün ortasında bulunan delik, toplanan kaynakların depolandığı merkezdir. Ormandan odun, madenden taş elde edilir. Oyun, açılı perspektif kameraya sahip 3D bir yapıda tasarlanacaktır.

## 2. Temel Oynanış Döngüsü

1. Köy, orman ve madenden otomatik olarak kaynak toplar.
2. Toplanan odun ve taş köyün ortasındaki depolama deliğinde birikir.
3. Oyuncu mevcut kaynakları takip eder ve duvar inşasına ayırır.
4. Oyuncu savunma duvarını yerleştirir ve şekillendirir.
5. Tsunami köye ulaşır; su, dalga ve duvar etkileşimi hesaplanır.
6. Köyün korunma durumuna göre sonuç gösterilir.

> Sonuç koşulları ve tek tsunami mi yoksa birden fazla dalga mı olacağı henüz belirlenmedi.

## 3. Sistemler ve Yapılacaklar

### Start Menu — Başlangıç Menüsü

- [ ] Oyunu başlatma butonu.
- [ ] Temel kontrolleri ve amacı açıklayan kısa bilgi alanı.
- [ ] Menüden oyun sahnesine geçiş.

### Cutscene — Açılış Sahnesi

- [ ] Köyü ve yaklaşan tsunami tehlikesini tanıtan kısa sahne.
- [ ] Köyün ortasındaki depolama deliğinin gösterilmesi.
- [ ] Oyuncunun duvar inşa ederek köyü koruma amacının aktarılması.
- [ ] Sahnenin ardından oynanışa geçiş.

### Wall Construction — Duvar İnşası

- [x] Oyuncunun iki grid düğümünü seçerek duvarın yerini ve şeklini belirleyebilmesi.
- [x] Sekiz yönlü en yakın yol, geçerli/geçersiz ön izleme ve maliyet gösterimi.
- [x] İnşa sırasında gereken odun/taşın tek işlemde kontrol edilip harcanması.
- [x] Sınır, aynı/ters kenar ve aynı hücrede çapraz kesişme kontrolleri.
- [x] Gelecekteki fizik için statik çarpıştırıcılı duvar parçaları.
- [x] Seçilebilir katlar ile Ahşap → Taş → Güçlendirilmiş duvar seviyeleri ve yükseltme maliyetleri.
- [ ] Duvarın su ve dalga sistemiyle etkileşmesi.

> Karar: Duvarlar hücrelerin üstüne değil, hücre köşelerindeki düğümler arasına yerleşir. İlk sol tık başlangıcı, ikinci sol tık bitişi seçer. Bitiş herhangi bir harita düğümü olabilir; sistem tek bir düz 45° başlığa zorlamak yerine Bresenham yaklaşımıyla yatay, dikey ve çapraz komşulardan oluşan 8-komşulu, gerektiğinde basamaklı bir yol üretir. Sağ tık veya `Esc` seçimi iptal eder.

### Resource Gathering — Kaynak Toplama

| Kaynak | Toplama alanı | Depolama noktası |
| --- | --- | --- |
| Odun | Orman | Köyün ortasındaki delik |
| Taş | Maden | Köyün ortasındaki delik |

- [ ] Orman ve maden toplama noktalarının oluşturulması.
- [ ] Toplanan kaynakların merkezi depoya aktarılması.
- [ ] Kaynak toplamanın görsel olarak anlaşılır hale getirilmesi.

### Resource Management — Kaynak Yönetimi

- [ ] Odun ve taş miktarlarının ayrı tutulması.
- [ ] Mevcut kaynakların arayüzde gösterilmesi.
- [ ] Duvar maliyetlerinin tanımlanması.
- [ ] Yetersiz kaynak durumunda oyuncuya geri bildirim verilmesi.
- [ ] Aynı kaynakların birden fazla kez harcanmasının engellenmesi.

### Idle Mechanic for Resource Gathering — Otomatik Kaynak Toplama

- [ ] Oyuncunun sürekli komut vermesine gerek kalmadan kaynak üretilmesi.
- [ ] Odun ve taş için toplama aralıklarının ve miktarlarının belirlenmesi.
- [ ] Kaynak üretiminin depodaki miktarlara yansıtılması.
- [ ] Toplama hızları ile duvar maliyetlerinin dengelenmesi.

> İlk sürüm için öneri: idle mekanik yalnızca oyun açıkken çalışır. Oyun kapalıyken üretim kapsamı henüz belirlenmedi.

### Fluid Physics — Akışkan Fiziği

- [ ] Suyun sahnede ilerlemesi ve köye ulaşması.
- [ ] Duvarların suyun ilerleyişini etkilemesi.
- [ ] Su ile köy arasındaki etkileşimin sonuç sistemine bağlanması.
- [ ] Performansa uygun bir uygulama yönteminin seçilmesi.

### Wave Physics — Dalga Fiziği

- [ ] Tsunami dalgasının oluşması ve köye doğru hareket etmesi.
- [ ] Dalga hızı, yüksekliği ve etkisi için ayarlanabilir değerler.
- [ ] Dalganın duvarlara ulaştığında vereceği tepkinin belirlenmesi.
- [ ] Dalga görünümü ile oynanış etkisinin uyumlu olması.

> İş ayrımı: akışkan sistemi suyun yayılmasını ve engellerle etkileşimini, dalga sistemi ise tsunaminin hareketini ve etkisini yönetir. İki sistem birlikte çalışmalıdır.

### Simple Physics — Temel Fizik

- [ ] Zemin ve duvarlar için temel çarpışmalar.
- [ ] Duvar parçalarının yerleşim ve temas kontrolleri.
- [ ] Gerekli nesnelerde basit fizik tepkileri.
- [ ] Fizik etkileşimlerinin kararlı ve performanslı çalışması.

## 4. Görsel Tasarım ve Kamera

- [ ] 3D köy sahnesi.
- [ ] Açılı perspektif kamera.
- [ ] Orman, maden ve merkezi depolama deliğinin kolay ayırt edilmesi.
- [ ] Duvar inşası sırasında yerleşimin rahat görülebilmesi.
- [ ] Tsunaminin geliş yönünün ve savunmaya etkisinin okunabilir olması.

## 4.1 Teknik Karar ve Duvar Prototipini Kullanma

- **Motor:** Unity `6000.6.6f1`, Universal Render Pipeline.
- **Girdi:** Input System `1.20.0`; eski `UnityEngine.Input` API'si kullanılmaz.
- **Harita uyumu:** Mevcut `World.Width`, `World.Height`, `World.CellSize` ve dünya dönüşümü kaynak kabul edilir. `MapView` ve hücrelerin 90° dönüş davranışı değiştirilmez. Duvarlar, mevcut hücre verisini değiştirmeyen ayrı bir düğüm/kenar katmanıdır.
- **Kat ve destek kuralı:** Kat 1'in taban yüksekliği (`Floor Zero Base Height`), kat yüksekliği (`Wall Height`) ve azami kat sayısı Inspector'dan ayarlanır. Bir üst kattaki her kenarın tam altında aynı kenar bulunmalı ve alt kenarın seviyesi üst kenarla eşit veya daha güçlü olmalıdır; böylece yüzen ya da zayıf desteğe oturan duvar oluşmaz. Kenar doluluğu ve hücre içi çapraz kesişme her kat için ayrı değerlendirilir.
- **Seviyeler ve maliyet:** Seviyeler **Ahşap → Taş → Güçlendirilmiş** sırasındadır. Inspector değerleri kenar başına kümülatif toplam bedeldir ve her kaynak için negatif olmayan, monoton bir diziye normalleştirilir. Yeni duvar hedef seviyenin tam bedelini, yükseltme yalnız eski ve yeni kümülatif bedeller arasındaki farkı öder; aynı seviyeyi yeniden kurma ve düşürme reddedilir. Varsayılan kümülatif bedeller Ahşap **1/1**, Taş **3/4**, Güçlendirilmiş **6/6** odun/taştır. Yolun tamamı geçerliyse `IWallResourceStore.TrySpend` yalnız bir kez çağrılır; kısmi inşa/harcama yapılmaz.
- **Entegre depo ve varsayılan denge:** `Well`, doğrudan `IWallResourceStore` uygular ve hem toplamanın hem inşaatın tek yetkili bakiyesidir. `ResourceTest` entegrasyonu kaynak üretmez veya boş depoyu doldurmaz; duvar için odun ve taş normal toplama döngüsüyle kazanılır. Yalnız ayrı, küçük duvar demosu başlangıçta **60 odun / 30 taş** verir. Varsayılan duvar bedeli kenar başına **1 odun / 1 taş**tır. `ResourceHUD`, Well değişiklik olayından harcamaları da anında gösterir.
- **İşlem güvenliği:** Tüm duvar görselleri önce etkin olmayan bir alanda hazırlanır. Hazırlama tamamlanmadan kaynak harcanmaz; harcama başarısızsa hazırlanan nesneler silinir. Kenarlar yalnızca başarılı harcamadan sonra modele eklenir. Yükseltmede eski parça, harcama ve model commit'i başarıyla tamamlanana kadar etkin kalır. `WallBuilt`/`WallTierBuilt` dinleyicilerindeki hatalar tamamlanmış inşayı geri alamaz ve aynı işlem içinden tekrar inşa çağrısı engellenir.
- **Yapı engelleri:** `Obstacle Layers` varsayılan olarak **Nothing** değerindedir. Köy, maden, orman veya merkezi depoyu korumak için bu nesneleri özel bir “ConstructionObstacle” layer'ına taşıyıp yalnızca bu layer'ı maskeye ekleyin. Zemin ve duvar layer'larını maskeye eklemeyin; böylece zemin ve ortak duvar uçları yanlışlıkla engel sayılmaz. Bu kontrol hem ön izlemede hem kesin inşada çalışır; `Obstacle Padding` ek boşluğu ayarlar.
- **Fizik:** Oluşan her parça doğru kat yüksekliğinde statiktir ve en az bir 3D `Collider` içerir. `WallSegment`, gelecekteki su/dalga sorguları için kenar, kat ve seviyeyi salt metadata olarak sunar; bu kapsam sahte akışkan veya can sistemi eklemez. Eski tüketiciler `WallBuilt` olayını kullanmaya devam edebilir, kat/seviye isteyenler `WallTierBuilt` olayına abone olabilir.

### Tek komutla demo

1. Entegre oynanış için `Assets/Scenes/ResourceTest.unity` sahnesini açıp Play'e basın. Eksik/eski sahneyi tek seferde hazırlamak için **Tools > BigEs > Kaynak Test Sahnesine Duvar Entegrasyonu Ekle** komutunu kullanın. Bu komut mevcut bakiyeyi değiştirmez; duvar kaynaklarını oynayarak toplamak gerekir. Ayrı, küçük demo için **Tools > Idle Town > Create Wall Construction Demo Scene** kullanılabilir; iki kurulum da bağımsız prototip deposu yerine `Well` kullanır.
2. Komut, `SampleScene` dosyasına dokunmadan `Assets/Scenes/WallConstructionDemo.unity` (isim doluysa benzersiz yeni bir ad) oluşturur ve açar.
3. Play'e basın. Perspektif kamera, görünür grid, zemin, merkezi başlangıç deposu ve durum/kontrol paneli hazırdır.
4. `ResourceTest` mevcut toplama davranışını korumak için **Toplama** modunda, ayrı duvar demosu **İnşa** modunda başlar. **B** veya paneldeki mod düğmesiyle İnşa/Toplama arasında geçin. İnşa modunda `PageUp`/`PageDown` ya da panel düğmeleri katı; `1`/`2`/`3` ya da seviye düğmeleri Ahşap/Taş/Güçlendirilmiş seviyesini seçer. Sol tıkla başlangıç ve bitiş düğümlerini seçin; ön izleme seçilen katın gerçek yüksekliğinde, yeşil geçerli ve kırmızı geçersiz görünür. İkinci sol tık, `Enter` veya **İnşa Et** onaylar. Toplama modunda kaynaklara sol tıklama çalışır. UI/panel tıklamaları iki sistemde de dünyaya aktarılmaz; sağ tık veya `Esc` seçimi iptal eder.

### Mevcut sahneye elle ekleme

1. `World` nesnesinden ayrı bir GameObject oluşturup `WallConstructionController` ekleyin.
2. `World`, kamera ve `IWallResourceStore` uygulayan `MonoBehaviour` referanslarını atayın.
3. İsteğe bağlı `Wall Prefab` atayın. Prefabın merkezi pivotlu, ileri ekseni **+Z**, model ölçüsü **1×1×1** olmalıdır; controller kalınlığı X'e, yüksekliği Y'ye ve kenar uzunluğunu Z'ye uygular. Prefab verilmezse renkli küp parça üretilir.
4. Sahnedeki `Well` bileşenini kaynak referansı olarak verin. `Well.TrySpend`, odun ve taşı tek atomik işlemde azaltır; yetersizlikte iki bakiyeyi de değiştirmez. Oyun sırasında depo değiştirilecekse `SetResourceStore` kullanılabilir. `SimpleWallResourceStore` yalnız eski test/uyumluluk amaçlıdır.

Bu teslim duvar inşa çekirdeği ile kat/seviye metadata'sını kapsar; menü, tsunami/akışkan simülasyonu ve duvar hasarı eklenmemiştir.

## 5. Dört Kişilik Ekip İçin Önerilen Görev Dağılımı

| Ekip üyesi | Ana sorumluluk | Ortak çalışma noktası |
| --- | --- | --- |
| Kişi 1 | Duvar inşası ve temel fizik | Kaynak maliyetleri ve su-duvar etkileşimi |
| Kişi 2 | Kaynak toplama, idle mekanik ve kaynak yönetimi | Depo, inşa sistemi ve kaynak arayüzü |
| Kişi 3 | Akışkan ve dalga sistemleri | Duvar etkileşimi ve köyün korunma kontrolü |
| Kişi 4 | 3D sahne, kamera, başlangıç menüsü ve cutscene | Görsel bütünlük ve sahne geçişleri |

> Bu dağılım öneridir. Özellikle akışkan ve dalga sistemlerinin kapsamı prototip sonucuna göre ekipçe daraltılmalıdır.

## 6. Önerilen Geliştirme Sırası

### Aşama 1 — Oynanabilir Çekirdek

- [ ] Basit köy, zemin ve kamera kurulumu.
- [ ] Odun ve taş sayaçları ile otomatik üretim.
- [ ] Kaynak harcayarak oyuncunun şekillendirdiği duvar inşası.
- [ ] Basit tsunami prototipi ve duvar etkileşimi.
- [ ] Köyün korunup korunmadığını gösteren sonuç kontrolü.

### Aşama 2 — Sistemlerin Birleştirilmesi

- [ ] Orman, maden ve merkezi depo bağlantıları.
- [ ] Kaynak göstergeleri ve inşa geri bildirimleri.
- [ ] Akışkan, dalga ve temel fizik sistemlerinin birlikte denenmesi.
- [ ] Baştan sona oynanabilen bir oyun turu.

### Aşama 3 — Sunum ve Teslim

- [ ] Başlangıç menüsü ve kısa cutscene.
- [ ] Görsel düzenlemeler ve anlaşılır kontroller.
- [ ] Kaynak üretimi, duvar maliyeti ve tsunami etkisi dengesi.
- [ ] Performans kontrolü, hata düzeltmeleri ve teslim derlemesi.

> Game jam kapsam önerisi: önce basitleştirilmiş su/dalga davranışıyla oynanış döngüsünü tamamlayın. Daha ayrıntılı fizik için kalan süreye göre karar verin.

## 7. Kararlaştırılacak Konular

- [x] Oyun motoru: Unity. Hedef platform ve game jam süresi ayrıca netleştirilecek.
- [x] Duvar şekillendirme: iki tıkla, düğüm/kenar tabanlı 8 yönlü yol.
- [x] Odun ve taş: her duvar kenarı için Inspector'dan ayarlanabilen ayrı maliyetler.
- [ ] Tsunaminin ne zaman başlayacağı ve dalga sayısı.
- [ ] Köy hasarı, kazanma ve kaybetme koşulları.
- [ ] Duvarların hasar alıp yıkılıp yıkılmayacağı.
- [ ] Akışkan ve dalga sistemlerinde hedeflenen fizik ayrıntısı.

## 8. İlk Oynanabilir Sürüm İçin Tamamlanma Ölçütleri

- [ ] Oyuncu menüden oyuna başlayabiliyor.
- [ ] Köy otomatik olarak odun ve taş topluyor.
- [ ] Kaynaklar merkezi depoda birikiyor ve miktarları görülebiliyor.
- [ ] Oyuncu kaynak harcayarak kendi şekillendirdiği duvarı inşa edebiliyor.
- [ ] Tsunami köye ilerliyor ve duvarla etkileşiyor.
- [ ] Oyun turunun sonucu oyuncuya gösteriliyor.
