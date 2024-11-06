Shader "Internal/SDFGenerator"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SourceTex ("Source", 2D) = "black" {}
        _Channel("Channel", Integer) = 3
        _EdgeValue("Edge value", Float) = 0.5
        _BorderColor("Border color", Color) = (0, 0, 0, 0)
        _Spread("Spread", Float) = 1
        _Feather("Feather", Range(0, 1)) = 0.125
        _Scale("Scale", Vector) = (2, 2, 2, 2)
        _PostRadius("PostRadius", Float) = 1
        _SuperTexFlags("Texture flags", Integer) = 3
        _SuperColor("SDF color", Color) = (1, 1, 0, 1)
        _SuperIntensity("SDF intensity", Float) = 1
        
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            Name "BlitInCenter"
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma shader_feature _ CLAMP_BORDER

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_PointClamp);

            float4 _MainTex_TexelSize;
            float2 _Scale;
            float4 _BorderColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f input) : SV_Target
            {
                float2 scaledUv = (input.uv - 0.5) * _Scale + 0.5;
#ifdef CLAMP_BORDER
                return SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, scaledUv);
#else
                return (scaledUv.x >= 0 && scaledUv.x <= 1 && scaledUv.y >= 0 && scaledUv.y <= 1) ?
                    SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, scaledUv) : _BorderColor;
