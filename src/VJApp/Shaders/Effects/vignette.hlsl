// @name Vinheta
// @param amount "Intensidade" 0 2 0.8
// @param softness "Suavidade" 0.05 1 0.45
float4 Effect(float2 uv)
{
    float aspect = Resolution.x / Resolution.y;
    float d = length((uv - 0.5) * float2(aspect, 1)) / length(float2(aspect, 1) * 0.5);
    float v = 1 - smoothstep(1 - softness, 1, d) * amount;
    float4 c = tex(uv);
    return float4(c.rgb * saturate(v), c.a);
}
