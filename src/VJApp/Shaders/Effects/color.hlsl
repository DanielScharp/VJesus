// @name Cor
// @param hueShift "Matiz" 0 1 0
// @param saturation "Saturação" 0 2 1
// @param contrast "Contraste" 0 2 1
// @param gain "Brilho" 0 2 1
// @param invert "Inverter" 0 1 0
float3 rgb2hsv(float3 c)
{
    float4 K = float4(0, -1.0 / 3.0, 2.0 / 3.0, -1);
    float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
    float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
    float d = q.x - min(q.w, q.y);
    return float3(abs(q.z + (q.w - q.y) / (6 * d + 1e-6)), d / (q.x + 1e-6), q.x);
}
float3 hsv2rgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3);
    return c.z * lerp(1, saturate(p - 1), c.y);
}
float4 Effect(float2 uv)
{
    float4 c = tex(uv);
    float3 hsv = rgb2hsv(c.rgb);
    hsv.x = frac(hsv.x + hueShift);
    hsv.y = saturate(hsv.y * saturation);
    float3 rgb = hsv2rgb(hsv);
    rgb = (rgb - 0.5) * contrast + 0.5;
    rgb *= gain;
    rgb = lerp(rgb, 1 - rgb, invert);
    return float4(saturate(rgb), c.a);
}