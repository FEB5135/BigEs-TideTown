using UnityEngine;

// Only the visual rig moves; navigation, heading and ground contact stay on the Gatherer root.
[DisallowMultipleComponent]
[RequireComponent(typeof(Gatherer))]
public sealed class VillagerAnimation : MonoBehaviour
{
    [SerializeField] Transform visualRoot;
    [SerializeField] Transform leftArm;
    [SerializeField] Transform rightArm;
    [SerializeField] Transform leftLeg;
    [SerializeField] Transform rightLeg;
    [SerializeField, Range(0f, 20f)] float walkAngle = 15f;
    [SerializeField, Min(0.1f)] float strideLength = 2f;
    [SerializeField, Min(0f)] float walkBob = 0.045f;
    [Tooltip("Yürürken gövdenin öne-arkaya en fazla eğilmesi (derece).")]
    [SerializeField, Range(0f, 10f)] float bodyRock = 3f;
    [Tooltip("Vuruş anında kolların açısı (öne, gövde hizası) ve baltayı kaldırınca (baş üstü, geride).")]
    [SerializeField] float strikeAngle = -70f;
    [SerializeField] float windUpAngle = -165f;
    [Tooltip("Vagonu iterken kolların açısı ve gövdenin öne eğilmesi.")]
    [SerializeField] float pushArmAngle = -72f;
    [SerializeField] float pushLean = 12f;
    [SerializeField, Min(0f)] float blendSpeed = 12f;

    Gatherer gatherer;
    Quaternion bodyRest, leftArmRest, rightArmRest, leftLegRest, rightLegRest;
    Vector3 bodyPosition, previousPosition;
    float walkPhase;
    bool initialized;

    public bool IsWalking { get; private set; }
    public bool IsChopping { get; private set; }
    public bool IsPushing { get; private set; }

    void Awake() => Initialize();

    void OnEnable()
    {
        Initialize();
        previousPosition = transform.position;
    }

    void Initialize()
    {
        if (initialized || visualRoot == null)
            return;
        gatherer = GetComponent<Gatherer>();
        bodyRest = visualRoot.localRotation;
        bodyPosition = visualRoot.localPosition;
        leftArmRest = Rest(leftArm);
        rightArmRest = Rest(rightArm);
        leftLegRest = Rest(leftLeg);
        rightLegRest = Rest(rightLeg);
        initialized = true;
    }

    void LateUpdate()
    {
        if (!initialized)
            return;
        var delta = transform.position - previousPosition;
        previousPosition = transform.position;
        delta.y = 0f;
        IsPushing = gatherer.isActiveAndEnabled && gatherer.Activity == GathererActivity.Pushing;
        IsWalking = gatherer.isActiveAndEnabled && (gatherer.Activity == GathererActivity.Walking || IsPushing) &&
                    delta.sqrMagnitude > 0.000001f;
        IsChopping = gatherer.isActiveAndEnabled && gatherer.Activity == GathererActivity.Harvesting &&
                     gatherer.Type == ResourceType.Wood && gatherer.CurrentTarget != null &&
                     gatherer.CurrentTarget.Remaining > 0;
        if (IsWalking)
            walkPhase = Mathf.Repeat(walkPhase + delta.magnitude / strideLength * Mathf.PI * 2f, Mathf.PI * 2f);
        float swing = IsWalking ? Mathf.Sin(walkPhase) * walkAngle : 0f;
        float lift = 0f, lean = IsPushing ? pushLean : 0f;
        if (IsChopping)
            Chop(out lift, out lean);
        float blend = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation,
            bodyRest * Quaternion.Euler(0f, 0f, lean + (IsWalking ? Mathf.Sin(walkPhase * 2f) * bodyRock : 0f)), blend);
        visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition,
            bodyPosition + Vector3.up * (IsWalking ? Mathf.Abs(Mathf.Sin(walkPhase)) * walkBob : 0f), blend);
        // The supplied model faces local -X and its shoulders run along Z.
        // Vagonu iterken kollar öne uzanır.
        // Balta hızlı iner; kollar o sırada çok daha hızlı izler, yoksa vuruş gecikip yumuşar.
        float armBlend = IsChopping ? 1f - Mathf.Exp(-40f * Time.deltaTime) : blend;
        Pose(leftArm, leftArmRest, IsChopping ? lift : IsPushing ? pushArmAngle : -swing * 0.7f, armBlend);
        Pose(rightArm, rightArmRest, IsChopping ? lift : IsPushing ? pushArmAngle : swing * 0.7f, armBlend);
        Pose(leftLeg, leftLegRest, swing, blend);
        Pose(rightLeg, rightLegRest, -swing, blend);
    }

    void OnDisable()
    {
        IsWalking = IsChopping = IsPushing = false;
        if (!initialized)
            return;
        visualRoot.SetLocalPositionAndRotation(bodyPosition, bodyRest);
        if (leftArm != null) leftArm.localRotation = leftArmRest;
        if (rightArm != null) rightArm.localRotation = rightArmRest;
        if (leftLeg != null) leftLeg.localRotation = leftLegRest;
        if (rightLeg != null) rightLeg.localRotation = rightLegRest;
    }

    // Balta vuruşu, toplayıcının vuruş anına bağlı: vuruşta kollar öne iner (kısa duruş), sonra yavaşça baş üstüne
    // kalkar ve bir sonraki vuruşa hızlanarak iner. Gövde kaldırırken hafif geriye, vururken öne eğilir.
    void Chop(out float arms, out float lean)
    {
        float interval = Mathf.Max(0.2f, gatherer.HitInterval);
        float t = Mathf.Repeat((Time.time - gatherer.LastHitTime) / interval, 1f);
        if (t < 0.12f)
        {
            arms = strikeAngle;
            lean = 10f;
        }
        else if (t < 0.8f)
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.68f);
            arms = Mathf.Lerp(strikeAngle, windUpAngle, k);
            lean = Mathf.Lerp(10f, -6f, k);
        }
        else
        {
            float k = (t - 0.8f) / 0.2f;
            k *= k; // hızlanarak iner
            arms = Mathf.Lerp(windUpAngle, strikeAngle, k);
            lean = Mathf.Lerp(-6f, 10f, k);
        }
    }

    static Quaternion Rest(Transform bone) => bone != null ? bone.localRotation : Quaternion.identity;

    static void Pose(Transform bone, Quaternion rest, float angle, float blend)
    {
        if (bone != null)
            bone.localRotation = Quaternion.Slerp(bone.localRotation, rest * Quaternion.Euler(0f, 0f, angle), blend);
    }
}
