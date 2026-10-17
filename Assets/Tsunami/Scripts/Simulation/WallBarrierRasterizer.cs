using System;

namespace Tsunami
{
    public static class WallBarrierRasterizer
    {
        // Follow diagonals as a connected staircase. Reversing a wall keeps exactly the same faces.
        public static void Trace(int ax, int az, int bx, int bz, Action<int, int, bool> face)
        {
            if (ax > bx || (ax == bx && az > bz))
            { (ax, bx) = (bx, ax); (az, bz) = (bz, az); }
            int steps = Math.Max(Math.Abs(bx-ax), Math.Abs(bz-az));
            int x = ax, z = az;
            for (int step = 1; step <= steps; step++)
            {
                int nx = (int)MathF.Round(ax + (bx-ax) * (step/(float)steps));
                int nz = (int)MathF.Round(az + (bz-az) * (step/(float)steps));
                for (int u = Math.Min(x,nx); u < Math.Max(x,nx); u++) face(u,z-1,false);
                for (int v = Math.Min(z,nz); v < Math.Max(z,nz); v++) face(nx-1,v,true);
                x = nx; z = nz;
            }
        }
    }
}
