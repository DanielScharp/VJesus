// @name Contornos
// @param strength "Força" 0 4 1.5
// @param keep "Manter original" 0 1 0
float4 Effect(float2 uv)
{
    float2 px = 1.0 / Resolution;
    float tl = luma(tex(uv + px * float2(-1, -1)).rgb), t = luma(tex(uv + px * float2(0, -1)).rgb), tr = luma(tex(uv + px * float2(1, -1)).rgb);
    float l  = luma(tex(uv + px * float2(-1,  0)).rgb),                                             r  = luma(tex(uv + px * float2(1,  0)).rgb);
    float bl = luma(tex(uv + px * float2(-1,  1)).rgb), b = luma(tex(uv + px * float2(0,  1)).rgb), br = luma(tex(uv + px * float2(1,  1)).rgb);
    float gx = -tl - 2 * l - bl + tr + 2 * r + br;
    float gy = -tl - 2 * t - tr + bl + 2 * b + br;
    float e = saturate(length(float2(gx, gy)) * strength);
    float4 c = tex(uv);
    return float4(saturate(c.rgb * keep + e * c.rgb * 2 + e * 0.2), c.a);
}