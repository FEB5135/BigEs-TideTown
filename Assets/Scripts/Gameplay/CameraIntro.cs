using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ana menüde kamerayı haritaya uzaktan ve bulanık baktırır. Play deyince yavaş-hızlı-yavaş
// bir geçişle oyun kamerasının duruşuna iner, bulanıklığı açar ve kontrolleri oyuncuya verir.
// Oyun duruşu IsometricCamera'nın sahnedeki ayarlarıdır; menü duruşu buradaki alanlardır.
[DefaultExecutionOrder(100)] // IsometricCamera.Awake'ten sonra.
[RequireComponent(typeof(IsometricCamera))]
public class CameraIntro : MonoBehaviour
{
    [Header("Menü duruşu")]
    [Tooltip("Menüde kameranın odak noktası, oyun odak noktasına göre.")]
    [SerializeField] Vector3 menuFocusOffset = Vector3.zero;
    [SerializeField] float menuDistance = 190f;
    [Tooltip("Şimdilik oyun açısıyla aynı. Farklı verilirse geçişte açı da döner.")]
    [SerializeField] float menuPitch = 45f;
    [SerializeField] float menuYaw = 45f;
    [Tooltip("Menüde odak noktası bu kadar metre yavaşça gidip gelir (0 = sabit).")]
    [SerializeField] float idleDrift = 4f;
    [SerializeField] float idleDriftPeriod = 24f;

    [Header("Geçiş")]
    [SerializeField] float duration = 3.2f;

    [Header("Bulanıklık")]
    [SerializeField] bool blurInMenu = true;
    [Tooltip("Bulanıklık geçişin ilk bu kadarlık kısmında (0-1) kaybolur.")]
    [Range(0.05f, 1f)]
    [SerializeField] float blurFadePortion = 0.55f;

    IsometricCamera iso;
    ClickHarvester harvester;
    Volume blurVolume;
    VolumeProfile blurProfile;
    Volume neutralVolume;
    VolumeProfile neutralProfile;
    UniversalAdditionalCameraData cameraData;
    bool postProcessingWasOn;

    Vector3 gameFocus;
    float gameDistance, gamePitch, gameYaw;
    Coroutine transition;
    float driftTime;

    public bool InMenu { get; private set; }

    void Awake()
    {
        iso = GetComponent<IsometricCamera>();
        harvester = GetComponent<ClickHarvester>();
        gameFocus = iso.FocusPoint;
        gameDistance = iso.Distance;
        gamePitch = iso.Pitch;
        gameYaw = iso.Yaw;
        if (blurInMenu)
            CreateBlur();
    }

    void OnDestroy()
    {
        if (blurProfile != null)
            Destroy(blurProfile);
        if (neutralProfile != null)
            Destroy(neutralProfile);
    }

    // Kamerayı menü duruşuna koyar ve kontrolleri kapatır.
    public void EnterMenu()
    {
        if (transition != null)
            StopCoroutine(transition);
        transition = null;
        InMenu = true;
        SetControls(false);
        driftTime = 0f;
        iso.SetPose(MenuFocus(), menuDistance, menuPitch, menuYaw);
        SetBlur(1f);
    }

    // Menüden oyuna geçer; bitince onDone çağrılır.
    public void PlayIntro(Action onDone = null)
    {
        if (transition != null)
            StopCoroutine(transition);
        transition = StartCoroutine(Transition(onDone));
    }

    void Update()
    {
        if (!InMenu || transition != null || idleDrift <= 0f)
            return;
        driftTime += Time.unscaledDeltaTime;
        iso.SetPose(MenuFocus(), menuDistance, menuPitch, menuYaw);
    }

    Vector3 MenuFocus()
    {
        var focus = gameFocus + menuFocusOffset;
        if (idleDrift > 0f && idleDriftPeriod > 0f)
        {
            float a = driftTime / idleDriftPeriod * Mathf.PI * 2f;
            // Ekranda yatay gidip gelir (kameranın sağ yönü boyunca).
            var right = Quaternion.Euler(0f, menuYaw, 0f) * Vector3.right;
            focus += right * (Mathf.Sin(a) * idleDrift);
        }
        return focus;
    }

