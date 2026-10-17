using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: InternalsVisibleTo("IdleTown.EditModeTests")]

namespace IdleTown.Construction
{
    public sealed class WallConstructionController : MonoBehaviour
    {
        static readonly Rect ConstructionPanelRect = new(16f, 16f, 440f, 225f);

        [Header("Scene references")]
        [SerializeField] World world;
        [Tooltip("Must implement IWallResourceStore.")]
        [SerializeField] MonoBehaviour resourceStoreSource;
        [SerializeField] Camera inputCamera;
        [SerializeField] GameObject wallPrefab;

        [Header("Placement blocking")]
        [Tooltip("Only colliders on these dedicated non-wall layers block construction. Keep ground and walls out of this mask.")]
        [SerializeField] LayerMask obstacleLayers;
        [SerializeField, Min(0f)] float obstaclePadding = 0.04f;

        [Header("Cost per edge")]
        [SerializeField, Min(0)] int woodCost = 1;
        [SerializeField, Min(0)] int stoneCost = 1;

        [Header("Generated wall geometry")]
        [SerializeField, Min(0.01f)] float wallHeight = 1.25f;
        [SerializeField, Min(0.01f)] float wallThickness = 0.18f;

        readonly WallConstructionModel model = new();
        readonly List<WallEdge> previewPath = new();
        IWallResourceStore resourceStore;
        LineRenderer previewLine;
        Material previewMaterial;
        Material generatedWallMaterial;
        bool hasStart;
        Vector2Int startNode;
        string status = "İlk düğümü seçin.";
        string confirmationStatus = "Henüz kalıcı inşa yapılmadı.";
        bool previewValid;
        int previewWood;
        int previewStone;
        bool buildInProgress;
        bool hasPreviewEndpoint;
        Vector2Int previewEndpoint;
        bool hasLastValidPreviewEndpoint;
        Vector2Int lastValidPreviewEndpoint;
        int lastValidPreviewCount;
        int lastValidPreviewWood;
        int lastValidPreviewStone;

        public IReadOnlyCollection<WallEdge> Edges => model.Edges;
        public LayerMask ObstacleLayers
        {
            get => obstacleLayers;
            set => obstacleLayers = value;
        }
        public event Action<WallEdge, GameObject> WallBuilt;

        void Awake()
        {
            ResolveReferences();
            CreatePreviewLine();
        }

        void OnValidate()
        {
            woodCost = Mathf.Max(0, woodCost);
            stoneCost = Mathf.Max(0, stoneCost);
            wallHeight = Mathf.Max(0.01f, wallHeight);
            wallThickness = Mathf.Max(0.01f, wallThickness);
            obstaclePadding = Mathf.Max(0f, obstaclePadding);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelSelection();
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                CancelSelection();
                return;
            }

