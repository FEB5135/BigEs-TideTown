using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Tsunami
{
    /// <summary>
    /// Mouse terrain sculpting: hold LMB to raise (build barriers), RMB to lower (dig channels).
    /// [ and ] change the brush radius. Clicks that start over UI never sculpt.
    /// Water depth is preserved under edits (see TerrainGrid.ModifyHeights), so no water is created.
    /// </summary>
    public class TerrainBrush : MonoBehaviour
    {
        [SerializeField] TerrainGrid terrain;
        [SerializeField] Camera viewCamera;

        [Header("Brush")]
        [SerializeField, Range(1f, 30f)] float radius = 7f;
        [SerializeField] Vector2 radiusLimits = new Vector2(2f, 25f);
        [Tooltip("Height change per second at the brush centre (m/s). Walls against mountain waves need ~20 m.")]
        [SerializeField, Range(0.5f, 40f)] float strength = 14f;
        [SerializeField] float maxRayDistance = 1000f;

        [Header("Cursor")]
        [Tooltip("Optional ring that follows the terrain under the mouse.")]
        [SerializeField] LineRenderer cursor;
        [SerializeField, Range(8, 64)] int cursorSegments = 40;
        [SerializeField] Color raiseColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] Color lowerColor = new Color(0.3f, 0.7f, 1f);

        Vector3[] cursorPoints;
        bool strokeAllowed;

        public float Radius => radius;

        void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (cursor != null)
            {
                cursorPoints = new Vector3[cursorSegments];
                cursor.positionCount = cursorSegments;
                cursor.loop = true;
                cursor.useWorldSpace = true;
                cursor.enabled = false;
            }
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (terrain == null || viewCamera == null || mouse == null) return;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftBracketKey.wasPressedThisFrame) radius = Mathf.Max(radiusLimits.x, radius / 1.2f);
                if (kb.rightBracketKey.wasPressedThisFrame) radius = Mathf.Min(radiusLimits.y, radius * 1.2f);
            }

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool raise = mouse.leftButton.isPressed;
            bool lower = mouse.rightButton.isPressed;

            // A stroke may only begin when the press starts outside the UI.
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) strokeAllowed = !overUI;
            if (!raise && !lower) strokeAllowed = false;

            if (overUI && !strokeAllowed)
            {
                if (cursor != null) cursor.enabled = false;
                return;
            }

            Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!terrain.Raycast(ray, maxRayDistance, out Vector3 hit))
            {
                if (cursor != null) cursor.enabled = false;
                return;
            }

            if (strokeAllowed && raise != lower)
                terrain.ModifyHeights(hit, radius, (raise ? strength : -strength) * Time.deltaTime);

            UpdateCursor(hit, lower && strokeAllowed);
        }

        void UpdateCursor(Vector3 center, bool lowering)
        {
            if (cursor == null) return;
            cursor.enabled = true;
            Color c = lowering ? lowerColor : raiseColor;
            cursor.startColor = c;
            cursor.endColor = c;
            float step = Mathf.PI * 2f / cursorPoints.Length;
            for (int i = 0; i < cursorPoints.Length; i++)
            {
                Vector3 p = center + new Vector3(Mathf.Cos(i * step) * radius, 0f, Mathf.Sin(i * step) * radius);
                p.y = terrain.SampleHeight(p) + 0.3f;
                cursorPoints[i] = p;
            }
            cursor.SetPositions(cursorPoints);
        }
    }
}
