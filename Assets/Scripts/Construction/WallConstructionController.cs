using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[assembly: InternalsVisibleTo("IdleTown.EditModeTests")]

namespace IdleTown.Construction
{
    public sealed class WallConstructionController : MonoBehaviour
    {
        internal static Rect PanelRect => WallConstructionHUD.VisibleRect;

        [Header("Scene references")]
        [SerializeField] World world;
        [Tooltip("Must implement IWallResourceStore.")]
        [SerializeField] MonoBehaviour resourceStoreSource;
        [SerializeField] Camera inputCamera;
        [SerializeField] GameObject wallPrefab;
        [SerializeField] WallTerrainSurface terrainSurface;

        [Header("Placement blocking")]
        [Tooltip("Additional trigger layers that block construction. Solid structures always block, even when this mask is empty.")]
        [SerializeField] LayerMask obstacleLayers;
        [SerializeField, Min(0f)] float obstaclePadding = 0.04f;

        [Header("Cumulative cost per edge")]
        [Tooltip("Legacy fields remain the Timber cumulative cost so existing scenes keep their configured price.")]
        [SerializeField, Min(0)] int woodCost = 1;
        [SerializeField, Min(0)] int stoneCost = 1;
        [SerializeField, Min(0)] int stoneTierWoodCost = 3;
        [SerializeField, Min(0)] int stoneTierStoneCost = 4;
        [SerializeField, Min(0)] int reinforcedWoodCost = 6;
        [SerializeField, Min(0)] int reinforcedStoneCost = 6;

        [Header("Floors")]
        [Tooltip("Local-space height of the bottom of floor 1 relative to World.")]
        [SerializeField] float floorZeroBaseHeight;
        [SerializeField, Min(1)] int maxFloors = 10;

        [Header("Input mode")]
        [SerializeField] bool constructionModeActive = true;

        [Header("Generated wall geometry")]
        public const float StoreyHeight = 2f;
        public const float MaximumWallHeight = 20f;
        [SerializeField, Min(0.01f)] float wallHeight = StoreyHeight;
        [SerializeField, Min(0.01f)] float wallThickness = 0.18f;

        readonly WallConstructionModel model = new();
        readonly List<WallEdge> previewPath = new();
        readonly Dictionary<WallInstanceKey, GameObject> wallInstances = new();
        IWallResourceStore resourceStore;
        LineRenderer previewLine;
        Material previewMaterial;
        Material timberWallMaterial;
        Material stoneWallMaterial;
        Material reinforcedWallMaterial;
        readonly WallArt art = new();
        readonly List<GameObject> previewGhosts = new();
        readonly HashSet<Vector2Int> previewNodes = new();
        readonly Dictionary<Vector2Int, float> previewNodeFill = new();
        readonly Dictionary<(Vector2Int node, int floor), GameObject> jointPosts = new();
        Material ghostMaterial;
        Collider[] overlapBuffer = new Collider[32];
        WallConstructionHUD hud;
        bool upArrowHeld, downArrowHeld;
        bool demolitionMode;
        bool demolitionKeyHeld;
        readonly List<LineRenderer> demolitionOutlines = new();
        Material demolitionOutlineMaterial;
        static readonly int[] OutlineCorners = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
        public bool DemolitionMode => demolitionMode;
        bool hasStart;
        Vector2Int startNode;
        string status = "İlk düğümü seçin.";
        string confirmationStatus = "Henüz kalıcı inşa yapılmadı.";
        bool previewValid;
        int previewWood;
        int previewStone;
        bool buildInProgress;
        int selectedFloor;
        int requestedFloor;
        bool manualFloorForSelection;
        bool automaticHeight = true;
        WallTier selectedTier;
        bool hasPreviewEndpoint;
        Vector2Int previewEndpoint;
        bool hasLastValidPreviewEndpoint;
        Vector2Int lastValidPreviewEndpoint;
        int lastValidPreviewCount;
        int lastValidPreviewWood;
        int lastValidPreviewStone;

        public IReadOnlyCollection<WallEdge> Edges => model.Edges;
        public IReadOnlyCollection<WallRecord> Walls => model.Walls;
        public int SelectedFloor => selectedFloor;
        public int MaxFloors => maxFloors;
        public bool AutomaticHeight => automaticHeight;
        public void SetAutomaticHeight(bool active)
        {
            automaticHeight = active;
            manualFloorForSelection = false;
            RefreshPreviewForSelectionChange();
        }
        public void ConfigureTerrain(WallTerrainSurface surface, int floors = 10)
        {
            terrainSurface = surface;
            maxFloors = Mathf.Max(1, floors);
            NormalizeConfiguration();
        }
        public WallTier SelectedTier => selectedTier;
        public bool ConstructionModeActive => constructionModeActive;
        internal string BuildStatus => status;
        internal string ConfirmationStatus => confirmationStatus;
        internal bool HasSelection => hasStart;
        internal bool CanConfirm => hasStart && hasLastValidPreviewEndpoint && (!hasPreviewEndpoint || previewValid);
        internal bool InvalidPreview => hasStart && !previewValid;
        internal string PreviewSummary => hasStart
            ? $"{previewPath.Count} parça · {CostText(previewWood, previewStone)}"
            : "Başlangıç ve bitiş noktalarını seç";
        internal string StockSummary => resourceStore != null
            ? $"{resourceStore.Wood} odun   /   {resourceStore.Stone} taş"
            : "Depo bağlı değil";
        public LayerMask ObstacleLayers
        {
            get => obstacleLayers;
            set => obstacleLayers = value;
        }
        public event Action<WallEdge, GameObject> WallBuilt;
        public event Action<WallRecord, GameObject> WallTierBuilt;
        public event Func<WallEdge, GameObject, bool> WallRemoved;
        public event Func<WallRecord, GameObject, bool> WallTierRemoved;

