// @name Kaleidoscope
// @param segments "Segmentos" 2 16 6 1
// @param rotation "Rotação" 0 1 0
// @param zoom "Zoom" 0.5 3 1
// @param spin "Girar sozinho" 0 1 0
float4 Effect(float2 uv)
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = (uv - 0.5) * float2(aspect, 1);
    float a = atan2(p.y, p.x) + (rotation + Time * spin * 0.1) * 2 * PI;
    float r = length(p) / zoom;
    float seg = 2 * PI / max(segments, 1);
    a = fmod(abs(a), seg);
    if (a > seg * 0.5) a = seg - a;
    float2 q = float2(cos(a), sin(a)) * r;
    q.x /= aspect;
    return tex(mirrorUV(q + 0.5));
}