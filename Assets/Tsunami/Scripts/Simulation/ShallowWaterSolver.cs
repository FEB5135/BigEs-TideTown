using System;

namespace Tsunami
{
    /// <summary>
    /// Grid-based "virtual pipes" shallow-water approximation on a staggered grid.
    /// Every face between two cells carries one signed flux that is accelerated by the difference in
    /// water-surface elevation (terrain + depth). A cell's outgoing fluxes are scaled so it can never send
    /// more water than it holds, so depths never go negative and no water is created inside the grid.
    ///
    /// The pipe cross-section is the water column above the higher of the two beds, so waves travel at
    /// roughly sqrt(g * depth): they slow down, bunch up and grow taller as the sea floor rises towards the
    /// coast (shoaling), and raised terrain fully blocks flow until the water tops it.
    ///
    /// This is a gameplay approximation, not a validated physical model.
    /// Pure C# (no UnityEngine dependency) so it can be unit-tested outside the editor.
    /// </summary>
    public sealed class ShallowWaterSolver
    {
        public readonly int Size;
        public readonly float CellSize;

        public readonly float[] Depth;
        public readonly float[] Speed; // approximate flow speed (m/s), used for foam and spray only
        public readonly float[] VelocityX, VelocityZ; // approximate flow velocity (m/s), visuals only

        // Signed face fluxes (m^3/s): qx[i] flows from cell i to i+1 (+x), qz[i] from cell i to i+n (+z).
        readonly float[] qx, qz;
        readonly float[] outflowScale;
        // Absorbing layer along the edges: weight (0..1) and which edge each cell is nearest to.
        readonly float[] spongeWeight;
        readonly BoundarySide[] spongeSide;

        // --- Tunables (set by WaterSimulation) ---
        public float Gravity = 9.81f;
        /// <summary>Wave-speed multiplier: waves travel at about sqrt(g * depth * FlowSpeed).</summary>
        public float FlowSpeed = 1f;
        /// <summary>
        /// Bed friction (1/s at 1 m depth). Scaled by 1/depth^2, so deep-ocean waves travel almost loss-free
        /// while thin floods on land settle quickly instead of sloshing.
        /// </summary>
        public float Friction = 0.3f;
        /// <summary>Extra drag (1/s) on flow over land (bed above sea level), so floods settle instead of sloshing.</summary>
        public float LandDrag = 0.25f;
        /// <summary>Small drag (1/s) everywhere, so leftover currents in the open ocean die out within ~30 s.</summary>
        public float BaseDrag = 0.04f;
        public float WetThreshold = 0.01f;
        const float MaxReportedSpeed = 30f;

        // --- Ocean boundary ---
        public bool OpenOceanBoundaries = true;
        public float SeaLevel;
        /// <summary>Target offset of the ocean boundary level (negative = sea draws back before the wave).</summary>
        public float BoundaryLevelOffset;
        /// <summary>Offset actually applied this step; follows BoundaryLevelOffset at OffsetRate.</summary>
        public float CurrentBoundaryOffset { get; private set; }
        /// <summary>Max change of the boundary offset per second (m/s), so drawback start/end is smooth.</summary>
        public float OffsetRate = 1.5f;
        /// <summary>
        /// Rate (1/s) at which the incoming wave fades in when the source starts and out when it stops, so
        /// Start/Stop never makes the edge jump by the full wave height in one step.
        /// </summary>
        public float SourceFadeRate = 1f;
        /// <summary>
        /// Momentum damping (1/s) in the absorbing band along the edges, so waves leave the grid instead of
        /// bouncing back. Not applied on the source edge while a wave is coming in.
        /// </summary>
        public float SpongeStrength = 3f;

        // --- Tsunami source ---
        /// <summary>While active, the ocean boundary carries the incoming wave signal (SurgeHeight over time).</summary>
        public bool SourceActive;
        public BoundarySide SourceSide = BoundarySide.South;
        public float SourceStart01;
        public float SourceEnd01 = 1f;
        /// <summary>
        /// Fraction of the source range at each end over which the wave fades out, so it never reaches the grid
        /// corners at full height (avoids corner artifacts). 0 = hard ends.
        /// </summary>
        public float SourceEdgeTaper = 0.2f;
        /// <summary>Extra rows of cells inward from the source edge that receive water (0 = edge only).</summary>
        public int SourceBandCells;
        /// <summary>
        /// Current height (m above the boundary level) of the wave signal far out at sea. Set every frame by the
        /// controller; the solver records its history and replays it along the edges with a travel delay.
        /// </summary>
        public float SurgeHeight;
        /// <summary>
        /// How far the wave reaches along the side edges (fraction of the grid width) before fading out.
        /// </summary>
        public float SideSurgeReach = 1f;
        /// <summary>Distance (m) the wave travels in the open ocean before reaching the source edge.</summary>
        public float ApproachDistance = 60f;

