using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tsunami
{
    /// <summary>Shape of the initial sea-surface disturbance.</summary>
    public enum SourceProfile
    {
        /// <summary>Single raised hump (smooth Gaussian).</summary>
        Gaussian,
        /// <summary>Leading trough followed by a crest (the sea first draws back at the coast).</summary>
        NWave
    }

    /// <summary>Initial flow of the disturbance.</summary>
    public enum SourceMotion
    {
        /// <summary>Water starts at rest: the disturbance spreads out in all directions.</summary>
        Radial,
        /// <summary>Water starts moving with the wave (long-wave relation u = eta * sqrt(g/h)): one main direction.</summary>
        Directional
    }

    /// <summary>Initial-condition description of a tsunami source (all lengths in metres, grid-local XZ).</summary>
    [Serializable]
    public struct TsunamiSource
    {
        public float centerX, centerZ;
        /// <summary>Gaussian radius along the travel direction (m).</summary>
        public float lengthRadius;
        /// <summary>Gaussian radius across the travel direction (m); larger = longer wave front.</summary>
        public float widthRadius;
        /// <summary>Travel direction (unit vector in XZ).</summary>
        public float dirX, dirZ;
        /// <summary>Peak surface displacement (m). Negative values start with a depression.</summary>
        public float amplitude;
        public SourceProfile profile;
        public SourceMotion motion;
    }

    /// <summary>
    /// A long wave coming in from the open sea beyond the domain (grid-local metres, solver time): a Gaussian ridge
    /// travelling at constant speed, its crest growing from <see cref="startShare"/> of the amplitude to the full
    /// amplitude over <see cref="growTime"/> (the far approach). The solver drives the sponge band on the side it
    /// comes from towards this wave instead of the still sea, so the wave flows into the domain as it is; the same
    /// function draws it beyond the domain, so outside and inside are one wave.
    /// </summary>
    [Serializable]
    public struct IncomingWave
    {
        public bool active;
        /// <summary>Crest centre at Time = 0 (grid-local XZ, m).</summary>
        public float startX, startZ;
        /// <summary>Unit travel direction (XZ).</summary>
        public float dirX, dirZ;
        /// <summary>Travel speed (m/s).</summary>
        public float speed;
        /// <summary>Full crest height above the still sea (m).</summary>
        public float amplitude;
        /// <summary>Crest height at Time = 0 as a share of the amplitude.</summary>
        public float startShare;
        /// <summary>Seconds until the crest reaches the full amplitude.</summary>
        public float growTime;
        /// <summary>Gaussian radii along / across the travel direction (m).</summary>
        public float lengthRadius, widthRadius;

        public float CrestHeight(double time)
        {
            float t = growTime > 0f ? (float)Math.Min(1.0, Math.Max(0.0, time / growTime)) : 1f;
            return amplitude * (startShare + (1f - startShare) * t * t);
        }

        public void CrestCentre(double time, out float x, out float z)
        {
            float d = speed * (float)time;
            x = startX + dirX * d;
            z = startZ + dirZ * d;
        }

        /// <summary>Surface elevation above the still sea at a grid-local point (m).</summary>
        public float Elevation(float x, float z, double time)
        {
            if (!active) return 0f;
            CrestCentre(time, out float cx, out float cz);
            float px = x - cx, pz = z - cz;
            float a = (px * dirX + pz * dirZ) / Math.Max(1f, lengthRadius);
            float b = (-px * dirZ + pz * dirX) / Math.Max(1f, widthRadius);
            float e = 0.5f * (a * a + b * b);
            return e > 30f ? 0f : CrestHeight(time) * MathF.Exp(-e);
        }

        /// <summary>Depth-averaged flow speed of the wave over still depth <paramref name="depth"/> (exact simple wave).</summary>
        public static float FlowSpeed(float eta, float depth, float gravity)
        {
            if (depth <= 0f) return 0f;
            return 2f * (MathF.Sqrt(gravity * Math.Max(0f, depth + eta)) - MathF.Sqrt(gravity * depth));
        }
    }

    /// <summary>
    /// Depth-averaged 2D shallow-water solver (Saint-Venant equations):
    ///   dh/dt + div(h u) = 0
    ///   d(h u)/dt + div(h u (x) u + g h^2 / 2 I) = -g h grad(z_b) - friction
    /// Discretisation: cell-centred finite volumes on a uniform grid, second-order MUSCL reconstruction
    /// (minmod limiter, first order next to dry cells and walls), HLL approximate Riemann solver, Audusse et al.
    /// hydrostatic reconstruction (well-balanced: a lake at rest stays at rest over any bathymetry; keeps depths
    /// non-negative at moving shorelines), Heun/SSP-RK2 time integration with a CFL-limited time step and
    /// semi-implicit Manning bottom friction.
    ///
    /// Thin walls on cell faces (player-built walls) enter the hydrostatic reconstruction as a raised crest:
    /// water only passes where its surface is above the wall top, like a weir.
    ///
    /// This is an illustrative model, not a validated tsunami forecasting code (no dispersion, no wave breaking
    /// physics, simplified friction, limited resolution, approximate open boundaries).
    /// Pure C# with optional multi-threading (no UnityEngine dependency) so it can be tested outside Unity.
    /// </summary>
    public sealed class TsunamiPhysicsSolver
    {
        public readonly int Size;
        public readonly float CellSize;

        /// <summary>Water depth h (m) per cell, row-major (index = z * Size + x).</summary>
        public readonly float[] Depth;
        /// <summary>Depth-integrated flow h*u and h*v (m^2/s).</summary>
        public readonly float[] Qx, Qz;
        /// <summary>Depth-averaged velocity (m/s) and its magnitude; updated after every step.</summary>
        public readonly float[] VelocityX, VelocityZ, Speed;
        /// <summary>Cells covered by the still sea at reset (ocean connected to the domain edge).</summary>
        public readonly bool[] InitiallyWet;
        /// <summary>Thin walls: top height on the face between cell i and i+1 (BarrierX) or i and i+n (BarrierZ).</summary>
        public readonly float[] BarrierX, BarrierZ;
        public readonly bool[] SealedBarrierX, SealedBarrierZ;
        /// <summary>Per cell: which faces carry a wall (1 = -x, 2 = +x, 4 = -z, 8 = +z). For rendering.</summary>
        public readonly byte[] BarrierMask;

        // ---- Physical / numerical settings (validated in Step) ----
        public float Gravity = 9.81f;
        /// <summary>Manning roughness of the sea floor (s/m^(1/3)).</summary>
        public float SeaManning = 0.025f;
        /// <summary>Manning roughness of land above sea level (s/m^(1/3)); higher = floods slow down sooner.</summary>
        public float LandManning = 0.06f;
        /// <summary>Courant number. 2D positivity of this scheme needs about 0.25-0.4.</summary>
        public float Cfl = 0.3f;
        /// <summary>Depth below which a cell is treated as dry (no velocity).</summary>
        public float DryDepth = 1e-3f;
        /// <summary>Depth below which reconstruction falls back to first order (shoreline robustness).</summary>
        public float ShorelineDepth = 0.05f;
        /// <summary>Velocity safety cap (m/s) against thin-film spikes.</summary>
        public float MaxVelocity = 40f;
        public bool SecondOrder = true;
        public float SeaLevel;
        /// <summary>True: open sea at the domain edge with an absorbing sponge. False: solid walls.</summary>
        public bool OpenBoundaries = true;
        /// <summary>Relaxation rate (1/s) towards the still sea inside the sponge band at the domain edge.</summary>
        public float SpongeStrength = 1.5f;
        public int SpongeCells = 6;
        /// <summary>
        /// Extra relaxation rate (1/s) towards <see cref="Incoming"/> in the sponge band on the side the wave comes
        /// from: strong enough to carry the wave into the domain at its full height.
        /// </summary>
        public float IncomingRelaxation = 30f;
        /// <summary>Wave coming in from beyond the domain (inactive after a reset).</summary>
        public IncomingWave Incoming;
        public bool UseThreads = true;

        public double Time { get; private set; }
        public float LastTimestep { get; private set; }
        public float MaxDepthChange { get; private set; }
        /// <summary>Changes whenever the still-water state is rebuilt (reset / sea level change).</summary>
        public int OpenOceanVersion { get; private set; }
        /// <summary>Total volume added by sources since the last reset (m^3).</summary>
        public double SourceVolume { get; private set; }

        float[] bed; // current bathymetry (set every step)
        readonly float[] stillDepth;
        readonly float[] h0, qx0, qz0;            // RK stage start
        readonly float[] pU, pV, pEta;             // primitives
        readonly float[] sxH, sxEta, sxU, sxV;     // limited x-slopes
        readonly float[] szH, szEta, szU, szV;     // limited z-slopes
        readonly float[] fxM, fxL, fxR, fxT;       // x faces: (Size+1) per row, index z*(Size+1)+k, face k between x=k-1 and k
        readonly float[] fzM, fzL, fzR, fzT;       // z faces: Size per row, index k*Size+x, face k between z=k-1 and k
        readonly float[] rowA, rowB;               // per-row reductions
        readonly float[] spongeWeight;
        readonly float[] staticBarrierX, staticBarrierZ;
        readonly Dictionary<int, List<OwnedBarrier>> barriersByOwner = new Dictionary<int, List<OwnedBarrier>>();
        readonly Dictionary<BarrierFaceKey, List<OwnedBarrier>> barriersByFace = new Dictionary<BarrierFaceKey, List<OwnedBarrier>>();

        sealed class OwnedBarrier
        {
            public int owner;
            public BarrierFaceKey face;
            public float top;
            public bool impermeable;
        }

        readonly struct BarrierFaceKey : IEquatable<BarrierFaceKey>
        {
            public readonly int x, z;
            public readonly bool alongX;
            public BarrierFaceKey(int x, int z, bool alongX) { this.x = x; this.z = z; this.alongX = alongX; }
            public bool Equals(BarrierFaceKey other) => x == other.x && z == other.z && alongX == other.alongX;
            public override bool Equals(object obj) => obj is BarrierFaceKey other && Equals(other);
            public override int GetHashCode() => ((z * 397) ^ x) * 2 + (alongX ? 1 : 0);
        }

        public TsunamiPhysicsSolver(int size, float cellSize)
        {
            if (size < 4) throw new ArgumentOutOfRangeException(nameof(size));
            if (!(cellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Size = size;
            CellSize = cellSize;
            int c = size * size;
            Depth = new float[c]; Qx = new float[c]; Qz = new float[c];
            VelocityX = new float[c]; VelocityZ = new float[c]; Speed = new float[c];
            InitiallyWet = new bool[c];
            BarrierX = new float[c]; BarrierZ = new float[c]; BarrierMask = new byte[c];
            SealedBarrierX = new bool[c]; SealedBarrierZ = new bool[c];
            stillDepth = new float[c];
            h0 = new float[c]; qx0 = new float[c]; qz0 = new float[c];
            pU = new float[c]; pV = new float[c]; pEta = new float[c];
            sxH = new float[c]; sxEta = new float[c]; sxU = new float[c]; sxV = new float[c];
            szH = new float[c]; szEta = new float[c]; szU = new float[c]; szV = new float[c];
            int faces = (size + 1) * size;
            fxM = new float[faces]; fxL = new float[faces]; fxR = new float[faces]; fxT = new float[faces];
            fzM = new float[faces]; fzL = new float[faces]; fzR = new float[faces]; fzT = new float[faces];
            rowA = new float[size]; rowB = new float[size];
            spongeWeight = new float[c];
            staticBarrierX = new float[c]; staticBarrierZ = new float[c];
            ClearBarriers();
            BuildSponge();
        }

        // ------------------------------------------------------------------ setup

        void BuildSponge()
        {
            int n = Size;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int e = Math.Min(Math.Min(x, z), Math.Min(n - 1 - x, n - 1 - z));
                    float w = SpongeCells > 0 ? 1f - (float)e / SpongeCells : 0f;
                    spongeWeight[z * n + x] = w > 0f ? w * w : 0f;
                }
        }

        /// <summary>
        /// Removes all motion and fills the sea that is connected to the domain edge up to SeaLevel
        /// (inland hollows below sea level stay dry). Walls are kept.
        /// </summary>
        public void ResetToSeaLevel(float[] terrain)
        {
            if (terrain == null || terrain.Length != Size * Size) throw new ArgumentException("terrain size");
            bed = terrain;
            int n = Size, c = n * n;
            Array.Clear(Depth, 0, c); Array.Clear(Qx, 0, c); Array.Clear(Qz, 0, c);
            Array.Clear(VelocityX, 0, c); Array.Clear(VelocityZ, 0, c); Array.Clear(Speed, 0, c);
            Array.Clear(InitiallyWet, 0, c);
            var queue = new int[c];
            int head = 0, tail = 0;
            for (int k = 0; k < n; k++)
            {
                Seed(k, 0); Seed(k, n - 1); Seed(0, k); Seed(n - 1, k);
            }
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % n, z = i / n;
                Depth[i] = SeaLevel - terrain[i];
                if (x > 0 && (BarrierMask[i] & 1) == 0) Seed(x - 1, z);
                if (x < n - 1 && (BarrierMask[i] & 2) == 0) Seed(x + 1, z);
                if (z > 0 && (BarrierMask[i] & 4) == 0) Seed(x, z - 1);
                if (z < n - 1 && (BarrierMask[i] & 8) == 0) Seed(x, z + 1);
            }
            Array.Copy(Depth, stillDepth, c);
            Incoming = default;
            Time = 0;
            SourceVolume = 0;
            MaxDepthChange = 1f; // force one render update
            OpenOceanVersion++;

            void Seed(int x, int z)
            {
                int i = z * n + x;
                if (InitiallyWet[i] || terrain[i] >= SeaLevel) return;
                InitiallyWet[i] = true;
                queue[tail++] = i;
            }
        }

        /// <summary>
        /// Water on land (every cell that is dry when the sea is at rest) seeps into the ground: each such cell keeps
        /// (1 - fraction) of its water, and its flow slows with it. The sea itself is not touched.
        /// </summary>
        public void DrainLand(float fraction)
        {
            float keep = 1f - Math.Clamp(fraction, 0f, 1f);
            for (int i = 0; i < Depth.Length; i++)
            {
                if (InitiallyWet[i] || Depth[i] <= 0f) continue;
                float h = Depth[i] * keep;
                if (h < DryDepth)
                {
                    Depth[i] = 0f;
                    Qx[i] = Qz[i] = 0f;
                    VelocityX[i] = VelocityZ[i] = Speed[i] = 0f;
                    continue;
                }
                Depth[i] = h;
                Qx[i] *= keep;
                Qz[i] *= keep;
            }
            MaxDepthChange = 1f; // redraw the surface
        }

        /// <summary>
        /// Adds an initial sea-surface disturbance (only on wet sea cells) and, for directional sources, the
        /// matching long-wave flow. Returns the added volume (m^3; an N-wave adds ~0).
        /// </summary>
        public double AddSource(TsunamiSource s)
        {
            int n = Size;
            float half = (n - 1) * 0.5f;
            float dl = Math.Max(1f, s.lengthRadius), dw = Math.Max(1f, s.widthRadius);
            float len = MathF.Sqrt(s.dirX * s.dirX + s.dirZ * s.dirZ);
            float ax = len > 1e-6f ? s.dirX / len : 1f, az = len > 1e-6f ? s.dirZ / len : 0f;
            double added = 0;
            float area = CellSize * CellSize;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float h = Depth[i];
                    if (h <= DryDepth) continue;
                    float px = (x - half) * CellSize - s.centerX, pz = (z - half) * CellSize - s.centerZ;
                    float along = px * ax + pz * az;      // + = ahead in the travel direction
                    float across = -px * az + pz * ax;
                    float g2 = (across * across) / (2f * dw * dw);
                    float eta;
                    if (s.profile == SourceProfile.NWave)
                    {
                        // Trough ahead (towards the coast), crest behind: the sea draws back first.
                        float a1 = (along + dl) / dl, a2 = (along - dl) / dl;
                        eta = s.amplitude * (MathF.Exp(-0.5f * a1 * a1 - g2) - 0.7f * MathF.Exp(-0.5f * a2 * a2 - g2));
                    }
                    else
                    {
                        float a = along / dl;
                        eta = s.amplitude * MathF.Exp(-0.5f * a * a - g2);
                    }
                    if (MathF.Abs(eta) < 1e-4f) continue;
                    float hn = h + eta;
                    if (hn < DryDepth) { eta = DryDepth - h; hn = DryDepth; }
                    Depth[i] = hn;
                    added += eta * area;
                    if (s.motion == SourceMotion.Directional)
                    {
                        // Simple-wave relation for a long wave travelling in +a: u = eta * sqrt(g / h).
                        float u = eta * MathF.Sqrt(Gravity / MathF.Max(h, 0.5f));
                        Qx[i] += hn * u * ax;
                        Qz[i] += hn * u * az;
                    }
                }
            }
            SourceVolume += added;
            UpdateDerived(0, n);
            MaxDepthChange = 1f;
            return added;
        }

        // ------------------------------------------------------------------ walls

        public void ClearBarriers()
        {
            barriersByOwner.Clear();
            barriersByFace.Clear();
            for (int i = 0; i < BarrierX.Length; i++)
            {
                staticBarrierX[i] = float.NegativeInfinity;
                staticBarrierZ[i] = float.NegativeInfinity;
                BarrierX[i] = float.NegativeInfinity;
                BarrierZ[i] = float.NegativeInfinity;
                BarrierMask[i] = 0;
                SealedBarrierX[i] = SealedBarrierZ[i] = false;
            }
        }

        /// <summary>
        /// Adds or raises a persistent, unowned barrier on a face. Owned player walls compose with this baseline;
        /// removing an owner never removes this contribution. ClearBarriers removes both kinds.
        /// </summary>
        public void SetBarrier(int x, int z, bool alongX, float top)
        {
            if (!ValidFace(x, z, alongX)) return;
            int i = z * Size + x;
            float[] baseline = alongX ? staticBarrierX : staticBarrierZ;
            if (top <= baseline[i]) return;
            baseline[i] = top;
            RecomputeFace(new BarrierFaceKey(x, z, alongX));
        }

        /// <summary>Adds one owner contribution. An owner can cover multiple faces and is removed atomically.</summary>
        public bool SetOwnedBarrier(int owner, int x, int z, bool alongX, float top, bool impermeable = false)
        {
            if (owner <= 0 || !ValidFace(x, z, alongX)) return false;
            var key = new BarrierFaceKey(x, z, alongX);
            if (!barriersByOwner.TryGetValue(owner, out List<OwnedBarrier> ownerFaces))
            {
                ownerFaces = new List<OwnedBarrier>(2);
                barriersByOwner.Add(owner, ownerFaces);
            }
            for (int i = 0; i < ownerFaces.Count; i++)
            {
                OwnedBarrier existing = ownerFaces[i];
                if (!existing.face.Equals(key)) continue;
                if (top <= existing.top && existing.impermeable == impermeable) return true;
                existing.top = MathF.Max(top, existing.top);
                existing.impermeable = impermeable;
                RecomputeFace(key);
                return true;
            }

            var contribution = new OwnedBarrier { owner = owner, face = key, top = top, impermeable = impermeable };
            ownerFaces.Add(contribution);
            if (!barriersByFace.TryGetValue(key, out List<OwnedBarrier> faceOwners))
            {
                faceOwners = new List<OwnedBarrier>(2);
                barriersByFace.Add(key, faceOwners);
            }
            faceOwners.Add(contribution);
            RecomputeFace(key);
            return true;
        }

        /// <summary>Removes all faces owned by owner while preserving static and other-owner crests.</summary>
        public bool RemoveBarrierOwner(int owner)
        {
            if (!barriersByOwner.TryGetValue(owner, out List<OwnedBarrier> owned)) return false;
            barriersByOwner.Remove(owner);
            for (int i = 0; i < owned.Count; i++)
            {
                OwnedBarrier contribution = owned[i];
                if (barriersByFace.TryGetValue(contribution.face, out List<OwnedBarrier> faceOwners))
                {
                    faceOwners.Remove(contribution);
                    if (faceOwners.Count == 0) barriersByFace.Remove(contribution.face);
                }
                RecomputeFace(contribution.face);
            }
            return true;
        }

        /// <summary>Removes every owned contribution but leaves persistent SetBarrier baselines intact.</summary>
        public void ClearBarrierOwners()
        {
            barriersByOwner.Clear();
            barriersByFace.Clear();
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    int i = z * Size + x;
                    if (x < Size - 1) ApplyEffective(new BarrierFaceKey(x, z, true), staticBarrierX[i]);
                    if (z < Size - 1) ApplyEffective(new BarrierFaceKey(x, z, false), staticBarrierZ[i]);
                }
        }

        void RecomputeFace(BarrierFaceKey key)
        {
            int i = key.z * Size + key.x;
            float top = key.alongX ? staticBarrierX[i] : staticBarrierZ[i];
            bool impermeable = false;
            if (barriersByFace.TryGetValue(key, out List<OwnedBarrier> owners))
                for (int k = 0; k < owners.Count; k++)
                { if (owners[k].top > top) top = owners[k].top; impermeable |= owners[k].impermeable; }
            ApplyEffective(key, top, impermeable);
        }

        void ApplyEffective(BarrierFaceKey key, float top, bool impermeable = false)
        {
            int i = key.z * Size + key.x;
            (key.alongX ? SealedBarrierX : SealedBarrierZ)[i] = impermeable;
            if (key.alongX)
            {
                BarrierX[i] = top;
                if (float.IsNegativeInfinity(top)) { BarrierMask[i] &= 253; BarrierMask[i + 1] &= 254; }
                else { BarrierMask[i] |= 2; BarrierMask[i + 1] |= 1; }
            }
            else
            {
                BarrierZ[i] = top;
                if (float.IsNegativeInfinity(top)) { BarrierMask[i] &= 247; BarrierMask[i + Size] &= 251; }
                else { BarrierMask[i] |= 8; BarrierMask[i + Size] |= 4; }
            }
        }

        bool ValidFace(int x, int z, bool alongX)
        {
            return x >= 0 && z >= 0 && x < Size && z < Size && (alongX ? x < Size - 1 : z < Size - 1);
        }

        /// <summary>Returns true when the addressed internal face currently carries a barrier.</summary>
        public bool HasBarrier(int x, int z, bool alongX)
        {
            if (x < 0 || z < 0 || x >= Size || z >= Size) return false;
            if (alongX)
                return x < Size - 1 && !float.IsNegativeInfinity(BarrierX[z * Size + x]);
            return z < Size - 1 && !float.IsNegativeInfinity(BarrierZ[z * Size + x]);
        }

        // ------------------------------------------------------------------ time stepping

        /// <summary>Largest stable time step for the current state (CFL condition).</summary>
        public float StableTimestep(float[] terrain)
        {
            bed = terrain;
            For(0, Size, MaxWaveSpeedRows);
            float smax = 0f;
            for (int z = 0; z < Size; z++) if (rowA[z] > smax) smax = rowA[z];
            float cfl = Clamp(Cfl, 0.05f, 0.45f);
            return smax > 1e-6f ? cfl * CellSize / smax : 1f;
        }

        /// <summary>
        /// Advances the solution by one step no longer than <paramref name="maxDt"/> and no longer than the CFL
        /// limit. Returns the step actually taken (s).
        /// </summary>
        public float Step(float[] terrain, float maxDt)
        {
            if (terrain == null || terrain.Length != Size * Size) throw new ArgumentException("terrain size");
            bed = terrain;
            float dt = MathF.Min(maxDt, StableTimestep(terrain));
            if (!(dt > 0f)) return 0f;
            int n = Size, c = n * n;

            Array.Copy(Depth, h0, c); Array.Copy(Qx, qx0, c); Array.Copy(Qz, qz0, c);
            // Heun (SSP-RK2): U1 = U0 + dt L(U0); U2 = U1 + dt L(U1); U = (U0 + U2) / 2.
            Stage(dt);
            Stage(dt);
            stageDt = dt;
            forceTime = Time + dt;
            For(0, n, AverageAndFinishRows);

            float m = 0f;
            for (int z = 0; z < n; z++) if (rowB[z] > m) m = rowB[z];
            MaxDepthChange = m;
            Time += dt;
            LastTimestep = dt;
            return dt;
        }

        float stageDt;
        double forceTime; // solver time at the end of the current step (the incoming wave is imposed there)

        void Stage(float dt)
        {
            stageDt = dt;
            int n = Size;
            For(0, n, PrimitiveRows);
            For(0, n, SlopeRows);
            For(0, n, XFaceRows);
            For(0, n + 1, ZFaceRows);
            For(0, n, UpdateRows);
        }

        void For(int from, int to, Action<int, int> body)
        {
            int count = to - from;
            if (!UseThreads || count < 32)
            {
                body(from, to);
                return;
            }
            int chunks = Math.Min(Environment.ProcessorCount * 2, count / 8);
            if (chunks < 2) { body(from, to); return; }
            int per = (count + chunks - 1) / chunks;
            Parallel.For(0, chunks, k =>
            {
                int a = from + k * per;
                int b = Math.Min(to, a + per);
                if (a < b) body(a, b);
            });
        }

        void MaxWaveSpeedRows(int z0, int z1)
        {
            int n = Size;
            float g = Gravity;
            for (int z = z0; z < z1; z++)
            {
                float m = 0f;
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = Depth[i];
                    if (h <= DryDepth) continue;
                    float c = MathF.Sqrt(g * h);
                    float u = MathF.Abs(Qx[i] / h), v = MathF.Abs(Qz[i] / h);
                    float s = (u > v ? u : v) + c;
                    if (s > m) m = s;
                }
                rowA[z] = m;
            }
        }

        void PrimitiveRows(int z0, int z1)
        {
            int n = Size;
            float dry = DryDepth, vmax = MaxVelocity;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = Depth[i];
                    pEta[i] = bed[i] + h;
                    if (h > dry)
                    {
                        float u = Qx[i] / h, v = Qz[i] / h;
                        pU[i] = u > vmax ? vmax : (u < -vmax ? -vmax : u);
                        pV[i] = v > vmax ? vmax : (v < -vmax ? -vmax : v);
                    }
                    else { pU[i] = 0f; pV[i] = 0f; }
                }
            }
        }

        static float Minmod(float a, float b)
        {
            if (a * b <= 0f) return 0f;
            return MathF.Abs(a) < MathF.Abs(b) ? a : b;
        }

        void SlopeRows(int z0, int z1)
        {
            int n = Size;
            bool second = SecondOrder;
            float shore = ShorelineDepth;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    byte walls = BarrierMask[i];
                    float h = Depth[i];
                    // x direction
                    if (second && x > 0 && x < n - 1 && (walls & 3) == 0 &&
                        h > shore && Depth[i - 1] > shore && Depth[i + 1] > shore)
                    {
                        sxH[i] = Minmod(h - Depth[i - 1], Depth[i + 1] - h);
                        sxEta[i] = Minmod(pEta[i] - pEta[i - 1], pEta[i + 1] - pEta[i]);
                        sxU[i] = Minmod(pU[i] - pU[i - 1], pU[i + 1] - pU[i]);
                        sxV[i] = Minmod(pV[i] - pV[i - 1], pV[i + 1] - pV[i]);
                    }
                    else { sxH[i] = 0f; sxEta[i] = 0f; sxU[i] = 0f; sxV[i] = 0f; }
                    // z direction
                    if (second && z > 0 && z < n - 1 && (walls & 12) == 0 &&
                        h > shore && Depth[i - n] > shore && Depth[i + n] > shore)
                    {
                        szH[i] = Minmod(h - Depth[i - n], Depth[i + n] - h);
                        szEta[i] = Minmod(pEta[i] - pEta[i - n], pEta[i + n] - pEta[i]);
                        szU[i] = Minmod(pU[i] - pU[i - n], pU[i + n] - pU[i]);
                        szV[i] = Minmod(pV[i] - pV[i - n], pV[i + n] - pV[i]);
                    }
                    else { szH[i] = 0f; szEta[i] = 0f; szU[i] = 0f; szV[i] = 0f; }
                }
            }
        }

        /// <summary>HLL flux in the face-normal frame (un = normal velocity, ut = tangential velocity).</summary>
        static void Hll(float g, float hL, float unL, float utL, float hR, float unR, float utR,
            out float fm, out float fn, out float ft)
        {
            if (hL <= 0f && hR <= 0f) { fm = 0f; fn = 0f; ft = 0f; return; }
            float cL = MathF.Sqrt(g * hL), cR = MathF.Sqrt(g * hR);
            float sL, sR;
            if (hL <= 0f) { sL = unR - 2f * cR; sR = unR + cR; }
            else if (hR <= 0f) { sL = unL - cL; sR = unL + 2f * cL; }
            else
            {
                sL = MathF.Min(unL - cL, unR - cR);
                sR = MathF.Max(unL + cL, unR + cR);
            }
            float qL = hL * unL, qR = hR * unR;
            float fmL = qL, fnL = qL * unL + 0.5f * g * hL * hL, ftL = qL * utL;
            float fmR = qR, fnR = qR * unR + 0.5f * g * hR * hR, ftR = qR * utR;
            if (sL >= 0f) { fm = fmL; fn = fnL; ft = ftL; return; }
            if (sR <= 0f) { fm = fmR; fn = fnR; ft = ftR; return; }
            float inv = 1f / (sR - sL);
            float sLR = sL * sR;
            fm = (sR * fmL - sL * fmR + sLR * (hR - hL)) * inv;
            fn = (sR * fnL - sL * fnR + sLR * (qR - qL)) * inv;
            ft = (sR * ftL - sL * ftR + sLR * (hR * utR - hL * utL)) * inv;
        }

        void XFaceRows(int z0, int z1)
        {
            int n = Size, stride = n + 1;
            float g = Gravity;
            bool open = OpenBoundaries;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                for (int k = 0; k <= n; k++)
                {
                    float hL, eL, uL, vL, hR, eR, uR, vR, wall = float.NegativeInfinity;
                    if (k > 0)
                    {
                        int a = row + k - 1;
                        hL = Depth[a] + 0.5f * sxH[a]; eL = pEta[a] + 0.5f * sxEta[a];
                        uL = pU[a] + 0.5f * sxU[a]; vL = pV[a] + 0.5f * sxV[a];
                    }
                    else { hL = eL = uL = vL = 0f; }
                    if (k < n)
                    {
                        int b = row + k;
                        hR = Depth[b] - 0.5f * sxH[b]; eR = pEta[b] - 0.5f * sxEta[b];
                        uR = pU[b] - 0.5f * sxU[b]; vR = pV[b] - 0.5f * sxV[b];
                    }
                    else { hR = eR = uR = vR = 0f; }
                    // Ghost states at the domain edge: open (zero gradient) or solid wall (mirrored normal flow).
                    if (k == 0) { hL = hR; eL = eR; vL = vR; uL = open ? uR : -uR; }
                    else if (k == n) { hR = hL; eR = eL; vR = vL; uR = open ? uL : -uL; }
                    else wall = BarrierX[row + k - 1];

                    float zL = eL - hL, zR = eR - hR;
                    float crest = zL > zR ? zL : zR;
                    if (wall > crest) crest = wall;
                    if (k > 0 && k < n && SealedBarrierX[row + k - 1]) crest = float.PositiveInfinity;
                    float hLs = eL - crest; if (hLs < 0f) hLs = 0f;
                    float hRs = eR - crest; if (hRs < 0f) hRs = 0f;
                    Hll(g, hLs, uL, vL, hRs, uR, vR, out float fm, out float fn, out float ft);
                    int f = z * stride + k;
                    fxM[f] = fm;
                    fxT[f] = ft;
                    fxL[f] = fn + 0.5f * g * (hL * hL - hLs * hLs);
                    fxR[f] = fn + 0.5f * g * (hR * hR - hRs * hRs);
                }
            }
        }

        void ZFaceRows(int k0, int k1)
        {
            int n = Size;
            float g = Gravity;
            bool open = OpenBoundaries;
            for (int k = k0; k < k1; k++)
            {
                for (int x = 0; x < n; x++)
                {
                    float hL, eL, uL, vL, hR, eR, uR, vR, wall = float.NegativeInfinity;
                    // Normal = +z: "u" here is the z-velocity, "v" the tangential x-velocity.
                    if (k > 0)
                    {
                        int a = (k - 1) * n + x;
                        hL = Depth[a] + 0.5f * szH[a]; eL = pEta[a] + 0.5f * szEta[a];
                        uL = pV[a] + 0.5f * szV[a]; vL = pU[a] + 0.5f * szU[a];
                    }
                    else { hL = eL = uL = vL = 0f; }
                    if (k < n)
                    {
                        int b = k * n + x;
                        hR = Depth[b] - 0.5f * szH[b]; eR = pEta[b] - 0.5f * szEta[b];
                        uR = pV[b] - 0.5f * szV[b]; vR = pU[b] - 0.5f * szU[b];
                    }
                    else { hR = eR = uR = vR = 0f; }
                    if (k == 0) { hL = hR; eL = eR; vL = vR; uL = open ? uR : -uR; }
                    else if (k == n) { hR = hL; eR = eL; vR = vL; uR = open ? uL : -uL; }
                    else wall = BarrierZ[(k - 1) * n + x];

                    float zL = eL - hL, zR = eR - hR;
                    float crest = zL > zR ? zL : zR;
                    if (wall > crest) crest = wall;
                    if (k > 0 && k < n && SealedBarrierZ[(k - 1) * n + x]) crest = float.PositiveInfinity;
                    float hLs = eL - crest; if (hLs < 0f) hLs = 0f;
                    float hRs = eR - crest; if (hRs < 0f) hRs = 0f;
                    Hll(g, hLs, uL, vL, hRs, uR, vR, out float fm, out float fn, out float ft);
                    int f = k * n + x;
                    fzM[f] = fm;
                    fzT[f] = ft;
                    fzL[f] = fn + 0.5f * g * (hL * hL - hLs * hLs);
                    fzR[f] = fn + 0.5f * g * (hR * hR - hRs * hRs);
                }
            }
        }

        void UpdateRows(int z0, int z1)
        {
            int n = Size, stride = n + 1;
            float dt = stageDt, inv = 1f / CellSize, g = Gravity, dry = DryDepth;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    int w = z * stride + x, e = w + 1;      // west / east x-faces
                    int s = z * n + x, nn = s + n;          // south / north z-faces
                    float h = Depth[i];
                    // Well-balanced centred bed-slope source: -g h dz/dx with dz = d(eta) - d(h) (reconstructed).
                    float sx = -g * h * (sxEta[i] - sxH[i]) * inv;
                    float sz = -g * h * (szEta[i] - szH[i]) * inv;
                    float dh = -((fxM[e] - fxM[w]) + (fzM[nn] - fzM[s])) * inv;
                    float dqx = -((fxL[e] - fxR[w]) + (fzT[nn] - fzT[s])) * inv + sx;
                    float dqz = -((fxT[e] - fxT[w]) + (fzL[nn] - fzR[s])) * inv + sz;
                    float hn = h + dt * dh;
                    float qxn = Qx[i] + dt * dqx, qzn = Qz[i] + dt * dqz;
                    if (hn <= dry)
                    {
                        if (hn < 0f) hn = 0f;
                        qxn = 0f; qzn = 0f;
                    }
                    Depth[i] = hn; Qx[i] = qxn; Qz[i] = qzn;
                }
            }
        }

        void AverageAndFinishRows(int z0, int z1)
        {
            int n = Size;
            float dt = stageDt, g = Gravity, dry = DryDepth, vmax = MaxVelocity, sea = SeaLevel;
            float n2Sea = SeaManning * SeaManning, n2Land = LandManning * LandManning;
            bool sponge = OpenBoundaries && SpongeStrength > 0f;
            float k = SpongeStrength * dt;
            bool incoming = OpenBoundaries && Incoming.active;
            float kIn = (SpongeStrength + IncomingRelaxation) * dt;
            float half = (n - 1) * 0.5f;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                float change = 0f;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = 0.5f * (h0[i] + Depth[i]);
                    float qx = 0.5f * (qx0[i] + Qx[i]);
                    float qz = 0.5f * (qz0[i] + Qz[i]);
                    if (h <= dry)
                    {
                        if (h < 0f) h = 0f;
                        qx = 0f; qz = 0f;
                    }
                    else
                    {
                        // Semi-implicit Manning friction: tau = g n^2 |u| / h^(4/3).
                        float u = qx / h, v = qz / h;
                        float sp = MathF.Sqrt(u * u + v * v);
                        float n2 = bed[i] > sea ? n2Land : n2Sea;
                        float tau = g * n2 * sp / MathF.Pow(h, 4f / 3f);
                        float f = 1f / (1f + dt * tau);
                        qx *= f; qz *= f;
                        // Velocity safety cap (thin films on steep slopes).
                        float cap = vmax * h;
                        float qm = MathF.Sqrt(qx * qx + qz * qz);
                        if (qm > cap) { float r = cap / qm; qx *= r; qz *= r; }
                    }
                    if (sponge || incoming)
                    {
                        float wgt = spongeWeight[i];
                        if (wgt > 0f && InitiallyWet[i])
                        {
                            float rest = stillDepth[i];
                            float px = (x - half) * CellSize, pz = (z - half) * CellSize;
                            // On the side the wave comes from (upstream half), the band relaxes towards the incoming
                            // wave: its height and its own flow, so it enters the domain travelling as it is.
                            if (incoming && rest > 0.5f && px * Incoming.dirX + pz * Incoming.dirZ < 0f)
                            {
                                float eta = Incoming.Elevation(px, pz, forceTime);
                                float target = rest + eta;
                                float u = IncomingWave.FlowSpeed(eta, rest, g);
                                float f = 1f / (1f + kIn * wgt);
                                h = target + (h - target) * f;
                                qx = target * u * Incoming.dirX + (qx - target * u * Incoming.dirX) * f;
                                qz = target * u * Incoming.dirZ + (qz - target * u * Incoming.dirZ) * f;
                            }
                            else if (sponge)
                            {
                                float f = 1f / (1f + k * wgt);
                                h = rest + (h - rest) * f;
                                qx *= f; qz *= f;
                            }
                        }
                    }
                    float d = h - h0[i];
                    if (d < 0f) d = -d;
                    if (d > change) change = d;
                    Depth[i] = h; Qx[i] = qx; Qz[i] = qz;
                }
                rowB[z] = change;
            }
            UpdateDerived(z0, z1);
        }

        void UpdateDerived(int z0, int z1)
        {
            int n = Size;
            float wet = DryDepth * 10f;
            for (int z = z0; z < z1; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = Depth[i];
                    if (h > wet)
                    {
                        float u = Qx[i] / h, v = Qz[i] / h;
                        VelocityX[i] = u; VelocityZ[i] = v; Speed[i] = MathF.Sqrt(u * u + v * v);
                    }
                    else { VelocityX[i] = 0f; VelocityZ[i] = 0f; Speed[i] = 0f; }
                }
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Water surface elevation relative to SeaLevel (m) for a wet cell; 0 for dry cells.</summary>
        public float SurfaceElevation(int i) => Depth[i] > DryDepth * 10f ? bed[i] + Depth[i] - SeaLevel : 0f;

        public double TotalVolume()
        {
            double sum = 0;
            for (int i = 0; i < Depth.Length; i++) sum += Depth[i];
            return sum * CellSize * CellSize;
        }

        static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
    }

    public enum BoundarySide
    {
        South,
        North,
        West,
        East
    }
}
