using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Idle toplayıcı: kaynağa gider, toplar, kuyuya getirip içine atar. Sonsuz döngü.
// Ev kaynağı verilirse (vagonun ocağı) yalnız oraya gider; verilmezse en yakın boş kaynağı arar (oduncu).
// Sahnede NavigationBaker varsa engellerin etrafından dolaşır, yoksa düz çizgide yürür.
// Köylü (villager) işaretliyse işi verilene kadar köyde dolaşır; oyuncu onu tutup ormana bırakınca oduncu olur,
// ocağa bırakınca o ocağın vagonunu iter (VillagerPicker). Vagon (needsPusher) iten biri yokken bekler.
public enum GathererActivity { Idle, Walking, Harvesting, Pushing }

public class Gatherer : MonoBehaviour
{
    [SerializeField] ResourceType resourceType = ResourceType.Wood;
    [Tooltip("Ev kaynağı (örn. vagonun ocağı). Boşsa en yakın boş kaynak aranır.")]
    [SerializeField] ResourceNode home;
    [SerializeField] int carryCapacity = 5;
    [SerializeField] float harvestDuration = 2f;
    [Tooltip("Odun toplarken ağaca kaç saniyede bir vurur. 0 = vurmaz.")]
    [SerializeField] float hitInterval = 0.5f;
    [SerializeField] float moveSpeed = 3f;
    [Tooltip("İşteyken (vagon, işteki köylü) yürüme hızı çarpanı; yükseltme çarpanıyla çarpılır. Boşta dolaşan köylüye uygulanmaz.")]
    [SerializeField] float workSpeedMultiplier = 1.5f;
    [Tooltip("İşteyken toplama süresi ve vuruş aralığı çarpanı (küçük = hızlı); yükseltme çarpanıyla çarpılır.")]
    [SerializeField] float workTimeMultiplier = 0.6f;
    [Tooltip("Saniyede en fazla kaç derece döner (köşelerde yumuşak dönüş).")]
    [SerializeField] float turnSpeed = 360f;
    [Tooltip("Kaynağa bu mesafeye gelince durur.")]
    [SerializeField] float stopDistance = 1.5f;
    [Tooltip("Kuyunun merkezine bu mesafeye gelince durup atar.")]
    [SerializeField] float wellStopDistance = 3f;
    [Tooltip("Taşırken gösterilecek obje (örn. sırttaki odun). Opsiyonel.")]
    [SerializeField] GameObject carryVisual;
    [Tooltip("Zemine yapışmak için aşağı doğru atılan ışının çarpacağı katmanlar.")]
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Köylü")]
    [Tooltip("Oyuncunun tutup işe koyabileceği köylü. İşi verilene kadar bulunduğu yerin çevresinde dolaşır.")]
    [SerializeField] bool villager;
    [SerializeField] float wanderRadius = 5f;
    [SerializeField] Vector2 wanderPause = new(1.5f, 4f);
    [Tooltip("Vagonu iterken vagonun arka ucuyla arasında kalan boşluk (eller kasaya değsin).")]
    [SerializeField] float pushGap = 0.35f;

    [Header("Vagon")]
    [Tooltip("Yalnız bir köylü iterken ilerler.")]
    [SerializeField] bool needsPusher;

    const float PathStopDistance = 0.1f;

    static readonly List<Gatherer> villagers = new();
    // Sahnedeki (etkin) köylüler.
    public static IReadOnlyList<Gatherer> Villagers => villagers;

    readonly RaycastHit[] groundHits = new RaycastHit[8];
    readonly List<Vector3> corners = new();
    float heightOffset;
    bool ready;
    Coroutine routine;
    ResourceNode claimed;
    Gatherer pushedCart;
    Gatherer reservedBy; // Vagon: ona doğru yürüyen ya da onu iten köylü.

    public ResourceType Type => resourceType;
    public GathererActivity Activity { get; private set; }
    public ResourceNode CurrentTarget { get; private set; }
    public bool IsVillager => villager;
    public bool HasJob { get; private set; }
    // Vagonu iten köylü (vagonda) ya da itilen vagon (köylüde).
    public Gatherer Pusher { get; private set; }
    public Gatherer PushedCart => pushedCart;
    public bool NeedsPusher { get => needsPusher; set => needsPusher = value; }
    public bool IsReserved => reservedBy != null;
    // Ağaca son vuruşun zamanı ve vuruş aralığı (balta animasyonu vuruşa denk gelsin diye).
    public float LastHitTime { get; private set; } = -100f;
    public float HitInterval => hitInterval * WorkTimeFactor;

