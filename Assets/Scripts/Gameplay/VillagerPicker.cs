using IdleTown.Construction;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// Köylüleri elle işe koyma (Tomodachi Life tarzı): imleç bir köylünün üzerine gelince açık ele döner, sol tıkla
// tutulur (kapalı el), köylü havada imleci takip edip sallanır. Bırakınca:
// - ormana (bir ağacın yakınına) bırakıldıysa oduncu olur ve sürekli odun toplar,
// - bir maden ocağına bırakıldıysa o ocağın vagonunu itmeye gider (vagonla madene girer, taşla kuyuya döner),
// - başka bir yere bırakıldıysa orada boşta dolaşır.
[RequireComponent(typeof(Camera))]
public class VillagerPicker : MonoBehaviour
{
    [SerializeField] Texture2D openHand;
    [SerializeField] Texture2D closedHand;
    [Tooltip("İmlecin tıklama noktası (dokunun içinde, piksel).")]
    [SerializeField] Vector2 hotspot = new(22f, 10f);
    [SerializeField] float maxRayDistance = 400f;
    [Tooltip("İmleç bir köylünün ekranda bu kadar yakınındaysa (1080p'de piksel) o köylü tutulabilir; uzaktan küçük görünen köylüleri yakalamak kolay olsun.")]
    [SerializeField] float pickRadius = 32f;
    [Tooltip("Tutulan köylü zeminden bu kadar yukarıda taşınır.")]
    [SerializeField] float holdHeight = 3f;
    [Tooltip("Bırakılan yerin bu yarıçapında bir ağaç varsa köylü oduncu olur.")]
    [SerializeField] float forestRadius = 6f;
    [Tooltip("Bırakılan yerin bu yarıçapında bir maden ocağı (ya da ray başı) varsa köylü vagonu iter.")]
    [SerializeField] float quarryRadius = 12f;

    // Köylünün üzerindeyken ya da birini tutarken true; tıklayarak toplama bu sırada çalışmaz.
    public static bool Busy { get; private set; }

    static readonly RaycastHit[] hits = new RaycastHit[16];

    Camera cam;
    WallConstructionController construction;
    Gatherer held;
    Vector3 holdPoint;
    float holdTime;
    Texture2D shownCursor;

    void Awake() => cam = GetComponent<Camera>();

    void OnDisable()
    {
        if (held != null)
            Drop();
        SetCursor(null);
        Busy = false;
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !Allowed())
        {
            if (held != null)
                Drop();
            SetCursor(null);
            Busy = false;
            return;
        }

        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (held != null)
        {
            if (TryGroundPoint(ray, out var ground))
                holdPoint = ground;
            Carry();
            if (!mouse.leftButton.isPressed)
                Drop();
            SetCursor(closedHand);
            Busy = true;
            return;
        }

