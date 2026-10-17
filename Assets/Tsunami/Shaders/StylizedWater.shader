// Stylised, low-cost URP water used by both the surrounding ocean and the simulated flood mesh.
// Visual detail only: depth tint, wave-face foam, backlit crest glow, ripples, fresnel and specular.
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
        _BreakStart ("Breaking Foam Start (Froude number)", Float) = 0.8
        _BreakFull ("Breaking Foam Full (Froude number)", Float) = 1.6
        _ShoreFoamDepth ("Shoreline Foam Depth (m)", Float) = 0.25
        _EdgeFade ("Soft Water Edge (m of depth)", Float) = 0.15
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.4
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
                half4 _SkyColor;
                half _FresnelPower;
                half _FresnelStrength;
                half _SpecularStrength;
                half _Gloss;
                float _RippleScale;
                half _RippleStrength;
                float _RippleSpeed;
                half4 _FoamColor;
                float _BreakStart;
                float _BreakFull;
                float _ShoreFoamDepth;
                float _EdgeFade;
                float _FoamNoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 data : TEXCOORD2;
                float fogFactor : TEXCOORD3;
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
                float t = _Time.y * _RippleSpeed;
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
                float rise = saturate((i.positionWS.y - _SeaLevel) / max(_CrestHeight, 0.01)); // height above sea

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

                // Foam where water breaks: flow fast relative to its depth (Froude number u / sqrt(g h)),
                // steep wave faces and the thin shoreline edge. Broken up by scrolling noise.
                float froude = speed / sqrt(9.81 * max(depth, 0.1));
                float noise = ValueNoise(foamUV + float2(t * 0.35, t * 0.22)) * 0.6
                            + ValueNoise(foamUV * 2.7 - t * 0.5) * 0.4;
                float foamSource = smoothstep(_BreakStart, _BreakFull, froude)
                                 + saturate(1.0 - depth / max(_ShoreFoamDepth, 0.01))
                                 + smoothstep(_FaceFoamStart, _FaceFoamEnd, steep);
                foamSource = saturate(foamSource);
                float foam = foamSource * smoothstep(0.45, 0.7, noise + foamSource * 0.35);
                col.rgb = lerp(col.rgb, _FoamColor.rgb * (0.7 + 0.3 * ndl), foam);
                col.a = max(col.a, foam * 0.95);

                float3 h = normalize(light.direction + v);
                col.rgb += pow(saturate(dot(n, h)), _Gloss) * _SpecularStrength * light.color;

                // Thin water fades out where it meets the ground, so the waterline is soft instead of hard-edged.
                col.a *= saturate(depth / max(_EdgeFade, 0.001));

                col.rgb = MixFog(col.rgb, i.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
