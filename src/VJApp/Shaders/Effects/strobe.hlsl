// @name Strobe
// @param rate "Piscadas por segundo (sem BPM)" 0.5 20 4
// @param duty "Tempo aceso" 0.05 0.95 0.5
// @param flash "Apagado: preto · branco" 0 1 0
// @param sync "Seguir o BPM (0 não · 1 sim)" 0 1 1 1
// @param beats "Batidas por piscada (com BPM)" 0.25 4 1 0.25
float4 Effect(float2 uv)
{
    float4 c = tex(uv);
    float phase = sync > 0.5 ? frac(Beat / max(beats, 0.25)) : frac(Time * rate);
    bool on = phase < duty;
    return on ? c : float4(flash.xxx, c.a);
}