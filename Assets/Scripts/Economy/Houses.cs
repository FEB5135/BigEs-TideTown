using System.Collections.Generic;
using UnityEngine;

// WFC haritasındaki evler. Ev karoları (id 10-13) 2×2'lik köşe parçalarından kurulur; köşe ve kenar parçaları
// (11, 13) birleşip uzun sıra evler de olabilir. Birbirine değen ev hücreleri bir bina; bina, her 4 hücresi için bir
// ev sayılır ve her evin bir kapısı (binanın yanındaki düz zemin hücrelerinden biri) olur: işçi orada doğar, tsunamide
// oraya koşup içeri girer. Harita her oyunda rastgele olduğu için ev sayısı (işçi sınırı) da her oyunda farklıdır.
// Ev ya ayaktadır ya yıkıktır: tsunami suyu yeterince derin ya da hızlıysa ev yıkılır (VillageFloodTracker karar
// verir), MapView evin karolarını yıkık modelle değiştirir. Tsunami sıfırlanınca evler yeniden ayağa kalkar.
public static class Houses
{
    public static event System.Action<House> Destroyed;
    public static event System.Action<House> Repaired;

    public class House
    {
        public readonly List<Vector3> Doors = new();
        public Vector3 Center;
        public Gatherer Resident;
        // Bu evin kapladığı ev karoları (World hücresi) ve hücre ortaları; yıkılınca MapView bu karoları değiştirir.
        public readonly List<Vector2Int> Cells = new();
        public readonly List<Vector3> CellCenters = new();

        public bool IsDestroyed { get; private set; }

        public void Destroy()
        {
            if (IsDestroyed)
                return;
            IsDestroyed = true;
            Destroyed?.Invoke(this);
        }

        public void Repair()
        {
            if (!IsDestroyed)
                return;
            IsDestroyed = false;
            Repaired?.Invoke(this);
        }

        public Vector3 RandomDoor() => Doors.Count > 0 ? Doors[Random.Range(0, Doors.Count)] : Center;

        // Kapılardan verilen noktaya en yakını.
        public Vector3 DoorNearest(Vector3 point)
        {
            var best = Center;
            var bestDistance = float.MaxValue;
            foreach (var door in Doors)
            {
                var distance = (door - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = door;
                }
            }
            return best;
        }
    }

    static readonly int[] HouseIds = { 10, 11, 12, 13 };
    const int GroundId = 0;

    static readonly List<House> all = new();
    static World builtFor;

    public static IReadOnlyList<House> All
    {
        get
        {
            Build();
            return all;
        }
    }

    public static int Count => All.Count;

    public static int DestroyedCount
    {
        get
        {
            var count = 0;
            foreach (var house in All)
                if (house.IsDestroyed)
                    count++;
            return count;
        }
    }

    // Kurulmuş evler; listeyi kurmaz (harita henüz çözülmemişken erken kurulup boş kalmasın diye).
    public static IReadOnlyList<House> Built => all;

    // Tsunami sıfırlanınca bütün evler onarılır.
    public static void RepairAll()
    {
        foreach (var house in all)
            house.Repair();
    }

    public static int Occupied
    {
        get
        {
            var occupied = 0;
            foreach (var house in All)
                if (house.Resident != null)
                    occupied++;
            return occupied;
        }
    }

    // Boş ve yıkılmamış bir ev (rastgele); yoksa null.
    public static House RandomFree()
    {
        var free = new List<House>();
        foreach (var house in All)
            if (house.Resident == null && !house.IsDestroyed)
                free.Add(house);
        return free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
    }

    static bool IsHouse(int id) => System.Array.IndexOf(HouseIds, id) >= 0;

    // Harita çözüldükten sonra ilk sorulduğunda bir kez kurulur (WFC Start'ta çözer).
    static void Build()
    {
        var world = Object.FindAnyObjectByType<World>();
        if (world == null || world == builtFor)
            return;
        builtFor = world;
        all.Clear();

        int width = world.Width, height = world.Height;
        var seen = new bool[width * height];
        var stack = new Stack<Vector2Int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                world.Get(x, y, out var id, out _);
                if (seen[y * width + x] || !IsHouse(id))
                    continue;

                // Kapı hücresi -> dokunduğu ev hücresi.
                var doors = new Dictionary<Vector2Int, Vector2Int>();
                var cells = new List<Vector2Int>();
                seen[y * width + x] = true;
                stack.Push(new Vector2Int(x, y));
                while (stack.Count > 0)
                {
                    var cell = stack.Pop();
                    cells.Add(cell);
                    foreach (var step in Steps)
                    {
                        var next = cell + step;
                        if (!world.InBounds(next.x, next.y))
                            continue;
                        world.Get(next.x, next.y, out var nextId, out _);
                        if (IsHouse(nextId))
                        {
                            if (!seen[next.y * width + next.x])
                            {
                                seen[next.y * width + next.x] = true;
                                stack.Push(next);
                            }
                        }
                        else if (nextId == GroundId)
                            doors.TryAdd(next, cell);
                    }
                }

                // Uzun sıra evler birkaç ev sayılır; her ev binanın çevresinden ayrı bir kapı alır.
                var doorList = new List<KeyValuePair<Vector2Int, Vector2Int>>(doors);
                var middle = Vector2.zero;
                foreach (var pair in doorList)
                    middle += pair.Key;
                middle /= Mathf.Max(1, doorList.Count);
                doorList.Sort((a, b) => Angle(a.Key, middle).CompareTo(Angle(b.Key, middle)));
                int homes = Mathf.Max(1, cells.Count / 4);
                int firstHome = all.Count;
                for (int i = 0; i < homes; i++)
                {
                    var house = new House();
                    if (doorList.Count > 0)
                    {
                        var door = doorList[i * doorList.Count / homes];
                        house.Doors.Add(world.CellCenter(door.Key.x, door.Key.y));
                        house.Center = world.CellCenter(door.Value.x, door.Value.y);
                    }
                    else
                        house.Center = world.CellCenter(x, y);
                    all.Add(house);
                }

                // Binanın hücreleri, ortası (kapısının dokunduğu hücre) en yakın olan eve düşer.
                foreach (var cell in cells)
                {
                    var center = world.CellCenter(cell.x, cell.y);
                    var owner = all[firstHome];
                    for (int i = firstHome + 1; i < all.Count; i++)
                        if ((all[i].Center - center).sqrMagnitude < (owner.Center - center).sqrMagnitude)
                            owner = all[i];
                    owner.Cells.Add(cell);
                    owner.CellCenters.Add(center);
                }
            }
    }

    // Kapıları binanın çevresinde sırayla dizmek için: kapıların ortasına göre açı.
    static float Angle(Vector2Int door, Vector2 middle) => Mathf.Atan2(door.y - middle.y, door.x - middle.x);

    static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
}