        var villager = VillagerUnder(ray, mouse.position.ReadValue());
        Busy = villager != null;
        SetCursor(villager != null ? openHand : null);
        if (villager != null && mouse.leftButton.wasPressedThisFrame)
            PickUp(villager, ray);
    }

    bool Allowed()
    {
        if (MainMenu.IsOpen)
            return false;
        construction ??= FindAnyObjectByType<WallConstructionController>();
        return construction == null || !construction.isActiveAndEnabled || !construction.ConstructionModeActive;
    }

    // Önce doğrudan ışınla, olmazsa ekranda imlece en yakın köylü (pickRadius içinde).
    Gatherer VillagerUnder(Ray ray, Vector2 mousePosition)
    {
        int count = Physics.RaycastNonAlloc(ray, hits, maxRayDistance, ~0, QueryTriggerInteraction.Collide);
        Gatherer best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var gatherer = hits[i].collider.GetComponentInParent<Gatherer>();
            if (gatherer != null && gatherer.IsVillager && hits[i].distance < bestDistance)
            {
                best = gatherer;
                bestDistance = hits[i].distance;
            }
        }
        if (best != null)
            return best;

        float radius = pickRadius * Screen.height / 1080f;
        bestDistance = radius;
        foreach (var villager in Gatherer.Villagers)
        {
            var screen = cam.WorldToScreenPoint(villager.transform.position + Vector3.up);
            if (screen.z <= 0f)
                continue;
            float distance = Vector2.Distance(screen, mousePosition);
            if (distance < bestDistance)
            {
                best = villager;
                bestDistance = distance;
            }
        }
        return best;
    }

    // Zemin noktası: köylüler, ağaç/ocak toplama alanları ve tetikleyiciler sayılmaz.
    bool TryGroundPoint(Ray ray, out Vector3 point)
    {
        int count = Physics.RaycastNonAlloc(ray, hits, maxRayDistance, ~0, QueryTriggerInteraction.Ignore);
        point = default;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (collider.GetComponentInParent<Gatherer>() != null || collider.GetComponentInParent<ResourceNode>() != null)
                continue;
            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                point = hits[i].point;
            }
        }
        if (bestDistance < float.MaxValue)
            return true;
        // Hiçbir şeye çarpmadıysa y = 0 düzlemi.
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    void PickUp(Gatherer villager, Ray ray)
    {
        held = villager;
        held.Stop();
        holdTime = 0f;
        holdPoint = TryGroundPoint(ray, out var ground) ? ground : villager.transform.position;
    }

    // Havada imleci takip eder, sarkaç gibi hafifçe sallanır.
    void Carry()
    {
        holdTime += Time.deltaTime;
        var target = holdPoint + Vector3.up * holdHeight;
        var t = held.transform;
        var previous = t.position;
        t.position = Vector3.Lerp(previous, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        var velocity = (t.position - previous) / Mathf.Max(Time.deltaTime, 0.0001f);
        float sway = Mathf.Sin(holdTime * 6f) * 8f;
        var tilt = Quaternion.Euler(Mathf.Clamp(velocity.z * 2f, -25f, 25f), 0f, Mathf.Clamp(-velocity.x * 2f, -25f, 25f) + sway);
        var yaw = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        t.rotation = Quaternion.Slerp(t.rotation, tilt * yaw, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    void Drop()
    {
        var villager = held;
        held = null;
        var spot = holdPoint;
        if (NavMesh.SamplePosition(spot, out var nav, 6f, NavMesh.AllAreas))
            spot = nav.position;
        var t = villager.transform;
        t.SetPositionAndRotation(new Vector3(spot.x, t.position.y, spot.z), Quaternion.Euler(0f, t.eulerAngles.y, 0f));
        villager.SnapToGround();

        var cart = QuarryCartNear(holdPoint);
        if (cart != null)
            villager.PushCart(cart);
        else if (TreeNear(holdPoint))
            villager.WorkInForest();
        else
            villager.Wander();
    }

    bool TreeNear(Vector3 point)
    {
        foreach (var collider in Physics.OverlapSphere(point, forestRadius, ~0, QueryTriggerInteraction.Collide))
        {
            var node = collider.GetComponentInParent<ResourceNode>();
            if (node != null && node.Type == ResourceType.Wood)
                return true;
        }
        return false;
    }

    // Bırakılan yere en yakın ocağın (kaya kütlesi ya da ray başı) boştaki vagonu.
    Gatherer QuarryCartNear(Vector3 point)
    {
        Gatherer best = null;
        float bestDistance = quarryRadius;
        foreach (var spawner in FindObjectsByType<GathererSpawner>())
        {
            var node = spawner.GetComponent<ResourceNode>();
            float distance = DistanceTo(node, point);
            if (distance > bestDistance)
                continue;
            var cart = spawner.FreeCart();
            if (cart == null)
                continue;
            best = cart;
            bestDistance = distance;
        }
        return best;
    }

    static float DistanceTo(ResourceNode node, Vector3 point)
    {
        float best = Flat(node.transform.position - point);
        foreach (var collider in node.GetComponentsInChildren<Collider>())
            best = Mathf.Min(best, Flat(collider.bounds.ClosestPoint(point) - point)); // MeshCollider'da ClosestPoint yok
        if (node.ApproachPath != null)
            foreach (var stop in node.ApproachPath)
                if (stop != null)
                    best = Mathf.Min(best, Flat(stop.position - point));
        return best;
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    void SetCursor(Texture2D texture)
    {
        if (shownCursor == texture)
            return;
        shownCursor = texture;
        if (texture == null)
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        else
            Cursor.SetCursor(texture, hotspot, CursorMode.ForceSoftware); // donanım imleci 32 px ile sınırlı olabiliyor
    }
}
