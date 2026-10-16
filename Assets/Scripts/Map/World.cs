using System;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class World : MonoBehaviour
{
    [SerializeField] int width = 16;
    [SerializeField] int height = 16;
    [SerializeField] float cellSize = 1f;

    int[] ids;
    int[] rotations;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    public event Action<int, int> CellChanged;

    void Awake()
    {
        if (cellSize <= 0f)
            cellSize = 1f;

        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        ids = new int[width * height];
        rotations = new int[width * height];
    }

    public bool InBounds(int x, int y)
    {
        return (uint)x < (uint)width && (uint)y < (uint)height;
    }

    public void Get(int x, int y, out int id, out int rotation)
    {
        if (!InBounds(x, y))
        {
            id = 0;
            rotation = 0;
            return;
        }

        var index = y * width + x;
        id = ids[index];
        rotation = rotations[index];
    }

    public void Set(int x, int y, int id, int rotation = 0)
    {
        if (!InBounds(x, y))
            return;

        rotation = ((rotation % 4) + 4) % 4;
        var index = y * width + x;
        if (ids[index] == id && rotations[index] == rotation)
            return;

        ids[index] = id;
        rotations[index] = rotation;
        CellChanged?.Invoke(x, y);
    }

    public Vector3 CellCenter(int x, int y)
    {
        var local = new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);
        return transform.TransformPoint(local);
    }
}
