using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tsunami.EditorTools
{
    public static class WallWaterSealChecks
    {
        public static void PrepareAndRun()
        {
            IdleTown.Editor.WallfixWorkshop.RefreshSolidMeshes();
            Run();
        }
        [MenuItem("Tools/Idle Town/Wallfix/Run Water Seal Tests")]
        public static void Run()
        {
            int passed = 0;
            try
            {
                foreach (bool alongX in new[] { true, false })
                {
                    var solver = new TsunamiPhysicsSolver(16, 1) { OpenBoundaries = false, UseThreads = false, SpongeStrength = 0 };
                    var bed = new float[256];
                    for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                    {
                        int i = z * 16 + x;
                        if ((alongX ? x : z) < 8) solver.Depth[i] = 4;
                    }
                    for (int k = 0; k < 16; k++)
                        solver.SetOwnedBarrier(k + 1, alongX ? 7 : k, alongX ? k : 7, alongX, 2, true);
                    for (int step = 0; step < 150; step++) solver.Step(bed, .01f);
                    Require(DrySide(solver, alongX) < 1e-7f, "Intact 2 m wall leaked under 4 m water."); passed++;
                    solver.SetOwnedBarrier(100, alongX ? 7 : 8, alongX ? 8 : 7, alongX, 4, true);
                    solver.RemoveBarrierOwner(9);
                    for (int step = 0; step < 50; step++) solver.Step(bed, .01f);
                    Require(DrySide(solver, alongX) < 1e-7f, "Removing one shared owner opened a surviving wall."); passed++;
                    solver.RemoveBarrierOwner(100);
                    for (int step = 0; step < 150; step++) solver.Step(bed, .01f);
                    Require(DrySide(solver, alongX) > .01f, "Destroyed wall did not let water through."); passed++;
                    solver.SetOwnedBarrier(101, alongX ? 7 : 8, alongX ? 8 : 7, alongX, 2, true);
                    Require((alongX ? solver.SealedBarrierX : solver.SealedBarrierZ)[alongX ? 8 * 16 + 7 : 7 * 16 + 8],
                        "Rebuilding did not restore seal."); passed++;
                    solver.ClearBarrierOwners();
                    Require(!solver.HasBarrier(7,7,alongX), "Reset left a ghost barrier."); passed++;
                }
                CheckDiagonalLoop(); passed++;
                CheckDamagedSeal(); passed++;
                CheckContactDamageDisabled(); passed++;
                CheckImpactDamagePreserved(); passed++;
                Write($"PASS: {passed}/14 water checks. Wall seals, destruction/rebuilding, damaged seal, no continuous contact damage (including legacy settings), and preserved impact damage verified.");
            }
            catch (Exception error) { Write($"FAIL after {passed} checks: {error}"); throw; }
        }

        static void CheckContactDamageDisabled()
        {
            var settings = WallDamageSettings.Default;
            // Reproduce serialized scenes from before the change: even an enormous positive rate
            // must not silently bring back continuous contact damage.
            settings.sustainedDamagePerKilonewtonSecond = 100f;
            Require(settings.Validated().sustainedDamagePerKilonewtonSecond == 0f, "Legacy contact rate remained enabled.");
            var state = new WallDamageState();
            var spreading = WallFaceLoad.Calculate(1000f, 9.81f, 2f, 10f, .5f, 0f, 0f);
            for (int i = 0; i < 30000; i++)
            {
                float force = i % 2 == 0 ? 0f : spreading.ImpactForceNewtons;
                Require(!state.Apply(force, 10000000f, .02f, settings, out var impact, out var contact), "Resting or slowly spreading water destroyed a wall.");
                Require(impact == 0f && contact == 0f, "Contact generated damage without an impact.");
            }
            Require(state.Damage == 0f && !state.Broken, "Ten minutes of contact wore down the wall.");
            state.Apply(100000f, 0f, .1f, settings, out _, out _);
            float damage = state.Damage;
            Require(damage > 0f, "Test impact did not damage the wall.");
            for (int i = 0; i < 1000; i++) state.Apply(0f, 10000000f, .02f, settings, out _, out _);
            Require(state.Damage == damage, "A damaged wall kept losing health after the wave passed.");
        }

        static void CheckImpactDamagePreserved()
        {
            var settings = WallDamageSettings.Default;
            var withContact = new WallDamageState();
            var impactOnly = new WallDamageState();
            for (int i = 0; i < 1000 && !impactOnly.Broken; i++)
            {
                withContact.Apply(100000f, 10000000f, .02f, settings, out _, out var contact);
                impactOnly.Apply(100000f, 0f, .02f, settings, out _, out _);
                Require(contact == 0f && Math.Abs(withContact.Damage - impactOnly.Damage) < .0001f,
                    "Pressure changed the impact damage or consumed its rate limit.");
            }
            Require(impactOnly.Broken && withContact.Broken, "A strong tsunami can no longer break a wall.");
        }

        static void CheckDamagedSeal()
        {
            var root = new GameObject("Temporary damage seal test");
            using var art = new IdleTown.Construction.WallArt();
            try
            {
                var source = new GameObject("Art", typeof(MeshFilter), typeof(MeshRenderer));
                source.transform.SetParent(root.transform, false);
                source.GetComponent<MeshFilter>().sharedMesh = art.Panel(IdleTown.Construction.WallTier.Timber,.117f);
                source.GetComponent<MeshRenderer>().sharedMaterials = art.Palette(IdleTown.Construction.WallTier.Timber);
                var visual = root.AddComponent<WallDamageVisual>();
                visual.SetDamage(.8f);
                var seal = root.transform.Find("Standing wall seal");
                Require(seal != null && seal.gameObject.activeSelf && seal.GetComponent<Renderer>().enabled,
                    "Damaged but standing wall has an open core.");
                var pieces = visual.DetachFragments();
                Require(!seal.gameObject.activeSelf, "Destroyed wall left a standing seal.");
                foreach (var piece in pieces) UnityEngine.Object.DestroyImmediate(piece);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void CheckDiagonalLoop()
        {
            var root = new GameObject("Temporary water seal test");
            root.SetActive(false);
            try
            {
                var terrain = root.AddComponent<TerrainGrid>();
                terrain.UseExternalHeights(16, 1, new float[256]);
                var simulation = root.AddComponent<WaterSimulation>();
                var settings = new SerializedObject(simulation);
                settings.FindProperty("terrain").objectReferenceValue = terrain;
                settings.ApplyModifiedPropertiesWithoutUndo();
                simulation.EnsureInitialized();
                var solver = simulation.Solver;
                solver.OpenBoundaries = false; solver.UseThreads = false; solver.SpongeStrength = 0;
                Array.Clear(solver.Depth, 0, solver.Depth.Length);
                var corners = new[] { new Vector2Int(8,3), new Vector2Int(13,8), new Vector2Int(8,13), new Vector2Int(3,8) };
                Vector3 Corner(Vector2Int p) => terrain.GridToWorld(p.x,p.y,0) - new Vector3(.5f,0,.5f);
                for (int edge = 0; edge < 4; edge++)
                {
                    var a = corners[edge]; var b = corners[(edge + 1) % 4];
                    var d = new Vector2Int(Math.Sign(b.x-a.x), Math.Sign(b.y-a.y));
                    for (int i = 0; i < 5; i++)
                        Require(simulation.AddWallSegment(Corner(a+d*i), Corner(a+d*(i+1)), 2, 0, true).IsValid,
                            "Diagonal piece failed to register.");
                }
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                    if (Mathf.Abs(x-7.5f) + Mathf.Abs(z-7.5f) > 6) solver.Depth[z*16+x] = 4;
                for (int i = 0; i < 250; i++) solver.Step(terrain.Heights, .01f);
                Require(solver.Depth[8*16+8] < 1e-7f, "Diagonal closed enclosure leaked through joints.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static float DrySide(TsunamiPhysicsSolver solver, bool alongX)
        {
            float volume = 0;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                float depth = solver.Depth[z*16+x];
                Require(!float.IsNaN(depth) && !float.IsInfinity(depth), "Non-finite water state.");
                if ((alongX ? x : z) >= 8) volume += depth;
            }
            return volume;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Write(string message)
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/Wallfix-water-tests.txt", message);
            Debug.Log("[WallWaterSeal] " + message);
        }
    }
}
