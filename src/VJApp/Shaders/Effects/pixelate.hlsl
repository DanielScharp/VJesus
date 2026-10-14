// @name Pixelate
// @param size "Tamanho do pixel" 1 100 16 1
float4 Effect(float2 uv)
{
    float2 cells = Resolution / max(size, 1);
    return tex((floor(uv * cells) + 0.5) / cells);
}