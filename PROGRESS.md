# İlerleme Kaydı — Kaynak Ekibi

Yeni bir oturuma (session) başlarken önce bu dosyayı oku. Her iş bitince "Son durum" ve "Sıradaki işler" bölümlerini güncelle.

Son güncelleme: 2026-10-17

## Bağlam

- **Yarışma:** Civil Game Jam, 16–18 Ekim 2026, 48 saat. Son teslim **18 Ekim 18.00 (UTC+3)**. Kurallar: `CIVIL_GAME_JAM_Kural_Kitapcigi_v09.pdf` (repoda değil, proje klasöründe).
- **Oyun:** Köyü tsunamiden koru (bkz. `Plan.md`). Ortada kuyu, çevresinde kasaba. Ağaçlardan odun, madenden taş toplanıp kuyuya atılır; kaynaklarla duvar yapılır.
- **Bizim sorumluluğumuz:** Kaynak toplama, idle mekanik ve kaynak yönetimi (Plan.md'deki Kişi 2).
- **Unity:** 6000.6.6f1, URP 17.6.0, Input System (yalnız yeni sistem açık). TextMeshPro içe aktarılmadı; arayüzde UGUI `Text` kullanılıyor.
- **Repo:** https://github.com/FEB5135/BigEs (şu an private, teslimde public olmalı).

## Branch'ler

- `main`: Ekibin ortak branch'i. Son merge edilen commit: `22f7139 Simple Grid Worldmodel` (harita grid sistemi, `well.obj`, ProBuilder).
- `feature/resource-gathering`: Bizim çalışmamız. Push edilmemiş commit'ler var; push etmeden önce kullanıcıya sor.
- `backup/resource-gathering-before-rewrite`: Sadece yerelde. İçinde Asset Store ağaç paketi var, **asla push etme**. İş bitince silinebilir.

## Son durum (yapılanlar)

- **Unity derleme hatası (CS0246 MaterialReferenceChanger):** Unity 6000.6.6f1'in kendi hatası. URP core ve universal paketleri `Packages/` içine gömüldü, eksik `.meta` dosyaları eklendi. Yeni bir URP projesinde aynı hata çıkarsa aynı çözüm uygulanır.
- **Kaynak sistemi** (`Assets/Scripts/Economy/`):
  - `Well`: Kuyu ve depo. `Deposit`, `CanAfford`, `TrySpend` (ya hepsi harcanır ya hiçbiri), `Changed` olayı. `Throw` ile eğik atış: ilk hız analitik olarak çözülür, tek parabol, duvarın üstünden geçmesi kontrol edilir.
  - `ResourcePickup`: Atılan parça. Kuyudaki tetikleyiciye girince depoya eklenir; 6 sn içinde girmezse yine eklenir.
  - `WellIntake`: Kuyu içindeki tetikleyici.
  - `ResourceNode`: Ağaç/kaya. Kapasite, tükenince gizlenme ve yeniden çıkma, tıklayınca sallanma. Collider yoksa modelin sınırlarına göre kendisi ekler, bu yüzden model değişse de çalışır.
  - `Gatherer`: Idle köylü. En yakın boş kaynağa gider, toplar, kuyunun dışında durup atar.
- **Oynanış** (`Assets/Scripts/Gameplay/`):
  - `IsometricCamera`: 45/45 bakış, tekerlekle zoom, sağ tıkla kaydırma (yönü kullanıcı onayladı).
  - `ClickHarvester`: Ağaca sol tıklayınca odun kuyuya atılır.
- **Arayüz** (`Assets/Scripts/UI/ResourceHUD.cs`): Sol altta odun ve taş butonları (Kenney ahşap çerçeve, saint11 ikonları). Butonların tıklama işlevi henüz yok.
- **Test sahnesi:** `Assets/Scenes/ResourceTest.unity`. Hepsi `Assets/Scripts/Editor/ResourceTestSetup.cs` ile kuruluyor (menü: Tools > BigEs > Kaynak Test Sahnesi Kur).
  - **Ölçek:** Ortalama ev genişliği ~3.8 m, kuyu çapı 5 ev = 19 m (`well.obj` buna göre büyütülüyor; `Flat.mat` + `palette.png` ile boyanıyor).
  - Kuyunun etrafında 3 halkada 49 konsept ev (kasaba yarıçapı ~36 m). Orman yönü köylülerin yolu olarak boş.
  - Solda kasabanın dışında 34 ağaçlık orman (FinottiGames), 5 oduncu (hız 5).
  - Sağda dağ sırası (yükseklik 32–58 m). Kasabanın arkasında vadi: sağ duvarı dağ sırası, sol duvarı tek büyük dağ. **Tsunami bu vadiden gelecek.**
  - Zemin 300 m. Kamera zoom 15–160 (başlangıç 70), kaydırma ±120 m.
- **Kural uyumu:** `THIRD_PARTY.md`'de tüm hazır kaynaklar ve AI kullanımı yazılı. FinottiGames ağaç paketi (Asset Store EULA) `.gitignore`'da ve geçmişten silindi.

## Sıradaki işler / açık konular

- [ ] Taş: sağdaki dağlara maden/kaya `ResourceNode`'ları ve taş toplayan köylüler.
- [ ] Sol alttaki odun/taş butonlarına işlev vermek (ne yapacakları henüz kararlaştırılmadı).
- [ ] Duvar ekibine `Well.TrySpend` / `ResourceCost` kullanımını anlatmak.
- [ ] Toplama hızı ile duvar maliyetini dengelemek.
- [ ] Arkadaşa iletilecek: `.obj` modellerin `.mtl` dosyaları repoda yok, modeller beyaz görünüyor. Malzemeleri `Flat.mat`'e eşlemeli.
- [ ] Teslimden önce: Repo public olmadan eski commit'lerdeki Asset Store dosyaları için GitHub Support'a başvurmak ya da temiz bir geçmişle yeniden oluşturmak.
- [ ] `ProjectSettings/Packages/com.unity.probuilder/Settings.json` Unity tarafından değişti, commit edilmedi.
- [ ] URP gölge mesafesi (`Assets/Settings/PC_RPAsset.asset`, `m_ShadowDistance: 50`) büyük haritada uzaktan bakınca gölgeleri kesiyor; ekip isterse artırılabilir (ortak ayar, dokunulmadı).

## Çalışma notları (oturumlar arası)

- **Sahneyi değiştirmek:** `ResourceTestSetup.cs` düzenlenir. Mevcut sahneye uygulamak için tek seferlik bir `Assets/Scripts/Editor/RebuildResourceTestOnce.cs` yazılır: `[InitializeOnLoad]` + `delayCall` kökü siler, menüyü çalıştırır, sahneyi kaydeder ve kendini siler.
- **Derleme:** Unity ancak penceresi öne gelince derler. Play modundayken derlemez. Sonuç `Logs/Editor.log`'dan (`Tundra build success`, `error CS`) okunur.
- **Ekran kontrolü:** Kullanıcının Unity düzeni değişebiliyor (Game görünümü üstte küçük). Fare/klavye ile test etmeden önce tam pencere görüntüsü alınmalı. Unity önde değilse kullanıcı bilgisayarı kullanıyordur; dokunulmaz.
- **İndirme:** Her dosya indirmesi için kullanıcıdan izin alınır. Yalnız CC0 ya da repoya konabilir lisanslar repoya girer; Asset Store paketleri `.gitignore`'a eklenir ve `THIRD_PARTY.md`'ye yazılır.
- **Model tasarımcısı var:** Görsellerde fazladan iş yapılmaz, placeholder'lar yeterli.
- Kullanıcıyla Türkçe konuşulur.
