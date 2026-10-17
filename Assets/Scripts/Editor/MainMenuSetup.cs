using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Açık sahneye ana menüyü kurar: arayüz (Main Menu canvas'ı) ve kameraya CameraIntro.
// Menü: Tools > BigEs > Ana Menüyü Kur (varsa silip yeniden kurar).
public static class MainMenuSetup
{
    const string RootName = "Main Menu";
    const string SpriteDir = "Assets/UI/Menu/";

    // Oyunun paleti: ahşap, kırmızı çatı, krem sıva, sarı vurgu.
    static readonly Color Cream = Hex("FFF4DC");
    static readonly Color CreamLip = Hex("D9C29A");
    static readonly Color Wood = Hex("9A6E43");
    static readonly Color WoodLip = Hex("5E3F24");
    static readonly Color Red = Hex("E8524A");
    static readonly Color RedLip = Hex("A8322E");
    static readonly Color Ink = Hex("4A3220");
    static readonly Color Yellow = Hex("FFE389");
    static readonly Color Backdrop = new Color(0.17f, 0.11f, 0.06f, 0.55f);

    static Font font;

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

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var root = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Kenarları koyulaştıran perde, arkadaki harita tıklanamasın diye ışınları da tutar.
        var backdrop = Stretch(NewImage(root.transform, "Backdrop", "Vignette.png", Backdrop));
        backdrop.GetComponent<Image>().raycastTarget = true;

        var mainPage = Page(root.transform, "Main Page");
        var items = BuildMainPage(mainPage.transform, out var highScore, out var play, out var howTo, out var settings, out var quit);

        var settingsPage = Page(root.transform, "Settings Page");
        var settingsBack = BuildSettingsPage(settingsPage.gameObject);

        var howToPage = Page(root.transform, "How To Page");
        var howToBack = BuildHowToPage(howToPage.transform);

        var menu = root.AddComponent<MainMenu>();
        var so = new SerializedObject(menu);
        so.FindProperty("cameraIntro").objectReferenceValue = intro;
        so.FindProperty("menuGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("mainPage").objectReferenceValue = mainPage;
        so.FindProperty("settingsPage").objectReferenceValue = settingsPage;
        so.FindProperty("howToPage").objectReferenceValue = howToPage;
        so.FindProperty("highScoreText").objectReferenceValue = highScore;
        so.FindProperty("playButton").objectReferenceValue = play;
        so.FindProperty("howToButton").objectReferenceValue = howTo;
        so.FindProperty("settingsButton").objectReferenceValue = settings;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        SetArray(so.FindProperty("backButtons"), settingsBack, howToBack);
        SetArray(so.FindProperty("introItems"), items);

        var hud = Object.FindAnyObjectByType<ResourceHUD>(FindObjectsInactive.Include);
        if (hud != null)
        {
            var hudGroup = hud.GetComponent<CanvasGroup>();
            if (hudGroup == null)
                hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            SetArray(so.FindProperty("gameplayUI"), hudGroup);
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        settingsPage.gameObject.SetActive(false);
        howToPage.gameObject.SetActive(false);
        return true;
    }

    // ---------------------------------------------------------------- Sayfalar

    static Object[] BuildMainPage(Transform page, out Text highScore, out Button play, out Button howTo, out Button settings, out Button quit)
    {
        // Logo: dalga amblemi, oyun adı ve alt başlık.
        var logo = NewRect(page, "Logo", new Vector2(900f, 260f), new Vector2(0f, 300f));
        var waves = NewImage(logo, "Waves", "Waves.png", Yellow);
        waves.sizeDelta = new Vector2(84f, 84f);
        waves.anchoredPosition = new Vector2(0f, 100f);
        var title = NewText(logo, "Title", "BigEs", 132, Cream, FontStyle.Bold);
        title.rectTransform.sizeDelta = new Vector2(900f, 150f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        var outline = title.gameObject.AddComponent<Outline>();
        outline.effectColor = WoodLip;
        outline.effectDistance = new Vector2(4f, -4f);
        var drop = title.gameObject.AddComponent<Shadow>();
        drop.effectColor = new Color(WoodLip.r, WoodLip.g, WoodLip.b, 0.8f);
        drop.effectDistance = new Vector2(0f, -10f);
        var subtitle = NewText(logo, "Subtitle", "KÖYÜ TSUNAMİDEN KORU", 30, Yellow, FontStyle.Bold);
        subtitle.rectTransform.sizeDelta = new Vector2(900f, 40f);
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, -96f);
        AddShadow(subtitle, WoodLip, 3f);

        // En yüksek skor kartı.
        var scoreCard = Card(page, "High Score", new Vector2(400f, 96f), new Vector2(0f, 120f), Cream, CreamLip, 7f, out var scoreFace);
        var badge = NewImage(scoreFace, "Badge", "Circle.png", Red);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.sizeDelta = new Vector2(62f, 62f);
        badge.anchoredPosition = new Vector2(52f, 0f);
        var trophy = NewImage(badge, "Trophy", "IconTrophy.png", Yellow);
        trophy.sizeDelta = new Vector2(38f, 38f);
        var label = NewText(scoreFace, "Label", "EN YÜKSEK SKOR", 20, Wood, FontStyle.Bold, TextAnchor.LowerLeft);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(280f, 26f);
        label.rectTransform.anchoredPosition = new Vector2(98f, 16f);
        highScore = NewText(scoreFace, "Value", "0", 40, Ink, FontStyle.Bold, TextAnchor.UpperLeft);
        highScore.rectTransform.anchorMin = highScore.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        highScore.rectTransform.pivot = new Vector2(0f, 0.5f);
        highScore.rectTransform.sizeDelta = new Vector2(280f, 46f);
        highScore.rectTransform.anchoredPosition = new Vector2(98f, -14f);

        play = MakeButton(page, "Play Button", "OYNA", "IconPlay.png", new Vector2(520f, 104f), new Vector2(0f, -10f), Red, RedLip, 46, 10f);
        howTo = MakeButton(page, "How To Button", "NASIL OYNANIR", "IconBook.png", new Vector2(520f, 80f), new Vector2(0f, -122f), Wood, WoodLip, 30, 8f);
        settings = MakeButton(page, "Settings Button", "AYARLAR", "IconGear.png", new Vector2(520f, 80f), new Vector2(0f, -218f), Wood, WoodLip, 30, 8f);
        quit = MakeButton(page, "Quit Button", "ÇIKIŞ", "IconExit.png", new Vector2(520f, 80f), new Vector2(0f, -314f), Wood, WoodLip, 30, 8f);

        var footer = NewText(page, "Footer", "Civil Game Jam 2026", 20, new Color(Cream.r, Cream.g, Cream.b, 0.7f), FontStyle.Normal);
        footer.rectTransform.anchorMin = footer.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        footer.rectTransform.sizeDelta = new Vector2(600f, 30f);
        footer.rectTransform.anchoredPosition = new Vector2(0f, 34f);

        return new Object[]
        {
            logo, scoreCard, (RectTransform)play.transform, (RectTransform)howTo.transform,
            (RectTransform)settings.transform, (RectTransform)quit.transform,
        };
    }

    static Button BuildSettingsPage(GameObject page)
    {
        var card = Card(page.transform, "Card", new Vector2(720f, 540f), new Vector2(0f, -10f), Cream, CreamLip, 9f, out var face);
        Header(face, "AYARLAR", "IconGear.png");

        RowLabel(face, "Volume", "Ses", 120f);
        var slider = MakeSlider(face, new Vector2(70f, 120f));
        var volumeValue = NewText(face, "Volume Value", "%80", 28, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
        volumeValue.rectTransform.sizeDelta = new Vector2(110f, 40f);
        volumeValue.rectTransform.anchoredPosition = new Vector2(285f, 120f);

        RowLabel(face, "Fullscreen", "Tam Ekran", 30f);
        var fullscreen = OptionPicker(face, "Fullscreen", 30f, out var fsPrev, out var fsNext);

        RowLabel(face, "Quality", "Grafik", -60f);
        var quality = OptionPicker(face, "Quality", -60f, out var qPrev, out var qNext);

        var back = MakeButton(face, "Back Button", "GERİ", null, new Vector2(300f, 72f), new Vector2(0f, -185f), Wood, WoodLip, 28, 7f);

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

    static Button BuildHowToPage(Transform page)
    {
        Card(page, "Card", new Vector2(820f, 700f), new Vector2(0f, -10f), Cream, CreamLip, 9f, out var face);
        Header(face, "NASIL OYNANIR", "IconBook.png");

        var body = NewText(face, "Body",
            "<b>Amaç:</b> Tsunami gelmeden köyün çevresine duvar ör, köyü koru.\n\n" +
            "• Köylüler ormandan odun, vagonlar madenden taş toplayıp köyün ortasındaki kuyuya atar.\n" +
            "• Ağaçlara ve maden ocaklarına tıklayarak toplamayı hızlandırabilirsin.\n" +
            "• Kuyuda biriken odun ve taşla duvarını inşa et.\n\n" +
            "<b>Kontroller</b>\n" +
            "Sol tık: kaynak topla\n" +
            "Sağ tık + sürükle: haritayı kaydır\n" +
            "Fare tekerleği: yakınlaş / uzaklaş",
            25, Ink, FontStyle.Normal, TextAnchor.UpperLeft);
        body.supportRichText = true;
        body.lineSpacing = 1.1f;
        var bodyRect = body.rectTransform;
        bodyRect.anchorMin = bodyRect.anchorMax = bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.sizeDelta = new Vector2(700f, 430f);
        bodyRect.anchoredPosition = new Vector2(0f, -132f);

        var back = MakeButton(face, "Back Button", "GERİ", null, new Vector2(300f, 72f), Vector2.zero, Wood, WoodLip, 28, 7f);
        var backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 70f);
        return back;
    }

    // ---------------------------------------------------------------- Parçalar

    static CanvasGroup Page(Transform parent, string name)
    {
        var rect = Stretch(NewRect(parent, name, Vector2.zero, Vector2.zero));
        return rect.gameObject.AddComponent<CanvasGroup>();
    }

    // Altında koyu bir kenar (kalınlık) ve yumuşak gölge olan yuvarlak kart. face: içerik buraya konur.
    static RectTransform Card(Transform parent, string name, Vector2 size, Vector2 position, Color faceColor, Color lipColor, float depth, out RectTransform face)
    {
        var root = NewRect(parent, name, size, position);
        var shadow = NewImage(root, "Shadow", "Shadow.png", new Color(0f, 0f, 0f, 0.35f));
        Stretch(shadow, new Vector2(-26f, -36f), new Vector2(26f, 14f));
        Sliced(shadow, 1f);
        var lip = Stretch(NewImage(root, "Lip", "Rounded.png", lipColor));
        Sliced(lip, 0.7f);
        face = Stretch(NewImage(root, "Face", "Rounded.png", faceColor), new Vector2(0f, depth), Vector2.zero);
        Sliced(face, 0.7f);
        return root;
    }

    static Button MakeButton(Transform parent, string name, string label, string icon, Vector2 size, Vector2 position, Color faceColor, Color lipColor, int fontSize, float depth)
    {
        var root = NewRect(parent, name, size, position);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear; // Tıklama alanı; yüz hareket etse de sabit kalır.

        var shadow = NewImage(root, "Shadow", "Shadow.png", new Color(0f, 0f, 0f, 0.3f));
        Stretch(shadow, new Vector2(-18f, -26f), new Vector2(18f, 8f));
        Sliced(shadow, 1f);
        shadow.GetComponent<Image>().raycastTarget = false;
        var lip = Stretch(NewImage(root, "Lip", "Rounded.png", lipColor));
        Sliced(lip, 1f);
        lip.GetComponent<Image>().raycastTarget = false;
        var face = Stretch(NewImage(root, "Face", "Rounded.png", faceColor), new Vector2(0f, depth), Vector2.zero);
        Sliced(face, 1f);
        var faceImage = face.GetComponent<Image>();
        faceImage.raycastTarget = false;

        var text = NewText(face, "Label", label, fontSize, Cream, FontStyle.Bold);
        Stretch(text.rectTransform);
        AddShadow(text, lipColor, 2f);
        if (icon != null)
        {
            var iconRect = NewImage(face, "Icon", icon, Cream);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            float s = size.y * 0.42f;
            iconRect.sizeDelta = new Vector2(s, s);
            iconRect.anchoredPosition = new Vector2(size.y * 0.55f, 0f);
            iconRect.GetComponent<Image>().raycastTarget = false;
        }

        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var pressable = root.gameObject.AddComponent<PressableButton>();
        var so = new SerializedObject(pressable);
        so.FindProperty("face").objectReferenceValue = face;
        so.FindProperty("faceImage").objectReferenceValue = faceImage;
        so.FindProperty("depth").floatValue = depth;
        so.ApplyModifiedPropertiesWithoutUndo();
        return button;
    }

    static void Header(RectTransform card, string title, string icon)
    {
        var header = NewText(card, "Header", title, 44, Ink, FontStyle.Bold);
        header.rectTransform.anchorMin = header.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        header.rectTransform.sizeDelta = new Vector2(600f, 60f);
        header.rectTransform.anchoredPosition = new Vector2(0f, -62f);
        var iconRect = NewImage(card, "Header Icon", icon, Red);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.sizeDelta = new Vector2(46f, 46f);
        float half = header.preferredWidth * 0.5f;
        iconRect.anchoredPosition = new Vector2(-half - 40f, -62f);
        var divider = NewImage(card, "Divider", "RoundedSmall.png", CreamLip);
        divider.anchorMin = divider.anchorMax = new Vector2(0.5f, 1f);
        divider.sizeDelta = new Vector2(card.rect.width > 0 ? card.rect.width - 120f : 560f, 4f);
        divider.anchoredPosition = new Vector2(0f, -108f);
    }

    static Text RowLabel(RectTransform card, string name, string text, float y)
    {
        var label = NewText(card, name + " Label", text, 30, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
        label.rectTransform.sizeDelta = new Vector2(220f, 44f);
        label.rectTransform.anchoredPosition = new Vector2(-225f, y);
        return label;
    }

    // "<  değer  >" seçicisi; değer metnini döner.
    static Text OptionPicker(RectTransform card, string name, float y, out Button prev, out Button next)
    {
        prev = MakeButton(card, name + " Prev", "‹", null, new Vector2(60f, 60f), new Vector2(0f, y), Wood, WoodLip, 40, 6f);
        next = MakeButton(card, name + " Next", "›", null, new Vector2(60f, 60f), new Vector2(240f, y), Wood, WoodLip, 40, 6f);
        var value = NewText(card, name + " Value", "-", 28, Ink, FontStyle.Bold);
        value.rectTransform.sizeDelta = new Vector2(170f, 44f);
        value.rectTransform.anchoredPosition = new Vector2(120f, y);
        return value;
    }

    static Slider MakeSlider(RectTransform card, Vector2 position)
    {
        var root = NewRect(card, "Volume Slider", new Vector2(260f, 44f), position);
        var track = NewImage(root, "Track", "RoundedSmall.png", CreamLip);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, 16f);
        Sliced(track, 1f);

        var fillArea = NewRect(root, "Fill Area", Vector2.zero, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(0f, 16f);
        var fill = NewImage(fillArea, "Fill", "RoundedSmall.png", Wood);
        fill.sizeDelta = Vector2.zero;
        Sliced(fill, 1f);

        var handleArea = NewRect(root, "Handle Area", Vector2.zero, Vector2.zero);
        Stretch(handleArea, new Vector2(16f, 0f), new Vector2(-16f, 0f));
        var handle = NewImage(handleArea, "Handle", "Circle.png", Red);
        handle.sizeDelta = new Vector2(38f, -6f);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        var ring = handle.gameObject.AddComponent<Outline>();
        ring.effectColor = RedLip;
        ring.effectDistance = new Vector2(0f, -3f);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = slider.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
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

    static RectTransform NewImage(Transform parent, string name, string sprite, Color color)
    {
        var rect = NewRect(parent, name, new Vector2(100f, 100f), Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = LoadSprite(SpriteDir + sprite);
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    static Text NewText(Transform parent, string name, string value, int size, Color color, FontStyle style, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var rect = NewRect(parent, name, new Vector2(400f, size * 1.4f), Vector2.zero);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void AddShadow(Text text, Color color, float distance)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(color.r, color.g, color.b, 0.7f);
        shadow.effectDistance = new Vector2(0f, -distance);
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

    static void Sliced(RectTransform rect, float cornerScale)
    {
        var image = rect.GetComponent<Image>();
        image.type = Image.Type.Sliced;
        // Küçük değer köşeleri büyütür.
        image.pixelsPerUnitMultiplier = 1f / cornerScale;
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

    static Vector4 Border(string path)
    {
        if (path.EndsWith("/Rounded.png"))
            return new Vector4(20f, 20f, 20f, 20f);
        if (path.EndsWith("/RoundedSmall.png"))
            return new Vector4(10f, 10f, 10f, 10f);
        if (path.EndsWith("/Shadow.png"))
            return new Vector4(44f, 44f, 44f, 44f);
        return Vector4.zero;
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var color);
        return color;
    }
}
