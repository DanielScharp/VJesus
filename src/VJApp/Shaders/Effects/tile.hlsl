// @name Mosaico
// @param count "Repetições" 1 8 2 1
float4 Effect(float2 uv)
{
    return tex(mirrorUV(uv * count));
}