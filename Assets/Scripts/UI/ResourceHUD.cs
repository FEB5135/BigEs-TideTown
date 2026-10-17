using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Sol alttaki odun/taş göstergeleri. Kuyudaki miktar değişince yazıyı günceller ve butonu kısaca büyütür.
public class ResourceHUD : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public ResourceType type;
        public Text amountText;
        [Tooltip("Miktar değişince büyüyüp küçülen obje (genelde buton).")]
        public RectTransform pulseTarget;
        [NonSerialized] public Coroutine pulse;
    }

    [SerializeField] Entry[] entries;
    [SerializeField] float pulseScale = 1.12f;
    [SerializeField] float pulseDuration = 0.15f;

    Well well;

    void Start()
    {
        well = Well.Instance;
        if (well == null)
        {
            Debug.LogWarning("ResourceHUD: sahnede Well yok.", this);
            return;
        }
        well.Changed += OnChanged;
        foreach (var entry in entries)
            SetText(entry, well.Get(entry.type));
    }

    void OnDestroy()
    {
        if (well != null)
            well.Changed -= OnChanged;
    }

    void OnChanged(ResourceType type, int amount)
    {
        foreach (var entry in entries)
        {
            if (entry.type != type)
                continue;
            SetText(entry, amount);
            if (entry.pulseTarget != null)
            {
                if (entry.pulse != null)
                    StopCoroutine(entry.pulse);
                entry.pulse = StartCoroutine(Pulse(entry));
            }
        }
    }

    static void SetText(Entry entry, int amount)
    {
        if (entry.amountText != null)
            entry.amountText.text = amount.ToString();
    }

    IEnumerator Pulse(Entry entry)
    {
        var target = entry.pulseTarget;
        for (float t = 0f; t < pulseDuration; t += Time.unscaledDeltaTime)
        {
            float s = Mathf.Lerp(pulseScale, 1f, t / pulseDuration);
            target.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        target.localScale = Vector3.one;
        entry.pulse = null;
    }
}
