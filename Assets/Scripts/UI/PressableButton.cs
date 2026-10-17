using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Kalın alt kenarlı (2.5B) buton görünümü: üzerine gelince yüz hafifçe kalkar ve açılır,
// basınca alt kenara iner. Tıklama işi aynı objedeki Button'da.
[RequireComponent(typeof(Button))]
public class PressableButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Hareket eden üst yüz.")]
    [SerializeField] RectTransform face;
    [SerializeField] Image faceImage;
    [Tooltip("Alt kenarın kalınlığı; basınca yüz bu kadar iner.")]
    [SerializeField] float depth = 8f;
    [SerializeField] float hoverLift = 3f;
    [SerializeField] float hoverBrighten = 0.08f;
    [SerializeField] float speed = 22f;

    Button button;
    Color baseColor;
    Vector2 restPosition;
    bool hovered, pressed;
    float offset;

    void Awake()
    {
        button = GetComponent<Button>();
        if (face != null)
            restPosition = face.anchoredPosition;
        if (faceImage != null)
            baseColor = faceImage.color;
    }

    void OnDisable()
    {
        hovered = pressed = false;
        offset = 0f;
        Refresh(1f);
    }

    void Update() => Refresh(1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));

    void Refresh(float blend)
    {
        if (face == null)
            return;
        bool interactable = button == null || button.IsInteractable();
        bool lit = interactable && hovered;
        float target = !interactable ? 0f : pressed ? -(depth - 2f) : lit ? hoverLift : 0f;
        offset = Mathf.Lerp(offset, target, blend);
        face.anchoredPosition = restPosition + new Vector2(0f, offset);

        if (faceImage != null)
        {
            var color = lit ? Color.Lerp(baseColor, Color.white, hoverBrighten) : baseColor;
            if (!interactable)
                color = Color.Lerp(baseColor, new Color(0.5f, 0.5f, 0.5f, baseColor.a), 0.5f);
            faceImage.color = Color.Lerp(faceImage.color, color, blend);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;
    public void OnPointerExit(PointerEventData eventData) => hovered = pressed = false;
    public void OnPointerDown(PointerEventData eventData) => pressed = true;
    public void OnPointerUp(PointerEventData eventData) => pressed = false;
}