    public void SetHome(ResourceNode node) => home = node;

    // Bağlı yükseltme: vagon kendi ocağınınkine, işteki oduncu ormanınkine bağlıdır.
    UpgradeLevel Upgrade
    {
        get
        {
            if (home != null)
                return home.Upgrade;
            if (resourceType == ResourceType.Wood && pushedCart == null && (!villager || HasJob))
                return UpgradeLevel.Forest;
            return null;
        }
    }

    // İşte: vagon, işe konmuş köylü (oduncu ya da vagon iten) ve köylü olmayan toplayıcı.
    bool Working => home != null || HasJob || !villager;

    float SpeedFactor => Working ? workSpeedMultiplier * (Upgrade != null ? Upgrade.SpeedMultiplier : 1f) : 1f;
    float WorkTimeFactor => Working ? workTimeMultiplier * (Upgrade != null ? Upgrade.WorkTimeMultiplier : 1f) : 1f;

    // Prefab'tan yeni üretilen işçiyi sahnedeki bir köylünün ayarlarıyla köylü yapar (Start'tan önce çağrılır).
    public void BecomeVillagerLike(Gatherer template)
    {
        if (template != null)
        {
            carryCapacity = template.carryCapacity;
            harvestDuration = template.harvestDuration;
            hitInterval = template.hitInterval;
            moveSpeed = template.moveSpeed;
            workSpeedMultiplier = template.workSpeedMultiplier;
            workTimeMultiplier = template.workTimeMultiplier;
            stopDistance = template.stopDistance;
            wellStopDistance = template.wellStopDistance;
            wanderRadius = template.wanderRadius;
            wanderPause = template.wanderPause;
            pushGap = template.pushGap;
            // Aynı boy ve zeminden aynı yükseklik (Start yüksekliği bulunduğu yerden ölçer).
            transform.localScale = template.transform.localScale;
            var position = transform.position;
            if (TryGetGroundHeight(position, out float groundY))
                transform.position = new Vector3(position.x, groundY + template.heightOffset, position.z);
        }
        villager = true;
        resourceType = ResourceType.Wood;
        home = null;
        needsPusher = false;
        EnsurePickCollider();
        if (isActiveAndEnabled && !villagers.Contains(this))
            villagers.Add(this);
    }

    void Awake()
    {
        if (villager)
            EnsurePickCollider();
    }

    void OnEnable()
    {
        if (villager && !villagers.Contains(this))
            villagers.Add(this);
    }

    void OnDisable() => villagers.Remove(this);

