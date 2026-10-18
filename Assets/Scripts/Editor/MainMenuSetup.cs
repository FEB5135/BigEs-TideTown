using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Açık sahneye ana menüyü kurar: arayüz (Main Menu canvas'ı) ve kameraya CameraIntro.
// Menü: Tools > BigEs > Ana Menüyü Kur (varsa silip yeniden kurar).
// Butonlar düz yüzlü, sert alt kalınlıklı ve kesik köşeli (low-poly); sprite'lar Art/UI/make_menu_sprites.py ile
// çizilir. Renkler: canlı yeşil zemine karşı mercan (Oyna), okyanus mavisi (ikincil), hayalet (Çıkış), lacivert panel.
// Yazı tipi Rubik: yazılar Bold, logo Black (köşeleri hafif yuvarlak geometrik, low-poly'ye uyuyor).
public static class MainMenuSetup
{
    const string RootName = "Main Menu";
    const string SpriteDir = "Assets/UI/Menu/";
    const string FontPath = "Assets/UI/Fonts/Rubik-Bold.ttf";
    const string TitleFontPath = "Assets/UI/Fonts/Rubik-Black.ttf";
    const string ClickPath = "Assets/Audio/UI/Click.ogg";

    // Palet: su/tsunami teması, canlı yeşil zemine karşı.
    static readonly Color Coral = Hex("F2542D");
    static readonly Color CoralDark = Hex("B83A1B");
    static readonly Color OceanDark = Hex("154566");
    static readonly Color Water = Hex("2EC4D6");
    static readonly Color Foam = Hex("E8F7FA");
    static readonly Color Navy = Hex("0B2A3D");
    static readonly Color Gold = Hex("FFD23F");
    static readonly Color Muted = Hex("CFE8F2");
    static readonly Color Backdrop = Alpha(Navy, 0.55f);
    static readonly Color ColumnScrim = Alpha(Navy, 0.35f);
    static readonly Color PanelColor = Alpha(Navy, 0.72f);
    static readonly Color CardColor = Alpha(Navy, 0.94f);

    // Buton sprite'ları 72 px yüksekliğinde, alt kalınlık 8 px.
    const float LipPixels = 8f;
    // Ekran kenar boşluğu (1920x1080 referansında).
    const float Margin = 64f;

    static Font font;
    static Font titleFont;
    static AudioClip click;

    [MenuItem("Tools/BigEs/Ana Menüyü Kur")]
    static void Rebuild()
    {
        var existing = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
        var intro = Object.FindAnyObjectByType<CameraIntro>(FindObjectsInactive.Include);
        if (intro != null)
            Object.DestroyImmediate(intro);
        Build();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // Sahnede menü yoksa kurar.
    internal static bool EnsureMenu()
    {
        if (Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include) != null)
            return false;
        return Build();
    }

    static bool Build()
    {
        var camera = Camera.main;
        if (camera == null || camera.GetComponent<IsometricCamera>() == null)
        {
            Debug.LogWarning("Ana menü kurulamadı: Main Camera'da IsometricCamera yok.");
            return false;
        }
        var intro = camera.GetComponent<CameraIntro>();
        if (intro == null)
            intro = camera.gameObject.AddComponent<CameraIntro>();

        font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
        {
            Debug.LogWarning($"Menü yazı tipi bulunamadı: {FontPath}. Unity'nin varsayılanı kullanılıyor.");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        titleFont = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        if (titleFont == null)
            titleFont = font;
        click = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickPath);

        var root = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AudioSource));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var audio = root.GetComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;

        // Kenarları lacivertle koyulaştıran perde (bakışı ortaya çeker); arkadaki harita tıklanamasın diye
        // ışınları da tutar. Buton sütununun arkasında ayrıca yumuşak bir koyuluk var.
        var backdrop = Stretch(NewImage(root.transform, "Backdrop", SpriteDir + "Vignette.png", Backdrop));
        backdrop.GetComponent<Image>().raycastTarget = true;
        var scrim = NewImage(root.transform, "Column Scrim", SpriteDir + "Scrim.png", ColumnScrim);
        scrim.sizeDelta = new Vector2(1100f, 1000f);
        scrim.anchoredPosition = new Vector2(0f, 20f);

        var mainPage = Page(root.transform, "Main Page");
        var items = BuildMainPage(mainPage.transform, out var highScore, out var play, out var settings, out var quit);

        var settingsPage = Page(root.transform, "Settings Page");
        var settingsBack = BuildSettingsPage(settingsPage.gameObject);

        var menu = root.AddComponent<MainMenu>();
        var so = new SerializedObject(menu);
        so.FindProperty("cameraIntro").objectReferenceValue = intro;
        so.FindProperty("menuGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("mainPage").objectReferenceValue = mainPage;
        so.FindProperty("settingsPage").objectReferenceValue = settingsPage;
        so.FindProperty("highScoreText").objectReferenceValue = highScore;
        so.FindProperty("playButton").objectReferenceValue = play;
        so.FindProperty("settingsButton").objectReferenceValue = settings;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        SetArray(so.FindProperty("backButtons"), settingsBack);
        SetArray(so.FindProperty("introItems"), items);

        var hud = Object.FindAnyObjectByType<ResourceHUD>(FindObjectsInactive.Include);
        if (hud != null)
        {
            var hudGroup = hud.GetComponent<CanvasGroup>();
            if (hudGroup == null)
                hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            SetArray(so.FindProperty("gameplayUI"), hudGroup);
        }
        // Duvar inşası menüdeyken çalışmasın (paneli menünün üstünde çizilir, B tuşunu dinler).
        var construction = Object.FindAnyObjectByType<IdleTown.Construction.WallConstructionController>(FindObjectsInactive.Include);
        if (construction != null)
            SetArray(so.FindProperty("gameplayBehaviours"), construction);
        so.ApplyModifiedPropertiesWithoutUndo();

        settingsPage.gameObject.SetActive(false);
        return true;
    }

    // ---------------------------------------------------------------- Sayfalar

    // Yerleşim (1920x1080, 8 px ızgara): logo üstte ortada, butonlar tek sütunda ortada. Gruplar arası boşluk
    // grup içinin iki katı: logo -> Oyna 88, Oyna -> Ayarlar 40, Ayarlar -> Çıkış 20. Skor sağ üst köşede.
    static Object[] BuildMainPage(Transform page, out Text highScore, out Button play, out Button settings, out Button quit)
    {
        // Logo: oyunun Tide Town logosu (Logo.png), üstte ortada; alt başlık yok.
        var logo = NewRect(page, "Logo", LogoSize, LogoPosition);
        LogoImage(logo);

        // En yüksek skor: menünün ortasından sağ üst köşeye, kenarlardan Margin içeride bir çip.
        var score = NewRect(page, "High Score", new Vector2(300f, 76f), Vector2.zero);
        Place(score, Vector2.one, new Vector2(300f, 76f), new Vector2(-Margin - 150f, -Margin - 38f));
        Sliced(Stretch(NewImage(score, "Background", SpriteDir + "Panel.png", PanelColor)), 1f);
        var trophy = NewImage(score, "Trophy", SpriteDir + "IconTrophy.png", Gold);
        Place(trophy, new Vector2(0f, 0.5f), new Vector2(40f, 40f), new Vector2(24f + 20f, 0f));
        AddShadow(trophy.GetComponent<Image>(), Navy, 2f, 1f);
        var label = NewText(score, "Label", "EN YÜKSEK SKOR", 16, Muted, TextAnchor.MiddleLeft);
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(200f, 20f), new Vector2(84f, 14f));
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        highScore = NewText(score, "Value", "0", 32, Color.white, TextAnchor.MiddleLeft);
        Place(highScore.rectTransform, new Vector2(0f, 0.5f), new Vector2(200f, 36f), new Vector2(84f, -12f));
        highScore.rectTransform.pivot = new Vector2(0f, 0.5f);

        // Oyna, ikincillerden ~1.4 kat yüksek; ikonu ve yazısı ortada bir grup. İkincillerde ikon solda sabit sütunda.
        play = MakeButton(page, "Play Button", "OYNA", SpriteDir + "IconPlay.png", new Vector2(440f, 112f), new Vector2(0f, -34f), "Coral", 56, true);
        settings = MakeButton(page, "Settings Button", "AYARLAR", SpriteDir + "IconGear.png", new Vector2(360f, 80f), new Vector2(0f, -170f), "Ocean", 38, false);
        quit = MakeButton(page, "Quit Button", "ÇIKIŞ", SpriteDir + "IconExit.png", new Vector2(360f, 72f), new Vector2(0f, -266f), "Ghost", 34, false);

        var footer = NewText(page, "Footer", "Civil Game Jam 2026", 16, Alpha(Color.white, 0.6f));
        Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(600f, 24f), new Vector2(0f, 36f));
        AddShadow(footer, Navy, 2f, 0.8f);

        return new Object[]
        {
            logo, score, (RectTransform)play.transform, (RectTransform)settings.transform, (RectTransform)quit.transform,
        };
    }

    static Button BuildSettingsPage(GameObject page)
    {
        var card = NewRect(page.transform, "Card", new Vector2(680f, 500f), new Vector2(0f, -10f));
        var shadow = NewImage(card, "Shadow", SpriteDir + "Shadow.png", new Color(0f, 0f, 0f, 0.35f));
        Sliced(Stretch(shadow, new Vector2(-30f, -40f), new Vector2(30f, 16f)), 1f);
        Sliced(Stretch(NewImage(card, "Background", SpriteDir + "Panel.png", CardColor)), 1f);
        var face = Stretch(NewRect(card, "Content", Vector2.zero, Vector2.zero));

        var header = NewText(face, "Header", "AYARLAR", 52, Color.white);
        Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, 64f), new Vector2(0f, -58f));
        AddShadow(header, Navy, 4f, 1f);
        var headerIcon = NewImage(face, "Header Icon", SpriteDir + "IconGear.png", Coral);
        Place(headerIcon, new Vector2(0.5f, 1f), new Vector2(44f, 44f), new Vector2(-header.preferredWidth * 0.5f - 38f, -58f));
        var divider = NewImage(face, "Divider", null, Alpha(Color.white, 0.18f));
        Place(divider, new Vector2(0.5f, 1f), new Vector2(560f, 3f), new Vector2(0f, -104f));

        RowLabel(face, "Music", "Müzik", 92f);
        var musicSlider = MakeSlider(face, "Music Slider", new Vector2(95f, 92f));
        var musicValue = NewText(face, "Music Value", "%60", 30, Water, TextAnchor.MiddleRight);
        Place(musicValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(90f, 40f), new Vector2(285f, 92f));

        RowLabel(face, "Effects", "Efektler", 8f);
        var effectsSlider = MakeSlider(face, "Effects Slider", new Vector2(95f, 8f));
        var effectsValue = NewText(face, "Effects Value", "%80", 30, Water, TextAnchor.MiddleRight);
        Place(effectsValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(90f, 40f), new Vector2(285f, 8f));

        RowLabel(face, "Fullscreen", "Tam Ekran", -76f);
        var fullscreen = OptionPicker(face, "Fullscreen", -76f, out var fsPrev, out var fsNext);

        var back = MakeButton(face, "Back Button", "GERİ", null, new Vector2(260f, 80f), Vector2.zero, "Ocean", 36, true);
        Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(260f, 80f), new Vector2(0f, 64f));

        var panel = page.AddComponent<SettingsPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("musicSlider").objectReferenceValue = musicSlider;
        so.FindProperty("musicValue").objectReferenceValue = musicValue;
        so.FindProperty("effectsSlider").objectReferenceValue = effectsSlider;
        so.FindProperty("effectsValue").objectReferenceValue = effectsValue;
        so.FindProperty("fullscreenPrev").objectReferenceValue = fsPrev;
        so.FindProperty("fullscreenNext").objectReferenceValue = fsNext;
        so.FindProperty("fullscreenValue").objectReferenceValue = fullscreen;
        so.ApplyModifiedPropertiesWithoutUndo();
        return back;
    }

    // Logo resmi 960x825; ekranda 400 yüksek, üst kenardan 40 içeride, Oyna'nın 78 üstünde biter.
    static readonly Vector2 LogoSize = new Vector2(465f, 400f);
    static readonly Vector2 LogoPosition = new Vector2(0f, 300f);
    const string LogoPath = SpriteDir + "Logo.png";
    const string IconPath = "Assets/UI/Icons/GameIcon.png";

    static RectTransform LogoImage(RectTransform logo)
    {
        var image = Stretch(NewImage(logo, "Image", LogoPath, Color.white));
        image.GetComponent<Image>().preserveAspect = true;
        // Resim ekranda ~2 kat küçüldüğü için mipmap açık (yoksa ahşap harflerin kenarları kırılıyor).
        if (AssetImporter.GetAtPath(LogoPath) is TextureImporter importer && !importer.mipmapEnabled)
        {
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        return image;
    }

    // Tools > BigEs > Logo ve İkonu Uygula, ya da komut satırından:
    // Unity -batchmode -quit -executeMethod MainMenuSetup.ApplyLogoAndIcon
    // Açık sahnedeki (batchmode'da SampleScene) eski yazı logosunu (dalga amblemi + "Tide Town" + alt başlık)
    // Logo.png ile değiştirip sahneyi kaydeder ve oyunun varsayılan ikonunu GameIcon.png yapar.
    [MenuItem("Tools/BigEs/Logo ve İkonu Uygula")]
    public static void ApplyLogoAndIcon()
    {
        var scene = Application.isBatchMode
            ? EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity")
            : EditorSceneManager.GetActiveScene();
        Debug.Log("[MainMenuSetup] " + UpgradeLogo(scene));
        EditorSceneManager.SaveScene(scene);

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null)
        {
            Debug.LogError("[MainMenuSetup] icon not found: " + IconPath);
            return;
        }
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("[MainMenuSetup] default icon set: " + IconPath);
    }

    public static string UpgradeLogo(UnityEngine.SceneManagement.Scene scene)
    {
        MainMenu menu = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            menu = root.GetComponentInChildren<MainMenu>(true);
            if (menu != null)
                break;
        }
        if (menu == null)
            return "main menu not found";
        var logo = menu.transform.Find("Main Page/Logo") as RectTransform;
        if (logo == null)
            return "logo not found";
        if (logo.Find("Image") != null)
            return "logo already upgraded";

        int removed = 0;
        foreach (var name in new[] { "Waves", "Title", "Subtitle" })
            if (logo.Find(name) is Transform old)
            {
                Object.DestroyImmediate(old.gameObject);
                removed++;
            }
        logo.sizeDelta = LogoSize;
        logo.anchoredPosition = LogoPosition;
        LogoImage(logo);
        EditorSceneManager.MarkSceneDirty(scene);
        return $"logo image added, {removed} old title objects removed";
    }

    // Sahnedeki eski Ayarlar sayfasını (Ses + Tam Ekran + Grafik) yeni düzene çevirir: Müzik + Efektler + Tam Ekran.
    // Menünün geri kalanına dokunmaz.
    public static string UpgradeSettingsPage(UnityEngine.SceneManagement.Scene scene)
    {
        SettingsPanel panel = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            panel = root.GetComponentInChildren<SettingsPanel>(true);
            if (panel != null)
                break;
        }
        if (panel == null)
            return "settings page not found";
        var content = panel.transform.Find("Card/Content");
        if (content == null)
            return "settings content not found";
        if (content.Find("Effects Slider") != null)
            return "settings page already upgraded";

        var musicLabel = Renamed(content, "Volume Label", "Music Label");
        var musicSlider = Renamed(content, "Volume Slider", "Music Slider");
        var musicValue = Renamed(content, "Volume Value", "Music Value");
        if (musicLabel == null || musicSlider == null || musicValue == null)
            return "volume row not found";
        musicLabel.GetComponent<Text>().text = "Müzik";

        var effectsLabel = Copy(musicLabel, "Effects Label", 8f);
        effectsLabel.GetComponent<Text>().text = "Efektler";
        var effectsSlider = Copy(musicSlider, "Effects Slider", 8f);
        var effectsValue = Copy(musicValue, "Effects Value", 8f);

        foreach (var name in new[] { "Fullscreen Label", "Fullscreen Prev", "Fullscreen Next", "Fullscreen Value" })
            if (content.Find(name) is RectTransform row)
                row.anchoredPosition = new Vector2(row.anchoredPosition.x, -76f);
        int removed = 0;
        foreach (var name in new[] { "Quality Label", "Quality Prev", "Quality Next", "Quality Value" })
            if (content.Find(name) is Transform row)
            {
                Object.DestroyImmediate(row.gameObject);
                removed++;
            }

        var so = new SerializedObject(panel);
        so.FindProperty("musicSlider").objectReferenceValue = musicSlider.GetComponent<Slider>();
        so.FindProperty("musicValue").objectReferenceValue = musicValue.GetComponent<Text>();
        so.FindProperty("effectsSlider").objectReferenceValue = effectsSlider.GetComponent<Slider>();
        so.FindProperty("effectsValue").objectReferenceValue = effectsValue.GetComponent<Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"settings page: music + effects sliders, fullscreen moved, {removed} graphics objects removed";
    }

    static RectTransform Renamed(Transform parent, string from, string to)
    {
        var child = parent.Find(from) as RectTransform;
        if (child != null)
            child.name = to;
        return child;
    }

    // Bir satır parçasının kopyası, aynı sırada ama başka yükseklikte.
    static RectTransform Copy(RectTransform original, string name, float y)
    {
        var copy = (RectTransform)Object.Instantiate(original.gameObject, original.parent).transform;
        copy.name = name;
        copy.SetSiblingIndex(original.GetSiblingIndex() + 1);
        copy.anchoredPosition = new Vector2(original.anchoredPosition.x, y);
        return copy;
    }

    // ---------------------------------------------------------------- Parçalar

    static CanvasGroup Page(Transform parent, string name)
    {
        var rect = Stretch(NewRect(parent, name, Vector2.zero, Vector2.zero));
        return rect.gameObject.AddComponent<CanvasGroup>();
    }

    // Kalınlıklı buton. sprite: "Button<style>" ya da "Square<style>" (style: Coral, Ocean, Ghost).
    // pixelScale < 1 köşeleri ve alt kalınlığı büyütür; ekrandaki kalınlık LipPixels / pixelScale.
    static Button PressableBase(Transform parent, string name, Vector2 size, Vector2 position, string sprite, float pixelScale, bool withShadow, out RectTransform content)
    {
        bool ghost = sprite.EndsWith("Ghost");
        float depth = ghost ? 2f : LipPixels / pixelScale;
        var root = NewRect(parent, name, size, position);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear; // Tıklama alanı; görsel değişse de sabit kalır.

        if (withShadow && !ghost)
        {
            var shadow = NewImage(root, "Shadow", SpriteDir + "Shadow.png", new Color(0f, 0f, 0f, 0.22f));
            Sliced(Stretch(shadow, new Vector2(-14f, -22f), new Vector2(14f, 2f)), 1f);
        }

        var normal = LoadSprite(SpriteDir + sprite + ".png");
        var pressed = LoadSprite(SpriteDir + sprite + "Pressed.png");
        var visual = Sliced(Stretch(NewImage(root, "Visual", null, Color.white)), pixelScale);
        var visualImage = visual.GetComponent<Image>();
        visualImage.sprite = normal;

        // İçerik yüzün ortasında durur (alt kalınlık hariç); PressableButton konumunu yönetir.
        content = Stretch(NewRect(root, "Content", Vector2.zero, Vector2.zero), Vector2.zero, Vector2.zero);
        content.anchoredPosition = new Vector2(0f, depth * 0.5f);

        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var pressable = root.gameObject.AddComponent<PressableButton>();
        var so = new SerializedObject(pressable);
        so.FindProperty("visual").objectReferenceValue = visualImage;
        so.FindProperty("normalSprite").objectReferenceValue = normal;
        so.FindProperty("pressedSprite").objectReferenceValue = pressed;
        so.FindProperty("content").objectReferenceValue = content;
        so.FindProperty("depth").floatValue = depth;
        so.FindProperty("clickSound").objectReferenceValue = click;
        if (size.x < 100f)
            so.FindProperty("hoverScale").floatValue = 1.08f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return button;
    }

    // İkon yüksekliği buton yüksekliğinin %42'si (büyük harf yüksekliğinin ~1.2 katı), ikon-yazı arası ikon
    // genişliğinin yarısı. centered: ikon ve yazı ortada bir grup (tek ana buton); değilse ikon soldan 32 px'te
    // sabit sütunda, yazı ortada (alt alta dizilen ikincil butonlar). Kontur yok, sert aşağı gölge var.
    static Button MakeButton(Transform parent, string name, string label, string icon, Vector2 size, Vector2 position, string style, int fontSize, bool centered)
    {
        // Büyük butonun kesik köşesi ve kalınlığı da orantılı büyüsün.
        float pixelScale = size.y >= 100f ? 0.8f : 1f;
        var button = PressableBase(parent, name, size, position, "Button" + style, pixelScale, true, out var content);
        var shade = style == "Coral" ? CoralDark : style == "Ocean" ? OceanDark : Navy;
        var text = NewText(content, "Label", label, fontSize, Color.white);
        Stretch(text.rectTransform);
        AddShadow(text, shade, 3f, style == "Ghost" ? 0.6f : 1f);
        if (icon == null)
            return button;

        float iconSize = Mathf.Round(size.y * 0.42f);
        var iconRect = NewImage(content, "Icon", icon, Color.white);
        AddShadow(iconRect.GetComponent<Image>(), shade, 3f, style == "Ghost" ? 0.6f : 1f);
        if (centered)
        {
            float gap = iconSize * 0.5f;
            float group = iconSize + gap + text.preferredWidth;
            Place(iconRect, new Vector2(0.5f, 0.5f), new Vector2(iconSize, iconSize), new Vector2(-group * 0.5f + iconSize * 0.5f, 0f));
            text.alignment = TextAnchor.MiddleLeft;
            Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(text.preferredWidth + 8f, size.y), new Vector2(-group * 0.5f + iconSize + gap + text.preferredWidth * 0.5f + 4f, 0f));
        }
        else
            Place(iconRect, new Vector2(0f, 0.5f), new Vector2(iconSize, iconSize), new Vector2(32f + iconSize * 0.5f, 0f));
        return button;
    }

    // Kare okyanus butonu, içinde ok (oyna ikonu döndürülerek).
    static Button ArrowButton(Transform parent, string name, Vector2 position, bool left)
    {
        var button = PressableBase(parent, name, new Vector2(62f, 62f), position, "SquareOcean", 1f, false, out var content);
        var arrow = NewImage(content, "Arrow", SpriteDir + "IconPlay.png", Color.white);
        arrow.sizeDelta = new Vector2(24f, 24f);
        if (left)
            arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
        AddShadow(arrow.GetComponent<Image>(), OceanDark, left ? -2f : 2f, 1f);
        return button;
    }

    static void RowLabel(RectTransform card, string name, string text, float y)
    {
        var label = NewText(card, name + " Label", text, 34, Foam, TextAnchor.MiddleLeft);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(220f, 44f), new Vector2(-180f, y));
    }

    // "<  değer  >" seçicisi; değer metnini döner.
    static Text OptionPicker(RectTransform card, string name, float y, out Button prev, out Button next)
    {
        prev = ArrowButton(card, name + " Prev", new Vector2(20f, y), true);
        next = ArrowButton(card, name + " Next", new Vector2(250f, y), false);
        var value = NewText(card, name + " Value", "-", 32, Water);
        Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(160f, 44f), new Vector2(135f, y));
        return value;
    }

    static Slider MakeSlider(RectTransform card, string name, Vector2 position)
    {
        var root = NewRect(card, name, new Vector2(270f, 52f), position);
        var track = Sliced(NewImage(root, "Track", SpriteDir + "Bar.png", Alpha(Color.white, 0.18f)), 1f);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, 20f);

        var fillArea = NewRect(root, "Fill Area", Vector2.zero, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(0f, 20f);
        var fill = Sliced(NewImage(fillArea, "Fill", SpriteDir + "Bar.png", Water), 1f);
        fill.sizeDelta = Vector2.zero;

        var handleArea = Stretch(NewRect(root, "Handle Area", Vector2.zero, Vector2.zero), new Vector2(14f, 0f), new Vector2(-14f, 0f));
        var handle = NewImage(handleArea, "Handle", SpriteDir + "Handle.png", Color.white);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        handle.sizeDelta = new Vector2(36f, 0f);
        handle.GetComponent<Image>().preserveAspect = true;
        // NewImage resimleri tıklamaya kapatır; kaydırıcının izi ve tutamağı fareyi yakalamalı, yoksa sürüklenemez.
        track.GetComponent<Image>().raycastTarget = true;
        handle.GetComponent<Image>().raycastTarget = true;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = slider.colors;
        colors.highlightedColor = new Color(1f, 0.92f, 0.88f);
        colors.pressedColor = new Color(0.85f, 0.8f, 0.78f);
        slider.colors = colors;
        slider.value = 0.8f;
        return slider;
    }

    // ---------------------------------------------------------------- Yardımcılar

    static RectTransform NewRect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    static RectTransform NewImage(Transform parent, string name, string spritePath, Color color)
    {
        var rect = NewRect(parent, name, new Vector2(100f, 100f), Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>();
        if (spritePath != null)
            image.sprite = LoadSprite(spritePath);
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    static Text NewText(Transform parent, string name, string value, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var rect = NewRect(parent, name, new Vector2(400f, size * 1.4f), Vector2.zero);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void AddShadow(Graphic graphic, Color color, float distance, float alpha)
    {
        var shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(color.r, color.g, color.b, alpha);
        shadow.effectDistance = new Vector2(0f, -distance);
    }

    static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static RectTransform Stretch(RectTransform rect) => Stretch(rect, Vector2.zero, Vector2.zero);

    static RectTransform Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    // pixelScale < 1 köşeleri ve kenarları büyütür.
    static RectTransform Sliced(RectTransform rect, float pixelScale)
    {
        var image = rect.GetComponent<Image>();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = pixelScale;
        return rect;
    }

    static void SetArray(SerializedProperty property, params Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static Sprite LoadSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        var border = Border(path);
        if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogWarning($"Menü sprite'ı bulunamadı: {path} (Art/UI/make_menu_sprites.py ile üretilir).");
        return sprite;
    }

    // Sprite kenar payları (sol, alt, sağ, üst). Butonlarda alt pay kalınlığı da kapsar.
    static Vector4 Border(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (name.StartsWith("Button") || name.StartsWith("Square"))
        {
            if (name.EndsWith("Ghost") || name.EndsWith("GhostPressed"))
                return new Vector4(14f, 14f, 14f, 14f);
            return name.EndsWith("Pressed") ? new Vector4(14f, 16f, 14f, 14f) : new Vector4(14f, 14f + LipPixels, 14f, 14f);
        }
        switch (name)
        {
            case "Panel":
                return new Vector4(18f, 18f, 18f, 18f);
            case "Bar":
                return new Vector4(6f, 0f, 6f, 0f);
            case "Shadow":
                return new Vector4(44f, 44f, 44f, 44f);
            default:
                return Vector4.zero;
        }
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var color);
        return color;
    }

    static Color Alpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}
