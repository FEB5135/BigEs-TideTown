using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Oyun sırasında yürünebilir alanı (NavMesh) kurar ve toplayıcılara yol bulur.
// Harita her oyunda rastgele kurulduğu için önceden pişirilmiş NavMesh yoktur: harita bitince Rebuild() çağrılır.
// Engeller sahnedeki her Renderer'ın kutusundan çıkarılır; böylece model okunabilir olmasa da, collider'ı olmasa da
// (rastgele yerleştirilen evler, ağaçlar, ocaklar) hesaba katılır. Hareket edenler (Gatherer) ve NavIgnore'lular hariç.
[DefaultExecutionOrder(1000)]
public class NavigationBaker : MonoBehaviour
{
    [Tooltip("Açıksa sahne açılınca kurulur. Haritayı sonradan üreten sistem kapatıp kendisi Rebuild() çağırabilir.")]
    [SerializeField] bool buildOnStart = true;
    [Tooltip("Vagonun yarı genişliği: engellerin kenarından bu kadar uzak durulur.")]
    [SerializeField] float agentRadius = 0.8f;
    [SerializeField] float agentHeight = 2f;
    [SerializeField] float maxSlope = 25f;
    [Tooltip("Bu yükseklikten alçak engellerin (raylar, moloz) üstünden geçilir.")]
    [SerializeField] float stepHeight = 0.4f;
    [SerializeField] float voxelSize = 0.4f;

    static NavigationBaker instance;
    NavMeshDataInstance dataInstance;
    NavMeshBuildSettings settings;

    // Sahnede kurucu yoksa toplayıcılar düz çizgide yürür.
    public static bool Exists => instance != null;
    public static bool Ready { get; private set; }

    void Awake()
    {
        instance = this;
        settings = NavMesh.CreateSettings();
        settings.agentRadius = agentRadius;
        settings.agentHeight = agentHeight;
        settings.agentSlope = maxSlope;
        settings.agentClimb = stepHeight;
        settings.overrideVoxelSize = true;
        settings.voxelSize = voxelSize;
    }

    void Start()
    {
        if (buildOnStart)
            Rebuild();
    }

    void OnDestroy()
    {
        dataInstance.Remove();
        if (instance == this)
        {
            instance = null;
            Ready = false;
        }
    }

    // Harita (WFC) her değiştiğinde çağrılır.
    public static void RequestRebuild()
    {
        if (instance != null)
            instance.Rebuild();
    }

    public void Rebuild()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var sources = new List<NavMeshBuildSource>();
        var bounds = new Bounds();
        bool any = false;
        foreach (var renderer in FindObjectsByType<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer)
                continue;
            if (renderer.GetComponentInParent<Gatherer>() != null || renderer.GetComponentInParent<NavIgnore>() != null)
                continue;
            // A WFC chunk contains both flat ground and slopes. Its bounding box is not terrain.
            var filter = renderer.GetComponent<MeshFilter>();
            if (renderer.GetComponentInParent<MapView>() != null && filter != null && filter.sharedMesh != null)
            {
                sources.Add(new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Mesh, sourceObject = filter.sharedMesh,
                    transform = filter.transform.localToWorldMatrix, area = 0
                });
                Encapsulate(ref bounds, ref any, renderer.bounds);
                continue;
            }
            var local = renderer.localBounds;
            var matrix = renderer.transform.localToWorldMatrix * Matrix4x4.Translate(local.center);
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = Vector3.Max(local.size, new Vector3(0.01f, 0.01f, 0.01f)),
                transform = matrix,
                area = 0,
            });
            Encapsulate(ref bounds, ref any, renderer.bounds);
        }
        foreach (var terrain in FindObjectsByType<Terrain>())
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Terrain,
                sourceObject = terrain.terrainData,
                transform = Matrix4x4.Translate(terrain.transform.position),
                area = 0,
            });
            Encapsulate(ref bounds, ref any, new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size));
        }
        if (!any)
            return;

        bounds.Expand(2f);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        dataInstance.Remove();
        dataInstance = NavMesh.AddNavMeshData(data);
        Ready = dataInstance.valid;
        Debug.Log($"NavMesh kuruldu: {sources.Count} kaynak, {watch.ElapsedMilliseconds} ms.");
    }

    static void Encapsulate(ref Bounds bounds, ref bool any, Bounds add)
    {
        if (any)
            bounds.Encapsulate(add);
        else
            bounds = add;
        any = true;
    }

    // from'dan to'ya yürünebilir yolun köşe noktaları. Hedef bir engelin içindeyse en yakın yürünebilir noktaya gider.
    public static bool TryFindPath(Vector3 from, Vector3 to, List<Vector3> corners)
    {
        corners.Clear();
        if (!Ready)
            return false;
        var filter = new NavMeshQueryFilter { agentTypeID = instance.settings.agentTypeID, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(from, out var start, 6f, filter) || !NavMesh.SamplePosition(to, out var end, 12f, filter))
            return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, filter, path) || path.status == NavMeshPathStatus.PathInvalid)
            return false;
        corners.AddRange(path.corners);
        return corners.Count > 0;
    }
}
