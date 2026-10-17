using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Alt kalınlığı olan (2.5B) buton: üzerine gelince hafifçe büyür, basınca kalınlığı olmayan
// sprite'a geçip içerik aşağı iner. Tıklama işi aynı objedeki Button'da.
[RequireComponent(typeof(Button))]
public class PressableButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Butonun görseli (kökü dolduran alt obje).")]
    [SerializeField] Image visual;
    [SerializeField] Sprite normalSprite;
    [Tooltip("Basılıyken kullanılan, alt kalınlığı olmayan sprite.")]
    [SerializeField] Sprite pressedSprite;
    [Tooltip("İkon ve yazı; basınca aşağı iner.")]
    [SerializeField] RectTransform content;
    [Tooltip("Alt kalınlığın ekrandaki yüksekliği.")]
    [SerializeField] float depth = 10f;
    [SerializeField] float hoverScale = 1.04f;
    [SerializeField] float speed = 20f;
    [SerializeField] AudioClip clickSound;

    Button button;
    AudioSource audioSource;
    bool hovered, pressed;
    float scale = 1f;

    void Awake()
    {
        button = GetComponent<Button>();
        audioSource = GetComponentInParent<AudioSource>();
        button.onClick.AddListener(() =>
        {
            if (clickSound != null && audioSource != null)
                audioSource.PlayOneShot(clickSound);
        });
    }

    void OnEnable() => ApplyPressed(false);

    void OnDisable()
    {
        hovered = pressed = false;
        scale = 1f;
        transform.localScale = Vector3.one;
    }

    void Update()
    {
        bool interactable = button.IsInteractable();
        float target = interactable && hovered && !pressed ? hoverScale : 1f;
        scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    void ApplyPressed(bool down)
    {
        pressed = down;
        if (visual != null)
        {
            if (normalSprite != null && pressedSprite != null)
                visual.sprite = down ? pressedSprite : normalSprite;
            // Düz sprite, kalınlık kadar aşağıda başlayan yüzü çizer.
            visual.rectTransform.offsetMax = new Vector2(0f, down ? -depth : 0f);
        }
        if (content != null)
            content.anchoredPosition = new Vector2(0f, down ? -depth * 0.5f : depth * 0.5f);
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        if (pressed)
            ApplyPressed(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button.IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            ApplyPressed(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (pressed)
            ApplyPressed(false);
    }
}
