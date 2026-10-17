namespace Tsunami
{
    /// <summary>
    /// Finds where moving water slams into something taller than itself: a neighbouring cell whose ground is above
    /// the water surface (mountain side, steep bank) or a wall on the face whose top is above the surface.
    /// The impact speed is the velocity component into that face. Pure C# (tested outside Unity).
    /// </summary>
    public static class ImpactDetection
    {
        /// <param name="i">Cell index.</param>
        /// <param name="minDepth">Ignore thinner water (m).</param>
        /// <param name="minSpeed">Ignore slower impacts (m/s).</param>
        /// <param name="clearance">How far (m) the obstacle must rise above the water surface.</param>
        /// <param name="dirX">Face direction (-1, 0, +1) along x.</param>
        /// <param name="dirZ">Face direction (-1, 0, +1) along z.</param>
        /// <param name="impactSpeed">Velocity into the face (m/s).</param>
        /// <param name="obstacleTop">Height of the obstacle (ground or wall top) relative to the grid (m).</param>
        public static bool Find(TsunamiPhysicsSolver s, float[] bed, int i, float minDepth, float minSpeed, float clearance,
            out int dirX, out int dirZ, out float impactSpeed, out float obstacleTop)
        {
            int bx = 0, bz = 0;
            float best = 0f, bestTop = 0f;
            dirX = dirZ = 0;
            impactSpeed = 0f;
            obstacleTop = 0f;
            float h = s.Depth[i];
            if (h < minDepth) return false;
            int n = s.Size;
            int x = i % n, z = i / n;
            float surface = bed[i] + h;
            float limit = surface + clearance;
            float vx = s.VelocityX[i], vz = s.VelocityZ[i];

            // x faces
            if (vx > minSpeed && x < n - 1) Check(1, 0, vx, bed[i + 1], s.BarrierX[i]);
            else if (vx < -minSpeed && x > 0) Check(-1, 0, -vx, bed[i - 1], s.BarrierX[i - 1]);
            // z faces
            if (vz > minSpeed && z < n - 1) Check(0, 1, vz, bed[i + n], s.BarrierZ[i]);
            else if (vz < -minSpeed && z > 0) Check(0, -1, -vz, bed[i - n], s.BarrierZ[i - n]);
            dirX = bx; dirZ = bz; impactSpeed = best; obstacleTop = bestTop;
            return best > 0f;

            void Check(int dx, int dz, float un, float groundNext, float wall)
            {
                float top = groundNext > wall ? groundNext : wall;
                if (top <= limit || un <= best) return;
                best = un; bestTop = top; bx = dx; bz = dz;
            }
        }
    }
}
