// @name Pulso no BPM
// @param amount "Intensidade" 0 1 0.6
// @param decay "Decaimento" 1 12 5
// @param beats "Batidas por pulso" 0.25 4 1 0.25
// @param zoom "Zoom no pulso" 0 0.2 0.04
float4 Effect(float2 uv)
{
    // a cada batida a imagem dá um "soco" de brilho e zoom que some rápido
    float phase = frac(Beat / max(beats, 0.25));
    float pulse = exp(-phase * decay);
    float2 p = (uv - 0.5) / (1 + zoom * pulse) + 0.5;
    float4 c = tex(p);
    return float4(saturate(c.rgb * (1 + amount * pulse)), c.a);
}