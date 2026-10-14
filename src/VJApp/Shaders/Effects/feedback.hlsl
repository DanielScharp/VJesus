// @name Feedback
// @feedback
// @param amount "Rastro" 0 0.98 0.85
// @param zoom "Zoom" 0.9 1.1 1.01
// @param rotate "Giro" -0.05 0.05 0.005
float4 Effect(float2 uv)
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = (uv - 0.5) * float2(aspect, 1);
    float s = sin(rotate), c = cos(rotate);
    p = float2(p.x * c - p.y * s, p.x * s + p.y * c) / zoom;
    p.x /= aspect;
    float4 trail = hist(p + 0.5) * amount;
    return max(tex(uv), trail);
}