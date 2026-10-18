using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Idle toplayıcı: kaynağa gider, toplar, kuyuya getirip içine atar. Sonsuz döngü.
// Ev kaynağı verilirse (vagonun ocağı) yalnız oraya gider; verilmezse en yakın boş kaynağı arar (oduncu).
// Sahnede NavigationBaker varsa engellerin etrafından dolaşır, yoksa düz çizgide yürür.
// Köylü (villager) işaretliyse işi verilene kadar köyde dolaşır; oyuncu onu tutup ormana bırakınca oduncu olur,
// ocağa bırakınca o ocağın vagonunu iter (VillagerPicker). Vagon (needsPusher) iten biri yokken bekler.
// Boştaki köylünün başında küçük bir belirteç durur. Tsunami alarmında (SetAlarm) köylüler işi bırakıp kolları
// havada evlerine koşar ve içeri girer; alarm bitince kapıdan çıkıp önceki işine döner (işi yoksa dolaşır).
public enum GathererActivity { Idle, Walking, Harvesting, Pushing, Fleeing }

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
    [Tooltip("Boştaki köylünün başında duran belirtecin malzemesi. Boşsa belirteç çizilmez.")]
    [SerializeField] Material idleMarkerMaterial;
    [Tooltip("Belirtecin ekrandaki boyu: kameradan uzaklığın bu katı kadar büyütülür (en az kendi boyu).")]
    [SerializeField] float markerScreenSize = 0.035f;
    [Tooltip("Tsunami alarmında evine koşarken yürüme hızı çarpanı.")]
    [SerializeField] float fleeSpeedMultiplier = 1.8f;

    [Header("Vagon")]
    [Tooltip("Yalnız bir köylü iterken ilerler.")]
    [SerializeField] bool needsPusher;

    const float PathStopDistance = 0.1f;

    static readonly List<Gatherer> villagers = new();
    // Sahnedeki (etkin) köylüler.
    public static IReadOnlyList<Gatherer> Villagers => villagers;

    // Tsunami alarmı (köprü: Tsunami/Scripts/Gameplay/TsunamiVillageAlarm). Açıkken köylüler evlerine kaçar.
    public static bool Alarm { get; private set; }

    public static void SetAlarm(bool on)
    {
        if (Alarm == on)
            return;
        Alarm = on;
        foreach (var villager in villagers.ToArray())
        {
            if (on)
                villager.Flee();
            else
                villager.LeaveShelter();
        }
    }

    readonly RaycastHit[] groundHits = new RaycastHit[8];
    readonly List<Vector3> corners = new();
    float heightOffset;
    bool ready;
    Coroutine routine;
    ResourceNode claimed;
    Gatherer pushedCart;
    Gatherer reservedBy; // Vagon: ona doğru yürüyen ya da onu iten köylü.
    // Alarmda bırakılan iş; alarm bitince köylü buna döner.
    bool resumeForest;
    Gatherer resumeCart;

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
    // Köylünün evi (doğduğu, tsunamide kaçtığı yer).
    public Houses.House Shelter { get; set; }
    // Evine girmiş (görünmez) ya da evine koşuyor: tutulamaz.
    public bool InShelter { get; private set; }
    public bool CanBePicked => villager && !Alarm && !InShelter;

    GameObject idleMarker;
    float markerHeight;
    float markerPhase;

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

    float SpeedFactor => Activity == GathererActivity.Fleeing ? fleeSpeedMultiplier
        : Working ? workSpeedMultiplier * (Upgrade != null ? Upgrade.SpeedMultiplier : 1f) : 1f;
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
            idleMarkerMaterial = template.idleMarkerMaterial;
            markerScreenSize = template.markerScreenSize;
            fleeSpeedMultiplier = template.fleeSpeedMultiplier;
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

    void OnDisable()
    {
        villagers.Remove(this);
        Stop();
    }

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
        if (villager)
            CreateIdleMarker();
        if (routine != null)
            yield break; // Beklerken iş verildi.
        if (villager && Alarm)
            Flee();
        else if (villager)
            Wander();
        else
            routine = StartCoroutine(WorkLoop());
    }

    // ---------------------------------------------------------------- Boştaki köylünün belirteci

    // Başın üstünde küçük, soluk bir elmas: yavaşça döner ve hafifçe iner kalkar; köylü işe konunca kaybolur.
    void CreateIdleMarker()
    {
        if (idleMarker != null || idleMarkerMaterial == null)
            return;
        float top = transform.position.y + 2f;
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, renderer.bounds.max.y);
        markerHeight = top - transform.position.y + 0.55f;
        idleMarker = new GameObject("Idle Marker", typeof(MeshFilter), typeof(MeshRenderer));
        idleMarker.transform.SetParent(transform, false);
        idleMarker.GetComponent<MeshFilter>().sharedMesh = MarkerMesh;
        var markerRenderer = idleMarker.GetComponent<MeshRenderer>();
        markerRenderer.sharedMaterial = idleMarkerMaterial;
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;
        markerPhase = Random.Range(0f, Mathf.PI * 2f);
    }

    void LateUpdate()
    {
        if (idleMarker == null)
            return;
        bool show = villager && !HasJob && !InShelter && Activity != GathererActivity.Fleeing;
        if (idleMarker.activeSelf != show)
            idleMarker.SetActive(show);
        if (!show)
            return;
        // Ekranda hep aynı boyda: kamera uzaklaştıkça büyür (oyun başındaki 80 m'de de görünsün), yakında küçük kalır.
        // Köylünün boyundan bağımsız.
        var cam = Camera.main;
        float size = cam != null ? Mathf.Max(1f, Vector3.Distance(cam.transform.position, transform.position) * markerScreenSize) : 1f;
        var scale = transform.lossyScale;
        idleMarker.transform.localScale = new Vector3(size / Mathf.Max(scale.x, 0.01f), size / Mathf.Max(scale.y, 0.01f), size / Mathf.Max(scale.z, 0.01f));
        float bob = Mathf.Sin(Time.time * 2f + markerPhase) * 0.08f * size;
        idleMarker.transform.position = transform.position + Vector3.up * (markerHeight + 0.24f * (size - 1f) + bob);
        idleMarker.transform.rotation = Quaternion.Euler(0f, Time.time * 60f, 0f);
    }

    static Mesh markerMesh;

    // Yassı bir elmas (sekizyüzlü), yüzleri ayrı köşeli.
    static Mesh MarkerMesh
    {
        get
        {
            if (markerMesh != null)
                return markerMesh;
            const float w = 0.16f, h = 0.24f;
            Vector3 top = new(0f, h, 0f), bottom = new(0f, -h, 0f);
            Vector3[] ring = { new(w, 0f, 0f), new(0f, 0f, w), new(-w, 0f, 0f), new(0f, 0f, -w) };
            var vertices = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                vertices.AddRange(new[] { top, b, a, bottom, a, b });
            }
            var triangles = new int[vertices.Count];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = i;
            markerMesh = new Mesh { name = "Idle Marker" };
            markerMesh.SetVertices(vertices);
            markerMesh.SetTriangles(triangles, 0);
            markerMesh.RecalculateNormals();
            markerMesh.RecalculateBounds();
            return markerMesh;
        }
    }

    // ---------------------------------------------------------------- Tsunami alarmı

    // İşi bırakıp kolları havada evinin kapısına koşar, içeri girer (görünmez olur).
    public void Flee()
    {
        if (!villager)
            return;
        if (!InShelter && Activity != GathererActivity.Fleeing)
        {
            resumeCart = pushedCart;
            resumeForest = HasJob && pushedCart == null;
        }
        Stop();
        HasJob = false;
        if (!ready)
            return; // Start alarmı görüp kendisi çağırır.
        routine = StartCoroutine(FleeLoop());
    }

    IEnumerator FleeLoop()
    {
        var house = Shelter ?? NearestHouse();
        if (house != null)
        {
            var door = ReachableDoor(house);
            // Yol bulunamazsa (tutulup bırakıldığı yer) biraz bekleyip yeniden dener; ilerleyemiyorsa kapıya düz
            // yürür. Yolun ortasında kaybolmaz.
            for (int attempt = 0; attempt < 6 && FlatDistance(door) > 1.5f; attempt++)
            {
                var before = transform.position;
                Activity = GathererActivity.Fleeing;
                yield return TravelTo(door, 0.6f);
                if (attempt > 0 && (transform.position - before).sqrMagnitude < 0.04f)
                    break;
            }
            Activity = GathererActivity.Fleeing;
            yield return MoveTo(door, 0.6f);
        }
        Activity = GathererActivity.Idle;
        SetHidden(true);
    }

    // Alarm bitti: kapıdan çıkıp önceki işine döner (vagonu başkası almışsa ya da işi yoksa evinin çevresinde dolaşır).
    public void LeaveShelter()
    {
        if (!villager)
            return;
        Stop();
        SetHidden(false);
        var cart = resumeCart;
        bool forest = resumeForest;
        resumeCart = null;
        resumeForest = false;
        if (!ready)
            return;
        if (cart != null && (cart.reservedBy == null || cart.reservedBy == this))
            PushCart(cart);
        else if (forest)
            WorkInForest();
        else
            Wander();
    }

    float FlatDistance(Vector3 point)
    {
        var offset = point - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    // Kapı hücresinin ortası evin ve ağaçların dibinde NavMesh dışında kalabilir: ona en yakın yürünebilir nokta.
    static Vector3 WalkableDoor(Houses.House house, Vector3 from)
    {
        var door = house.DoorNearest(from);
        return NavMesh.SamplePosition(door, out var hit, 4f, NavMesh.AllAreas) ? hit.position : door;
    }

    // Kendi evine yolu yoksa (köyün kopuk bir köşesinde kalmış), yolu olan en yakın evin kapısı.
    Vector3 ReachableDoor(Houses.House own)
    {
        var door = WalkableDoor(own, transform.position);
        if (NavigationBaker.IsReachable(transform.position, door))
            return door;
        var houses = new List<Houses.House>(Houses.All);
        houses.Sort((a, b) => (a.Center - transform.position).sqrMagnitude.CompareTo((b.Center - transform.position).sqrMagnitude));
        for (int i = 0; i < houses.Count && i < 15; i++)
        {
            var other = WalkableDoor(houses[i], transform.position);
            if (NavigationBaker.IsReachable(transform.position, other))
                return other;
        }
        return door;
    }

    Houses.House NearestHouse()
    {
        Houses.House best = null;
        float bestDistance = float.MaxValue;
        foreach (var house in Houses.All)
        {
            float distance = (house.Center - transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = house;
            }
        }
        return best;
    }

    void SetHidden(bool hidden)
    {
        InShelter = hidden;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = !hidden;
        foreach (var collider in GetComponents<Collider>())
            collider.enabled = !hidden;
    }

    // ---------------------------------------------------------------- Köylünün işleri

    // Bulunduğu yerin çevresinde dolaşır (işsiz).
    public void Wander()
    {
        Stop();
        HasJob = false;
        routine = StartCoroutine(WanderLoop(transform.position));
    }

    // Geliştirirken Play'de köylüyü tutup taşımadan işe koymak için (Inspector'da bileşenin menüsü).
    [ContextMenu("Test: Ormanda Çalış")]
    void TestWorkInForest()
    {
        if (Application.isPlaying)
            WorkInForest();
    }

    [ContextMenu("Test: Vagon İt")]
    void TestPushCart()
    {
        if (!Application.isPlaying)
            return;
        Gatherer best = null;
        float bestDistance = float.MaxValue;
        foreach (var cart in FindObjectsByType<Gatherer>())
        {
            if (!cart.needsPusher || cart.IsReserved)
                continue;
            float distance = (cart.transform.position - transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = cart;
            }
        }
        if (best != null)
            PushCart(best);
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
        offMeshReturn = null; // Tutulup başka yere bırakıldı.

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
            var node = home != null ? home : ResourceNode.FindNearestFree(resourceType, transform.position, IsUnreachable);
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

            if (!node.TryClaim(this))
            {
                yield return new WaitForSeconds(.25f);
                continue;
            }
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
            // Yolu bulunamayan ağaç (sık ormanın ortası, tepenin üstü): bir süre atlanır, başkasına gidilir.
            if (home == null && FlatDistance(node.transform.position) > stopDistance + MaxFinalStep)
            {
                unreachable[node] = Time.time + UnreachableSeconds;
                node.Release(this);
                claimed = null;
                CurrentTarget = null;
                yield return null;
                continue;
            }
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

            // Kuyunun bu taraftaki kenarına gidilir; ortasına yol aranırsa kuyunun etrafında dolanır. O kenar evlerin
            // arasında kalıp yolu yoksa kuyunun çevresinde yolu olan en yakın kenar seçilir.
            yield return TravelTo(WellRim(well), 0.5f);
            // Kuyunun kenarına yol bulunamadıysa (başladığı yer kopuktu), yaklaştığı yerden bir kez daha dener.
            if (well != null && FlatDistance(well.transform.position) > Mathf.Max(wellStopDistance, well.ApproachRadius) + MaxFinalStep)
                yield return TravelTo(WellRim(well), 0.5f);
            SetCarrying(false);
            if (well != null)
                well.Throw(resourceType, carried, transform.position + Vector3.up);
        }
    }

    const float UnreachableSeconds = 30f;
    readonly Dictionary<ResourceNode, float> unreachable = new();

    bool IsUnreachable(ResourceNode node) => unreachable.TryGetValue(node, out var until) && Time.time < until;

    Vector3 WellRim(Well well)
    {
        float approach = Mathf.Max(wellStopDistance, well.ApproachRadius) + 0.5f;
        var fromWell = transform.position - well.transform.position;
        fromWell.y = 0f;
        if (fromWell.sqrMagnitude < 0.01f)
            fromWell = Vector3.forward;
        var first = well.transform.position + fromWell.normalized * approach;
        // Kenardan dışarı doğru halkalar; her halkada bu taraftan başlayıp iki yana sırayla 22.5°'lik adımlarla.
        for (int ring = 0; ring < 5; ring++)
        {
            float radius = approach + ring * 1.5f;
            for (int step = 0; step < 16; step++)
            {
                float angle = (step + 1) / 2 * 22.5f * (step % 2 == 0 ? 1f : -1f);
                var rim = well.transform.position + Quaternion.Euler(0f, angle, 0f) * fromWell.normalized * radius;
                if (NavigationBaker.IsReachable(transform.position, rim))
                    return rim;
            }
        }
        return first;
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

    // NavMesh varsa engellerin etrafından, yoksa düz çizgide gider. NavMesh olup yol bulunamazsa yerinde kalır;
    // düz gitse evlerin ve dağların içinden geçerdi.
    IEnumerator TravelTo(Vector3 target, float distance)
    {
        if (!NavigationBaker.Exists)
        {
            yield return MoveTo(target, distance);
            yield break;
        }
        // Son adımda NavMesh dışına (ağaçların arasına) girdiyse önce girdiği yoldan geri çıkar; o cepten NavMesh ile
        // yol bulunmaz.
        if (offMeshReturn is Vector3 back)
        {
            offMeshReturn = null;
            yield return MoveTo(back, PathStopDistance);
        }
        if (!NavigationBaker.TryFindPath(transform.position, target, corners))
        {
            if (Activity != GathererActivity.Fleeing)
                Activity = GathererActivity.Idle;
            yield return new WaitForSeconds(0.5f);
            yield break;
        }
        var points = corners.ToArray();
        for (int i = 1; i < points.Length; i++)
            yield return MoveTo(points[i], PathStopDistance);
        // Hedef bir engelin içindeyse (ağaç, kuyu) NavMesh'in kenarından son birkaç metre düz yürünür, yoksa uzaktan
        // vurur/atardı. Hedef daha uzaksa (yol yarıda kaldı) NavMesh'in bittiği yerde durulur; düz gitse evlerin
        // içinden geçerdi.
        var edge = NavigationBaker.ClampToNavMesh(points[points.Length - 1], target);
        var rest = target - edge;
        rest.y = 0f;
        bool offMesh = rest.magnitude <= MaxFinalStep + distance && rest.magnitude > distance;
        if (offMesh)
            yield return MoveTo(edge, PathStopDistance);
        yield return MoveTo(offMesh ? target : edge, distance);
        if (offMesh)
            offMeshReturn = edge;
    }

    const float MaxFinalStep = 3f;
    Vector3? offMeshReturn;

    IEnumerator MoveTo(Vector3 target, float distance)
    {
        while (true)
        {
            yield return WaitForPusher();
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= distance)
            {
                if (Activity != GathererActivity.Fleeing)
                    Activity = GathererActivity.Idle;
                yield break;
            }

            if (Activity != GathererActivity.Fleeing)
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

    // Zemin ayağın bu kadar üstünden aşağı bakılarak bulunur. Harita parçalarının collider'ında ev çatısı ve ağaç
    // tepesi de var; yukarıdan bakılsaydı yanlarından geçerken çatıya, ağacın üstüne çıkılırdı.
    const float MaxClimb = 0.75f;

    bool TryGetGroundHeight(Vector3 position, out float height)
    {
        // Önce ayağın hemen üstünden; altında hiçbir şey yoksa (zeminin altına düşmüş) eskisi gibi yukarıdan.
        return TryGetGroundHeight(position, position.y - heightOffset + MaxClimb, out height)
            || TryGetGroundHeight(position, position.y + 50f, out height);
    }

    bool TryGetGroundHeight(Vector3 position, float fromY, out float height)
    {
        var origin = new Vector3(position.x, fromY, position.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 100f, groundMask, QueryTriggerInteraction.Ignore);
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
