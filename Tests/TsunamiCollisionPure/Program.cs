using System;
using Tsunami;

internal static class Program
{
    static int Main()
    {
        try
        {
            NoLoadDoesNoDamage();
            DamageUsesSimulationTimeNotFrameCount();
            PausedStepDoesNoDamage();
            BreakIsReportedOnce();
            OwnedBarriersPreserveStaticAndOtherOwnersInEveryRemovalOrder();
            FutureStaticWritesComposeWithOwners();
            ClearingOwnersPreservesStaticAndOpensOwnerOnlyFaces();
            TwoSidedFaceLoadsUseNetHydroAndSummedDirectionalImpact();
            SharedFaceOwnersReceiveMetadataBeforeVisualDeduplication();
            ImpactDetectionDistinguishesWallFromTerrain();
            Console.WriteLine("Tsunami collision pure tests: 10 passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    static WallDamageSettings TestSettings => new WallDamageSettings
    {
        durability = 100f,
        impactThresholdKilonewtons = 0f,
        impactDamagePerKilonewtonSecond = 1f,
        sustainedThresholdKilonewtons = 0f,
        sustainedDamagePerKilonewtonSecond = 0f
    };

    static void NoLoadDoesNoDamage()
    {
        var state = new WallDamageState();
        Assert(!state.Apply(0f, 0f, 1f, TestSettings, out _, out _), "zero load broke a wall");
        Near(state.Damage, 0f, 0f, "zero load caused damage");
    }

    static void DamageUsesSimulationTimeNotFrameCount()
    {
        var thirtyFps = new WallDamageState();
        var oneTwentyFps = new WallDamageState();
        for (int i = 0; i < 150; i++) thirtyFps.Apply(10_000f, 0f, 1f / 30f, TestSettings, out _, out _);
        for (int i = 0; i < 600; i++) oneTwentyFps.Apply(10_000f, 0f, 1f / 120f, TestSettings, out _, out _);
        Near(thirtyFps.Damage, oneTwentyFps.Damage, 0.001f, "damage depends on render-sized chunks");
    }

    static void PausedStepDoesNoDamage()
    {
        var state = new WallDamageState();
        state.Apply(1_000_000f, 1_000_000f, 0f, TestSettings, out _, out _);
        Near(state.Damage, 0f, 0f, "zero simulation timestep caused damage");
    }

    static void BreakIsReportedOnce()
    {
        var state = new WallDamageState();
        Assert(state.Apply(100_000f, 0f, 1f, TestSettings, out _, out _), "wall did not break at capacity");
        Assert(!state.Apply(100_000f, 0f, 1f, TestSettings, out _, out _), "wall broke more than once");
    }

    static void OwnedBarriersPreserveStaticAndOtherOwnersInEveryRemovalOrder()
    {
        TestOrder(10, 20);
        TestOrder(20, 10);

        var ownerOnly = new TsunamiPhysicsSolver(6, 2f);
        ownerOnly.SetOwnedBarrier(10, 1, 1, true, 2f);
        ownerOnly.SetOwnedBarrier(20, 1, 1, true, 3f);
        ownerOnly.RemoveBarrierOwner(10);
        Assert(ownerOnly.HasBarrier(1, 1, true), "shared face opened before the last owner left");
        ownerOnly.RemoveBarrierOwner(20);
        Assert(!ownerOnly.HasBarrier(1, 1, true), "owner-only face did not open after the last owner left");

        static void TestOrder(int first, int second)
        {
            var solver = new TsunamiPhysicsSolver(6, 2f);
            solver.SetBarrier(1, 1, true, 1f);
            Assert(solver.SetOwnedBarrier(10, 1, 1, true, 2f), "first owner was rejected");
            Assert(solver.SetOwnedBarrier(20, 1, 1, true, 3f), "second owner was rejected");
            Near(solver.BarrierX[7], 3f, 0f, "highest owner did not win");

            Assert(solver.RemoveBarrierOwner(first), "first owner removal failed");
            Near(solver.BarrierX[7], first == 20 ? 2f : 3f, 0f, "remaining owner crest was not preserved");
            Assert(solver.RemoveBarrierOwner(second), "second owner removal failed");
            Near(solver.BarrierX[7], 1f, 0f, "static crest was not restored after last owner");
        }
    }

    static void FutureStaticWritesComposeWithOwners()
    {
        var solver = new TsunamiPhysicsSolver(6, 2f);
        solver.SetOwnedBarrier(1, 1, 1, false, 3f);
        solver.SetBarrier(1, 1, false, 4f);
        Assert(solver.RemoveBarrierOwner(1), "owner removal failed");
        Near(solver.BarrierZ[7], 4f, 0f, "static write made after owner registration was lost");
    }

    static void ClearingOwnersPreservesStaticAndOpensOwnerOnlyFaces()
    {
        var solver = new TsunamiPhysicsSolver(6, 2f);
        solver.SetBarrier(1, 1, true, 1f);
        solver.SetOwnedBarrier(1, 1, 1, true, 2f);
        solver.SetOwnedBarrier(2, 2, 1, true, 2f);
        solver.ClearBarrierOwners();
        Near(solver.BarrierX[7], 1f, 0f, "static face was cleared with owners");
        Assert(!solver.HasBarrier(2, 1, true), "owner-only face did not reopen");
    }

    static void TwoSidedFaceLoadsUseNetHydroAndSummedDirectionalImpact()
    {
        WallFaceLoad equal = WallFaceLoad.Calculate(1000f, 10f, 2f, 1f, 2f, 1f, 3f);
        Near(equal.SustainedForceNewtons, 0f, 0f, "equal water columns did not cancel hydrostatic load");
        Near(equal.ImpactForceNewtons, 13_000f, 0.001f, "two directional impact loads were not summed once");

        WallFaceLoad unequal = WallFaceLoad.Calculate(1000f, 10f, 2f, 2f, 0f, 1f, 0f);
        Near(unequal.SustainedForceNewtons, 30_000f, 0.001f, "hydrostatic net pressure difference is wrong");
    }

    static void SharedFaceOwnersReceiveMetadataBeforeVisualDeduplication()
    {
        int delivered = 0;
        bool firstMetadataCorrect = false;
        bool secondMetadataCorrect = false;
        Action<TestImpactMetadata> receive = metadata =>
        {
            delivered++;
            if (metadata.Owner == 10)
                firstMetadataCorrect = metadata.Face == 13 && metadata.ProjectedPosition == 2.5f && metadata.Normal == 1f;
            if (metadata.Owner == 20)
                secondMetadataCorrect = metadata.Face == 13 && metadata.ProjectedPosition == 3.5f && metadata.Normal == -1f;
        };
        var visualGate = new ImpactFaceEmissionGate(36);

        WallImpactDelivery.Deliver(receive, new TestImpactMetadata(10, 13, 2.5f, 1f));
        bool firstVisual = visualGate.ShouldEmit(7, 13, true);
        WallImpactDelivery.Deliver(receive, new TestImpactMetadata(20, 13, 3.5f, -1f));
        bool secondVisual = visualGate.ShouldEmit(7, 13, true);

        Assert(delivered == 2 && firstMetadataCorrect && secondMetadataCorrect,
            "shared-face owner metadata was suppressed or changed before delivery");
        Assert(firstVisual && !secondVisual, "visual face deduplication did not stay downstream of metadata delivery");
    }

    readonly struct TestImpactMetadata
    {
        public readonly int Owner;
        public readonly int Face;
        public readonly float ProjectedPosition;
        public readonly float Normal;

        public TestImpactMetadata(int owner, int face, float projectedPosition, float normal)
        {
            Owner = owner;
            Face = face;
            ProjectedPosition = projectedPosition;
            Normal = normal;
        }
    }

    static void ImpactDetectionDistinguishesWallFromTerrain()
    {
        var solver = new TsunamiPhysicsSolver(6, 1f) { UseThreads = false, OpenBoundaries = false };
        var bed = new float[36];
        solver.ResetToSeaLevel(bed);
        int i = 2 * 6 + 2;
        solver.Depth[i] = 1f;
        solver.Qx[i] = 4f;
        solver.SetBarrier(2, 2, true, 3f);
        solver.Step(bed, 0.001f);
        bool found = ImpactDetection.Find(solver, bed, i, 0.1f, 0.1f, 0f,
            out _, out _, out _, out _, out bool isWall);
        Assert(found && isWall, "wall impact was not classified as a wall");
    }

    static void Near(float actual, float expected, float tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{message}: {actual} != {expected}");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