    IEnumerator Transition(Action onDone)
    {
        var fromFocus = iso.FocusPoint;
        float fromDistance = iso.Distance, fromPitch = iso.Pitch, fromYaw = iso.Yaw;
        // Uzaklık logaritmik yaklaştırılır, yoksa sona doğru yakınlaşma çok hızlı hissedilir.
        float logFrom = Mathf.Log(fromDistance), logTo = Mathf.Log(gameDistance);

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = SmootherStep(t / duration);
            iso.SetPose(
                Vector3.Lerp(fromFocus, gameFocus, k),
                Mathf.Exp(Mathf.Lerp(logFrom, logTo, k)),
                Mathf.LerpAngle(fromPitch, gamePitch, k),
                Mathf.LerpAngle(fromYaw, gameYaw, k));
            SetBlur(1f - Mathf.Clamp01(t / (duration * blurFadePortion)));
            yield return null;
        }

        iso.SetPose(gameFocus, gameDistance, gamePitch, gameYaw);
        SetBlur(0f);
        InMenu = false;
        SetControls(true);
        transition = null;
        onDone?.Invoke();
    }

    // Yavaş başlar, ortada hızlanır, yavaş biter.
    static float SmootherStep(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * x * (x * (x * 6f - 15f) + 10f);
    }

    void SetControls(bool on)
    {
        iso.InputEnabled = on;
        if (harvester != null)
            harvester.enabled = on;
    }

    // Kameranın önüne her şeyi bulanıklaştıran bir alan derinliği (depth of field) efekti koyar.
    void CreateBlur()
    {
        cameraData = GetComponent<Camera>().GetUniversalAdditionalCameraData();
        if (cameraData != null)
            postProcessingWasOn = cameraData.renderPostProcessing;

        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        blurProfile.name = "Menu Blur (runtime)";
        var dof = blurProfile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        // Odak kameranın dibinde, diyafram tam açık: haritanın tamamı odak dışında kalır.
        dof.focusDistance.Override(0.1f);
        dof.focalLength.Override(300f);
        dof.aperture.Override(1f);

        var go = new GameObject("Menu Blur Volume");
        go.transform.SetParent(transform, false);
        blurVolume = go.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.sharedProfile = blurProfile;
        blurVolume.weight = 0f;

        // Oyunda post-processing kapalıysa, bulanıklık için açılınca sahnenin kendi efektleri (bloom, vinyet,
        // tonemapping...) de devreye girip renkleri değiştiriyordu; bulanıklık bitince renk birden sıçrıyordu.
        // Bu yüzden sahnedeki her efekt varsayılan (etkisiz) değerlerle ezilir: menüde de oyundaki renkler görünür.
        if (!postProcessingWasOn)
            CreateNeutral(go);
    }

    void CreateNeutral(GameObject host)
    {
        neutralProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        neutralProfile.name = "Menu Neutral (runtime)";
        foreach (var volume in FindObjectsByType<Volume>())
        {
            if (volume == blurVolume || volume.sharedProfile == null)
                continue;
            foreach (var component in volume.sharedProfile.components)
            {
                if (component == null || component is DepthOfField || neutralProfile.Has(component.GetType()))
                    continue;
                var neutral = neutralProfile.Add(component.GetType(), true); // tüm parametreler varsayılan, ezili
                neutral.active = true;
            }
        }
        neutralVolume = host.AddComponent<Volume>();
        neutralVolume.isGlobal = true;
        neutralVolume.priority = 99f;
        neutralVolume.sharedProfile = neutralProfile;
        neutralVolume.weight = 1f;
        neutralVolume.enabled = false;
    }

    void SetBlur(float weight)
    {
        if (blurVolume == null)
            return;
        blurVolume.weight = weight;
        blurVolume.enabled = weight > 0f;
        if (neutralVolume != null)
            neutralVolume.enabled = weight > 0f;
        // Post-processing yalnız bulanıklık için açılır; bitince sahnenin kendi ayarına döner.
        if (cameraData != null)
            cameraData.renderPostProcessing = weight > 0f || postProcessingWasOn;
    }
}
