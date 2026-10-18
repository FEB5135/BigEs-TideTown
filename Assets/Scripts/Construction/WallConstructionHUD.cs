using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace IdleTown.Construction
{
    public sealed class WallConstructionHUD : MonoBehaviour
    {
        static WallConstructionHUD current;
        static readonly string[] Names = { "Tahta", "Taş", "Güçlü", "Silme" };
        static readonly Color Ink = new(.28f, .18f, .10f);
        static readonly Color Selected = new(1f, .87f, .55f);
        readonly Vector3[] corners = new Vector3[4];
        readonly Button[] cards = new Button[4];
        readonly Text[] labels = new Text[4];
        WallConstructionController controller;
        Canvas canvas;
        RectTransform panel, content, resourceTemplate;
        Button open, lower, higher, automatic, confirm;
        Text floor, autoLabel, summary, buildLabel;
        Font font;
        Sprite backgroundSprite;
        Color backgroundColor;
        static readonly Vector2 CardSize = new(230f, 88f);

        public static RectTransform Toolbar => current != null && current.content.gameObject.activeInHierarchy ? current.content : null;
        internal static Rect VisibleRect
        {
            get
            {
                if (current == null || current.panel == null || !current.panel.gameObject.activeInHierarchy) return UnityEngine.Rect.zero;
                current.panel.GetWorldCorners(current.corners);
                var min = current.corners[0]; var max = current.corners[2];
                return UnityEngine.Rect.MinMaxRect(Mathf.Floor(min.x), Mathf.Floor(Screen.height - max.y),
                    Mathf.Ceil(max.x), Mathf.Ceil(Screen.height - min.y));
            }
        }

        internal void Initialize(WallConstructionController source)
        {
            controller = source;
            current = this;
            resourceTemplate = FindAnyObjectByType<UpgradeHUD>()?.ResourceCardTemplate;
            font = resourceTemplate != null ? resourceTemplate.Find("Amount")?.GetComponent<Text>()?.font : null;
            if (font == null) font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name.Contains("Rubik"));
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var sample = resourceTemplate != null ? resourceTemplate.GetComponent<Image>() : null;
            backgroundSprite = sample != null ? sample.sprite : null;
            backgroundColor = sample != null ? sample.color : new Color(.61f, .44f, .27f);
            var root = new GameObject("Construction HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            panel = Rect("Construction tools", root.transform, Vector2.zero, new Vector2(956, 192));
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            content = Rect("Expanded toolbar", panel, Vector2.zero, panel.sizeDelta);
            var controls = Rect("Height and confirmation", content, Vector2.zero, new Vector2(956, 54));
            Background(controls);
            floor = Label("Height", controls, new Vector2(18, 13), new Vector2(180, 28), 23);
            lower = SmallButton("Down", controls, 200, 44, "↓", () => controller.SetSelectedFloor(controller.SelectedFloor - 1), out _);
            higher = SmallButton("Up", controls, 250, 44, "↑", () => controller.SetSelectedFloor(controller.SelectedFloor + 1), out _);
            automatic = SmallButton("Automatic height", controls, 310, 220, "", () => controller.SetAutomaticHeight(!controller.AutomaticHeight), out autoLabel);
            confirm = SmallButton("Confirm", controls, 544, 332, "İnşa et · Enter", () => controller.ConfirmLastValidPreview(), out buildLabel);
            SmallButton("Close construction", controls, 890, 48, "×", () => controller.SetConstructionMode(false), out _);
            summary = Label("Preview", content, new Vector2(8, 63), new Vector2(940, 30), 19);
            summary.color = Color.white;
            var outline = summary.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, .85f); outline.effectDistance = new Vector2(1, -1);
            for (int i = 0; i < cards.Length; i++)
            {
                int kind = i;
                cards[i] = Card(Names[i], content, new Vector2(i * 242, 104), i, out labels[i]);
                cards[i].onClick.AddListener(() => { if (kind == 3) controller.SetDemolitionMode(!controller.DemolitionMode); else { controller.SetDemolitionMode(false); controller.SetSelectedTier((WallTier)kind); } });
            }
            open = Card("Open construction", panel, Vector2.zero, 0, out var openLabel);
            openLabel.text = "İnşa\n<size=19>B</size>";
            open.onClick.AddListener(() => controller.SetConstructionMode(true));
            Refresh();
        }

        void LateUpdate() { if (controller != null && panel != null) Refresh(); }

        void Refresh()
        {
            bool visible = controller.isActiveAndEnabled && !MainMenu.IsOpen;
            panel.gameObject.SetActive(visible);
            if (!visible) return;
            bool expanded = controller.ConstructionModeActive;
            content.gameObject.SetActive(expanded);
            open.gameObject.SetActive(!expanded);
            panel.sizeDelta = expanded ? new Vector2(956, 192) : CardSize;
            float left = 540f;
            if (resourceTemplate != null)
            {
                resourceTemplate.GetWorldCorners(corners);
                left = corners[2].x / Mathf.Max(canvas.scaleFactor, .001f) + 16f;
            }
            float available = Screen.width / Mathf.Max(canvas.scaleFactor, .001f) - left - 24f;
            float scale = Mathf.Clamp01(available / 956f);
            panel.localScale = Vector3.one * (expanded ? Mathf.Max(.55f, scale) : 1f);
            panel.anchoredPosition = expanded ? new Vector2(left, 24) : new Vector2(24, 140);
            if (!expanded) return;
            bool deleting = controller.DemolitionMode;
            for (int i = 0; i < cards.Length; i++)
            {
                bool selected = i == 3 ? deleting : !deleting && controller.SelectedTier == (WallTier)i;
                var colors = cards[i].colors;
                colors.normalColor = selected ? Selected : Color.white;
                colors.selectedColor = colors.normalColor;
                colors.highlightedColor = new Color(1.08f, 1.03f, .85f);
                colors.pressedColor = new Color(.83f, .72f, .5f);
                cards[i].colors = colors;
                if (i == 3) labels[i].text = "Silme\n<size=19>N</size>";
                else
                {
                    var cost = controller.CostFor((WallTier)i);
                    string price = cost.Wood == 0 ? $"{cost.Stone} taş" : cost.Stone == 0 ? $"{cost.Wood} odun" : $"{cost.Wood} odun + {cost.Stone} taş";
                    labels[i].text = $"{Names[i]}\n<size=17>{i + 1} · {price}</size>";
                }
            }
            floor.text = $"Kat {controller.SelectedFloor + 1} · {(controller.SelectedFloor + 1) * WallConstructionController.StoreyHeight:0} m";
            autoLabel.text = controller.AutomaticHeight ? "Oto kat: Açık" : "Oto kat: Kapalı";
            lower.interactable = !deleting && controller.SelectedFloor > 0;
            higher.interactable = !deleting && controller.SelectedFloor + 1 < controller.MaxFloors;
            automatic.interactable = !deleting;
            confirm.interactable = !deleting && controller.CanConfirm;
            buildLabel.text = deleting ? "Silmek için duvara tıkla" : "İnşa et · Enter";
            summary.text = deleting ? "Sarı çerçeveli duvarı sol tıkla sil · Üst katları da kaldırılır · İade yok"
                : controller.HasSelection ? controller.PreviewSummary + "  ·  " + controller.BuildStatus
                : "Sol tık: başlangıç / bitiş  ·  ↑ ↓: kat  ·  Sağ tık / Esc: iptal  ·  B: çık";
            summary.color = controller.InvalidPreview && !deleting ? new Color(1f, .65f, .4f) : Color.white;
        }

        Button Card(string name, Transform parent, Vector2 position, int kind, out Text label)
        {
            RectTransform rect;
            if (resourceTemplate != null)
            {
                rect = Instantiate(resourceTemplate, parent, false);
                rect.name = name;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(position.x, -position.y);
                rect.sizeDelta = CardSize;
                rect.localScale = Vector3.one;
            }
            else { rect = Rect(name, parent, position, CardSize); Background(rect); }
            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick = new Button.ButtonClickedEvent();
            label = rect.Find("Amount")?.GetComponent<Text>();
            if (label == null) label = Label("Amount", rect, new Vector2(87, 14), new Vector2(133, 64), 26);
            label.font = font; label.fontSize = 26; label.supportRichText = true;
            label.color = Color.white; label.raycastTarget = false; label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var lr = label.rectTransform;
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0, 1);
            lr.anchoredPosition = new Vector2(87, -9); lr.sizeDelta = new Vector2(137, 70);
            var slot = rect.Find("Icon Slot") as RectTransform;
            if (slot == null) slot = Rect("Icon Slot", rect, new Vector2(12, 14), new Vector2(64, 60));
            foreach (var old in slot.GetComponentsInChildren<Graphic>()) if (old.transform != slot) old.enabled = false;
            var iconRect = Rect("Wall icon", slot, new Vector2(3, 3), new Vector2(58, 54));
            var icon = iconRect.gameObject.AddComponent<WallToolbarIcon>();
            icon.Initialize(kind);
            return button;
        }

        Button SmallButton(string name, Transform parent, float x, float width, string caption, UnityEngine.Events.UnityAction action, out Text label)
        {
            var rect = Rect(name, parent, new Vector2(x, 7), new Vector2(width, 40));
            var image = Background(rect);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            label = Label("Label", rect, Vector2.zero, rect.sizeDelta, 21); label.text = caption; label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(action);
            return button;
        }

        Image Background(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = backgroundSprite; image.type = Image.Type.Sliced; image.color = backgroundColor;
            return image;
        }
        Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.color = Ink; text.supportRichText = true; text.raycastTarget = false;
            text.fontStyle = FontStyle.Bold; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(position.x, -position.y); rect.sizeDelta = size;
            return rect;
        }
        void OnDestroy() { if (current == this) current = null; }
    }
}
