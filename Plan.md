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

- [ ] Oyuncunun duvarın yerini ve şeklini belirleyebilmesi.
- [ ] Duvar yerleştirme ön izlemesi ve geçerli alan kontrolü.
- [ ] İnşa sırasında gereken kaynakların kontrol edilmesi ve harcanması.
- [ ] Duvarın su ve dalga sistemiyle etkileşmesi.
- [ ] Duvar şekillendirme yönteminin seçilmesi: parça yerleştirme, çizim veya başka bir yöntem.

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

- [ ] Oyun motoru, hedef platform ve game jam süresi.
- [ ] Duvarın oyuncu tarafından nasıl şekillendirileceği.
- [ ] Odun ve taşın duvar maliyetlerindeki rolleri.
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