        /// <summary>Mean ocean depth along the grid edges (m), measured at reset.</summary>
        public float ReferenceDepth { get; private set; } = 6f;
        /// <summary>Open-ocean wave speed used to delay the incoming signal along the edges (m/s).</summary>
        public float TravelSpeed => MathF.Sqrt(Gravity * FlowSpeed * ReferenceDepth);

        /// <summary>Largest change in any cell's depth during the last step (m). ~0 when the water is at rest.</summary>
        public float MaxDepthChange { get; private set; }
        /// <summary>Incremented whenever the open-ocean surface (incoming wave / drawback) changes.</summary>
        public int OpenOceanVersion { get; private set; }

        /// <summary>Net volume (m^3) exchanged with the ocean boundary since the last reset. Diagnostic only.</summary>
        public double BoundaryVolumeExchange { get; private set; }

        const int HistorySize = 4096; // power of two; ~68 s at 60 Hz
        readonly float[] surgeHistory = new float[HistorySize];
        int historyHead;
        float lastDt = 1f / 60f;
        float sourceBlend;
        bool wasOceanActive;

        public ShallowWaterSolver(int size, float cellSize)
        {
            if (size < 3) throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            CellSize = cellSize;
            int count = size * size;
            Depth = new float[count];
            Speed = new float[count];
            VelocityX = new float[count];
            VelocityZ = new float[count];
            qx = new float[count];
            qz = new float[count];
            outflowScale = new float[count];
            spongeWeight = new float[count];
            spongeSide = new BoundarySide[count];
            const int spongeCells = 5;
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int e = z; BoundarySide side = BoundarySide.South;
                    if (size - 1 - z < e) { e = size - 1 - z; side = BoundarySide.North; }
                    if (x < e) { e = x; side = BoundarySide.West; }
                    if (size - 1 - x < e) { e = size - 1 - x; side = BoundarySide.East; }
                    float w = 1f - (float)e / spongeCells;
                    spongeWeight[z * size + x] = w > 0f ? w * w : 0f;
                    spongeSide[z * size + x] = side;
                }
            }
        }

        /// <summary>
        /// Removes all water and fluxes, then fills cells below sea level that are connected to the grid edge
        /// (open ocean). Inland depressions below sea level stay dry.
        /// </summary>
        public void ResetToSeaLevel(float[] terrain)
        {
            Array.Clear(Depth, 0, Depth.Length);
            Array.Clear(Speed, 0, Speed.Length);
            Array.Clear(VelocityX, 0, VelocityX.Length);
            Array.Clear(VelocityZ, 0, VelocityZ.Length);
            Array.Clear(qx, 0, qx.Length);
            Array.Clear(qz, 0, qz.Length);
            Array.Clear(surgeHistory, 0, surgeHistory.Length);
            BoundaryVolumeExchange = 0;
            BoundaryLevelOffset = 0f;
            CurrentBoundaryOffset = 0f;
            SourceActive = false;
            SurgeHeight = 0f;
            sourceBlend = 0f;
            MaxDepthChange = 0f;
            OpenOceanVersion++;

            int n = Size;
            double edgeDepth = 0;
            for (int k = 0; k < n; k++)
            {
                edgeDepth += MathF.Max(0f, SeaLevel - terrain[k]) + MathF.Max(0f, SeaLevel - terrain[(n - 1) * n + k])
                           + MathF.Max(0f, SeaLevel - terrain[k * n]) + MathF.Max(0f, SeaLevel - terrain[k * n + n - 1]);
            }
            ReferenceDepth = MathF.Max(1f, (float)(edgeDepth / (4 * n)));

            if (!OpenOceanBoundaries) return;

            var visited = new bool[n * n];
            var queue = new int[n * n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                TryEnqueue(i, 0); TryEnqueue(i, n - 1); TryEnqueue(0, i); TryEnqueue(n - 1, i);
            }
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % n, z = c / n;
                Depth[c] = SeaLevel - terrain[c];
                if (x > 0) TryEnqueue(x - 1, z);
                if (x < n - 1) TryEnqueue(x + 1, z);
                if (z > 0) TryEnqueue(x, z - 1);
                if (z < n - 1) TryEnqueue(x, z + 1);
            }

            void TryEnqueue(int x, int z)
            {
                int c = z * n + x;
                if (visited[c] || terrain[c] >= SeaLevel) return;
                visited[c] = true;
                queue[tail++] = c;
            }
        }

        public void Step(float[] terrain, float dt)
        {
            int n = Size;
            float area = CellSize * CellSize;
            float gain = dt * Gravity * FlowSpeed;
            float frictionDt = Friction * dt;
            float landDragDt = LandDrag * dt;
            float baseDragDt = BaseDrag * dt;
            float sea = SeaLevel;
            lastDt = dt;

            historyHead = (historyHead + 1) & (HistorySize - 1);
            surgeHistory[historyHead] = SourceActive ? SurgeHeight : 0f;
            float fade = SourceFadeRate * dt;
            sourceBlend = SourceActive ? MathF.Min(1f, sourceBlend + fade) : MathF.Max(0f, sourceBlend - fade);
            float offsetStep = OffsetRate * dt;
            float offsetDelta = BoundaryLevelOffset - CurrentBoundaryOffset;
            CurrentBoundaryOffset += offsetDelta > offsetStep ? offsetStep : (offsetDelta < -offsetStep ? -offsetStep : offsetDelta);
            if (sourceBlend > 0f || offsetDelta != 0f || wasOceanActive) OpenOceanVersion++;
            wasOceanActive = sourceBlend > 0f || offsetDelta != 0f;
            ApplyBoundaries(terrain, dt);

            // 1) Accelerate each face flux by the surface difference across it.
            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float b = terrain[i];
                    float s = b + Depth[i];
                    if (x < n - 1) qx[i] = Face(qx[i], i + 1);
                    if (z < n - 1) qz[i] = Face(qz[i], i + n);

                    float Face(float q, int j)
                    {
                        float bj = terrain[j];
                        float sj = bj + Depth[j];
                        // Water column through the face: above the higher bed, up to the higher surface.
                        float h = (s > sj ? s : sj) - (b > bj ? b : bj);
                        if (h <= 0f) return 0f;
                        float hf = h > 0.1f ? h : 0.1f;
                        float drag = baseDragDt + frictionDt / (hf * hf) + ((b > bj ? b : bj) > sea ? landDragDt : 0f);
                        return q / (1f + drag) + gain * h * (s - sj);
                    }
                }
            }

            // 2) Absorb outgoing waves near the edges.
            if (OpenOceanBoundaries && SpongeStrength > 0f)
            {
                float k = SpongeStrength * dt;
                for (int i = 0; i < spongeWeight.Length; i++)
                {
                    float w = spongeWeight[i];
                    if (w <= 0f || (sourceBlend > 0f && spongeSide[i] == SourceSide)) continue;
                    float damp = 1f / (1f + k * w);
                    qx[i] *= damp;
                    qz[i] *= damp;
                }
            }

            // 3) A cell cannot send more water than it contains: scale its outgoing fluxes.
            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float o = 0f;
                    if (x < n - 1 && qx[i] > 0f) o += qx[i];
                    if (x > 0 && qx[i - 1] < 0f) o -= qx[i - 1];
                    if (z < n - 1 && qz[i] > 0f) o += qz[i];
                    if (z > 0 && qz[i - n] < 0f) o -= qz[i - n];
                    float outVolume = o * dt;
                    float available = Depth[i] * area;
                    outflowScale[i] = outVolume > available ? available / outVolume : 1f;
                }
            }
            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    if (x < n - 1) qx[i] *= qx[i] > 0f ? outflowScale[i] : outflowScale[i + 1];
                    if (z < n - 1) qz[i] *= qz[i] > 0f ? outflowScale[i] : outflowScale[i + n];
                }
            }

            // 4) Move water and estimate velocity (for foam).
            float invArea = 1f / area;
            float maxChange = 0f;
            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float west = x > 0 ? qx[i - 1] : 0f;
                    float east = x < n - 1 ? qx[i] : 0f;
                    float south = z > 0 ? qz[i - n] : 0f;
                    float north = z < n - 1 ? qz[i] : 0f;

                    float oldDepth = Depth[i];
                    float newDepth = oldDepth + dt * (west - east + south - north) * invArea;
                    if (newDepth < 0f) newDepth = 0f; // only float round-off can reach here
                    Depth[i] = newDepth;
                    float change = newDepth - oldDepth;
                    if (change < 0f) change = -change;
                    if (change > maxChange) maxChange = change;

                    float avg = 0.5f * (oldDepth + newDepth);
                    if (avg > WetThreshold)
                    {
                        float fx = 0.5f * (west + east);
                        float fz = 0.5f * (south + north);
                        // Thin films would give huge, meaningless speeds; floor the depth used here.
                        float inv = 1f / (CellSize * MathF.Max(avg, 0.25f));
                        float vx = fx * inv, vz = fz * inv;
                        float v = MathF.Sqrt(vx * vx + vz * vz);
                        if (v > MaxReportedSpeed) { float k = MaxReportedSpeed / v; vx *= k; vz *= k; v = MaxReportedSpeed; }
                        Speed[i] = v;
                        VelocityX[i] = vx;
                        VelocityZ[i] = vz;
                    }
                    else
                    {
                        Speed[i] = VelocityX[i] = VelocityZ[i] = 0f;
                    }
                }
            }
            MaxDepthChange = maxChange;
        }

        /// <summary>
        /// Wave height (m, relative to the boundary level) at a point that is <paramref name="distance"/> metres
        /// inland from the source edge (negative = further out at sea) and <paramref name="along01"/> along it.
        /// Used by the boundary cells and by the surrounding ocean mesh, so both always agree.
        /// </summary>
        public float IncomingWaveAt(float distance, float along01)
        {
            if (sourceBlend <= 0f) return 0f;
            if (along01 < SourceStart01 - 1e-4f || along01 > SourceEnd01 + 1e-4f) return 0f;
            float w = 1f;
            if (SourceEdgeTaper > 0f)
            {
                float span = (SourceEnd01 - SourceStart01) * SourceEdgeTaper;
                if (span > 1e-4f)
                {
                    float e = MathF.Min(along01 - SourceStart01, SourceEnd01 - along01) / span;
                    if (e < 1f) { e = e < 0f ? 0f : e; w = e * e * (3f - 2f * e); }
                    if (w <= 0f) return 0f;
                }
            }
            if (distance > 0f)
            {
                float reach = SideSurgeReach * (Size - 1) * CellSize;
                w *= reach > 0f ? 1f - distance / reach : 0f;
                if (w <= 0f) return 0f;
            }
            float delay = (distance + ApproachDistance) / TravelSpeed;
            int steps = delay > 0f ? (int)(delay / lastDt + 0.5f) : 0;
            if (steps > HistorySize - 1) steps = HistorySize - 1;
            return surgeHistory[(historyHead - steps) & (HistorySize - 1)] * w * sourceBlend;
        }

        /// <summary>Distance (m) inland from the source edge and position along it (0..1) for a grid point.</summary>
        public void SourceFrame(float gx, float gz, out float distance, out float along01)
        {
            float last = Size - 1;
            switch (SourceSide)
            {
                case BoundarySide.North: distance = (last - gz) * CellSize; along01 = gx / last; break;
                case BoundarySide.West: distance = gx * CellSize; along01 = gz / last; break;
                case BoundarySide.East: distance = (last - gx) * CellSize; along01 = gz / last; break;
                default: distance = gz * CellSize; along01 = gx / last; break;
            }
        }

        void ApplyBoundaries(float[] terrain, float dt)
        {
            int n = Size;
            float baseLevel = SeaLevel + CurrentBoundaryOffset;
            float area = CellSize * CellSize;

            if (OpenOceanBoundaries)
            {
                for (int k = 0; k < n; k++)
                {
                    Hold(k, 0, true);
                    Hold(k, n - 1, true);
                    Hold(0, k, true);
                    Hold(n - 1, k, true);
                }
            }

            if (sourceBlend <= 0f || SourceBandCells <= 0) return;
            int band = Math.Min(SourceBandCells, n - 2);
            for (int along = 1; along < n - 1; along++)
            {
                for (int inward = 1; inward <= band; inward++)
                {
                    int x, z;
                    switch (SourceSide)
                    {
                        case BoundarySide.North: x = along; z = n - 1 - inward; break;
                        case BoundarySide.West: x = inward; z = along; break;
                        case BoundarySide.East: x = n - 1 - inward; z = along; break;
                        default: x = along; z = inward; break;
                    }
                    Hold(x, z, false);
                }
            }

            void Hold(int x, int z, bool isEdge)
            {
                int i = z * n + x;
                SourceFrame(x, z, out float distance, out float along01);
                float target = baseLevel + IncomingWaveAt(distance, along01);
                float surface = terrain[i] + Depth[i];
                float delta = target - surface;
                if (!isEdge && delta <= 0f) return; // interior source cells only ever add water
                // Edges follow the (already smooth) ocean signal exactly. Rate-limiting here starves big waves:
                // a 14 m crest needs metres of extra depth per step at the edge.
                float d = Depth[i] + delta;
                if (d < 0f) d = 0f;
                BoundaryVolumeExchange += (d - Depth[i]) * area;
                Depth[i] = d;
            }
        }

        public double TotalVolume()
        {
            double sum = 0;
            for (int i = 0; i < Depth.Length; i++) sum += Depth[i];
            return sum * CellSize * CellSize;
        }
    }

    public enum BoundarySide
    {
        South,
        North,
        West,
        East
    }
}
