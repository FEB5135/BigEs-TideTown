using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2D wave function collapse. Each side lists the ids that may sit there.
/// Both tiles must list each other. The four rotations of a rule are generated,
/// and the lists turn with them. Blender's X is flipped on import, so Left is +X and Right is -X.
/// The mesh pivot is the bottom-left corner, on the +Z edge, and the quad extends
/// toward -Z. Front is that far edge. Back is the pivot edge.
/// </summary>
[DefaultExecutionOrder(-50)]
public class WfcSolver : MonoBehaviour
{
    const int MaxAttempts = 200000;

    static readonly int[] DX = { -1, 1, 0, 0 };
    static readonly int[] DY = { 0, 0, 1, -1 };
    static readonly int[] Opposite = { 1, 0, 3, 2 };
    // Clockwise from above, as world-direction indexes: +Z, +X, -Z, -X.
    static readonly int[] Clockwise = { 2, 1, 3, 0 };

    [SerializeField] World world;
    [SerializeField] int seed;
    [SerializeField] Rule[] rules;
    [SerializeField] FixedCell[] initialCells;

    readonly List<FixedCell> pins = new();
    readonly List<Tile> tiles = new();
    readonly Queue<int> dirty = new();

    bool[] possible;
    int[] counts;
    bool[] queued;
    System.Random random;
    int width;
    int height;
    int tileCount;
    int attempts;
    int usedSeed;

    void Start()
    {
        Solve();
    }

    public void Pin(int x, int y, int id, int rotation = 0)
    {
        pins.Add(new FixedCell { X = x, Y = y, Id = id, Rotation = rotation });
    }

