Shader "Custom/CloudMesh"
{
    Properties
    {
        [Header(Color)]
        _ColorValley      ("Valley Color",           Color)          = (0.80, 0.84, 0.92, 1)
        _ColorPeak        ("Peak Color",             Color)          = (1.00, 1.00, 1.00, 1)

        [Header(Fresnel)]
        _FresnelColor     ("Fresnel Color",          Color)          = (0.92, 0.95, 1.00, 1)
        _FresnelPower     ("Fresnel Power",          Range(1, 10))   = 4.0
        _FresnelStrength  ("Fresnel Strength",       Range(0, 2))    = 0.6

        [Header(Vertex Displacement)]
        _DispScale        ("Displacement Scale",     Float)          = 1.8
        _DispStrength     ("Displacement Strength",  Float)          = 0.18
        _DispDetailScale  ("Detail Scale (x base)",  Float)          = 4.0
        _DispDetailStrength("Detail Strength",       Range(0, 1))    = 0.4

        [Header(Surface Detail)]
        _NormalScale      ("Normal Detail Scale",    Float)          = 3.5
        _NormalStrength   ("Normal Strength",        Range(0, 6))    = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorValley;
                float4 _ColorPeak;
                float4 _FresnelColor;
                float  _FresnelPower;
                float  _FresnelStrength;
                float  _DispScale;
                float  _DispStrength;
                float  _DispDetailScale;
                float  _DispDetailStrength;
                float  _NormalScale;
                float  _NormalStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                // displacement value passed to fragment for peak-valley lerp
                float  dispValue   : TEXCOORD5;
            };

            // ---- 3D gradient noise ----

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7,  74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float GradNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(dot(Hash3(i+float3(0,0,0)), f-float3(0,0,0)),
                              dot(Hash3(i+float3(1,0,0)), f-float3(1,0,0)), u.x),
                         lerp(dot(Hash3(i+float3(0,1,0)), f-float3(0,1,0)),
                              dot(Hash3(i+float3(1,1,0)), f-float3(1,1,0)), u.x), u.y),
                    lerp(lerp(dot(Hash3(i+float3(0,0,1)), f-float3(0,0,1)),
                              dot(Hash3(i+float3(1,0,1)), f-float3(1,0,1)), u.x),
                         lerp(dot(Hash3(i+float3(0,1,1)), f-float3(0,1,1)),
                              dot(Hash3(i+float3(1,1,1)), f-float3(1,1,1)), u.x), u.y),
                    u.z);
            }

            float FBM3(float3 p)
            {
                float v = 0, a = 0.5, f = 1.0;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    v += a * GradNoise3(p * f);
                    a *= 0.5;
                    f *= 2.1;
                }
                return v;
            }

            // ---- Vertex ----

            Varyings Vert(Attributes IN)
            {
                float3 p = IN.positionOS.xyz;

                // Large-scale displacement (big lumps)
                float3 dp    = p * _DispScale;
                float  hBase = FBM3(dp) * 0.5 + 0.5;           // [0,1]

                // Fine-detail displacement layered on top
                float  hDetail = FBM3(dp * _DispDetailScale) * 0.5 + 0.5;
                float  h       = hBase + hDetail * _DispDetailStrength * 0.5;

                // Displace along vertex normal in object space
                float3 displacedOS = p + IN.normalOS * h * _DispStrength;

                // Recompute normal from displacement gradient (finite differences)
                float  eps  = 0.015 / _DispScale;
                float3 tx   = normalize(float3(1, 0, 0) - IN.normalOS * IN.normalOS.x);
                float3 bx   = cross(IN.normalOS, tx);
                float  hpx  = FBM3((p + tx * eps) * _DispScale) * 0.5 + 0.5;
                float  hpz  = FBM3((p + bx * eps) * _DispScale) * 0.5 + 0.5;
                float3 newNormalOS = normalize(IN.normalOS
                                  - tx * ((hpx - hBase) / eps) * _DispStrength
                                  - bx * ((hpz - hBase) / eps) * _DispStrength);

                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(displacedOS);
                OUT.positionCS  = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.positionOS  = p;
                OUT.normalWS    = TransformObjectToWorldNormal(newNormalOS);
                OUT.tangentWS   = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.bitangentWS = cross(OUT.normalWS, OUT.tangentWS) * IN.tangentOS.w;
                OUT.dispValue   = h;
                return OUT;
            }

            // ---- Fragment ----

            half4 Frag(Varyings IN) : SV_Target
            {
                // Fine surface normal bump (higher frequency than displacement)
                float3 p   = IN.positionOS * _NormalScale;
                float  eps = 0.025;
                float  n0  = FBM3(p);
                float3 grad = float3(
                    FBM3(p + float3(eps, 0,   0  )) - n0,
                    FBM3(p + float3(0,   eps, 0  )) - n0,
                    FBM3(p + float3(0,   0,   eps)) - n0);
                float3 normalWS = normalize(IN.normalWS
                                + TransformObjectToWorldDir(grad) * _NormalStrength);

                // ---- Lighting ----
                Light  light   = GetMainLight();

                // Squared half-Lambert — very soft, pushes everything toward white
                float  NdotL   = dot(normalWS, light.direction) * 0.5 + 0.5;
                float  diffuse = NdotL * NdotL;

                // Peak-valley: displacement height drives the color lerp
                float  peak      = saturate(IN.dispValue);
                half3  baseColor = lerp(_ColorValley.rgb, _ColorPeak.rgb, peak * diffuse);

                // ---- Fresnel rim ----
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float  NdotV   = saturate(dot(normalWS, viewDir));
                float  fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                baseColor     += _FresnelColor.rgb * fresnel;

                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster pass so the cloud casts shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
