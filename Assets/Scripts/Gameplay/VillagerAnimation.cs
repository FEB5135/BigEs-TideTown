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
    [SerializeField, Min(0.1f)] float chopFrequency = 1.8f;
    [SerializeField, Min(0f)] float blendSpeed = 12f;

    Gatherer gatherer;
    Quaternion bodyRest, leftArmRest, rightArmRest, leftLegRest, rightLegRest;
    Vector3 bodyPosition, previousPosition;
    float walkPhase, chopPhase;
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
        if (IsChopping)
            chopPhase = Mathf.Repeat(chopPhase + Time.deltaTime * chopFrequency * Mathf.PI * 2f, Mathf.PI * 2f);
        else
            chopPhase = 0f;

        float swing = IsWalking ? Mathf.Sin(walkPhase) * walkAngle : 0f;
        float lift = IsChopping ? -Mathf.Lerp(45f, 125f, (Mathf.Sin(chopPhase) + 1f) * 0.5f) : 0f;
        float blend = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation,
            bodyRest * Quaternion.Euler(IsChopping ? 5f : 0f, 0f, swing), blend);
        visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition,
            bodyPosition + Vector3.up * (IsWalking ? Mathf.Abs(Mathf.Sin(walkPhase)) * walkBob : 0f), blend);
        // The supplied model faces local -X and its shoulders run along Z.
        // Vagonu iterken kollar öne uzanır.
        Pose(leftArm, leftArmRest, IsChopping ? lift : IsPushing ? -80f : -swing * 0.7f, blend);
        Pose(rightArm, rightArmRest, IsChopping ? lift : IsPushing ? -80f : swing * 0.7f, blend);
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

    static Quaternion Rest(Transform bone) => bone != null ? bone.localRotation : Quaternion.identity;

    static void Pose(Transform bone, Quaternion rest, float angle, float blend)
    {
        if (bone != null)
            bone.localRotation = Quaternion.Slerp(bone.localRotation, rest * Quaternion.Euler(0f, 0f, angle), blend);
    }
}
