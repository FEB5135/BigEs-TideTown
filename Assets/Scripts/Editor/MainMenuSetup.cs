using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Açık sahneye ana menüyü kurar: arayüz (Main Menu canvas'ı) ve kameraya CameraIntro.
// Menü: Tools > BigEs > Ana Menüyü Kur (varsa silip yeniden kurar).
// Buton ve paneller Kenney UI Pack 2.0'ın oyunun paletine göre yeniden renklendirilmiş halleri
// (Art/UI/adapt_kenney.py), yazı tipi Baloo 2 ExtraBold.
public static class MainMenuSetup
{
    const string RootName = "Main Menu";
    const string SpriteDir = "Assets/UI/Menu/";
    const string KenneyDir = "Assets/UI/Menu/Kenney/";
    const string FontPath = "Assets/UI/Fonts/Baloo2-ExtraBold.ttf";
    const string ClickPath = "Assets/Audio/UI/Click.ogg";

    // Oyunun paleti: ahşap, kırmızı çatı, krem sıva, sarı vurgu.
    static readonly Color Cream = Hex("FFF4DC");
    static readonly Color Wood = Hex("9A6E43");
    static readonly Color WoodDark = Hex("5E3F24");
    static readonly Color Red = Hex("E8524A");
    static readonly Color RedDark = Hex("A8322E");
    static readonly Color Ink = Hex("4A3220");
    static readonly Color Yellow = Hex("FFE389");
    static readonly Color Backdrop = new Color(0.17f, 0.11f, 0.06f, 0.5f);

    // Kenney sprite'ları 64 px yüksekliğinde, alt kalınlık 6 px. Ekranda büyütülerek kullanılır.
    const float ButtonPixelScale = 0.6f; // Sliced pixelsPerUnitMultiplier: köşeler 1/0.6 kat büyür.
    const float KenneyDepth = 6f;

    static Font font;
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

        // Kenarları koyulaştıran perde, arkadaki harita tıklanamasın diye ışınları da tutar.
        var backdrop = Stretch(NewImage(root.transform, "Backdrop", SpriteDir + "Vignette.png", Backdrop));
        backdrop.GetComponent<Image>().raycastTarget = true;

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

    static Object[] BuildMainPage(Transform page, out Text highScore, out Button play, out Button settings, out Button quit)
    {
        // Logo: dalga amblemi, oyun adı ve alt başlık. Oyunun logosu gelince bu obje değiştirilir.
        var logo = NewRect(page, "Logo", new Vector2(900f, 280f), new Vector2(0f, 300f));
        var waves = NewImage(logo, "Waves", SpriteDir + "Waves.png", Yellow);
        waves.sizeDelta = new Vector2(70f, 70f);
        waves.anchoredPosition = new Vector2(0f, 112f);
        AddShadow(waves.GetComponent<Image>(), WoodDark, 4f, 0.9f);

        var title = NewText(logo, "Title", "BigEs", 156, Cream);
        title.rectTransform.sizeDelta = new Vector2(900f, 170f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 12f);
        var outline = title.gameObject.AddComponent<Outline>();
        outline.effectColor = WoodDark;
        outline.effectDistance = new Vector2(5f, 5f);
        var outline2 = title.gameObject.AddComponent<Outline>();
        outline2.effectColor = WoodDark;
        outline2.effectDistance = new Vector2(3f, 3f);
        AddShadow(title, WoodDark, 12f, 0.85f);

        var subtitle = NewText(logo, "Subtitle", "KÖYÜ TSUNAMİDEN KORU", 36, Yellow);
        subtitle.rectTransform.sizeDelta = new Vector2(900f, 46f);
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, -96f);
        var subOutline = subtitle.gameObject.AddComponent<Outline>();
        subOutline.effectColor = WoodDark;
        subOutline.effectDistance = new Vector2(2f, 2f);
        AddShadow(subtitle, WoodDark, 4f, 0.85f);

        // En yüksek skor rozeti.
        const float scoreScale = 0.55f;
        var score = NewRect(page, "High Score", new Vector2(360f, 86f), new Vector2(0f, 128f));
        Sliced(Stretch(NewImage(score, "Background", KenneyDir + "PanelCream.png", Color.white)), scoreScale);
        var scoreContent = Stretch(NewRect(score, "Content", Vector2.zero, Vector2.zero), new Vector2(0f, KenneyDepth / scoreScale), Vector2.zero);
        var badge = NewImage(scoreContent, "Badge", SpriteDir + "Circle.png", Red);
        Place(badge, new Vector2(0f, 0.5f), new Vector2(56f, 56f), new Vector2(46f, 0f));
        var badgeRing = badge.gameObject.AddComponent<Outline>();
        badgeRing.effectColor = RedDark;
        badgeRing.effectDistance = new Vector2(0f, -3f);
        var trophy = NewImage(badge, "Trophy", SpriteDir + "IconTrophy.png", Yellow);
        trophy.sizeDelta = new Vector2(34f, 34f);
        var label = NewText(scoreContent, "Label", "EN YÜKSEK SKOR", 20, Wood, TextAnchor.MiddleLeft);
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, 24f), new Vector2(88f, 14f));
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        highScore = NewText(scoreContent, "Value", "0", 36, Ink, TextAnchor.MiddleLeft);
        Place(highScore.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, 40f), new Vector2(88f, -14f));
        highScore.rectTransform.pivot = new Vector2(0f, 0.5f);

        play = MakeButton(page, "Play Button", "OYNA", SpriteDir + "IconPlay.png", new Vector2(440f, 108f), new Vector2(0f, -8f), "Red", 58);
        settings = MakeButton(page, "Settings Button", "AYARLAR", SpriteDir + "IconGear.png", new Vector2(440f, 86f), new Vector2(0f, -130f), "Wood", 40);
        quit = MakeButton(page, "Quit Button", "ÇIKIŞ", SpriteDir + "IconExit.png", new Vector2(440f, 86f), new Vector2(0f, -232f), "Wood", 40);

        var footer = NewText(page, "Footer", "Civil Game Jam 2026", 22, new Color(Cream.r, Cream.g, Cream.b, 0.75f));
        Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(600f, 30f), new Vector2(0f, 36f));
        AddShadow(footer, WoodDark, 2f, 0.8f);

        return new Object[]
        {
            logo, score, (RectTransform)play.transform, (RectTransform)settings.transform, (RectTransform)quit.transform,
        };
    }

    static Button BuildSettingsPage(GameObject page)
    {
        const float panelScale = 0.5f;
        var card = NewRect(page.transform, "Card", new Vector2(680f, 500f), new Vector2(0f, -10f));
        var shadow = NewImage(card, "Shadow", SpriteDir + "Shadow.png", new Color(0f, 0f, 0f, 0.35f));
        Sliced(Stretch(shadow, new Vector2(-30f, -40f), new Vector2(30f, 16f)), 1f);
        Sliced(Stretch(NewImage(card, "Background", KenneyDir + "PanelCream.png", Color.white)), panelScale);
        var face = Stretch(NewRect(card, "Content", Vector2.zero, Vector2.zero), new Vector2(0f, KenneyDepth / panelScale), Vector2.zero);

        var header = NewText(face, "Header", "AYARLAR", 54, Ink);
        Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, 64f), new Vector2(0f, -58f));
        var headerIcon = NewImage(face, "Header Icon", SpriteDir + "IconGear.png", Red);
        Place(headerIcon, new Vector2(0.5f, 1f), new Vector2(46f, 46f), new Vector2(-header.preferredWidth * 0.5f - 38f, -58f));
        var divider = NewImage(face, "Divider", KenneyDir + "Divider.png", Color.white);
        Place(divider, new Vector2(0.5f, 1f), new Vector2(560f, 4f), new Vector2(0f, -104f));

        RowLabel(face, "Volume", "Ses", 92f);
        var slider = MakeSlider(face, new Vector2(95f, 92f));
        var volumeValue = NewText(face, "Volume Value", "%80", 30, Wood, TextAnchor.MiddleRight);
        Place(volumeValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(90f, 40f), new Vector2(285f, 92f));

        RowLabel(face, "Fullscreen", "Tam Ekran", 8f);
        var fullscreen = OptionPicker(face, "Fullscreen", 8f, out var fsPrev, out var fsNext);

        RowLabel(face, "Quality", "Grafik", -76f);
        var quality = OptionPicker(face, "Quality", -76f, out var qPrev, out var qNext);

        var back = MakeButton(face, "Back Button", "GERİ", null, new Vector2(260f, 80f), Vector2.zero, "Wood", 36);
        Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(260f, 80f), new Vector2(0f, 64f));

        var panel = page.AddComponent<SettingsPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("volumeSlider").objectReferenceValue = slider;
        so.FindProperty("volumeValue").objectReferenceValue = volumeValue;
        so.FindProperty("fullscreenPrev").objectReferenceValue = fsPrev;
        so.FindProperty("fullscreenNext").objectReferenceValue = fsNext;
        so.FindProperty("fullscreenValue").objectReferenceValue = fullscreen;
        so.FindProperty("qualityPrev").objectReferenceValue = qPrev;
        so.FindProperty("qualityNext").objectReferenceValue = qNext;
        so.FindProperty("qualityValue").objectReferenceValue = quality;
        so.ApplyModifiedPropertiesWithoutUndo();
        return back;
    }

    // ---------------------------------------------------------------- Parçalar

    static CanvasGroup Page(Transform parent, string name)
    {
        var rect = Stretch(NewRect(parent, name, Vector2.zero, Vector2.zero));
        return rect.gameObject.AddComponent<CanvasGroup>();
    }

    // Kalınlıklı Kenney butonu. style: "Red", "Wood" (Button<style>.png) ya da "SquareWood".
    static Button PressableBase(Transform parent, string name, Vector2 size, Vector2 position, string normalSprite, string pressedSprite, bool withShadow, out RectTransform content)
    {
        float depth = KenneyDepth / ButtonPixelScale;
        var root = NewRect(parent, name, size, position);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear; // Tıklama alanı; görsel değişse de sabit kalır.

        if (withShadow)
        {
            var shadow = NewImage(root, "Shadow", SpriteDir + "Shadow.png", new Color(0f, 0f, 0f, 0.28f));
            Sliced(Stretch(shadow, new Vector2(-16f, -24f), new Vector2(16f, 4f)), 1f);
        }

        var normal = LoadSprite(KenneyDir + normalSprite);
        var pressed = LoadSprite(KenneyDir + pressedSprite);
        var visual = Sliced(Stretch(NewImage(root, "Visual", null, Color.white)), ButtonPixelScale);
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

    static Button MakeButton(Transform parent, string name, string label, string icon, Vector2 size, Vector2 position, string style, int fontSize)
    {
        var button = PressableBase(parent, name, size, position, $"Button{style}.png", $"Button{style}Pressed.png", true, out var content);
        var shade = style == "Red" ? RedDark : WoodDark;
        var text = NewText(content, "Label", label, fontSize, Cream);
        Stretch(text.rectTransform);
        var textOutline = text.gameObject.AddComponent<Outline>();
        textOutline.effectColor = new Color(shade.r, shade.g, shade.b, 0.6f);
        textOutline.effectDistance = new Vector2(1.5f, 1.5f);
        AddShadow(text, shade, 3f, 0.9f);
        if (icon != null)
        {
            float s = size.y * 0.4f;
            var iconRect = NewImage(content, "Icon", icon, Cream);
            Place(iconRect, new Vector2(0f, 0.5f), new Vector2(s, s), new Vector2(size.y * 0.55f, 0f));
            AddShadow(iconRect.GetComponent<Image>(), shade, 3f, 0.9f);
        }
        return button;
    }

    // Kare ahşap buton, içinde ok (oyna ikonu döndürülerek).
    static Button ArrowButton(Transform parent, string name, Vector2 position, bool left)
    {
        var button = PressableBase(parent, name, new Vector2(62f, 62f), position, "SquareWood.png", "SquareWoodPressed.png", false, out var content);
        var arrow = NewImage(content, "Arrow", SpriteDir + "IconPlay.png", Cream);
        arrow.sizeDelta = new Vector2(24f, 24f);
        if (left)
            arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
        AddShadow(arrow.GetComponent<Image>(), WoodDark, left ? -2f : 2f, 0.9f);
        return button;
    }

    static void RowLabel(RectTransform card, string name, string text, float y)
    {
        var label = NewText(card, name + " Label", text, 34, Ink, TextAnchor.MiddleLeft);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(220f, 44f), new Vector2(-180f, y));
    }

    // "<  değer  >" seçicisi; değer metnini döner.
    static Text OptionPicker(RectTransform card, string name, float y, out Button prev, out Button next)
    {
        prev = ArrowButton(card, name + " Prev", new Vector2(20f, y), true);
        next = ArrowButton(card, name + " Next", new Vector2(250f, y), false);
        var value = NewText(card, name + " Value", "-", 32, Wood);
        Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(160f, 44f), new Vector2(135f, y));
        return value;
    }

    static Slider MakeSlider(RectTransform card, Vector2 position)
    {
        var root = NewRect(card, "Volume Slider", new Vector2(270f, 48f), position);
        var track = Sliced(NewImage(root, "Track", KenneyDir + "SliderTrack.png", Color.white), 0.75f);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, 22f);

        var fillArea = NewRect(root, "Fill Area", Vector2.zero, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(0f, 22f);
        var fill = Sliced(NewImage(fillArea, "Fill", KenneyDir + "SliderFill.png", Color.white), 0.75f);
        fill.sizeDelta = Vector2.zero;

        var handleArea = Stretch(NewRect(root, "Handle Area", Vector2.zero, Vector2.zero), new Vector2(14f, 0f), new Vector2(-14f, 0f));
        var handle = NewImage(handleArea, "Handle", KenneyDir + "SliderHandle.png", Color.white);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        handle.sizeDelta = new Vector2(36f, 0f);
        handle.GetComponent<Image>().preserveAspect = true;

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
            Debug.LogWarning($"Menü sprite'ı bulunamadı: {path} (Art/UI/ altındaki scriptlerle üretilir).");
        return sprite;
    }

    // Sprite kenar payları (sol, alt, sağ, üst).
    static Vector4 Border(string path)
    {
        switch (System.IO.Path.GetFileNameWithoutExtension(path))
        {
            case "ButtonRed":
            case "ButtonWood":
            case "SquareWood":
            case "PanelCream":
                return new Vector4(12f, 18f, 12f, 12f);
            case "ButtonRedPressed":
            case "ButtonWoodPressed":
            case "SquareWoodPressed":
                return new Vector4(12f, 12f, 12f, 12f);
            case "SliderTrack":
            case "SliderFill":
                return new Vector4(8f, 0f, 8f, 0f);
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
}