        public bool TryGetWallInstance(WallEdge edge, out GameObject instance) => TryGetWallInstance(edge, 0, out instance);
        public bool TryGetWallInstance(WallEdge edge, int floor, out GameObject instance) =>
            wallInstances.TryGetValue(new WallInstanceKey(edge, floor), out instance) && instance != null;

        readonly struct WallInstanceKey : IEquatable<WallInstanceKey>
        {
            public WallInstanceKey(WallEdge edge, int floor)
            {
                Edge = edge;
                Floor = floor;
            }

            public WallEdge Edge { get; }
            public int Floor { get; }
            public bool Equals(WallInstanceKey other) => Floor == other.Floor && Edge.Equals(other.Edge);
            public override bool Equals(object obj) => obj is WallInstanceKey other && Equals(other);
            public override int GetHashCode() => (Edge.GetHashCode() * 397) ^ Floor;
        }

        void Awake()
        {
            NormalizeConfiguration();
            ResolveReferences();
            CreatePreviewLine();
            if (Application.isPlaying)
            {
                hud = gameObject.AddComponent<WallConstructionHUD>();
                hud.Initialize(this);
            }
        }

        void OnValidate()
        {
            NormalizeConfiguration();
        }

        void NormalizeConfiguration()
        {
            woodCost = Mathf.Max(1, woodCost);
            stoneCost = 0;
            stoneTierWoodCost = 0;
            stoneTierStoneCost = Mathf.Max(1, stoneTierStoneCost);
            reinforcedWoodCost = Mathf.Max(woodCost, reinforcedWoodCost);
            reinforcedStoneCost = Mathf.Max(stoneTierStoneCost, reinforcedStoneCost);
            wallHeight = StoreyHeight;
            maxFloors = Mathf.Clamp(maxFloors, 1, Mathf.FloorToInt(MaximumWallHeight / StoreyHeight));
            selectedFloor = Mathf.Clamp(selectedFloor, 0, maxFloors - 1);
            wallHeight = Mathf.Max(0.01f, wallHeight);
            wallThickness = Mathf.Max(0.01f, wallThickness);
            obstaclePadding = Mathf.Max(0f, obstaclePadding);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
            {
                SetConstructionMode(!constructionModeActive);
                return;
            }

            if (!constructionModeActive)
                return;

            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Timber);
                else if (keyboard.digit2Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Stone);
                else if (keyboard.digit3Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Reinforced);
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (demolitionMode) SetDemolitionMode(false);
                CancelSelection();
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                CancelSelection();
                return;
            }

