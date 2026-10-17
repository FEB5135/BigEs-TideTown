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
        static readonly Rect ConstructionPanelRect = new(16f, 16f, 470f, 390f);

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
        [SerializeField, Min(1)] int maxFloors = 3;

        [Header("Input mode")]
        [SerializeField] bool constructionModeActive = true;

        [Header("Generated wall geometry")]
        [SerializeField, Min(0.01f)] float wallHeight = 1.25f;
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
        bool hasStart;
        Vector2Int startNode;
        string status = "İlk düğümü seçin.";
        string confirmationStatus = "Henüz kalıcı inşa yapılmadı.";
        bool previewValid;
        int previewWood;
        int previewStone;
        bool buildInProgress;
        int selectedFloor;
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
        public WallTier SelectedTier => selectedTier;
        public bool ConstructionModeActive => constructionModeActive;
        public LayerMask ObstacleLayers
        {
            get => obstacleLayers;
            set => obstacleLayers = value;
        }
        public event Action<WallEdge, GameObject> WallBuilt;
        public event Action<WallRecord, GameObject> WallTierBuilt;

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
        }

        void OnValidate()
        {
            NormalizeConfiguration();
        }

        void NormalizeConfiguration()
        {
            woodCost = Mathf.Max(0, woodCost);
            stoneCost = Mathf.Max(0, stoneCost);
            stoneTierWoodCost = Mathf.Max(woodCost, stoneTierWoodCost);
            stoneTierStoneCost = Mathf.Max(stoneCost, stoneTierStoneCost);
            reinforcedWoodCost = Mathf.Max(stoneTierWoodCost, reinforcedWoodCost);
            reinforcedStoneCost = Mathf.Max(stoneTierStoneCost, reinforcedStoneCost);
            maxFloors = Mathf.Max(1, maxFloors);
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
                if (keyboard.pageUpKey.wasPressedThisFrame)
                    SetSelectedFloor(selectedFloor + 1);
                else if (keyboard.pageDownKey.wasPressedThisFrame)
                    SetSelectedFloor(selectedFloor - 1);
                else if (keyboard.digit1Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Timber);
                else if (keyboard.digit2Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Stone);
                else if (keyboard.digit3Key.wasPressedThisFrame)
                    SetSelectedTier(WallTier.Reinforced);
            }

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

            var pointerOverPanel = mouse != null && IsPointerOverConstructionUi(mouse.position.ReadValue());
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
            if (GUILayout.Button(constructionModeActive ? "Mod: İnşa (B ile toplamaya geç)" : "Mod: Toplama (B ile inşaata geç)"))
                SetConstructionMode(!constructionModeActive);
            if (!constructionModeActive)
            {
                GUILayout.Label("Kaynak toplamak için dünyadaki kaynaklara sol tıklayın.");
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("Sol tık: başlangıç / bitiş   •   Enter: ön izlemeyi inşa et");
            GUILayout.Label("Sağ tık/Esc: iptal   •   PageUp/PageDown: kat   •   1/2/3: seviye");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Kat -"))
                SetSelectedFloor(selectedFloor - 1);
            GUILayout.Label($"Kat {selectedFloor + 1}/{maxFloors} — dünya yüksekliği {FloorBaseHeight(selectedFloor):0.##}–{FloorBaseHeight(selectedFloor + 1):0.##} m");
            if (GUILayout.Button("Kat +"))
                SetSelectedFloor(selectedFloor + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawTierButton(WallTier.Timber, "1 Ahşap");
            DrawTierButton(WallTier.Stone, "2 Taş");
            DrawTierButton(WallTier.Reinforced, "3 Güçlendirilmiş");
            GUILayout.EndHorizontal();
            var selectedCost = CostFor(selectedTier);
            GUILayout.Label($"Seçim: {TierName(selectedTier)} — kümülatif kenar bedeli {selectedCost.Wood} odun / {selectedCost.Stone} taş");
            if (resourceStore != null)
                GUILayout.Label($"Merkez depo — Odun: {resourceStore.Wood}   Taş: {resourceStore.Stone}");
            else
                GUILayout.Label("Merkez depo bağlantısı yok (IWallResourceStore gerekli).");

            GUILayout.Label($"Kalıcı duvar — {model.Walls.Count} katlı parça");
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
            RefreshPreviewForSelectionChange();
        }

        public void SetConstructionMode(bool active)
        {
            if (constructionModeActive == active)
                return;
            constructionModeActive = active;
            CancelSelection();
            status = active ? "İnşa modu: ilk düğümü seçin." : "Toplama modu etkin.";
        }

        public void SetSelectedFloor(int floor)
        {
            var clamped = Mathf.Clamp(floor, 0, Mathf.Max(1, maxFloors) - 1);
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

            var path = WallConstructionModel.CreateSnappedPath(startNode, endpoint);
            if (!Validate(path, selectedFloor, selectedTier, out var reason, out var spentWood, out var spentStone))
            {
                status = reason;
                confirmationStatus = $"Başarısız: {reason}";
                previewValid = false;
                hasLastValidPreviewEndpoint = false;
                return false;
            }

            if (!TryBuild(startNode, endpoint, selectedFloor, selectedTier, out reason))
            {
                status = reason;
                confirmationStatus = $"Başarısız: {reason}";
                previewValid = false;
                hasLastValidPreviewEndpoint = false;
                return false;
            }

            var builtCount = path.Count;
            hasStart = false;
            hasPreviewEndpoint = false;
            hasLastValidPreviewEndpoint = false;
            previewValid = false;
            previewPath.Clear();
            previewLine.positionCount = 0;
            status = $"İnşa tamamlandı: Kat {selectedFloor + 1}, {TierName(selectedTier)}, {builtCount} parça; {spentWood} odun ve {spentStone} taş harcandı.";
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
            previewWood = 0;
            previewStone = 0;
            hasPreviewEndpoint = hoveredNode.HasValue;
            if (!hoveredNode.HasValue)
            {
                previewLine.positionCount = 0;
                previewValid = false;
                RevalidateCachedEndpoint();
                status = "Harita sınırları içindeki bir düğüme gelin.";
                return;
            }

            previewEndpoint = hoveredNode.Value;
            previewPath.AddRange(WallConstructionModel.CreateSnappedPath(startNode, hoveredNode.Value));
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

        void RevalidateCachedEndpoint()
        {
            if (!hasLastValidPreviewEndpoint)
                return;

            var cachedPath = WallConstructionModel.CreateSnappedPath(startNode, lastValidPreviewEndpoint);
            if (!Validate(
                    cachedPath,
                    selectedFloor,
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
            return ConstructionPanelRect.Contains(guiPosition);
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
            var previewHeight = FloorBaseHeight(selectedFloor) + wallHeight + 0.05f;
            previewLine.SetPosition(0, NodePosition(previewPath[0].A, previewHeight));
            for (var i = 0; i < previewPath.Count; i++)
                previewLine.SetPosition(i + 1, NodePosition(previewPath[i].B, previewHeight));
        }

        bool HasObstacleClearance(IReadOnlyList<WallEdge> path, int floor, out string reason)
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
                var start = NodePosition(edge.A, FloorBaseHeight(floor) + wallHeight * 0.5f);
                var end = NodePosition(edge.B, FloorBaseHeight(floor) + wallHeight * 0.5f);
                var direction = end - start;
                var rotation = Quaternion.LookRotation(direction.normalized, world.transform.up);
                var halfExtents = new Vector3(
                    wallThickness * 0.5f + obstaclePadding,
                    Mathf.Max(0.01f, wallHeight * 0.45f),
                    direction.magnitude * 0.5f + obstaclePadding);

                var overlaps = Physics.OverlapBox(
                    (start + end) * 0.5f,
                    halfExtents,
                    rotation,
                    obstacleLayers,
                    QueryTriggerInteraction.Collide);
                var ownWall = wallInstances.TryGetValue(new WallInstanceKey(edge, floor), out var instance)
                    ? instance
                    : null;
                var blocked = false;
                for (var overlapIndex = 0; overlapIndex < overlaps.Length; overlapIndex++)
                {
                    if (ownWall != null && overlaps[overlapIndex].transform.IsChildOf(ownWall.transform))
                        continue;
                    blocked = true;
                    break;
                }
                if (blocked)
                {
                    reason = "Duvar yolu korunan bir yapı veya engelle çakışıyor.";
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
                instance.GetComponent<Renderer>().sharedMaterial = MaterialFor(record.Tier);
            }

            instance.name = $"Wall F{record.Floor + 1} {record.Tier} {edge.A.x},{edge.A.y} - {edge.B.x},{edge.B.y}";
            instance.transform.localScale = new Vector3(wallThickness, wallHeight, direction.magnitude);
            if (instance.GetComponentInChildren<Collider>() == null)
                instance.AddComponent<BoxCollider>();
            var metadata = instance.GetComponent<WallSegment>() ?? instance.AddComponent<WallSegment>();
            metadata.Configure(record);

            // A 3D collider with no Rigidbody is a static physics collider.
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                DestroyObject(body);
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

        void DrawTierButton(WallTier tier, string label)
        {
            var oldColor = GUI.backgroundColor;
            if (selectedTier == tier)
                GUI.backgroundColor = new Color(0.45f, 0.9f, 0.5f);
            if (GUILayout.Button(label))
                SetSelectedTier(tier);
            GUI.backgroundColor = oldColor;
        }

        float FloorBaseHeight(int floor) => floorZeroBaseHeight + floor * wallHeight;

        WallCost CostFor(WallTier tier)
        {
            return tier switch
            {
                WallTier.Timber => new WallCost(woodCost, stoneCost),
                WallTier.Stone => new WallCost(stoneTierWoodCost, stoneTierStoneCost),
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
            if (timberWallMaterial != null)
                DestroyObject(timberWallMaterial);
            if (stoneWallMaterial != null)
                DestroyObject(stoneWallMaterial);
            if (reinforcedWallMaterial != null)
                DestroyObject(reinforcedWallMaterial);
        }
    }
}
