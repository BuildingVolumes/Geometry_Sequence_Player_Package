Shader "Pointclouds/Lit"
{
    Properties
    {
       _MainTex ("UV (Dont set a texture here)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Lambert
        
        sampler2D _MainTex;

        struct Input
        {
            float4 color: Color;
            float2 uv_MainTex : TEXCOORD0;
        };

        float circle(float2 uv) 
        {
            float2 center = float2(0, 0);
	        float d = length(center - uv) - 0.5;
            float t = step(0.5, 1.0 - d);
	        return t;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo.rgb = IN.color.rgb;
            float a = circle(IN.uv_MainTex);
            clip(a - 0.5);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