            var pointerOverPanel = mouse != null && IsPointerOverConstructionUi(mouse.position.ReadValue());
            if (demolitionMode)
            {
                RefreshDemolitionPreview(mouse != null && !pointerOverPanel
                    ? FindDemolitionTarget(mouse.position.ReadValue()) : null);
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && !pointerOverPanel)
                    TryDemolishAt(mouse.position.ReadValue());
                return;
            }
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
            // The panel is IMGUI and would draw over the loading curtain.
            if (SceneTransition.Active)
                return;
            // Arrows have one input route, latched until release to ignore key repeat.
            var input = Event.current;
            if ((input.type == EventType.KeyDown || input.type == EventType.KeyUp) && input.keyCode == KeyCode.N)
            {
                HandleDemolitionKey(input.type == EventType.KeyDown);
                input.Use();
            }
            if ((input.type == EventType.KeyDown || input.type == EventType.KeyUp) &&
                HandleFloorKey(input.keyCode, input.type == EventType.KeyDown))
                input.Use();
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
            RefreshPreviewForSelectionChange();
        }

        public void SetConstructionMode(bool active)
        {
            if (constructionModeActive == active)
                return;
            constructionModeActive = active;
            demolitionMode = false;
            CancelSelection();
            status = active ? "İnşa modu: ilk düğümü seçin." : "Toplama modu etkin.";
        }

        internal bool HandleFloorKey(KeyCode key, bool pressed)
        {
            if (key != KeyCode.UpArrow && key != KeyCode.DownArrow) return false;
            ref bool held = ref (key == KeyCode.UpArrow ? ref upArrowHeld : ref downArrowHeld);
            bool wasHeld = held;
            held = pressed;
            if (constructionModeActive && pressed && !wasHeld)
                SetSelectedFloor(selectedFloor + (key == KeyCode.UpArrow ? 1 : -1));
            return constructionModeActive;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                upArrowHeld = downArrowHeld = demolitionKeyHeld = false;
                ClearDemolitionPreview();
            }
        }

        internal void HandleDemolitionKey(bool pressed)
        {
            bool wasHeld = demolitionKeyHeld;
            demolitionKeyHeld = pressed;
            if (pressed && !wasHeld) SetDemolitionMode(!demolitionMode);
        }

        public void SetDemolitionMode(bool active)
        {
            CancelSelection();
            if (active) constructionModeActive = true;
            demolitionMode = active;
            status = active ? "Silmek için bir duvara tıkla." : "İlk düğümü seçin.";
        }

        internal bool TryDemolishAt(Vector2 screenPosition)
            => TryDemolishWall(FindDemolitionTarget(screenPosition));

        WallSegment FindDemolitionTarget(Vector2 screenPosition)
        {
            var camera = inputCamera != null ? inputCamera : Camera.main;
            if (camera == null) return null;
            Physics.SyncTransforms();
            if (!Physics.Raycast(camera.ScreenPointToRay(screenPosition), out var hit, Mathf.Infinity,
                ~0, QueryTriggerInteraction.Ignore)) return null;
            var segment = hit.collider.GetComponentInParent<WallSegment>();
            return IsOwnedWall(segment) ? segment : null;
        }

        bool IsOwnedWall(WallSegment segment) => segment != null &&
            TryGetWallInstance(segment.Edge, segment.Floor, out var owned) && owned == segment.gameObject;

        internal void RefreshDemolitionPreview(WallSegment target)
        {
            ClearDemolitionPreview();
            if (!constructionModeActive || !demolitionMode) return;
            if (!IsOwnedWall(target))
            {
                status = "Ön izleme için fareni bir duvarın üzerine getir.";
                return;
            }
            int count = 0;
            for (int floor = target.Floor; floor < maxFloors; floor++)
            {
                if (!TryGetWallInstance(target.Edge, floor, out var wall)) continue;
                var box = wall.GetComponent<BoxCollider>();
                if (box == null) continue;
                if (count == demolitionOutlines.Count)
                {
                    if (demolitionOutlineMaterial == null)
                        demolitionOutlineMaterial = WallArt.Material(new Color(1f, .82f, .04f), true);
                    var outline = new GameObject("Demolition Outline");
                    outline.transform.SetParent(transform, false);
                    var line = outline.AddComponent<LineRenderer>();
                    line.sharedMaterial = demolitionOutlineMaterial;
                    line.useWorldSpace = true;
                    line.widthMultiplier = .045f;
                    line.numCornerVertices = 3;
                    line.numCapVertices = 3;
                    line.startColor = line.endColor = new Color(1f, .82f, .04f);
                    demolitionOutlines.Add(line);
                }
                var renderer = demolitionOutlines[count++];
                renderer.gameObject.SetActive(true);
                renderer.positionCount = OutlineCorners.Length;
                // Include the shared end posts; local padding shrinks with the thin wall scale.
                var scale = box.transform.lossyScale;
                var padding = new Vector3(.14f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                    .04f / Mathf.Max(.001f, Mathf.Abs(scale.y)),
                    .14f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
                for (int i = 0; i < OutlineCorners.Length; i++)
                {
                    int corner = OutlineCorners[i];
                    var signs = new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    var local = box.center + Vector3.Scale(box.size * .5f + padding, signs);
                    var slope = wall.GetComponent<WallSlopeGeometry>();
                    if (slope != null) local.y += (corner & 2) == 0 ? slope.BaseHeightOffset(local.z) : slope.HeightOffset(local.z);
                    renderer.SetPosition(i, box.transform.TransformPoint(local));
                }
            }
            status = $"{count} duvar parçası seçili · Sol tık: sil · N / Esc: çık";
        }

        void ClearDemolitionPreview()
        {
            foreach (var outline in demolitionOutlines)
                if (outline != null) outline.gameObject.SetActive(false);
        }

        void OnDisable() => ClearDemolitionPreview();

        internal bool TryDemolishWall(WallSegment segment)
        {
            if (!constructionModeActive || !demolitionMode || segment == null ||
                !TryGetWallInstance(segment.Edge, segment.Floor, out var owned) || owned != segment.gameObject)
            {
                status = "Bu atölyede inşa edilmiş bir duvara tıkla.";
                return false;
            }
            int before = model.Walls.Count;
            if (!TryDetachDestroyedWall(segment.Edge, segment.Floor, out _)) return false;
            ClearDemolitionPreview();
            status = $"{before - model.Walls.Count} duvar parçası kaldırıldı.";
            confirmationStatus = status;
            return true;
        }

        public void SetSelectedFloor(int floor)
        {
            var clamped = Mathf.Clamp(floor, 0, Mathf.Max(1, maxFloors) - 1);
            requestedFloor = clamped;
            manualFloorForSelection = hasStart;
            if (selectedFloor == clamped)
                return;
            selectedFloor = clamped;
            RefreshPreviewForSelectionChange();
        }

        public void SetSelectedTier(WallTier tier)
        {
            if (!WallTierRules.IsTier(tier) || selectedTier == tier)
                return;
            selectedTier = tier;
            RefreshPreviewForSelectionChange();
        }

        void RefreshPreviewForSelectionChange()
        {
            // A cached endpoint belongs to the floor/tier under which it was validated.
            hasLastValidPreviewEndpoint = false;
            if (!hasStart)
                return;

            RefreshPreview(hasPreviewEndpoint ? previewEndpoint : (Vector2Int?)null);
        }

        public bool TryBuild(Vector2Int start, Vector2Int end, out string reason)
        {
            // Backward-compatible API always means a floor-zero Timber wall.
            return TryBuild(start, end, 0, WallTier.Timber, out reason);
        }

        public bool TryBuild(Vector2Int start, Vector2Int end, int floor, WallTier tier, out string reason)
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
            if (!Validate(path, floor, tier, out reason, out var wood, out var stone))
                return false;

            buildInProgress = true;
            GameObject stagingRoot = null;
            var stagedWalls = new List<GameObject>(path.Count);
            var replacedWalls = new List<GameObject>(path.Count);
            try
            {
                stagingRoot = new GameObject("Staged Wall Build");
                stagingRoot.transform.SetParent(transform, false);
                stagingRoot.SetActive(false);

                try
                {
                    for (var i = 0; i < path.Count; i++)
                    {
                        CreateWall(new WallRecord(path[i], floor, tier), stagingRoot.transform, stagedWalls);
                        wallInstances.TryGetValue(new WallInstanceKey(path[i], floor), out var replaced);
                        replacedWalls.Add(replaced);
                    }
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
                model.Set(path, floor, tier);
                stagingRoot.name = "Wall Build";
                // Instantiate preserves the prefab root's activeSelf value. A prefab kept inactive as
                // a scene template would otherwise remain invisible after its staging parent commits.
                for (var i = 0; i < stagedWalls.Count; i++)
                {
                    stagedWalls[i].SetActive(true);
                    wallInstances[new WallInstanceKey(path[i], floor)] = stagedWalls[i];
                    if (replacedWalls[i] != null)
                    {
                        replacedWalls[i].SetActive(false);
                        DestroyObject(replacedWalls[i]);
                    }
                }
                stagingRoot.SetActive(true);

                reason = string.Empty;
                NotifyWallBuilt(path, floor, tier, stagedWalls);
                RebuildJointPosts();
                if (Application.isPlaying)
                    foreach (var built in stagedWalls)
                    {
                        var visual = built.transform.Find("Wall Art");
                        if (visual != null) built.AddComponent<WallBuildReveal>().Begin(visual);
                    }
                return true;
            }
            finally
            {
                buildInProgress = false;
            }
        }

        /// <summary>
        /// Removes a destroyed edge from the model. A WallRemoved subscriber may claim the detached visual for
        /// debris; otherwise it is destroyed and the output is null. Rebuild uses normal cost; no resources refund.
        /// </summary>
        public bool TryDetachDestroyedWall(WallEdge edge, out GameObject instance)
            => TryDetachDestroyedWall(edge, 0, out instance);

        public bool TryDetachDestroyedWall(WallEdge edge, int floor, out GameObject instance)
        {
            instance = null;
            if (buildInProgress || !model.TryGet(edge, floor, out _)) return false;
            // Unsupported upper floors fall too. Detach top-down so event observers never see floating supports.
            for (int upper = maxFloors - 1; upper > floor; upper--)
                DetachSingle(edge, upper, out _);
            var removed = DetachSingle(edge, floor, out instance);
            if (removed) RebuildJointPosts();
            return removed;
        }

        bool DetachSingle(WallEdge edge, int floor, out GameObject instance)
        {
            instance = null;
            var key = new WallInstanceKey(edge, floor);
            if (!model.TryGet(edge, floor, out var record) || !wallInstances.TryGetValue(key, out instance)) return false;
            if (!model.Remove(edge, floor)) return false;
            wallInstances.Remove(key);

            Transform oldParent = instance != null ? instance.transform.parent : null;
            if (instance != null)
            {
                // The standing barrier is gone immediately, even if a subscriber keeps its art as debris.
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                instance.transform.SetParent(null, true);
            }
            bool hasRemovalOwner = NotifyWallTierRemoved(record, instance);
            if (floor == 0) hasRemovalOwner |= NotifyWallRemoved(edge, instance);
            if (!hasRemovalOwner && instance != null)
            {
                DestroyObject(instance);
                instance = null;
            }
            if (oldParent != null && oldParent.childCount == 0) DestroyObject(oldParent.gameObject);
            return true;
        }

        internal void HandleNodeClick(Vector2Int node)
        {
            if (!hasStart)
            {
                manualFloorForSelection = false;
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

            var path = WallConstructionModel.CreateSnappedPath(startNode, endpoint);
            selectedFloor = InteractiveFloor(path);
            if (!Validate(path, selectedFloor, selectedTier, out var reason, out var spentWood, out var spentStone))
            {
                status = reason;
                confirmationStatus = $"Başarısız: {reason}";
                previewValid = false;
                hasLastValidPreviewEndpoint = false;
                RefreshPreview(endpoint);
                return false;
            }

            if (!TryBuild(startNode, endpoint, selectedFloor, selectedTier, out reason))
            {
                status = reason;
                confirmationStatus = $"Başarısız: {reason}";
                previewValid = false;
                hasLastValidPreviewEndpoint = false;
                RefreshPreview(endpoint);
                return false;
            }

            var builtCount = path.Count;
            hasStart = false;
            hasPreviewEndpoint = false;
            hasLastValidPreviewEndpoint = false;
            previewValid = false;
            previewPath.Clear();
            previewLine.positionCount = 0;
            HideGhosts();
            status = $"İnşa tamamlandı: Kat {selectedFloor + 1}, {TierName(selectedTier)}, {builtCount} parça; {CostText(spentWood, spentStone)} harcandı.";
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
            if (terrainSurface == null && world != null && world.TryGetComponent<MapView>(out var map))
            {
                terrainSurface = world.GetComponent<WallTerrainSurface>();
                if (terrainSurface == null) terrainSurface = world.gameObject.AddComponent<WallTerrainSurface>();
                terrainSurface.ConfigureMap(map);
            }
        }

        void CancelSelection()
        {
            manualFloorForSelection = false;
            ClearDemolitionPreview();
            hasStart = false;
            previewPath.Clear();
            previewValid = false;
            hasPreviewEndpoint = false;
            hasLastValidPreviewEndpoint = false;
            if (previewLine != null)
                previewLine.positionCount = 0;
            HideGhosts();
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
            Vector3 point;
            if (terrainSurface != null)
            {
                if (!terrainSurface.Raycast(ray, out var hit)) return false;
                point = hit.point;
            }
            else
            {
                if (!plane.Raycast(ray, out var distance)) return false;
                point = ray.GetPoint(distance);
            }
            var local = world.transform.InverseTransformPoint(point);
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
            previewWood = 0;
            previewStone = 0;
            hasPreviewEndpoint = hoveredNode.HasValue;
            if (!hoveredNode.HasValue)
            {
                previewLine.positionCount = 0;
                HideGhosts();
                previewValid = false;
                RevalidateCachedEndpoint();
                status = "Harita sınırları içindeki bir düğüme gelin.";
                return;
            }

            previewEndpoint = hoveredNode.Value;
            previewPath.AddRange(WallConstructionModel.CreateSnappedPath(startNode, hoveredNode.Value));
            selectedFloor = InteractiveFloor(previewPath);
            previewValid = Validate(
                previewPath,
                selectedFloor,
                selectedTier,
                out var reason,
                out previewWood,
                out previewStone);
            if (previewValid)
            {
                hasLastValidPreviewEndpoint = true;
                lastValidPreviewEndpoint = hoveredNode.Value;
                lastValidPreviewCount = previewPath.Count;
                lastValidPreviewWood = previewWood;
                lastValidPreviewStone = previewStone;
            }
            else
            {
                RevalidateCachedEndpoint();
            }
            status = previewValid
                ? "Geçerli ön izleme: sol tık, Enter veya İnşa Et ile onaylayın."
                : reason;
            DrawPreview();
        }

        int InteractiveFloor(IReadOnlyList<WallEdge> path)
        {
            int floor = Mathf.Clamp(requestedFloor, 0, maxFloors - 1);
            if (!automaticHeight || manualFloorForSelection || path.Count == 0) return floor;
            // Skip a complete occupied layer, but preserve material upgrades and reject unsupported mixed paths.
            while (floor < maxFloors - 1)
            {
                foreach (var edge in path)
                    if (!model.TryGet(edge, floor, out var wall) || wall.Tier < selectedTier) return floor;
                floor++;
            }
            return floor;
        }

        void RevalidateCachedEndpoint()
        {
            if (!hasLastValidPreviewEndpoint)
                return;

            var cachedPath = WallConstructionModel.CreateSnappedPath(startNode, lastValidPreviewEndpoint);
            if (!Validate(
                    cachedPath,
                    InteractiveFloor(cachedPath),
                    selectedTier,
                    out _,
                    out lastValidPreviewWood,
                    out lastValidPreviewStone))
            {
                hasLastValidPreviewEndpoint = false;
                lastValidPreviewCount = 0;
                lastValidPreviewWood = 0;
                lastValidPreviewStone = 0;
                return;
            }

            lastValidPreviewCount = cachedPath.Count;
        }

        internal static bool IsPointerOverConstructionPanel(Vector2 screenPosition)
        {
            // Input System screen coordinates start at bottom-left; IMGUI starts at top-left.
            var guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return PanelRect.Contains(guiPosition);
        }

        public static bool IsPointerOverConstructionUi(Vector2 screenPosition)
        {
            return IsPointerOverConstructionPanel(screenPosition) ||
                   (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }

        bool Validate(
            IReadOnlyList<WallEdge> path,
            int floor,
            WallTier tier,
            out string reason,
            out int wood,
            out int stone)
        {
            wood = 0;
            stone = 0;
            if (!model.Validate(path, world.Width, world.Height, floor, tier, maxFloors, out reason))
                return false;

            if (terrainSurface != null)
                foreach (var edge in path)
                    if (!terrainSurface.Sample(NodePosition(edge.A, 0), out _) ||
                        !terrainSurface.Sample(NodePosition(edge.B, 0), out _))
                    { reason = "Duvarın iki ucu da arazi üzerinde olmalı."; return false; }

            if (!HasObstacleClearance(path, floor, out reason))
                return false;

            var timber = CostFor(WallTier.Timber);
            var stoneTier = CostFor(WallTier.Stone);
            var reinforced = CostFor(WallTier.Reinforced);
            long totalWood = 0;
            long totalStone = 0;
            for (var i = 0; i < path.Count; i++)
            {
                WallTier? existingTier = model.TryGet(path[i], floor, out var existing)
                    ? existing.Tier
                    : null;
                if (!WallTierRules.TryGetBuildCost(
                        existingTier,
                        tier,
                        timber,
                        stoneTier,
                        reinforced,
                        out var edgeCost))
                {
                    reason = "Duvar maliyetleri geçersiz veya istenen değişiklik bir yükseltme değil.";
                    return false;
                }
                totalWood += edgeCost.Wood;
                totalStone += edgeCost.Stone;
            }
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

            bool canAfford;
            try
            {
                canAfford = resourceStore.CanAfford(wood, stone);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                reason = "Kaynak deposu maliyeti kontrol edemedi.";
                return false;
            }

            if (!canAfford)
            {
                reason = $"Yetersiz kaynak: {CostText(wood, stone)} gerekiyor.";
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
            previewLine.positionCount = 0;
            previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewLine.receiveShadows = false;
            previewLine.widthMultiplier = wallThickness * 1.4f;
            // URP/Lit ignores LineRenderer vertex colors. Set the actual unlit material color, too.
            previewMaterial = WallArt.Material(Color.white, true);
            ghostMaterial = WallArt.Material(Color.green, true);
            ghostMaterial.SetFloat("_Surface", 1);
            ghostMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ghostMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ghostMaterial.SetFloat("_ZWrite", 0);
            ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            previewLine.sharedMaterial = previewMaterial;
        }

        void DrawPreview()
        {
            if (previewPath.Count == 0)
            {
                previewLine.positionCount = 0;
                HideGhosts();
                return;
            }

            var color = previewValid
                ? new Color(0.2f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.18f, 0.12f, 0.9f);
            previewLine.startColor = color;
            previewLine.endColor = color;
            previewMaterial.color = color;
            var ghostColor = new Color(color.r, color.g, color.b, .38f);
            ghostMaterial.color = ghostColor;
            if (previewMaterial.HasProperty("_BaseColor")) previewMaterial.SetColor("_BaseColor", color);
            if (ghostMaterial.HasProperty("_BaseColor")) ghostMaterial.SetColor("_BaseColor", ghostColor);
            previewLine.widthMultiplier = wallThickness * .16f;
            var previewHeight = FloorBaseHeight(selectedFloor) + wallHeight + 0.05f;
            var capPoints = new List<Vector3>();
            foreach (var edge in previewPath)
            {
                var profile = TerrainProfile(edge, out _);
                var a = NodePosition(edge.A, previewHeight) + world.transform.up * EndpointFill(edge, profile, false);
                var b = NodePosition(edge.B, previewHeight) + world.transform.up * EndpointFill(edge, profile, true);
                if (capPoints.Count == 0 || (capPoints[capPoints.Count - 1] - a).sqrMagnitude > .0001f) capPoints.Add(a);
                capPoints.Add(b);
            }
            previewLine.positionCount = capPoints.Count;
            previewLine.SetPositions(capPoints.ToArray());
            DrawGhosts();
        }

        bool HasObstacleClearance(IReadOnlyList<WallEdge> path, int floor, out string reason)
        {
            Physics.SyncTransforms();

            for (var i = 0; i < path.Count; i++)
            {
                var edge = path[i];
                var start = NodePosition(edge.A, FloorBaseHeight(floor) + wallHeight * 0.5f);
                var end = NodePosition(edge.B, FloorBaseHeight(floor) + wallHeight * 0.5f);
                var direction = end - start;
                var rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, world.transform.up).normalized, world.transform.up);
                var halfExtents = new Vector3(
                    JointHalfWidth + obstaclePadding,
                    Mathf.Max(0.001f, wallHeight * .5f - .015f) + Mathf.Abs(Vector3.Dot(direction, world.transform.up)) * .5f,
                    Vector3.ProjectOnPlane(direction, world.transform.up).magnitude * .5f + JointHalfWidth + obstaclePadding);

                int count;
                // Grow rather than silently miss a blocker in a dense scene.
                while ((count = Physics.OverlapBoxNonAlloc((start + end) * .5f, halfExtents,
                           overlapBuffer, rotation, ~0, QueryTriggerInteraction.Collide)) == overlapBuffer.Length)
                    Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
                for (var overlapIndex = 0; overlapIndex < count; overlapIndex++)
                {
                    var overlap = overlapBuffer[overlapIndex];
                    var resource = overlap.GetComponentInParent<ResourceNode>();
                    if (resource != null && resource.Type == ResourceType.Wood) continue;
                    if (terrainSurface != null && terrainSurface.Contains(overlap)) continue;
                    var map = overlap.GetComponentInParent<MapView>();
                    if (map != null && map.OwnsSurface(overlap) &&
                        map.IsConstructionGroundPath(start, end, JointHalfWidth + obstaclePadding)) continue;
                    if (map == null && terrainSurface != null && terrainSurface.Contains(overlap)) continue;
                    // Picking/collection triggers are not solid buildings. Explicit trigger layers opt in.
                    if (overlap.isTrigger && (obstacleLayers.value & (1 << overlap.gameObject.layer)) == 0)
                        continue;
                    var segment = overlap.GetComponentInParent<WallSegment>();
                    if (segment != null && wallInstances.TryGetValue(new WallInstanceKey(segment.Edge, segment.Floor), out var owned)
                        && owned != null && overlap.transform.IsChildOf(owned.transform))
                    {
                        // The model already rejects duplicates and diagonal crossings. Adjacent panels and
                        // the exact old panel in a tier upgrade may touch; unrelated walls may not overlap.
                        if (segment.Floor != floor || segment.Edge.Equals(edge) || ShareNode(segment.Edge, edge))
                            continue;
                    }
                    reason = $"Duvar yolu bir yapı veya engelle çakışıyor: {overlap.gameObject.name}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        void CreateWall(WallRecord record, Transform stagingParent, ICollection<GameObject> stagedWalls)
        {
            var edge = record.Edge;
            var elevation = FloorBaseHeight(record.Floor) + wallHeight * 0.5f;
            var start = NodePosition(edge.A, elevation);
            var end = NodePosition(edge.B, elevation);
            var direction = end - start;
            var horizontal = Vector3.ProjectOnPlane(direction, world.transform.up);
            var rotation = Quaternion.LookRotation(horizontal.normalized, world.transform.up);
            GameObject instance;

            if (wallPrefab != null)
            {
                instance = Instantiate(wallPrefab, (start + end) * 0.5f, rotation, stagingParent);
                stagedWalls.Add(instance);
            }
            else
            {
                instance = new GameObject("Wall");
                stagedWalls.Add(instance);
                instance.transform.SetParent(stagingParent, true);
                instance.transform.SetPositionAndRotation((start + end) * 0.5f, rotation);
                var visual = new GameObject("Wall Art", typeof(MeshFilter), typeof(MeshRenderer));
                visual.transform.SetParent(instance.transform, false);
                visual.GetComponent<MeshFilter>().sharedMesh = art.Panel(record.Tier, PanelInset(edge, horizontal.magnitude));
                visual.GetComponent<MeshRenderer>().sharedMaterials = art.Palette(record.Tier);
            }

            instance.name = $"Wall F{record.Floor + 1} {record.Tier} {edge.A.x},{edge.A.y} - {edge.B.x},{edge.B.y}";
            instance.transform.localScale = new Vector3(wallThickness, wallHeight, horizontal.magnitude);
            if (instance.GetComponentInChildren<Collider>() == null)
                instance.AddComponent<BoxCollider>();
            if (terrainSurface != null && wallPrefab == null)
            {
                var profile = TerrainProfile(edge, out var ground);
                instance.AddComponent<WallSlopeGeometry>().Configure(instance.GetComponentInChildren<MeshFilter>(),
                    instance.GetComponentInChildren<MeshFilter>().sharedMesh,
                    Vector3.Dot(direction, world.transform.up) / wallHeight, true, profile, record.Floor == 0 ? ground : null);
            }
            var metadata = instance.GetComponent<WallSegment>() ?? instance.AddComponent<WallSegment>();
            metadata.Configure(record);

            // A 3D collider with no Rigidbody is a static physics collider.
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                DestroyObject(body);
            }
        }

        float JointHalfWidth => wallThickness * .65f;

        float PanelInset(WallEdge edge, float length)
        {
            // A diagonal panel clears the corner of the one shared square post.
            float inset = edge.IsDiagonal ? JointHalfWidth * Mathf.Sqrt(2f) + wallThickness * .5f : JointHalfWidth;
            return Mathf.Min(.42f, inset / Mathf.Max(.01f, length));
        }

        static bool ShareNode(WallEdge a, WallEdge b) => a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B;

        void HideGhosts()
        {
            foreach (var ghost in previewGhosts) if (ghost != null) ghost.SetActive(false);
        }

        void DrawGhosts()
        {
            int used = 0;
            previewNodes.Clear();
            previewNodeFill.Clear();
            for (int i = 0; i < previewPath.Count; i++)
            {
                var ghost = GetGhost(used++);
                var edge = previewPath[i];
                previewNodes.Add(edge.A); previewNodes.Add(edge.B);
                var a = NodePosition(edge.A, FloorBaseHeight(selectedFloor) + wallHeight * .5f);
                var b = NodePosition(edge.B, FloorBaseHeight(selectedFloor) + wallHeight * .5f);
                var horizontal = Vector3.ProjectOnPlane(b - a, world.transform.up);
                ghost.SetActive(true);
                ghost.GetComponent<WallPreviewMotion>().SetTarget((a + b) * .5f,
                    Quaternion.LookRotation(horizontal.normalized, world.transform.up),
                    new Vector3(wallThickness * 1.035f, wallHeight * 1.015f, horizontal.magnitude));
                var filter = ghost.GetComponent<MeshFilter>();
                var panel = art.Panel(selectedTier, PanelInset(edge, horizontal.magnitude));
                if (terrainSurface != null)
                {
                    var slope = ghost.GetComponent<WallSlopeGeometry>();
                    if (slope == null) slope = ghost.AddComponent<WallSlopeGeometry>();
                    var profile = TerrainProfile(edge, out var ground);
                    float fillA = EndpointFill(edge, profile, false), fillB = EndpointFill(edge, profile, true);
                    previewNodeFill.TryGetValue(edge.A, out var oldA); previewNodeFill.TryGetValue(edge.B, out var oldB);
                    previewNodeFill[edge.A] = Mathf.Max(oldA, fillA); previewNodeFill[edge.B] = Mathf.Max(oldB, fillB);
                    slope.Configure(filter, panel, Vector3.Dot(b - a, world.transform.up) / wallHeight, false,
                        profile, selectedFloor == 0 ? ground : null);
                }
                else filter.sharedMesh = panel;
            }
            foreach (var node in previewNodes)
            {
                var ghost = GetGhost(used++);
                ghost.SetActive(true);
                ghost.GetComponent<MeshFilter>().sharedMesh = art.Post;
                previewNodeFill.TryGetValue(node, out var fill);
                float postHeight = wallHeight + (selectedFloor == 0 ? fill : 0);
                float postOffset = selectedFloor == 0 ? fill * .5f : fill;
                ghost.GetComponent<WallPreviewMotion>().SetTarget(NodePosition(node, FloorBaseHeight(selectedFloor) + wallHeight * .5f) + world.transform.up * postOffset,
                    world.transform.rotation, new Vector3(JointHalfWidth * 2.035f, postHeight * 1.015f, JointHalfWidth * 2.035f));
            }
            for (int i = used; i < previewGhosts.Count; i++) previewGhosts[i].SetActive(false);
        }

        GameObject GetGhost(int index)
        {
            if (index < previewGhosts.Count) return previewGhosts[index];
            var created = new GameObject("Wall Ghost", typeof(MeshFilter), typeof(MeshRenderer), typeof(WallPreviewMotion));
            created.transform.SetParent(previewLine.transform, false);
            var renderer = created.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterials = new[] { ghostMaterial, ghostMaterial, ghostMaterial };
            previewGhosts.Add(created);
            return created;
        }

        void RebuildJointPosts()
        {
            // Exactly one post per node and floor, even where several walls or separate builds meet.
            var wanted = new Dictionary<(Vector2Int node, int floor), WallTier>();
            var fills = new Dictionary<(Vector2Int node, int floor), float>();
            foreach (var record in model.Walls)
            {
                var profile = TerrainProfile(record.Edge, out _);
                Include(record.Edge.A, record.Floor, record.Tier, EndpointFill(record.Edge, profile, false));
                Include(record.Edge.B, record.Floor, record.Tier, EndpointFill(record.Edge, profile, true));
            }
            void Include(Vector2Int node, int floor, WallTier tier, float fill)
            {
                var key = (node, floor);
                if (!wanted.TryGetValue(key, out var old) || tier > old) wanted[key] = tier;
                fills.TryGetValue(key, out float previous); fills[key] = Mathf.Max(previous, fill);
            }
            var removed = new List<(Vector2Int, int)>();
            foreach (var pair in jointPosts)
                if (!wanted.ContainsKey(pair.Key))
                {
                    if (pair.Value != null) { pair.Value.SetActive(false); DestroyObject(pair.Value); }
                    removed.Add(pair.Key);
                }
            foreach (var key in removed) jointPosts.Remove(key);
            if (wallPrefab != null) return;
            foreach (var pair in wanted)
            {
                if (!jointPosts.TryGetValue(pair.Key, out var post) || post == null)
                {
                    post = new GameObject("Wall Joint", typeof(MeshFilter), typeof(MeshRenderer));
                    post.transform.SetParent(transform, false);
                    post.GetComponent<MeshFilter>().sharedMesh = art.Post;
                    jointPosts[pair.Key] = post;
                }
                float fill = fills[pair.Key];
                float postHeight = wallHeight + (pair.Key.floor == 0 ? fill : 0);
                float postOffset = pair.Key.floor == 0 ? fill * .5f : fill;
                post.transform.SetPositionAndRotation(NodePosition(pair.Key.node,
                        FloorBaseHeight(pair.Key.floor) + wallHeight * .5f) + world.transform.up * postOffset, world.transform.rotation);
                post.transform.localScale = new Vector3(JointHalfWidth * 2, postHeight, JointHalfWidth * 2);
                post.GetComponent<MeshRenderer>().sharedMaterials = art.Palette(pair.Value);
            }
        }

        void NotifyWallBuilt(
            IReadOnlyList<WallEdge> path,
            int floor,
            WallTier tier,
            IReadOnlyList<GameObject> instances)
        {
            for (var edgeIndex = 0; edgeIndex < path.Count; edgeIndex++)
            {
                NotifySubscribers(WallBuilt, path[edgeIndex], instances[edgeIndex]);
                NotifySubscribers(WallTierBuilt, new WallRecord(path[edgeIndex], floor, tier), instances[edgeIndex]);
            }
        }

        void NotifySubscribers<T>(Action<T, GameObject> notification, T value, GameObject instance)
        {
            var subscribers = notification?.GetInvocationList();
            if (subscribers == null)
                return;

            for (var subscriberIndex = 0; subscriberIndex < subscribers.Length; subscriberIndex++)
            {
                try
                {
                    ((Action<T, GameObject>)subscribers[subscriberIndex])(value, instance);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        float FloorBaseHeight(int floor) => floorZeroBaseHeight + floor * wallHeight;

        static string CostText(int wood, int stone) => wood == 0 ? $"{stone} taş" : stone == 0 ? $"{wood} odun" : $"{wood} odun · {stone} taş";

        internal WallCost CostFor(WallTier tier)
        {
            return tier switch
            {
                WallTier.Timber => new WallCost(woodCost, 0),
                WallTier.Stone => new WallCost(0, stoneTierStoneCost),
                WallTier.Reinforced => new WallCost(reinforcedWoodCost, reinforcedStoneCost),
                _ => default
            };
        }

        static string TierName(WallTier tier)
        {
            return tier switch
            {
                WallTier.Timber => "Ahşap",
                WallTier.Stone => "Taş",
                WallTier.Reinforced => "Güçlendirilmiş",
                _ => "Geçersiz"
            };
        }

        Material MaterialFor(WallTier tier)
        {
            switch (tier)
            {
                case WallTier.Timber:
                    return timberWallMaterial ??= CreateColorMaterial(new Color(0.48f, 0.29f, 0.14f, 1f));
                case WallTier.Stone:
                    return stoneWallMaterial ??= CreateColorMaterial(new Color(0.42f, 0.45f, 0.5f, 1f));
                case WallTier.Reinforced:
                    return reinforcedWallMaterial ??= CreateColorMaterial(new Color(0.24f, 0.3f, 0.38f, 1f));
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown wall tier.");
            }
        }

        bool NotifyWallRemoved(WallEdge edge, GameObject instance)
        {
            var subscribers = WallRemoved?.GetInvocationList();
            if (subscribers == null || subscribers.Length == 0) return false;
            bool claimed = false;
            for (var i = 0; i < subscribers.Length; i++)
            {
                try { claimed |= ((Func<WallEdge, GameObject, bool>)subscribers[i])(edge, instance); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            return claimed;
        }

        bool NotifyWallTierRemoved(WallRecord record, GameObject instance)
        {
            bool claimed = false;
            var subscribers = WallTierRemoved?.GetInvocationList();
            if (subscribers == null) return false;
            foreach (var subscriber in subscribers)
            {
                try { claimed |= ((Func<WallRecord, GameObject, bool>)subscriber)(record, instance); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
            return claimed;
        }

        Vector3 NodePosition(Vector2Int node, float y)
        {
            var ground = world.transform.TransformPoint(new Vector3(node.x * world.CellSize, 0, node.y * world.CellSize));
            if (terrainSurface != null && terrainSurface.Sample(ground, out var sampled)) ground = sampled;
            return ground + world.transform.TransformVector(new Vector3(0, y, 0));
        }

        float[] TerrainProfile(WallEdge edge, out float[] groundProfile)
        {
            groundProfile = null;
            if (terrainSurface == null) return null;
            var a = NodePosition(edge.A, 0);
            var b = NodePosition(edge.B, 0);
            var center = (a + b) * .5f;
            const int sections = 8;
            var profile = new float[sections + 1];
            bool curved = false;
            for (int i = 0; i <= sections; i++)
            {
                var line = Vector3.Lerp(a, b, i / (float)sections);
                if (!terrainSurface.Sample(line, out var sample)) sample = line;
                profile[i] = Vector3.Dot(sample - center, world.transform.up) / wallHeight;
                curved |= Mathf.Abs(Vector3.Dot(sample - line, world.transform.up)) > .002f;
            }
            var filled = WallSlopeGeometry.FillCliffProfile(profile);
            if (!ReferenceEquals(filled, profile)) { groundProfile = profile; return filled; }
            return curved ? profile : null;
        }

        float EndpointFill(WallEdge edge, float[] profile, bool end)
        {
            if (profile == null) return 0;
            float difference = Vector3.Dot(NodePosition(edge.B, 0) - NodePosition(edge.A, 0), world.transform.up);
            return Mathf.Max(0, profile[end ? profile.Length - 1 : 0] * wallHeight + (end ? -.5f : .5f) * difference);
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
            if (demolitionOutlineMaterial != null) DestroyObject(demolitionOutlineMaterial);
            art.Dispose();
            if (ghostMaterial != null) DestroyObject(ghostMaterial);
            if (previewMaterial != null)
                DestroyObject(previewMaterial);
            if (timberWallMaterial != null)
                DestroyObject(timberWallMaterial);
            if (stoneWallMaterial != null)
                DestroyObject(stoneWallMaterial);
            if (reinforcedWallMaterial != null)
                DestroyObject(reinforcedWallMaterial);
        }
    }
}