    public void Solve()
    {
        if (world == null)
        {
            Debug.LogError("WFC needs a World.", this);
            return;
        }

        if (!BuildTiles())
            return;

        width = world.Width;
        height = world.Height;
        var cellCount = width * height;
        tileCount = tiles.Count;
        possible = new bool[cellCount * tileCount];
        counts = new int[cellCount];
        queued = new bool[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            counts[i] = tileCount;
            for (var tile = 0; tile < tileCount; tile++)
                possible[i * tileCount + tile] = true;
        }

        if (!ApplyFixed(initialCells) || !ApplyFixed(pins))
            return;

        usedSeed = seed != 0 ? seed : Environment.TickCount;
        random = new System.Random(usedSeed);
        attempts = 0;
        dirty.Clear();

        for (var i = 0; i < cellCount; i++)
            Enqueue(i);

        if (!Propagate() || !Search())
        {
            Debug.LogError($"WFC failed to fill the map (seed {usedSeed}).", this);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var tile = ChosenTile(y * width + x);
                world.Set(x, y, tiles[tile].Id, tiles[tile].Rotation);
            }
        }
    }

    bool BuildTiles()
    {
        tiles.Clear();
        if (rules == null || rules.Length == 0)
        {
            Debug.LogError("WFC has no rules.", this);
            return false;
        }

        var seen = new HashSet<int>();
        foreach (var rule in rules)
        {
            if (rule == null)
                continue;

            if (rule.Id < 0)
            {
                Debug.LogError($"WFC rule id {rule.Id} is invalid.", this);
                return false;
            }

            if (!seen.Add(rule.Id))
            {
                Debug.LogError($"WFC rule id {rule.Id} is duplicated.", this);
                return false;
            }

            for (var rotation = 0; rotation < 4; rotation++)
            {
                tiles.Add(new Tile
                {
                    Id = rule.Id,
                    Rotation = rotation,
                    Edges = RotateEdges(rule, rotation)
                });
            }
        }

        if (tiles.Count == 0)
        {
            Debug.LogError("WFC has no rules.", this);
            return false;
        }

        return true;
    }

    static int[][] RotateEdges(Rule rule, int rotation)
    {
        // 0 -X right, 1 +X left, 2 +Z back (pivot edge), 3 -Z front (far edge).
        int[][] authored = { rule.Right, rule.Left, rule.Back, rule.Front };
        var edges = new int[4][];
        for (var step = 0; step < 4; step++)
        {
            var worldDir = Clockwise[step];
            var authoredDir = Clockwise[(step - rotation + 4) % 4];
            edges[worldDir] = authored[authoredDir] ?? Array.Empty<int>();
        }

        return edges;
    }

    bool ApplyFixed(IReadOnlyList<FixedCell> cells)
    {
        if (cells == null)
            return true;

        foreach (var cell in cells)
        {
            if (!world.InBounds(cell.X, cell.Y))
            {
                Debug.LogError($"WFC fixed cell ({cell.X}, {cell.Y}) is outside the map.", this);
                return false;
            }

            var index = cell.Y * width + cell.X;
            var kept = 0;
            for (var tile = 0; tile < tileCount; tile++)
            {
                var match = tiles[tile].Id == cell.Id && (cell.Rotation < 0 || tiles[tile].Rotation == Normalize(cell.Rotation));
                possible[index * tileCount + tile] = match;
                if (match)
                    kept++;
            }

            counts[index] = kept;
            if (kept == 0)
            {
                Debug.LogError($"WFC has no tile for fixed cell ({cell.X}, {cell.Y}) id {cell.Id}.", this);
                return false;
            }
        }

        return true;
    }

    bool Search()
    {
        var cell = PickCell();
        if (cell == -1)
            return true;
        if (cell < -1)
            return false;

        var choiceCount = counts[cell];
        var choices = new int[choiceCount];
        var choice = 0;
        var cellBase = cell * tileCount;
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (possible[cellBase + tile])
                choices[choice++] = tile;
        }

        for (var i = choiceCount - 1; i > 0; i--)
        {
            var swap = random.Next(i + 1);
            (choices[i], choices[swap]) = (choices[swap], choices[i]);
        }

        var savedPossible = (bool[])possible.Clone();
        var savedCounts = (int[])counts.Clone();

        foreach (var tile in choices)
        {
            if (++attempts > MaxAttempts)
                return false;

            Array.Copy(savedPossible, possible, possible.Length);
            Array.Copy(savedCounts, counts, counts.Length);
            dirty.Clear();
            Array.Clear(queued, 0, queued.Length);

            if (Collapse(cell, tile) && Search())
                return true;
        }

        return false;
    }

    int PickCell()
    {
        var min = int.MaxValue;
        var chosen = -1;
        var ties = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var count = counts[i];
            if (count == 0)
                return -2;
            if (count <= 1)
                continue;

            if (count < min)
            {
                min = count;
                chosen = i;
                ties = 1;
            }
            else if (count == min && random.Next(++ties) == 0)
            {
                chosen = i;
            }
        }

        return chosen;
    }

    bool Collapse(int cell, int tile)
    {
        var cellBase = cell * tileCount;
        for (var other = 0; other < tileCount; other++)
        {
            if (other == tile || !possible[cellBase + other])
                continue;

            possible[cellBase + other] = false;
            counts[cell]--;
        }

        Enqueue(cell);
        return Propagate();
    }

    bool Propagate()
    {
        while (dirty.Count > 0)
        {
            var cell = dirty.Dequeue();
            queued[cell] = false;
            var x = cell % width;
            var y = cell / width;

            for (var dir = 0; dir < 4; dir++)
            {
                var nx = x + DX[dir];
                var ny = y + DY[dir];
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                var neighbor = ny * width + nx;
                if (!Prune(neighbor, cell, Opposite[dir]))
                    continue;

                if (counts[neighbor] == 0)
                    return false;

                Enqueue(neighbor);
            }
        }

        return true;
    }

    bool Prune(int cell, int other, int dirFromCellToOther)
    {
        var changed = false;
        var cellBase = cell * tileCount;
        var otherBase = other * tileCount;
        var opposite = Opposite[dirFromCellToOther];

        for (var a = 0; a < tileCount; a++)
        {
            if (!possible[cellBase + a])
                continue;

            var fits = false;
            for (var b = 0; b < tileCount; b++)
            {
                if (possible[otherBase + b] && Compatible(a, b, dirFromCellToOther, opposite))
                {
                    fits = true;
                    break;
                }
            }

            if (fits)
                continue;

            possible[cellBase + a] = false;
            counts[cell]--;
            changed = true;
        }

        return changed;
    }

    bool Compatible(int tileA, int tileB, int dirFromAToB, int opposite)
    {
        return Allows(tiles[tileA].Edges[dirFromAToB], tiles[tileB].Id)
            && Allows(tiles[tileB].Edges[opposite], tiles[tileA].Id);
    }

    static bool Allows(int[] ids, int id)
    {
        if (ids == null)
            return false;

        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] == id)
                return true;
        }

        return false;
    }

    void Enqueue(int cell)
    {
        if (queued[cell])
            return;

        queued[cell] = true;
        dirty.Enqueue(cell);
    }

    int ChosenTile(int cell)
    {
        var cellBase = cell * tileCount;
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (possible[cellBase + tile])
                return tile;
        }

        return 0;
    }

    static int Normalize(int rotation)
    {
        rotation %= 4;
        return rotation < 0 ? rotation + 4 : rotation;
    }

    struct Tile
    {
        public int Id;
        public int Rotation;
        public int[][] Edges;
    }

    [Serializable]
    public class Rule
    {
        public int Id;
        [Tooltip("Ids allowed on this side. Left is +X and right is -X, matching Blender. Front is the far edge, -Z. Back is the pivot edge, +Z.")]
        public int[] Left;
        public int[] Right;
        public int[] Front;
        public int[] Back;
    }

    [Serializable]
    public struct FixedCell
    {
        public int X;
        public int Y;
        public int Id;
        [Tooltip("Quarter-turns clockwise. -1 allows any rotation of this id.")]
        public int Rotation;
    }
}