#endif
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "SDFPass"
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature _ FIRSTPASS

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _EdgeValue;
            float _Spread;
            int _Channel;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 sampleEdgeUV(float2 uv)
            {
                float4 edge = tex2Dlod(_MainTex, float4(uv, 0, 0));
                if (edge.a < 0.5) edge.xy = -10000;
                return edge;
            }

            float inBounds(float2 uv)
            {
                return all(uv > 0) && all(uv < 1);
            }

            static float2 offsets[] = {
                float2(+0, -1),
                float2(-1, +0),
                float2(+1, +0),
                float2(+0, +1),
                float2(-1, -1),
                float2(-1, +1),
                float2(+1, -1),
                float2(+1, +1),
            };

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;

                // How far each sample should spread
                float2 uvo = _MainTex_TexelSize.xy * _Spread;

                // Compute a distance factor based on the smaller edge
                float2 dstFact = _MainTex_TexelSize.zw / max(_MainTex_TexelSize.z, _MainTex_TexelSize.w);

                #if defined(FIRSTPASS)
                // Detect edges
                float solidCount = 0;
                float4 self = tex2D(_MainTex, uv);
                bool selfSolid = self[_Channel] >= _EdgeValue;
                float4 outuv = float4(-1000, -1000, selfSolid ? 1 : 0, 0);
                for (float i = 0; i < 8; ++i) {
                    float2 euv = uv + uvo * offsets[i];
                    float4 edge = tex2Dlod(_MainTex, float4(euv, 0, 0)) * inBounds(euv);
                    // An edge is when two neighbouring pixels have different "solid" results
                    if ((edge[_Channel] >= _EdgeValue) != selfSolid) {
                        float l = (0.5f - self[_Channel]) / (edge[_Channel] - self[_Channel]);
                        euv = lerp(uv, euv, l);
                        if (length((euv.xy - uv) * dstFact) < length((outuv.xy - uv) * dstFact)) {
                            outuv.xy = euv.xy;
                            outuv.a = 1;    // Mark this result as valid
                        }
                    }
                }
                return outuv;
                #else
                // Get the current nearest edge
                float4 outuv = sampleEdgeUV(uv);
                for (float i = 0; i < 8; ++i)
                {
                    // Sample 8 points to find another nearest candidate
                    float2 euv = uv + uvo * offsets[i];
                    float4 edge = sampleEdgeUV(euv);
                    // If the new edge is nearer, use it instead
                    if (length((edge.xy - uv) * dstFact) < length((outuv.xy - uv) * dstFact))
                    {
                        outuv.xy = edge.xy;
                        outuv.a = 1; // Mark this result as valid
                    }
                }
                return outuv;
                #endif
            }
            ENDCG
        }

        Pass
        {
            Name "FinalSDFPass"
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile BOTH_SIDES INSIDE_ONLY OUTSIDE_ONLY
            #pragma shader_feature _ INVERT_SDF

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _Feather;
            float2 _Scale;
            

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 sampleEdgeUV(float2 uv)
            {
                float4 edge = tex2D(_MainTex, uv);
                if (edge.a < 0.5) edge.xy = -10000;
                return edge;
            }

            fixed frag(v2f input) : SV_Target
            {
                // Get the computed nearest edge
                float4 edge = sampleEdgeUV(input.uv);
                // Compute a distance factor based on the smaller edge
                float2 dstFact = _MainTex_TexelSize.zw / max(_MainTex_TexelSize.z, _MainTex_TexelSize.w);
                // Compute the distance
                float dst = length((input.uv - edge.xy) * _Scale * dstFact);
                // Compute the SDF from distance (based on 'solid' (in b) and 'feather' distance)
#if defined(BOTH_SIDES)
                dst = 0.5 + dst * (step(0.5, edge.b) * 2 - 1) / _Feather;
#elif defined(INSIDE_ONLY)
                dst = dst * (step(0.5, edge.b) * 2 - 1) / _Feather;
#elif defined(OUTSIDE_ONLY)
                dst = 1 + dst * (step(0.5, edge.b) * 2 - 1) / _Feather;
#endif
#if defined(INVERT_SDF)
                dst = 1 - dst;
#endif
                return dst;
            }
            ENDCG
        }

        Pass
        {
            Name "PostSDFEffect"
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile BLUR PROGRESSIVE_BLUR MAX_KERNEL MIN_KERNEL

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _PostRadius;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            
            float currentWeight(float offsetX, float offsetY, float radius)
            {
                //The real sigma is half of this, but it simplifies the formulas this way.
                float sigma = radius * radius;
                
                float weight = 0;
                for (int deltax = -1; deltax <= 1; ++deltax)
                {
                    for (int deltay = -1; deltay <= 1; ++deltay)
                    {
                        float x = offsetX + deltax * 0.333333;
                        float y = offsetY + deltay * 0.333333;
                        weight += exp(-(x*x + y*y) / max(sigma, 1e-15));
                    }
                }
                
                return weight;
            }

            fixed frag(v2f input) : SV_Target
            {
                float processedSample = 0;
                float sampleWeight = 1;
                
#if defined(BLUR) | defined(PROGRESSIVE_BLUR)
                sampleWeight = 0;
#elif defined(MIN_KERNEL)
                processedSample = 1;
#endif
                
#if defined(PROGRESSIVE_BLUR)
                float radius = (1 - tex2D(_MainTex, input.uv).r) * _PostRadius;
#else
                float radius = _PostRadius;
#endif
                
                for (float offsetX = -50; offsetX <= 50; ++offsetX)
                {
                    for (float offsetY = -50; offsetY <= 50; ++offsetY)
                    {
                        float2 uvOffset = input.uv + float2(offsetX, offsetY) * _MainTex_TexelSize.xy;
                        float sample = tex2D(_MainTex, uvOffset).r;
                        float validSample = (uvOffset.x > 0) * (uvOffset.x < 1) * (uvOffset.y > 0) * (uvOffset.y < 1);
                        validSample *= (offsetX * offsetX + offsetY * offsetY <= radius * radius);

#if defined(BLUR) | defined(PROGRESSIVE_BLUR)
                        float weight = validSample * currentWeight(offsetX, offsetY, _PostRadius);
                        sampleWeight += weight;
                        processedSample += sample * weight;
#elif defined(MAX_KERNEL)
                        processedSample = validSample ? max(processedSample, sample) : processedSample;
#elif defined(MIN_KERNEL)
                        processedSample = validSample ? min(processedSample, sample) : processedSample; 
#endif
                    }
                }
                return processedSample / max(sampleWeight, 1e-15);
            }
            ENDCG
        }

        Pass
        {
            Name "SuperposeSDF"
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile SOURCE_OVER_SDF SDF_OVER_SOURCE MEAN_BLENDING

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            sampler2D _SourceTex;
            float4 _SourceTex_ST;
            float4 _SourceTex_TexelSize;
            int _Channel;
            int _SuperTexFlags;
            float4 _SuperColor;
            float _SuperIntensity;
            


            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f input) : SV_Target
            {
                float sdf = tex2D(_MainTex, input.uv)[_Channel];
                sdf *= (_SuperTexFlags & 1);
                float4 sdfColor = _SuperColor * _SuperIntensity * sdf;
                float2 sourceUV = TRANSFORM_TEX((input.uv - 0.5), _SourceTex) + 0.5;
                sourceUV = (sourceUV - 0.5) * _MainTex_TexelSize.zw * _SourceTex_TexelSize.xy + 0.5;
                float4 sourceColor = tex2D(_SourceTex, sourceUV);
                sourceColor *= (sourceUV.x >= 0) * (sourceUV.y >= 0) * (sourceUV.x <= 1) * (sourceUV.y <= 1);
                sourceColor *= ((_SuperTexFlags >> 1) & 1);
                sourceColor *= (sourceColor.a > 0);
#if defined(SDF_OVER_SOURCE)
                float4 swap = sdfColor;
                sdfColor = sourceColor;
                sourceColor = swap;
#endif
#if defined(MEAN_BLENDING)
                float4 superposedColor = (sourceColor + sdfColor) / 2.;
#else
                float superposedAlpha = sourceColor.a + sdfColor.a - sourceColor.a * sdfColor.a;

                float4 superposedColor = saturate(sourceColor + (1. - sourceColor.a) * sdfColor);
                superposedColor = float4(lerp(sdfColor.rgb, sourceColor.rgb, sourceColor.a / max(superposedAlpha, 0.0001f)), superposedAlpha);
#endif
                return superposedColor;
            }
            ENDCG
        }
    }
}