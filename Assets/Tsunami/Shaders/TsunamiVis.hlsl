// Shared scientific-visualisation globals and colour maps (set by TsunamiVisualizationManager).
// The C# legend (TsunamiVisualizationManager.Colormap) uses the exact same stops.
#ifndef TSUNAMI_VIS_INCLUDED
#define TSUNAMI_VIS_INCLUDED

float _TsunamiVisMode;     // 0 realistic, 1 surface elevation, 2 water depth, 3 flow speed
float _TsunamiSeaLevel;    // world height of the still sea
float _TsunamiElevRange;   // |eta| (m) shown at full colour
float _TsunamiDepthRange;  // depth (m) shown as the deepest colour
float _TsunamiSpeedRange;  // speed (m/s) shown at full colour
float _TsunamiNoFoam;      // 1 = foam off
float _TsunamiTime;        // simulation-driven animation time (frozen while paused)
float _TsunamiTimeSet;     // 1 when _TsunamiTime is driven by the manager
float _TsunamiBathyVis;    // 1 = colour the sea floor by depth below the still sea

float TsunamiAnimTime()
{
    return lerp(_Time.y, _TsunamiTime, saturate(_TsunamiTimeSet));
}

float TsunamiSeaLevel(float fallback)
{
    return _TsunamiTimeSet > 0.5 ? _TsunamiSeaLevel : fallback;
}

// Diverging blue - white - red for t in [-1, 1].
half3 TsunamiElevationColor(float t)
{
    t = clamp(t, -1.0, 1.0);
    half3 neg = half3(0.06, 0.22, 0.62), mid = half3(0.95, 0.95, 0.93), pos = half3(0.72, 0.04, 0.06);
    return t < 0 ? lerp(mid, neg, -t) : lerp(mid, pos, t);
}

// Light cyan -> blue -> dark navy for t in [0, 1].
half3 TsunamiDepthColor(float t)
{
    t = saturate(t);
    half3 a = half3(0.78, 0.95, 0.98), b = half3(0.20, 0.55, 0.85), c = half3(0.02, 0.08, 0.30);
    return t < 0.5 ? lerp(a, b, t * 2.0) : lerp(b, c, t * 2.0 - 1.0);
}

// Viridis-like for t in [0, 1].
half3 TsunamiSpeedColor(float t)
{
    t = saturate(t) * 4.0;
    half3 c0 = half3(0.27, 0.00, 0.33), c1 = half3(0.23, 0.32, 0.55), c2 = half3(0.13, 0.57, 0.55),
          c3 = half3(0.37, 0.79, 0.38), c4 = half3(0.99, 0.91, 0.14);
    if (t < 1.0) return lerp(c0, c1, t);
    if (t < 2.0) return lerp(c1, c2, t - 1.0);
    if (t < 3.0) return lerp(c2, c3, t - 2.0);
    return lerp(c3, c4, t - 3.0);
}

#endif
