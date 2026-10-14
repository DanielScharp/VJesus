// @name Blur
// @param amount "Quantidade" 0 1 0.3
float4 Effect(float2 uv)
{
    float2 stepPx = amount * 6.0 / Resolution;
    float4 sum = 0;
    float wsum = 0;
    [unroll] for (int x = -3; x <= 3; x++)
    [unroll] for (int y = -3; y <= 3; y++)
    {
        float w = exp(-(x * x + y * y) / 8.0);
        sum += tex(uv + float2(x, y) * stepPx) * w;
        wsum += w;
    }
    return sum / wsum;
}