// @name RGB Shift
// @param amount "Deslocamento" 0 1 0.2
// @param angle "Ângulo" 0 1 0
float4 Effect(float2 uv)
{
    float2 d = float2(cos(angle * 2 * PI), sin(angle * 2 * PI)) * amount * 0.03;
    float4 c = tex(uv);
    c.r = tex(uv + d).r;
    c.b = tex(uv - d).b;
    return c;
}