            var pointerOverPanel = mouse != null && IsPointerOverConstructionPanel(mouse.position.ReadValue());
            var hoveredNode = default(Vector2Int);
            var hasNode = mouse != null && TryGetNode(mouse.position.ReadValue(), out hoveredNode);
            if (hasStart && !pointerOverPanel)
                RefreshPreview(hasNode ? hoveredNode : (Vector2Int?)null);

            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                ConfirmHoveredPreview();
                return;
            }

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || pointerOverPanel)
                return;

            if (!hasNode)
            {
                status = "Harita sınırları içindeki bir düğüme tıklayın.";
                return;
            }

            HandleNodeClick(hoveredNode);
        }

        void OnGUI()
        {
            GUILayout.BeginArea(ConstructionPanelRect, GUI.skin.box);
            GUILayout.Label("DUVAR İNŞASI");
            GUILayout.Label("Sol tık: başlangıç / bitiş   •   Enter: ön izlemeyi inşa et");
            GUILayout.Label("Sağ tık veya Esc: iptal");
            if (resourceStore != null)
                GUILayout.Label($"Merkez depo — Odun: {resourceStore.Wood}   Taş: {resourceStore.Stone}");
            else
                GUILayout.Label("Merkez depo bağlantısı yok (IWallResourceStore gerekli).");

            GUILayout.Label($"Kalıcı duvar — {model.Edges.Count} parça");
            if (hasStart)
                GUILayout.Label($"Ön izleme (henüz inşa edilmedi) — {previewPath.Count} parça, {previewWood} odun, {previewStone} taş");

            var wasEnabled = GUI.enabled;
            GUI.enabled = hasStart && hasLastValidPreviewEndpoint;
            var buildButtonLabel = hasLastValidPreviewEndpoint
                ? $"İnşa Et — son geçerli: {lastValidPreviewCount} parça, {lastValidPreviewWood} odun, {lastValidPreviewStone} taş"
                : "İnşa Et — geçerli ön izleme gerekli";
            if (GUILayout.Button(buildButtonLabel))
                ConfirmLastValidPreview();
            GUI.enabled = wasEnabled;

            var oldColor = GUI.contentColor;
            GUI.contentColor = hasStart && !previewValid ? new Color(1f, 0.45f, 0.35f) : Color.white;
            GUILayout.Label($"Durum: {status}");
            GUILayout.Label($"Son onay: {confirmationStatus}");
            GUI.contentColor = oldColor;
            GUILayout.EndArea();
        }

        public void Configure(World targetWorld, MonoBehaviour resources, Camera camera = null)
        {
            world = targetWorld;
            resourceStoreSource = resources;
            inputCamera = camera;
            ResolveReferences();
        }

        public void SetResourceStore(IWallResourceStore store)
        {
            resourceStore = store;
            resourceStoreSource = store as MonoBehaviour;
        }

        public bool TryBuild(Vector2Int start, Vector2Int end, out string reason)
        {
            if (buildInProgress)
            {
                reason = "Başka bir duvar inşa işlemi devam ediyor.";
                return false;
            }

            if (world == null)
            {
                reason = "WallConstructionController için World atanmamış.";
                return false;
            }

            var path = WallConstructionModel.CreateSnappedPath(start, end);
            if (!Validate(path, out reason, out var wood, out var stone))
                return false;

            buildInProgress = true;
            GameObject stagingRoot = null;
            var stagedWalls = new List<GameObject>(path.Count);
            try
            {
                stagingRoot = new GameObject("Staged Wall Build");
                stagingRoot.transform.SetParent(transform, false);
                stagingRoot.SetActive(false);

                try
                {
                    for (var i = 0; i < path.Count; i++)
                        CreateWall(path[i], stagingRoot.transform, stagedWalls);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    RollbackStaged(stagingRoot, stagedWalls);
                    reason = "Duvar parçaları oluşturulamadı; kaynak harcanmadı.";
                    return false;
                }

                bool spent;
                try
                {
                    // The resource implementation owns the atomic check-and-deduct operation.
                    spent = resourceStore.TrySpend(wood, stone);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    RollbackStaged(stagingRoot, stagedWalls);
                    reason = "Kaynak deposu işlemi tamamlanamadı; duvar kurulmadı.";
                    return false;
                }

                if (!spent)
                {
                    RollbackStaged(stagingRoot, stagedWalls);
                    reason = "Kaynaklar başka bir sistem tarafından değişti; yeterli odun veya taş yok.";
                    return false;
                }

                // Resource spend and model mutation are the commit point. All visuals already exist.
                model.Add(path);
                stagingRoot.name = "Wall Build";
                // Instantiate preserves the prefab root's activeSelf value. A prefab kept inactive as
                // a scene template would otherwise remain invisible after its staging parent commits.
                for (var i = 0; i < stagedWalls.Count; i++)
                    stagedWalls[i].SetActive(true);
                stagingRoot.SetActive(true);

                reason = string.Empty;
                NotifyWallBuilt(path, stagedWalls);
                return true;
            }
            finally
            {
                buildInProgress = false;
            }
        }

        internal void HandleNodeClick(Vector2Int node)
        {
            if (!hasStart)
            {
                hasStart = true;
                startNode = node;
                hasPreviewEndpoint = false;
                hasLastValidPreviewEndpoint = false;
                status = "Bitiş düğümünü seçin.";
                confirmationStatus = "Ön izleme henüz onaylanmadı.";
                RefreshPreview(node);
                return;
            }

            ConfirmEndpoint(node);
        }

        internal bool ConfirmEndpoint(Vector2Int endpoint)
        {
            if (!hasStart)
            {
                status = "Önce bir başlangıç düğümü seçin.";
                confirmationStatus = "Başarısız: başlangıç düğümü seçilmedi; kaynak harcanmadı.";
                return false;
            }

            if (!TryBuild(startNode, endpoint, out var reason))
            {
                status = reason;
                confirmationStatus = $"Başarısız: {reason}";
                previewValid = false;
                hasLastValidPreviewEndpoint = false;
                return false;
            }

            var builtCount = WallConstructionModel.CreateSnappedPath(startNode, endpoint).Count;
            var spentWood = woodCost * builtCount;
            var spentStone = stoneCost * builtCount;
            hasStart = false;
            hasPreviewEndpoint = false;
            hasLastValidPreviewEndpoint = false;
            previewValid = false;
            previewPath.Clear();
            previewLine.positionCount = 0;
            status = $"İnşa tamamlandı: {builtCount} parça eklendi; {spentWood} odun ve {spentStone} taş harcandı.";
            confirmationStatus = status;
            return true;
        }

        internal bool ConfirmHoveredPreview()
        {
            if (!hasStart)
            {
                status = "Enter ile inşa etmeden önce bir başlangıç düğümü seçin.";
                confirmationStatus = $"Başarısız: {status}";
                return false;
            }

            if (!previewValid || !hasPreviewEndpoint)
            {
                status = "Enter ile inşa etmek için geçerli bir ön izleme seçin.";
                confirmationStatus = $"Başarısız: {status}";
                return false;
            }

            return ConfirmEndpoint(previewEndpoint);
        }

        internal bool ConfirmLastValidPreview()
        {
            if (!hasStart || !hasLastValidPreviewEndpoint)
            {
                status = "İnşa etmek için geçerli bir ön izleme seçin.";
                confirmationStatus = $"Başarısız: {status}";
                return false;
            }

            return ConfirmEndpoint(lastValidPreviewEndpoint);
        }

        void ResolveReferences()
        {
            resourceStore = resourceStoreSource as IWallResourceStore;
            if (inputCamera == null)
                inputCamera = Camera.main;
        }

        void CancelSelection()
        {
            hasStart = false;
            previewPath.Clear();
            previewValid = false;
            hasPreviewEndpoint = false;
            hasLastValidPreviewEndpoint = false;
            if (previewLine != null)
                previewLine.positionCount = 0;
            status = "İnşa iptal edildi. İlk düğümü seçin.";
            confirmationStatus = "Seçim iptal edildi; kaynak harcanmadı.";
        }

        bool TryGetNode(Vector2 screenPosition, out Vector2Int node)
        {
            node = default;
            if (world == null || inputCamera == null || world.CellSize <= 0f)
                return false;

            var plane = new Plane(world.transform.up, world.transform.position);
            var ray = inputCamera.ScreenPointToRay(screenPosition);
            if (!plane.Raycast(ray, out var distance))
                return false;

            var local = world.transform.InverseTransformPoint(ray.GetPoint(distance));
            node = new Vector2Int(
                Mathf.RoundToInt(local.x / world.CellSize),
                Mathf.RoundToInt(local.z / world.CellSize));
            return node.x >= 0 && node.y >= 0 && node.x <= world.Width && node.y <= world.Height;
        }

        internal void RefreshPreview(Vector2Int? hoveredNode)
        {
            // Editor callers can request a preview before Unity invokes Awake.
            CreatePreviewLine();
            previewPath.Clear();
            hasPreviewEndpoint = hoveredNode.HasValue;
            if (!hoveredNode.HasValue)
            {
                previewLine.positionCount = 0;
                previewValid = false;
                status = "Harita sınırları içindeki bir düğüme gelin.";
                return;
            }

            previewEndpoint = hoveredNode.Value;
            previewPath.AddRange(WallConstructionModel.CreateSnappedPath(startNode, hoveredNode.Value));
            previewValid = Validate(previewPath, out var reason, out previewWood, out previewStone);
            if (previewValid)
            {
                hasLastValidPreviewEndpoint = true;
                lastValidPreviewEndpoint = hoveredNode.Value;
                lastValidPreviewCount = previewPath.Count;
                lastValidPreviewWood = previewWood;
                lastValidPreviewStone = previewStone;
            }
            status = previewValid
                ? "Geçerli ön izleme: sol tık, Enter veya İnşa Et ile onaylayın."
                : reason;
            DrawPreview();
        }

        internal static bool IsPointerOverConstructionPanel(Vector2 screenPosition)
        {
            // Input System screen coordinates start at bottom-left; IMGUI starts at top-left.
            var guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return ConstructionPanelRect.Contains(guiPosition);
        }

        bool Validate(IReadOnlyList<WallEdge> path, out string reason, out int wood, out int stone)
        {
            wood = 0;
            stone = 0;
            if (!model.Validate(path, world.Width, world.Height, out reason))
                return false;

            if (!HasObstacleClearance(path, out reason))
                return false;

            var totalWood = (long)woodCost * path.Count;
            var totalStone = (long)stoneCost * path.Count;
            if (totalWood > int.MaxValue || totalStone > int.MaxValue)
            {
                reason = "Duvar maliyeti desteklenen sınırı aşıyor.";
                return false;
            }

            wood = (int)totalWood;
            stone = (int)totalStone;
            if (resourceStore == null)
            {
                reason = "Kaynak deposu IWallResourceStore arayüzünü uygulamıyor.";
                return false;
            }

            if (!resourceStore.CanAfford(wood, stone))
            {
                reason = $"Yetersiz kaynak: {wood} odun ve {stone} taş gerekiyor.";
                return false;
            }

            return true;
        }

        void CreatePreviewLine()
        {
            if (previewLine != null)
                return;

            var previewObject = new GameObject("Wall Preview");
            previewObject.transform.SetParent(transform, false);
            previewLine = previewObject.AddComponent<LineRenderer>();
            previewLine.useWorldSpace = true;
            previewLine.loop = false;
            previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewLine.receiveShadows = false;
            previewLine.widthMultiplier = wallThickness * 1.4f;
            previewMaterial = CreateColorMaterial(new Color(0.2f, 1f, 0.35f, 0.85f));
            previewLine.sharedMaterial = previewMaterial;
        }

        void DrawPreview()
        {
            if (previewPath.Count == 0)
            {
                previewLine.positionCount = 0;
                return;
            }

            var color = previewValid
                ? new Color(0.2f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.18f, 0.12f, 0.9f);
            previewLine.startColor = color;
            previewLine.endColor = color;
            previewLine.widthMultiplier = wallThickness * 1.4f;
            previewLine.positionCount = previewPath.Count + 1;
            previewLine.SetPosition(0, NodePosition(previewPath[0].A, wallHeight + 0.05f));
            for (var i = 0; i < previewPath.Count; i++)
                previewLine.SetPosition(i + 1, NodePosition(previewPath[i].B, wallHeight + 0.05f));
        }

        bool HasObstacleClearance(IReadOnlyList<WallEdge> path, out string reason)
        {
            if (obstacleLayers.value == 0)
            {
                reason = string.Empty;
                return true;
            }

            Physics.SyncTransforms();

            for (var i = 0; i < path.Count; i++)
            {
                var edge = path[i];
                var start = NodePosition(edge.A, wallHeight * 0.5f);
                var end = NodePosition(edge.B, wallHeight * 0.5f);
                var direction = end - start;
                var rotation = Quaternion.LookRotation(direction.normalized, world.transform.up);
                var halfExtents = new Vector3(
                    wallThickness * 0.5f + obstaclePadding,
                    Mathf.Max(0.01f, wallHeight * 0.45f),
                    direction.magnitude * 0.5f + obstaclePadding);

                if (Physics.CheckBox(
                        (start + end) * 0.5f,
                        halfExtents,
                        rotation,
                        obstacleLayers,
                        QueryTriggerInteraction.Collide))
                {
                    reason = "Duvar yolu korunan bir yapı veya engelle çakışıyor.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        void CreateWall(WallEdge edge, Transform stagingParent, ICollection<GameObject> stagedWalls)
        {
            var start = NodePosition(edge.A, wallHeight * 0.5f);
            var end = NodePosition(edge.B, wallHeight * 0.5f);
            var direction = end - start;
            var rotation = Quaternion.LookRotation(direction.normalized, world.transform.up);
            GameObject instance;

            if (wallPrefab != null)
            {
                instance = Instantiate(wallPrefab, (start + end) * 0.5f, rotation, stagingParent);
                stagedWalls.Add(instance);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stagedWalls.Add(instance);
                instance.transform.SetParent(stagingParent, true);
                instance.transform.SetPositionAndRotation((start + end) * 0.5f, rotation);
                generatedWallMaterial ??= CreateColorMaterial(new Color(0.48f, 0.29f, 0.14f, 1f));
                instance.GetComponent<Renderer>().sharedMaterial = generatedWallMaterial;
            }

            instance.name = $"Wall {edge.A.x},{edge.A.y} - {edge.B.x},{edge.B.y}";
            instance.transform.localScale = new Vector3(wallThickness, wallHeight, direction.magnitude);
            if (instance.GetComponentInChildren<Collider>() == null)
                instance.AddComponent<BoxCollider>();

            // A 3D collider with no Rigidbody is a static physics collider.
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                DestroyObject(body);
            }
        }

        void NotifyWallBuilt(IReadOnlyList<WallEdge> path, IReadOnlyList<GameObject> instances)
        {
            var subscribers = WallBuilt?.GetInvocationList();
            if (subscribers == null)
                return;

            for (var edgeIndex = 0; edgeIndex < path.Count; edgeIndex++)
            {
                for (var subscriberIndex = 0; subscriberIndex < subscribers.Length; subscriberIndex++)
                {
                    try
                    {
                        ((Action<WallEdge, GameObject>)subscribers[subscriberIndex])(
                            path[edgeIndex],
                            instances[edgeIndex]);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }
        }

        Vector3 NodePosition(Vector2Int node, float y)
        {
            return world.transform.TransformPoint(
                new Vector3(node.x * world.CellSize, y, node.y * world.CellSize));
        }

        static Material CreateColorMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            return material;
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        static void RollbackStaged(GameObject stagingRoot, IReadOnlyList<GameObject> stagedWalls)
        {
            for (var i = stagedWalls.Count - 1; i >= 0; i--)
                DestroyObject(stagedWalls[i]);
            DestroyObject(stagingRoot);
        }

        void OnDestroy()
        {
            if (previewMaterial != null)
                DestroyObject(previewMaterial);
            if (generatedWallMaterial != null)
                DestroyObject(generatedWallMaterial);
        }
    }
}
