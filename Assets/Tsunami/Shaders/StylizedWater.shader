// Stylised, low-cost URP water used by both the surrounding ocean and the simulated flood mesh.
// Visual detail only: depth tint, wave-face and crest foam, backlit crest glow, ripples, fresnel, specular and
// the darker stormy sea (TsunamiStorm).
// All water movement comes from the simulation (vertex positions); UV0 = (water depth m, flow speed m/s)
// written by WaterMeshRenderer / OceanRing. No textures, refraction or screen-space effects: one pass.
Shader "Tsunami/StylizedWater"
{
    Properties
    {
        [Header(Colour)]
        _WaterShallow ("Shallow Colour (A = opacity)", Color) = (0.26, 0.78, 0.76, 0.75)
        _WaterDeep ("Deep Colour (A = opacity)", Color) = (0.02, 0.17, 0.34, 1.0)
        _DeepAt ("Depth For Deep Colour (m)", Float) = 7
        _SeaLevel ("Sea Level (m)", Float) = 0

        [Header(Wave Crests)]
        _CrestColor ("Crest Glow Colour", Color) = (0.38, 0.95, 0.88, 1)
        _CrestStrength ("Crest Glow Strength", Range(0, 2)) = 0.9
        _CrestHeight ("Height For Full Crest Glow (m)", Float) = 8
        _FaceFoamStart ("Wave-Face Foam Start (1 - normal.y)", Range(0, 1)) = 0.35
        _FaceFoamEnd ("Wave-Face Foam Full", Range(0, 1)) = 0.75
        _CrestFoamStart ("Sea Crest Foam Start (m above the still sea)", Float) = 1.5
        _CrestFoamFull ("Sea Crest Foam Full (m above the still sea)", Float) = 6

        [Header(Surface)]
        _SkyColor ("Fresnel Sky Colour", Color) = (0.72, 0.88, 0.96, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.5
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0.8
        _Gloss ("Specular Gloss", Range(4, 256)) = 90
        _RippleScale ("Ripple Scale", Float) = 0.3
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.22
        _RippleSpeed ("Ripple Speed", Float) = 1.1

        [Header(Foam)]
        _FoamColor ("Foam Colour", Color) = (0.95, 0.98, 1, 1)
        _BreakFroudeStart ("Breaking Foam Start (Froude number)", Float) = 1.3
        _BreakFroudeFull ("Breaking Foam Full (Froude number)", Float) = 2.8
        _FoamFlow ("Foam Moves With Flow (0..1)", Range(0, 1.5)) = 1
        _FoamCycle ("Foam Flow Cycle (s)", Range(0.3, 4)) = 1.2
        _FoamCoverage ("Foam Coverage", Range(0, 1)) = 0.55
        _ShoreFoamDepth ("Shoreline Foam Depth (m)", Float) = 0.25
        _EdgeFade ("Soft Water Edge (m of depth)", Float) = 0.15
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.4

        [Header(Storm)]
        _StormSeaColor ("Stormy Sea Colour", Color) = (0.03, 0.05, 0.09, 1)

        [Header(Clip Box)]
        _ClipBox ("Clip Box (centre XZ, half size, W = on)", Vector) = (0, 0, 0, 0)
        _ClipBand ("Clip Band (fade from X to Y m inside the box)", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TsunamiVis.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterShallow;
                half4 _WaterDeep;
                float _DeepAt;
                float _SeaLevel;
                half4 _CrestColor;
                half _CrestStrength;
                float _CrestHeight;
                half _FaceFoamStart;
                half _FaceFoamEnd;
                float _CrestFoamStart;
                float _CrestFoamFull;
                half4 _SkyColor;
                half _FresnelPower;
                half _FresnelStrength;
                half _SpecularStrength;
                half _Gloss;
                float _RippleScale;
                half _RippleStrength;
                float _RippleSpeed;
                half4 _FoamColor;
                float _BreakFroudeStart;
                float _BreakFroudeFull;
                float _FoamFlow;
                float _FoamCycle;
                half _FoamCoverage;
                float _ShoreFoamDepth;
                float _EdgeFade;
                float _FoamNoiseScale;
                half4 _StormSeaColor;
                float4 _ClipBox;
                float4 _ClipBand;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1; // depth-averaged velocity (m/s), from the simulation
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 data : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float2 flow : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.data = input.uv;
                o.flow = input.flow;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = TsunamiAnimTime() * _RippleSpeed;
                float2 p = i.positionWS.xz * _RippleScale;
                float2 foamUV = i.positionWS.xz * _FoamNoiseScale;
                float3 meshNormal = normalize(i.normalWS);

                // Analytic ripple slopes from three crossing sine waves (no textures).
                float2 slope = float2(0, 0);
                slope += float2(1.0, 0.3) * cos(dot(p, float2(1.0, 0.3)) + t);
                slope += float2(-0.4, 1.0) * cos(dot(p, float2(-0.4, 1.0)) * 1.7 + t * 1.3) * 0.6;
                slope += float2(0.7, -0.8) * cos(dot(p, float2(0.7, -0.8)) * 2.9 + t * 1.9) * 0.35;
                // Ripples fade out on steep wave faces so the big wave keeps a clean silhouette.
                float3 n = normalize(meshNormal + float3(slope.x, 0, slope.y) * _RippleStrength * meshNormal.y * meshNormal.y);

                float depth = max(i.data.x, 0);
                float speed = i.data.y;
                float steep = 1.0 - saturate(meshNormal.y);                         // 0 flat .. 1 vertical
                float seaY = TsunamiSeaLevel(_SeaLevel);
                float eta = i.positionWS.y - seaY;                                     // height above the still sea
                float rise = saturate(eta / max(_CrestHeight, 0.01));
                // 1 over the sea (the bed is below the still sea: depth - eta = still depth), 0 for water on land.
                float overSea = saturate((depth - eta) * 0.5);

                half4 col = lerp(_WaterShallow, _WaterDeep, saturate(depth / max(_DeepAt, 0.01)));

                Light light = GetMainLight();
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ndl = saturate(dot(n, light.direction));
                col.rgb *= (0.5 + 0.5 * ndl) * light.color;

                // Backlit glow through tall, steep wave faces (light passing through thin water).
                half backlight = pow(saturate(dot(v, -light.direction) * 0.5 + 0.5), 3.0);
                half crest = rise * (0.35 + 0.65 * smoothstep(0.05, 0.45, steep)) * (0.4 + 0.6 * backlight);
                col.rgb = lerp(col.rgb, _CrestColor.rgb * (0.6 + 0.4 * light.color), saturate(crest * _CrestStrength));

                half fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower) * _FresnelStrength;
                col.rgb = lerp(col.rgb, _SkyColor.rgb, fresnel);
                col.a = saturate(col.a + fresnel * 0.3);

                // Stormy sea: darker water under and behind the storm clouds (the foam on top stays bright).
                col.rgb = lerp(col.rgb, _StormSeaColor.rgb, TsunamiStormShade(i.positionWS) * overSea);

                // Foam where water breaks: flow fast relative to its depth (Froude number u / sqrt(g h)),
                // steep wave faces and the thin shoreline edge. The foam texture is carried along by the simulated
                // velocity (two-phase flow map), so it streams with the water instead of sitting still and speckling.
                float froude = speed / sqrt(9.81 * max(depth, 0.3));
                float foamSource = smoothstep(_BreakFroudeStart, _BreakFroudeFull, froude)
                                 + saturate(1.0 - depth / max(_ShoreFoamDepth, 0.01))
                                 + smoothstep(_FaceFoamStart, _FaceFoamEnd, steep)
                                 // A raised sea reads as an incoming wave: whitecaps on the crest, even where the long
                                 // wave is still smooth and slow in deep water (same look far out and at the coast).
                                 + smoothstep(_CrestFoamStart, _CrestFoamFull, eta) * overSea;
                foamSource = saturate(foamSource) * (1.0 - saturate(_TsunamiNoFoam));
                float cycle = max(_FoamCycle, 0.05);
                float ph0 = frac(TsunamiAnimTime() / cycle), ph1 = frac(ph0 + 0.5);
                float2 drift = i.flow * (_FoamFlow * cycle * _FoamNoiseScale);
                float n0 = ValueNoise(foamUV - drift * ph0) * 0.65 + ValueNoise(foamUV * 2.3 - drift * ph0 * 2.3) * 0.35;
                float n1 = ValueNoise(foamUV - drift * ph1 + 17.3) * 0.65 + ValueNoise(foamUV * 2.3 - drift * ph1 * 2.3 + 5.1) * 0.35;
                float noise = lerp(n0, n1, abs(ph0 - 0.5) * 2.0);
                // Soft threshold: coverage grows with foamSource, edges stay feathered (no pin-hole speckle).
                float cut = lerp(0.85, 0.2, foamSource * _FoamCoverage);
                float foam = foamSource * smoothstep(cut - 0.18, cut + 0.18, noise);
                col.rgb = lerp(col.rgb, _FoamColor.rgb * (0.7 + 0.3 * ndl), foam);
                col.a = max(col.a, foam * 0.95);

                float3 h = normalize(light.direction + v);
                col.rgb += pow(saturate(dot(n, h)), _Gloss) * _SpecularStrength * light.color;

                // Scientific colour maps replace the realistic look (shape still lit so the wave stays readable).
                if (_TsunamiVisMode > 0.5)
                {
                    half3 cm;
                    if (_TsunamiVisMode < 1.5) cm = TsunamiElevationColor((i.positionWS.y - seaY) / max(_TsunamiElevRange, 0.01));
                    else if (_TsunamiVisMode < 2.5) cm = TsunamiDepthColor(depth / max(_TsunamiDepthRange, 0.01));
                    else cm = TsunamiSpeedColor(speed / max(_TsunamiSpeedRange, 0.01));
                    col.rgb = cm * (0.62 + 0.38 * ndl) + pow(saturate(dot(n, h)), _Gloss) * 0.25 * light.color;
                    col.a = 0.94;
                }

                // Thin water fades out where it meets the ground, so the waterline is soft instead of hard-edged.
                col.a *= saturate(depth / max(_EdgeFade, 0.001));

                // Clip box (the incoming swell): fade out inside the simulated area, over the same edge band where
                // the simulated water blends into the open sea, so the simulation carries the wave on from there.
                if (_ClipBox.w > 0.5)
                {
                    float2 d = abs(i.positionWS.xz - _ClipBox.xy);
                    float inside = _ClipBox.z - max(d.x, d.y);
                    col.a *= 1.0 - smoothstep(0.0, 1.0, saturate((inside - _ClipBand.x) / max(_ClipBand.y - _ClipBand.x, 0.01)));
                }

                col.rgb = MixFog(col.rgb, i.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
