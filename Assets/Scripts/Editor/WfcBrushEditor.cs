using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(WfcSolver.Fit))]
public class WfcFitDrawer : PropertyDrawer
{
    static readonly int[] Rotations = { -1, 0, 1, 2, 3 };
    static readonly string[] RotationLabels = { "Any", "0", "1", "2", "3" };

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var id = property.FindPropertyRelative("Id");
        var rotation = property.FindPropertyRelative("Rotation");
        var gap = 4f;
        var idWidth = position.width * 0.62f;
        var idRect = new Rect(position.x, position.y, idWidth, position.height);
        var rotRect = new Rect(position.x + idWidth + gap, position.y, position.width - idWidth - gap, position.height);
        EditorGUI.PropertyField(idRect, id, new GUIContent("Id"));
        var selected = 0;
        for (var i = 0; i < Rotations.Length; i++)
        {
            if (Rotations[i] == rotation.intValue)
                selected = i;
        }

        rotation.intValue = Rotations[EditorGUI.Popup(rotRect, selected, RotationLabels)];
        EditorGUI.EndProperty();
    }
}

[CustomEditor(typeof(WfcSolver))]
public class WfcBrushEditor : Editor
{
    static bool brushEnabled;
    static int brushId;
    static int brushRotation;
    Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
    bool lastErase;

    void OnEnable()
    {
        SceneView.duringSceneGui += DuringScene;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DuringScene;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        brushEnabled = EditorGUILayout.Toggle("Paint Initial Cells", brushEnabled);
        if (brushEnabled)
        {
            EditorGUILayout.HelpBox("Drag on the grid to paint. Shift-drag erases a cell. Rotation -1 allows any rotation.", MessageType.Info);
            brushId = IdField(brushId);
            brushRotation = EditorGUILayout.IntPopup("Rotation", brushRotation, new[] { "Any", "0", "1", "2", "3" }, new[] { -1, 0, 1, 2, 3 });
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    int IdField(int current)
    {
        var rules = serializedObject.FindProperty("rules");
        if (rules.arraySize == 0)
            return EditorGUILayout.IntField("Id", current);

        var ids = new int[rules.arraySize];
        var labels = new GUIContent[rules.arraySize];
        var selected = 0;
        for (var i = 0; i < rules.arraySize; i++)
        {
            var id = rules.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue;
            ids[i] = id;
            labels[i] = new GUIContent(id.ToString());
            if (id == current)
                selected = i;
        }

        selected = EditorGUILayout.Popup(new GUIContent("Id"), selected, labels);
        return ids[selected];
    }

    void DuringScene(SceneView view)
    {
        if (!brushEnabled || Application.isPlaying)
            return;

        var solver = target as WfcSolver;
        if (solver == null)
            return;

        var world = serializedObject.FindProperty("world").objectReferenceValue as World;
        if (world == null || world.CellSize <= 0f)
            return;

        DrawCells(world);
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        var current = Event.current;
        var inside = HoverCell(world, current.mousePosition, out var hovered);
        if (inside)
            DrawCell(world, hovered.x, hovered.y, new Color(1f, 1f, 1f, 0.35f));

        if (current.type == EventType.MouseMove)
            view.Repaint();

        var paint = current.type == EventType.MouseDown || current.type == EventType.MouseDrag;
        if (!paint || current.button != 0 || !inside)
            return;

        var erase = current.shift;
        if (hovered != lastCell || erase != lastErase)
        {
            lastCell = hovered;
            lastErase = erase;
            ApplyBrush(hovered.x, hovered.y, erase);
        }

        current.Use();
    }

    void ApplyBrush(int x, int y, bool erase)
    {
        serializedObject.Update();
        var cells = serializedObject.FindProperty("initialCells");
        var found = -1;
        for (var i = 0; i < cells.arraySize; i++)
        {
            var entry = cells.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("X").intValue != x || entry.FindPropertyRelative("Y").intValue != y)
                continue;

            if (found < 0)
                found = i;
            else
                cells.DeleteArrayElementAtIndex(i--);
        }

        if (erase)
        {
            if (found >= 0)
                cells.DeleteArrayElementAtIndex(found);
        }
        else if (found >= 0)
        {
            var entry = cells.GetArrayElementAtIndex(found);
            entry.FindPropertyRelative("Id").intValue = brushId;
            entry.FindPropertyRelative("Rotation").intValue = brushRotation;
        }
        else
        {
            var index = cells.arraySize;
            cells.InsertArrayElementAtIndex(index);
            var entry = cells.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("X").intValue = x;
            entry.FindPropertyRelative("Y").intValue = y;
            entry.FindPropertyRelative("Id").intValue = brushId;
            entry.FindPropertyRelative("Rotation").intValue = brushRotation;
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawCells(World world)
    {
        var cells = serializedObject.FindProperty("initialCells");
        for (var i = 0; i < cells.arraySize; i++)
        {
            var entry = cells.GetArrayElementAtIndex(i);
            var id = entry.FindPropertyRelative("Id").intValue;
            DrawCell(world, entry.FindPropertyRelative("X").intValue, entry.FindPropertyRelative("Y").intValue, ColorFor(id));
        }
    }

    static void DrawCell(World world, int x, int y, Color color)
    {
        var size = world.CellSize;
        var a = world.transform.TransformPoint(new Vector3(x * size, 0.02f, y * size));
        var b = world.transform.TransformPoint(new Vector3((x + 1) * size, 0.02f, y * size));
        var c = world.transform.TransformPoint(new Vector3((x + 1) * size, 0.02f, (y + 1) * size));
        var d = world.transform.TransformPoint(new Vector3(x * size, 0.02f, (y + 1) * size));
        Handles.DrawSolidRectangleWithOutline(new[] { a, b, c, d }, color, Color.black);
    }

    static bool HoverCell(World world, Vector2 mouse, out Vector2Int cell)
    {
        cell = default;
        var plane = new Plane(world.transform.up, world.transform.position);
        var ray = HandleUtility.GUIPointToWorldRay(mouse);
        if (!plane.Raycast(ray, out var distance))
            return false;

        var local = world.transform.InverseTransformPoint(ray.GetPoint(distance));
        var x = Mathf.FloorToInt(local.x / world.CellSize);
        var y = Mathf.FloorToInt(local.z / world.CellSize);
        if (!world.InBounds(x, y))
            return false;

        cell = new Vector2Int(x, y);
        return true;
    }

    static Color ColorFor(int id)
    {
        var color = Color.HSVToRGB(Mathf.Repeat(id * 0.17f, 1f), 0.65f, 0.95f);
        color.a = 0.45f;
        return color;
    }
}
