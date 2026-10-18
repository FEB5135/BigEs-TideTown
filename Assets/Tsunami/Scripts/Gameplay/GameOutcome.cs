using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tsunami
{
    /// <summary>
    /// Ends the game after the tsunami. Lose: the moment loseHouses houses have fallen (by default the first one:
    /// the village has to be walled off completely). Win: the wave has passed without that, i.e. the tsunami has run for minWaveSeconds of simulation
    /// time and no house has fallen for calmSeconds (or maxWaveSeconds is over). When the tsunami comes as a sequence
    /// of waves (TsunamiController.UsesWaves), the game is won only once the last wave is over (WavesFinished). The end card shows the standing and
    /// destroyed houses and the score (standing houses x pointsPerHouse, saved to HighScore), then stops the game;
    /// "Tekrar Oyna" reloads the scene straight into play, "Ana Menü" reloads it to the menu.
    /// Needs no scene setup: like TsunamiVillageAlarm it adds itself to any scene with a TsunamiController.
    /// </summary>
    public class GameOutcome : MonoBehaviour
    {
        [Tooltip("This many destroyed houses lose the game.")]
        [SerializeField, Min(1)] int loseHouses = 1;
        [Tooltip("Simulation seconds after the release before the game can be won.")]
        [SerializeField] float minWaveSeconds = 35f;
        [Tooltip("Simulation seconds without a house falling that count as the wave having passed.")]
        [SerializeField] float calmSeconds = 12f;
        [Tooltip("Simulation seconds after the release when the game is won anyway, if not lost.")]
        [SerializeField] float maxWaveSeconds = 120f;
        [SerializeField] int pointsPerHouse = 10;
        [Tooltip("Real seconds between the last house that loses the game and the end card.")]
        [SerializeField] float loseDelay = 3f;

        static readonly Color Navy = new(0.043f, 0.165f, 0.239f, 0.96f);
        static readonly Color Gold = new(1f, 0.824f, 0.247f);
        static readonly Color Coral = new(1f, 0.42f, 0.32f);
        static readonly Color Foam = new(0.910f, 0.969f, 0.980f);
        static readonly Color Muted = new(0.812f, 0.910f, 0.949f, 0.8f);
        static readonly Color Water = new(0.180f, 0.769f, 0.839f);
        const float ButtonIcon = 34f;

        TsunamiController controller;
        bool running;
        double runStart;
        double lastFall;
        bool ended;

        Font font;
        Sprite panelSprite;
        CanvasGroup screen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        static void Attach()
        {
            var tsunami = FindAnyObjectByType<TsunamiController>();
            if (tsunami == null || FindAnyObjectByType<GameOutcome>() != null)
                return;
            new GameObject("Game Outcome").AddComponent<GameOutcome>().controller = tsunami;
        }

        void OnEnable() => Houses.Destroyed += OnHouseDestroyed;
        void OnDisable() => Houses.Destroyed -= OnHouseDestroyed;

        void Start()
        {
            if (controller == null)
                controller = FindAnyObjectByType<TsunamiController>();
            if (controller != null)
            {
                controller.StateChanged += OnStateChanged;
                controller.WavesFinished += OnWavesFinished;
                OnStateChanged(controller.State);
            }
            var hints = FindAnyObjectByType<TutorialHints>();
            if (hints != null)
            {
                font = hints.CardFont;
                panelSprite = hints.CardSprite;
            }
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void OnDestroy()
        {
            if (controller != null)
            {
                controller.StateChanged -= OnStateChanged;
                controller.WavesFinished -= OnWavesFinished;
            }
            if (ended)
                Time.timeScale = 1f;
        }

        double SimTime => controller != null && controller.Simulation != null ? controller.Simulation.SimTime : 0.0;

        void OnStateChanged(TsunamiState state)
        {
            if (ended)
                return;
            if (state == TsunamiState.Running && !running)
            {
                running = true;
                runStart = SimTime;
                lastFall = runStart;
            }
            else if (state == TsunamiState.Idle || state == TsunamiState.Warning)
            {
                // Reset or a new release: the village is repaired and the wave starts over.
                running = false;
            }
        }

        // With waves, every wave fended off lets the game go on; it is won when the last one is over.
        void OnWavesFinished()
        {
            if (!ended && (Houses.All.Count == 0 || Houses.DestroyedCount < loseHouses))
                StartCoroutine(End(true, 0f));
        }

        void OnHouseDestroyed(Houses.House house)
        {
            if (running)
                lastFall = SimTime;
        }

        void Update()
        {
            if (ended || !running || controller == null || controller.State != TsunamiState.Running)
                return;

            var houses = Houses.All;
            int total = houses.Count;
            int destroyed = Houses.DestroyedCount;
            if (total > 0 && destroyed >= loseHouses)
            {
                StartCoroutine(End(false, loseDelay));
                return;
            }

            if (controller.UsesWaves)
                return; // won in OnWavesFinished
            double now = SimTime;
            double elapsed = now - runStart;
            if ((elapsed >= minWaveSeconds && now - lastFall >= calmSeconds) || elapsed >= maxWaveSeconds)
                StartCoroutine(End(true, 0f));
        }

        IEnumerator End(bool won, float delay)
        {
            ended = true;
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            // Everything stops behind the card (the water too), so the counts on it stay true; the card and its
            // buttons use unscaled time.
            Time.timeScale = 0f;

            int total = Houses.All.Count;
            int destroyed = Houses.DestroyedCount;
            int standing = total - destroyed;
            int score = standing * pointsPerHouse;
            int previousBest = HighScore.Best;
            bool record = HighScore.Submit(score);

            BuildScreen(won, standing, destroyed, total, score, record ? score : previousBest, record);
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                screen.alpha = Mathf.SmoothStep(0f, 1f, t / 0.5f);
                yield return null;
            }
            screen.alpha = 1f;
        }

        // The new map is built behind a loading curtain (SceneTransition), not on a frozen frame.
        void Reload(bool play) => SceneTransition.Reload(play, font);

        // ------------------------------------------------------------------ end card

        void BuildScreen(bool won, int standing, int destroyed, int total, int score, int best, bool record)
        {
            var canvasObject = new GameObject("Game Outcome Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // over the HUD and the tutorial, under the main menu
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            screen = canvasObject.AddComponent<CanvasGroup>();
            screen.alpha = 0f;

            // The dimmed game behind the card; it also catches clicks meant for the world.
            var dim = NewRect("Dim", canvasObject.transform).gameObject.AddComponent<Image>();
            Stretch(dim.rectTransform);
            dim.color = new Color(0f, 0.04f, 0.08f, 0.6f);

            var card = NewRect("Card", canvasObject.transform);
            card.sizeDelta = new Vector2(760f, 560f);
            var background = card.gameObject.AddComponent<Image>();
            background.sprite = panelSprite;
            background.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            background.color = Navy;

            var accent = NewRect("Accent", card).gameObject.AddComponent<Image>();
            accent.rectTransform.anchorMin = new Vector2(0f, 1f);
            accent.rectTransform.anchorMax = new Vector2(1f, 1f);
            accent.rectTransform.pivot = new Vector2(0.5f, 1f);
            accent.rectTransform.sizeDelta = new Vector2(-28f, 5f);
            accent.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            accent.color = won ? Water : Coral;

            var title = Label("Title", card, won ? "KÖY KURTARILDI!" : "KÖY YIKILDI", 60, won ? Gold : Coral, -48f, 76f);
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(0f, -4f);
            Label("Subtitle", card, won ? (destroyed == 0 ? "Tsunami geçti, hiçbir ev yıkılmadı." : "Tsunami geçti, köy ayakta kaldı.")
                    : "Su köye ulaştı, evler yıkıldı.",
                28, Muted, -128f, 40f);

            Row(card, "Ayakta kalan evler", $"{standing} / {total}", Foam, -200f);
            Row(card, "Yıkılan evler", destroyed.ToString(), destroyed > 0 ? Coral : Foam, -248f);
            Row(card, "Skor", score.ToString("N0"), Gold, -296f);
            Row(card, record ? "Yeni rekor!" : "En yüksek skor", best.ToString("N0"), record ? Gold : Muted, -344f);

            var menu = FindAnyObjectByType<MainMenu>();
            var again = MakeButton(card, menu != null ? menu.PlayButtonTemplate : null, "TEKRAR OYNA", () => Reload(true));
            var toMenu = MakeButton(card, menu != null ? menu.QuitButtonTemplate : null, "ANA MENÜ", () => Reload(false));
            Place(again, new Vector2(-172f, 74f), new Vector2(316f, 84f));
            Place(toMenu, new Vector2(172f, 74f), new Vector2(316f, 84f));
        }

        Text Label(string name, RectTransform parent, string text, int size, Color color, float top, float height)
        {
            var label = NewText(name, parent, size, color, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = new Vector2(0f, 1f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.offsetMin = new Vector2(40f, top - height);
            label.rectTransform.offsetMax = new Vector2(-40f, top);
            label.text = text;
            return label;
        }

        void Row(RectTransform parent, string name, string value, Color valueColor, float top)
        {
            var left = Label(name, parent, name, 30, Foam, top, 40f);
            left.alignment = TextAnchor.MiddleLeft;
            left.rectTransform.offsetMin = new Vector2(90f, left.rectTransform.offsetMin.y);
            var right = Label(name + " Value", parent, value, 34, valueColor, top, 40f);
            right.alignment = TextAnchor.MiddleRight;
            right.rectTransform.offsetMax = new Vector2(-90f, right.rectTransform.offsetMax.y);
        }

        // A copy of the menu's own button (same sprites and press effect), or a plain one if there is no menu.
        RectTransform MakeButton(RectTransform parent, Button template, string label, UnityEngine.Events.UnityAction action)
        {
            Button button;
            if (template != null)
            {
                button = Instantiate(template, parent);
                button.onClick.RemoveAllListeners();
                button.interactable = true;
                // The menu sized the label to its own word; here the icon sits on the left and the label fills the rest.
                var content = button.transform.Find("Content");
                var icon = content != null ? content.Find("Icon") as RectTransform : null;
                var text = content != null && content.Find("Label") != null ? content.Find("Label").GetComponent<Text>() : null;
                if (icon != null)
                {
                    icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                    icon.sizeDelta = new Vector2(ButtonIcon, ButtonIcon);
                    icon.anchoredPosition = new Vector2(28f + ButtonIcon * 0.5f, 0f);
                }
                if (text != null)
                {
                    text.text = label;
                    text.fontSize = 32;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.rectTransform.anchorMin = Vector2.zero;
                    text.rectTransform.anchorMax = Vector2.one;
                    text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    text.rectTransform.offsetMin = new Vector2(icon != null ? 28f + ButtonIcon : 16f, 0f);
                    text.rectTransform.offsetMax = new Vector2(-16f, 0f);
                }
            }
            else
            {
                var rect = NewRect(label, parent);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = Water;
                button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var text = NewText("Label", rect, 30, Foam, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
                text.text = label;
            }
            button.gameObject.SetActive(true);
            button.onClick.AddListener(action);
            return (RectTransform)button.transform;
        }

        static void Place(RectTransform rect, Vector2 bottomCenterOffset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = bottomCenterOffset;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        Text NewText(string name, Transform parent, int size, Color color, TextAnchor alignment)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
