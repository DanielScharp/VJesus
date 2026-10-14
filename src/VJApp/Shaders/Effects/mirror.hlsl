// @name Espelho
// @param mode "Modo (0 H · 1 V · 2 ambos)" 0 2 0 1
float4 Effect(float2 uv)
{
    float2 v = uv;
    if (mode < 0.5 || mode > 1.5) v.x = v.x > 0.5 ? 1 - v.x : v.x;
    if (mode > 0.5) v.y = v.y > 0.5 ? 1 - v.y : v.y;
    return tex(v);
}