    // Köylü imleçle tutulabilsin diye görselinin boyunda tetikleyici kapsül (zemin ve tıklama ışınlarını etkilemez).
    void EnsurePickCollider()
    {
        foreach (var collider in GetComponents<Collider>())
            if (collider.isTrigger)
                return;
        Bounds? bounds = null;
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (bounds is Bounds b)
            {
                b.Encapsulate(renderer.bounds);
                bounds = b;
            }
            else
                bounds = renderer.bounds;
        }
        if (bounds is not Bounds box)
            return;
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        var scale = transform.lossyScale;
        float vertical = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        capsule.center = transform.InverseTransformPoint(box.center);
        capsule.height = box.size.y / vertical;
        // Küçük köylüyü yakalamak kolay olsun diye biraz geniş.
        capsule.radius = Mathf.Max(box.extents.x, box.extents.z, 0.6f) / horizontal;
    }

    IEnumerator Start()
    {
        // Pivot ile zemin arasındaki mesafe korunur (örn. capsule'ün merkezi zeminin üstünde).
        if (TryGetGroundHeight(transform.position, out float groundY))
            heightOffset = transform.position.y - groundY;
        SetCarrying(false);
        // Harita kurulup NavMesh hazır olana kadar beklenir.
        while (NavigationBaker.Exists && !NavigationBaker.Ready)
            yield return null;
        ready = true;
        if (routine != null)
            yield break; // Beklerken iş verildi.
        if (villager)
            Wander();
        else
            routine = StartCoroutine(WorkLoop());
    }

    // ---------------------------------------------------------------- Köylünün işleri

    // Bulunduğu yerin çevresinde dolaşır (işsiz).
    public void Wander()
    {
        Stop();
        HasJob = false;
        routine = StartCoroutine(WanderLoop(transform.position));
    }

    // Oduncu olur: en yakın boş ağacı bulur, toplar, kuyuya götürür; sürekli.
    public void WorkInForest()
    {
        Stop();
        resourceType = ResourceType.Wood;
        home = null;
        HasJob = true;
        routine = StartCoroutine(WorkLoop());
    }

    // Vagonun arkasına geçip iter: vagon ocağa girip taşla dolar, kuyuya döner; sürekli.
    public void PushCart(Gatherer cart)
    {
        Stop();
        if (cart == null || (cart.reservedBy != null && cart.reservedBy != this))
        {
            Wander();
            return;
        }
        HasJob = true;
        cart.reservedBy = this;
        pushedCart = cart;
        routine = StartCoroutine(PushLoop(cart));
    }

    // Tutulunca: ne yapıyorsa bırakır (sahiplendiği ağaç, ittiği vagon serbest kalır).
    public void Stop()
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        if (claimed != null)
            claimed.Release(this);
        claimed = null;
        CurrentTarget = null;
        if (pushedCart != null && pushedCart.Pusher == this)
            pushedCart.Pusher = null;
        if (pushedCart != null && pushedCart.reservedBy == this)
            pushedCart.reservedBy = null;
        pushedCart = null;
        Activity = GathererActivity.Idle;
        SetCarrying(false);
    }

    // Tutulup bırakıldıktan sonra yeni yerindeki zemine göre yükseklik farkı korunur.
    public void SnapToGround()
    {
        var position = transform.position;
        if (TryGetGroundHeight(position, out float groundY))
            transform.position = new Vector3(position.x, groundY + heightOffset, position.z);
    }

    IEnumerator WanderLoop(Vector3 anchor)
    {
        while (!ready)
            yield return null;
        while (true)
        {
            Activity = GathererActivity.Idle;
            yield return new WaitForSeconds(Random.Range(wanderPause.x, wanderPause.y));
            var offset = Random.insideUnitCircle * wanderRadius;
            var target = anchor + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas))
                target = hit.position;
            yield return TravelTo(target, 0.3f);
        }
    }

    IEnumerator PushLoop(Gatherer cart)
    {
        while (!ready)
            yield return null;
        // Vagonun arkasına yürür, sonra vagonla birlikte hareket eder.
        yield return TravelTo(BehindCart(cart), 0.4f);
        yield return FaceTarget(cart.transform.position);
        cart.Pusher = this;
        Activity = GathererActivity.Pushing;
        while (cart != null)
        {
            var target = BehindCart(cart);
            if (TryGetGroundHeight(target, out float groundY))
                target.y = groundY + heightOffset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            var forward = cart.transform.position - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(forward), turnSpeed * Time.deltaTime);
            yield return null;
        }
    }

    Vector3 BehindCart(Gatherer cart)
    {
        var back = -cart.transform.forward;
        back.y = 0f;
        return cart.transform.position + back.normalized * (cart.BackLength + pushGap);
    }

    float backLength = -1f;

    // Vagonun merkezinden arka ucuna uzaklık (modelin kendi boyutundan, bir kez hesaplanır).
    float BackLength
    {
        get
        {
            if (backLength >= 0f)
                return backLength;
            backLength = 0f;
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.GetComponentInParent<Gatherer>() != this)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i < 4 ? b.min.z : b.max.z);
                    var local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    backLength = Mathf.Max(backLength, -local.z);
                }
            }
            return backLength;
        }
    }

    // ---------------------------------------------------------------- Toplama döngüsü

    IEnumerator WorkLoop()
    {
        while (!ready)
            yield return null;
        while (true)
        {
            yield return WaitForPusher();
            var well = Well.Instance;
            var node = home != null ? home : ResourceNode.FindNearestFree(resourceType, transform.position);
            CurrentTarget = node;
            Activity = GathererActivity.Idle;
            if (well == null || node == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Yolu olan kaynakta (maden ocağı) önce yolun başına gidilir, oradan yol boyunca içeri girilir.
            var path = PathPoints(node);
            if (home != null && home.Remaining <= 0)
            {
                // Ocak tükenmiş: girişte yenilenmesini bekler.
                yield return TravelTo(path.Length > 0 ? path[0] : node.transform.position, stopDistance);
                yield return new WaitForSeconds(1f);
                continue;
            }

            node.Claim(this);
            claimed = node;
            if (path.Length > 0)
            {
                yield return TravelTo(path[0], PathStopDistance);
                for (int i = 1; i < path.Length; i++)
                    yield return MoveTo(path[i], PathStopDistance);
            }
            else
                yield return TravelTo(node.transform.position, stopDistance);
            if (node == null)
                continue;
            if (resourceType == ResourceType.Wood)
                yield return FaceTarget(node.transform.position);
            if (node == null)
                continue;
            Activity = GathererActivity.Harvesting;
            // Odun toplarken ağaca aralıklarla vurur (ağaç sallanır). Ocaklarda vurulmaz.
            // Yükseltme toplama süresini ve vuruş aralığını aynı oranda kısaltır.
            float duration = harvestDuration * WorkTimeFactor;
            float interval = HitInterval;
            for (float t = 0f; t < duration; t += interval)
            {
                if (node != null && resourceType == ResourceType.Wood && interval > 0f)
                {
                    LastHitTime = Time.time;
                    node.PlayHitFeedback();
                }
                yield return new WaitForSeconds(interval > 0f ? Mathf.Min(interval, duration - t) : duration);
                if (interval <= 0f)
                    break;
            }
            Activity = GathererActivity.Idle;
            if (node == null)
                continue;
            int carried = node.Harvest(carryCapacity);
            node.Release(this);
            claimed = null;
            CurrentTarget = null;
            SetCarrying(carried > 0);
            for (int i = path.Length - 2; i >= 0; i--)
                yield return MoveTo(path[i], PathStopDistance);
            if (carried <= 0)
                continue;

            // Kuyunun bu taraftaki kenarına gidilir; ortasına yol aranırsa kuyunun etrafında dolanır.
            float approach = Mathf.Max(wellStopDistance, well.ApproachRadius);
            var fromWell = transform.position - well.transform.position;
            fromWell.y = 0f;
            var rim = well.transform.position + (fromWell.sqrMagnitude > 0.01f ? fromWell.normalized : Vector3.forward) * (approach + 0.5f);
            yield return TravelTo(rim, 0.5f);
            SetCarrying(false);
            if (well != null)
                well.Throw(resourceType, carried, transform.position + Vector3.up);
        }
    }

    // Vagon: iten köylü yoksa olduğu yerde bekler.
    IEnumerator WaitForPusher()
    {
        while (needsPusher && Pusher == null)
        {
            Activity = GathererActivity.Idle;
            yield return null;
        }
    }

    static Vector3[] PathPoints(ResourceNode node)
    {
        var path = node.ApproachPath;
        if (path == null)
            return System.Array.Empty<Vector3>();
        return System.Array.ConvertAll(System.Array.FindAll(path, p => p != null), p => p.position);
    }

    // NavMesh varsa engellerin etrafından, yoksa düz çizgide gider.
    IEnumerator TravelTo(Vector3 target, float distance)
    {
        if (NavigationBaker.TryFindPath(transform.position, target, corners))
        {
            var points = corners.ToArray();
            for (int i = 1; i < points.Length; i++)
                yield return MoveTo(points[i], PathStopDistance);
        }
        // Hedef bir engelin içindeyse NavMesh kenarında biter; kalan kısa mesafe düz gidilir.
        yield return MoveTo(target, distance);
    }

    IEnumerator MoveTo(Vector3 target, float distance)
    {
        while (true)
        {
            yield return WaitForPusher();
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= distance)
            {
                Activity = GathererActivity.Idle;
                yield break;
            }

            Activity = GathererActivity.Walking;
            var step = Vector3.ClampMagnitude(toTarget, moveSpeed * SpeedFactor * Time.deltaTime);
            var next = transform.position + step;
            if (TryGetGroundHeight(next, out float groundY))
                next.y = groundY + heightOffset;
            transform.position = next;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget), turnSpeed * Time.deltaTime);
            yield return null;
        }
    }

    IEnumerator FaceTarget(Vector3 target)
    {
        var direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            yield break;
        var facing = Quaternion.LookRotation(direction);
        while (Quaternion.Angle(transform.rotation, facing) > 1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, Mathf.Max(1f, turnSpeed) * Time.deltaTime);
            yield return null;
        }
        transform.rotation = facing;
    }

    bool TryGetGroundHeight(Vector3 position, out float height)
    {
        int count = Physics.RaycastNonAlloc(position + Vector3.up * 50f, Vector3.down, groundHits, 100f, groundMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        height = position.y;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<ResourceNode>() != null
                || hit.collider.GetComponentInParent<Well>() != null
                || hit.collider.GetComponentInParent<Gatherer>() != null)
                continue;
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                height = hit.point.y;
            }
        }
        return bestDistance < float.MaxValue;
    }

    void SetCarrying(bool carrying)
    {
        if (carryVisual != null)
            carryVisual.SetActive(carrying);
    }
}
