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
    [SerializeField, Tooltip("Each placed tile only constrains and searches cells within this distance. The solved area grows outward. 0 searches the whole map on every placement, which is slow on large maps.")]
    int maxDistance = 16;
    [SerializeField] Rule[] rules;
    [SerializeField] FixedCell[] initialCells;

    readonly List<FixedCell> pins = new();
    readonly List<Tile> tiles = new();
    readonly Queue<int> dirty = new();

    bool[] possible;
    int[] counts;
    bool[] queued;
    bool[] expanded;
    int[] coverage;
    bool[] compatible;
    int[] activeIndex;
    bool[] inWave;
    readonly List<int> active = new();
    readonly List<Edit> edits = new();
    readonly Queue<int> wave = new();
    System.Random random;
    int width;
    int height;
    int tileCount;
    int attempts;
    int attemptLimit;
    int usedSeed;
    int propagateOrigin = -1;

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
        expanded = maxDistance > 0 ? new bool[cellCount] : null;
        coverage = maxDistance > 0 ? new int[cellCount] : null;
        activeIndex = new int[cellCount];
        inWave = new bool[cellCount];
        compatible = new bool[tileCount * tileCount * 4];
        active.Clear();
        edits.Clear();
        BuildCompatibility();
        for (var i = 0; i < cellCount; i++)
        {
            counts[i] = tileCount;
            activeIndex[i] = -1;
            for (var tile = 0; tile < tileCount; tile++)
                possible[i * tileCount + tile] = true;
        }

        if (!ApplyFixed(initialCells) || !ApplyFixed(pins))
            return;

        usedSeed = seed != 0 ? seed : Environment.TickCount;
        random = new System.Random(usedSeed);
        attempts = 0;
        attemptLimit = Math.Max(MaxAttempts, cellCount * 32);
        dirty.Clear();
        wave.Clear();

        var origins = new List<int>();
        for (var i = 0; i < cellCount; i++)
        {
            if (counts[i] < tileCount)
                origins.Add(i);
        }

        if (!PropagateInitial(origins) || !Search(origins))
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

    void BuildCompatibility()
    {
        for (var a = 0; a < tileCount; a++)
        {
            for (var b = 0; b < tileCount; b++)
            {
                for (var dir = 0; dir < 4; dir++)
                {
                    var opposite = Opposite[dir];
                    compatible[(a * tileCount + b) * 4 + dir] =
                        Allows(tiles[a].Edges[dir], tiles[b].Id)
                        && Allows(tiles[b].Edges[opposite], tiles[a].Id);
                }
            }
        }
    }

    bool Search(List<int> origins)
    {
        if (!OpenInitialWindows(origins))
            return false;

        var steps = new List<SearchStep>();

        while (true)
        {
            if (attempts > attemptLimit)
                return false;

            var cell = PickCell();
            if (cell == -2)
            {
                if (!Backtrack(steps))
                    return false;
                continue;
            }

            if (cell < 0)
                return true;

            steps.Add(new SearchStep
            {
                Cell = cell,
                Choices = ShuffledChoices(cell),
                Index = 0,
                Mark = edits.Count
            });

            if (!TryCurrent(steps))
                return false;
        }
    }

    bool TryCurrent(List<SearchStep> steps)
    {
        while (steps.Count > 0)
        {
            var step = steps[steps.Count - 1];
            if (step.Index >= step.Choices.Length)
            {
                steps.RemoveAt(steps.Count - 1);
                if (steps.Count == 0)
                    return false;

                step = steps[steps.Count - 1];
                Undo(step.Mark);
                step.Index++;
                steps[steps.Count - 1] = step;
                continue;
            }

            if (++attempts > attemptLimit)
                return false;

            if (Collapse(step.Cell, step.Choices[step.Index]))
                return true;

            step.Index++;
            steps[steps.Count - 1] = step;
        }

        return false;
    }

    bool Backtrack(List<SearchStep> steps)
    {
        if (steps.Count == 0)
            return false;

        var step = steps[steps.Count - 1];
        Undo(step.Mark);
        step.Index++;
        steps[steps.Count - 1] = step;
        return TryCurrent(steps);
    }

    void Undo(int mark)
    {
        for (var i = edits.Count - 1; i >= mark; i--)
        {
            var edit = edits[i];
            if (edit.Kind == EditRemove)
                UndoRemove(edit);
            else
                UndoExpand(edit.Cell);
        }

        if (edits.Count > mark)
            edits.RemoveRange(mark, edits.Count - mark);
    }

    int[] ShuffledChoices(int cell)
    {
        var count = 0;
        var cellBase = cell * tileCount;
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (possible[cellBase + tile])
                count++;
        }

        var choices = new int[count];
        var index = 0;
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (possible[cellBase + tile])
                choices[index++] = tile;
        }

        for (var i = count - 1; i > 0; i--)
        {
            var swap = random.Next(i + 1);
            (choices[i], choices[swap]) = (choices[swap], choices[i]);
        }

        return choices;
    }

    const byte EditRemove = 0;
    const byte EditExpand = 1;

    struct SearchStep
    {
        public int Cell;
        public int[] Choices;
        public int Index;
        public int Mark;
    }

    struct Edit
    {
        public byte Kind;
        public int Cell;
        public int Tile;
    }

    int PickCell()
    {
        var min = int.MaxValue;
        var bestCover = -1;
        var chosen = -1;
        var ties = 0;
        var i = 0;
        while (i < active.Count)
        {
            var cell = active[i];
            var count = counts[cell];
            if (count == 0)
                return -2;
            if (count <= 1)
            {
                Deactivate(cell);
                continue;
            }

            var cover = coverage == null ? 0 : coverage[cell];
            if (count < min || (count == min && cover > bestCover))
            {
                min = count;
                bestCover = cover;
                chosen = cell;
                ties = 1;
            }
            else if (count == min && cover == bestCover && random.Next(++ties) == 0)
            {
                chosen = cell;
            }

            i++;
        }

        return chosen;
    }

    bool Collapse(int cell, int tile)
    {
        var mark = edits.Count;
        var cellBase = cell * tileCount;
        for (var other = 0; other < tileCount; other++)
        {
            if (other != tile && possible[cellBase + other])
                RemovePossibility(cell, other);
        }

        if (!Spread(cell))
        {
            ClearQueue();
            DrainWave();
            Undo(mark);
            return false;
        }

        return true;
    }

    bool Spread(int origin)
    {
        if (maxDistance <= 0)
        {
            propagateOrigin = -1;
            Enqueue(origin);
            return Propagate();
        }

        Consider(origin);
        while (wave.Count > 0)
        {
            var cell = wave.Dequeue();
            inWave[cell] = false;
            if (counts[cell] != 1 || expanded[cell])
                continue;

            propagateOrigin = cell;
            Enqueue(cell);
            if (!Propagate())
            {
                propagateOrigin = -1;
                return false;
            }

            propagateOrigin = -1;
            Expand(cell);
            ConsiderSquare(cell);
        }

        return true;
    }

    void Consider(int cell)
    {
        if (inWave[cell] || expanded[cell] || counts[cell] != 1)
            return;

        inWave[cell] = true;
        wave.Enqueue(cell);
    }

    void ConsiderSquare(int cell)
    {
        var x = cell % width;
        var y = cell / width;
        var y0 = Math.Max(0, y - maxDistance);
        var y1 = Math.Min(height - 1, y + maxDistance);
        var x0 = Math.Max(0, x - maxDistance);
        var x1 = Math.Min(width - 1, x + maxDistance);
        for (var ny = y0; ny <= y1; ny++)
        {
            for (var nx = x0; nx <= x1; nx++)
                Consider(ny * width + nx);
        }
    }

    void DrainWave()
    {
        while (wave.Count > 0)
            inWave[wave.Dequeue()] = false;
    }

    bool PropagateInitial(List<int> origins)
    {
        if (maxDistance <= 0)
        {
            for (var i = 0; i < counts.Length; i++)
                Enqueue(i);
            return Propagate();
        }

        for (var i = 0; i < origins.Count; i++)
        {
            propagateOrigin = origins[i];
            Enqueue(origins[i]);
            if (!Propagate())
            {
                propagateOrigin = -1;
                ClearQueue();
                return false;
            }
        }

        propagateOrigin = -1;
        return true;
    }

    bool OpenInitialWindows(List<int> origins)
    {
        if (maxDistance <= 0)
        {
            for (var i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 1)
                    Activate(i);
            }

            return true;
        }

        var opened = false;
        for (var i = 0; i < origins.Count; i++)
        {
            var cell = origins[i];
            opened = true;
            if (counts[cell] == 1)
            {
                if (!Spread(cell))
                {
                    ClearQueue();
                    DrainWave();
                    return false;
                }
            }
            else if (coverage[cell] == 0)
            {
                coverage[cell] = 1;
            }

            if (counts[cell] > 1)
                Activate(cell);
        }

        if (opened)
            return true;

        var center = (height / 2) * width + width / 2;
        coverage[center] = 1;
        if (counts[center] > 1)
            Activate(center);
        return true;
    }

    void Expand(int cell)
    {
        if (maxDistance <= 0 || expanded[cell] || counts[cell] != 1)
            return;

        expanded[cell] = true;
        edits.Add(new Edit { Kind = EditExpand, Cell = cell });
        VisitSquare(cell, true);
    }

    void UndoExpand(int cell)
    {
        expanded[cell] = false;
        VisitSquare(cell, false);
    }

    void VisitSquare(int cell, bool add)
    {
        var x = cell % width;
        var y = cell / width;
        var y0 = Math.Max(0, y - maxDistance);
        var y1 = Math.Min(height - 1, y + maxDistance);
        var x0 = Math.Max(0, x - maxDistance);
        var x1 = Math.Min(width - 1, x + maxDistance);
        for (var ny = y0; ny <= y1; ny++)
        {
            for (var nx = x0; nx <= x1; nx++)
            {
                var index = ny * width + nx;
                if (add)
                {
                    coverage[index]++;
                    if (counts[index] > 1)
                        Activate(index);
                }
                else if (--coverage[index] == 0)
                {
                    Deactivate(index);
                }
            }
        }
    }

    void RemovePossibility(int cell, int tile)
    {
        possible[cell * tileCount + tile] = false;
        counts[cell]--;
        edits.Add(new Edit { Kind = EditRemove, Cell = cell, Tile = tile });
        if (counts[cell] <= 1)
            Deactivate(cell);
    }

    void UndoRemove(Edit edit)
    {
        possible[edit.Cell * tileCount + edit.Tile] = true;
        counts[edit.Cell]++;
        if (counts[edit.Cell] > 1 && (maxDistance <= 0 || coverage[edit.Cell] > 0))
            Activate(edit.Cell);
    }

    void Activate(int cell)
    {
        if (activeIndex[cell] >= 0)
            return;

        activeIndex[cell] = active.Count;
        active.Add(cell);
    }

    void Deactivate(int cell)
    {
        var index = activeIndex[cell];
        if (index < 0)
            return;

        var last = active[active.Count - 1];
        active[index] = last;
        activeIndex[last] = index;
        active.RemoveAt(active.Count - 1);
        activeIndex[cell] = -1;
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
                if (maxDistance > 0 && propagateOrigin >= 0 && Distance(propagateOrigin, neighbor) > maxDistance)
                    continue;

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
        var otherCount = counts[other];
        var single = -1;
        if (otherCount == 1)
        {
            for (var b = 0; b < tileCount; b++)
            {
                if (!possible[otherBase + b])
                    continue;

                single = b;
                break;
            }
        }

        for (var a = 0; a < tileCount; a++)
        {
            if (!possible[cellBase + a])
                continue;

            var fits = false;
            if (single >= 0)
            {
                fits = compatible[(a * tileCount + single) * 4 + dirFromCellToOther];
            }
            else
            {
                for (var b = 0; b < tileCount; b++)
                {
                    if (possible[otherBase + b] && compatible[(a * tileCount + b) * 4 + dirFromCellToOther])
                    {
                        fits = true;
                        break;
                    }
                }
            }

            if (fits)
                continue;

            RemovePossibility(cell, a);
            changed = true;
            if (counts[cell] == 0)
                return true;
        }

        return changed;
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

    void ClearQueue()
    {
        while (dirty.Count > 0)
            queued[dirty.Dequeue()] = false;
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

    int Distance(int a, int b)
    {
        var dx = a % width - b % width;
        var dy = a / width - b / width;
        if (dx < 0)
            dx = -dx;
        if (dy < 0)
            dy = -dy;
        return dx > dy ? dx : dy;
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
