// @name Onda
// @param amount "Amplitude" 0 0.1 0.02
// @param freq "Frequência" 1 40 10
// @param speed "Velocidade" 0 10 2
float4 Effect(float2 uv)
{
    uv.x += sin(uv.y * freq + Time * speed) * amount;
    uv.y += cos(uv.x * freq * 0.7 + Time * speed * 1.3) * amount * 0.5;
    return tex(uv);
}