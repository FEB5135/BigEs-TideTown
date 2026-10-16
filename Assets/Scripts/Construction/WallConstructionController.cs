using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IdleTown.Construction
{
    public sealed class WallConstructionController : MonoBehaviour
    {
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
        bool previewValid;
        int previewWood;
        int previewStone;
        bool buildInProgress;

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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CancelSelection();

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
                CancelSelection();

            var hasNode = TryGetNode(mouse.position.ReadValue(), out var hoveredNode);
            if (hasStart)
                RefreshPreview(hasNode ? hoveredNode : (Vector2Int?)null);

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            if (!hasNode)
            {
                status = "Harita sınırları içindeki bir düğüme tıklayın.";
                return;
            }

            if (!hasStart)
            {
                hasStart = true;
                startNode = hoveredNode;
                status = "Bitiş düğümünü seçin.";
                RefreshPreview(hoveredNode);
                return;
            }

            if (TryBuild(startNode, hoveredNode, out var reason))
            {
                hasStart = false;
                previewPath.Clear();
                previewLine.positionCount = 0;
                status = "Duvar inşa edildi. Yeni bir başlangıç düğümü seçin.";
            }
            else
            {
                status = reason;
            }
        }

        void OnGUI()
        {
            const int width = 440;
            GUILayout.BeginArea(new Rect(16f, 16f, width, 145f), GUI.skin.box);
            GUILayout.Label("DUVAR İNŞASI");
            GUILayout.Label("Sol tık: başlangıç / bitiş   •   Sağ tık veya Esc: iptal");
            if (resourceStore != null)
                GUILayout.Label($"Merkez depo — Odun: {resourceStore.Wood}   Taş: {resourceStore.Stone}");
            else
                GUILayout.Label("Merkez depo bağlantısı yok (IWallResourceStore gerekli).");

            if (hasStart)
                GUILayout.Label($"Ön izleme — {previewPath.Count} parça, {previewWood} odun, {previewStone} taş");

            var oldColor = GUI.contentColor;
            GUI.contentColor = hasStart && !previewValid ? new Color(1f, 0.45f, 0.35f) : Color.white;
            GUILayout.Label(status);
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
            if (previewLine != null)
                previewLine.positionCount = 0;
            status = "İnşa iptal edildi. İlk düğümü seçin.";
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

        void RefreshPreview(Vector2Int? hoveredNode)
        {
            previewPath.Clear();
            if (!hoveredNode.HasValue)
            {
                previewLine.positionCount = 0;
                previewValid = false;
                status = "Harita sınırları içindeki bir düğüme gelin.";
                return;
            }

            previewPath.AddRange(WallConstructionModel.CreateSnappedPath(startNode, hoveredNode.Value));
            previewValid = Validate(previewPath, out var reason, out previewWood, out previewStone);
            status = previewValid ? "Geçerli: inşa etmek için sol tıklayın." : reason;
            DrawPreview();
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
