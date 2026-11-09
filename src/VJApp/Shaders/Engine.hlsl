// Shaders do motor (os efeitos ficam em Shaders/Effects).
//  PSSource: desenha a fonte de uma layer (vídeo encaixado, plasma ou test card) numa textura 1920x1080.
//  PSBlend:  mistura a layer já com efeitos sobre a composição acumulada (opacidade + blend mode).
//  VSSlice/PSSlice: desenha os slices do mapeamento (com soft-edge) em cada tela de saída.
//  PSMask: pinta as máscaras de preto.   PSBlit: cópia reduzida para o preview da UI.
// Um único triângulo cobre a tela inteira (sem vertex buffer).

cbuffer Frame : register(b0)
{
    float2 Resolution;  // tamanho do alvo atual (composição ou saída)
    float  Time;
    float  Speed;
    float  Brightness;
    float  OutputMode;  // reservado
    float2 CompSize;    // tamanho da composição
    float  Beat;        // batidas desde o início (BPM); frac(Beat) = fase da batida
    float  Bpm;
    float2 _framePad;
};

cbuffer LayerParams : register(b1)
{
    float Opacity;
    float Blend;        // ordem igual ao enum BlendMode em C#
    float SourceKind;   // 0 = vídeo, 1 = plasma, 2 = test card
    float VideoAspect;
    float ColorMode;     // 0 = RGB, 1 = RGBA (alpha do vídeo), 2 = YCoCg escalado (HAP Q)
    float2 UvScale;      // parte útil da textura (HAP arredonda para múltiplos de 4)
    float TransitionMix; // 0..1
    float TransitionKind;// 0 corte, 1 crossfade, 2 escuro, 3 cortina
    float FitMode;       // ajuste do clip: 0 encaixar, 1 preencher, 2 esticar
    float Zoom;          // 1 = normal
    float OffsetX;       // deslocamento do clip (-1..1 = meia tela)
    float OffsetY;
    float ClipRotation;  // giro do clip (radianos)
    float ClipScaleX;    // largura e altura do clip (1 = 100%)
    float ClipScaleY;
    float4 ClipRadius;   // cantos do clip em pixels: ↖ ↗ ↘ ↙
    float LayerX;        // posição da layer inteira (-1..1 = meia tela)
    float LayerY;
    float LayerScaleX;   // largura e altura da layer (1 = 100%)
    float LayerScaleY;
    float LayerRotation; // giro da layer (radianos)
    float3 _layerPad;
    float4 LayerRadius;  // cantos da layer em pixels: ↖ ↗ ↘ ↙
};

float2 rotate2(float2 v, float a)
{
    float s = sin(a), c = cos(a);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// retângulo de cantos arredondados (um raio por canto): 1 dentro, 0 fora, borda suave de 1 pixel
float roundedMask(float2 p, float2 size, float4 r)
{
    if (all(r <= 0)) return 1;
    float2 h = size * 0.5;
    float2 q = p - h;
    float rad = q.x < 0 ? (q.y < 0 ? r.x : r.w) : (q.y < 0 ? r.y : r.z);
    rad = min(max(rad, 0), min(h.x, h.y));
    float2 d = abs(q) - h + rad;
    float sd = length(max(d, 0)) + min(max(d.x, d.y), 0) - rad;
    return saturate(0.5 - sd);
}

// leva um ponto da tela para dentro de um retângulo que foi movido, girado e escalado; devolve 0..1 dentro dele
float2 untransform(float2 uv, float2 offset, float rotation, float2 scale)
{
    float2 px = uv * Resolution - Resolution * (0.5 + float2(offset.x, -offset.y) * 0.5);
    px = rotate2(px, -rotation) / scale;
    return px / Resolution + 0.5;
}

Texture2D Tex0 : register(t0);
Texture2D Tex1 : register(t1);
SamplerState LinearClamp : register(s0);

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float3 hue(float h)
{
    return saturate(abs(frac(h + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1);
}

// ---------- geradores ----------

float3 testCard(float2 uv, float2 res, float t)
{
    float2 px = uv * res;
    float aspect = res.x / res.y;

    float3 col = hue(floor(uv.x * 8) / 8 + t * 0.05) * 0.35;           // barras de cor

    float2 cell = res / float2(16, 9);                                  // grade 16x9
    float2 g = abs(frac(px / cell + 0.5) - 0.5) * cell;
    col = lerp(col, float3(1, 1, 1), step(min(g.x, g.y), 1.0) * 0.8);

    float2 c = abs(px - res * 0.5);                                     // cruz central
    col = lerp(col, float3(1, 0.2, 0.2), step(min(c.x, c.y), 1.5));

    float2 d = (uv - 0.5) * float2(aspect, 1);                          // círculo
    col = lerp(col, float3(0.2, 1, 0.4), step(abs(length(d) - 0.4), 0.003));

    float2 e = min(px, res - px);                                       // borda
    col = lerp(col, float3(1, 1, 0), step(min(e.x, e.y), 4));
    return col;
}

// ---------- fundos gerados (antigo "Plasma"): tipo, cores, velocidade, intensidade, escala ----------

cbuffer GenParams : register(b5)
{
    float4 GenColor1;
    float4 GenColor2;
    float4 GenColor3;
    float  GenType;       // 0 ondas, 1 nuvens, 2 aurora, 3 luzes (bokeh), 4 gradiente, 5 feixes de luz, 6 arco-íris
    float  GenSpeed;      // multiplica o tempo
    float  GenIntensity;  // brilho final
    float  GenScale;      // tamanho das formas (maior = formas maiores)
    float  GenDetail;     // complexidade / contraste
    float  GenAngle;      // direção do movimento (radianos)
    float2 _genPad;
};

float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
}

float fbm(float2 p, int oct)
{
    float v = 0, a = 0.5;
    [loop] for (int k = 0; k < oct; k++) { v += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;
}

// 0..1 → as três cores, com transição suave entre elas
float3 palette3(float v)
{
    v = saturate(v);
    return v < 0.5 ? lerp(GenColor1.rgb, GenColor2.rgb, smoothstep(0, 1, v * 2)) : lerp(GenColor2.rgb, GenColor3.rgb, smoothstep(0, 1, v * 2 - 1));
}

float3 generator(float2 uv, float2 res, float t)
{
    float aspect = res.x / res.y;
    float2 p = (uv - 0.5) * float2(aspect, 1) / max(GenScale, 0.05);
    float2 dir = float2(cos(GenAngle), sin(GenAngle));
    float detail = saturate(GenDetail);
    float v;
    int type = (int)(GenType + 0.5);
    if (type == 1)            // nuvens: ruído fractal escorrendo devagar
    {
        float2 q = p * 1.5 + dir * t * 0.15;
        float n = fbm(q + fbm(q * 0.7 + t * 0.05, 3), 3 + (int)(detail * 3));
        v = smoothstep(0.2, 0.85, n);
    }
    else if (type == 2)       // aurora: faixas verticais que ondulam
    {
        float wave = sin(p.x * 2.2 + t * 0.6 + sin(p.x * 0.7 - t * 0.3) * 2) * 0.25;
        float band = exp(-pow((p.y - wave) * (2.5 + detail * 4), 2));
        float glow = fbm(float2(p.x * 1.5 + t * 0.2, p.y * 0.5), 3);
        v = saturate(band * (0.6 + glow * 0.8) + (1 - uv.y) * 0.25);
    }
    else if (type == 3)       // luzes desfocadas (bokeh) subindo devagar
    {
        float acc = 0;
        [loop] for (int k = 0; k < 14; k++)
        {
            float2 seed = float2(k * 1.37, k * 2.91);
            float2 c = float2(hash21(seed) - 0.5, hash21(seed + 3.1) - 0.5) * float2(aspect, 1) * 1.6 / max(GenScale, 0.05);
            c += dir * frac(t * 0.03 * (0.5 + hash21(seed + 7))) * 3 - dir * 1.5;
            float r = (0.12 + hash21(seed + 11) * 0.25) * (0.6 + detail);
            acc += smoothstep(r, r * 0.3, length(p - c)) * (0.35 + 0.65 * hash21(seed + 5));
        }
        float bg = 0.15 + 0.2 * uv.y;
        v = saturate(bg + acc * 0.55);
    }
    else if (type == 4)       // gradiente que "respira" girando devagar
    {
        float a = t * 0.08 + GenAngle;
        float g = dot(p * GenScale, float2(cos(a), sin(a))) * 0.6 + 0.5;
        v = g + sin(t * 0.4) * 0.1 * (0.5 + detail);
    }
    else if (type == 5)       // feixes de luz vindo de cima
    {
        float2 o = float2(0, -0.7);
        float2 d = p * max(GenScale, 0.05) - o;
        float ang = atan2(d.x, d.y);
        float rays = 0.5 + 0.5 * sin(ang * (6 + detail * 14) + t * 0.5) * sin(ang * 3.3 - t * 0.31);
        float fall = saturate(1.1 - length(d) * 0.55);
        v = saturate(rays * fall + fall * 0.35);
    }
    else if (type == 6)       // arco-íris (o plasma colorido de antes)
    {
        float2 q = p * 6 * GenScale * 0.5;
        float s = sin(q.x + t) + sin(q.y * 1.3 + t * 1.1) + sin(length(q) * 1.5 - t * 1.7);
        return hue(s * 0.15 + t * 0.05) * GenIntensity;
    }
    else                      // ondas suaves
    {
        float2 q = p * (1.5 + detail * 3);
        float s = sin(q.x + t) + sin(q.y * 1.3 - t * 0.8) + sin(length(q + dir * sin(t * 0.3)) * 1.4 - t * 1.2);
        v = 0.5 + s / 6;
    }
    return palette3(v) * GenIntensity;
}
float3 plasma(float2 uv, float2 res, float t)
{
    float2 p = (uv - 0.5) * float2(res.x / res.y, 1) * 6;
    float v = sin(p.x + t) + sin(p.y * 1.3 + t * 1.1) + sin(length(p) * 1.5 - t * 1.7);
    return hue(v * 0.15 + t * 0.05);
}

// encaixa uma imagem de proporção srcAspect dentro do alvo; alpha 0 fora dela
float4 fit(Texture2D tex, float2 uv, float dstAspect, float srcAspect)
{
    if (srcAspect <= 0) return float4(0, 0, 0, 0);
    float2 scale = dstAspect > srcAspect
        ? float2(dstAspect / srcAspect, 1)
        : float2(1, srcAspect / dstAspect);
    float2 v = (uv - 0.5) * scale + 0.5;
    if (any(v < 0) || any(v > 1)) return float4(0, 0, 0, 0);
    return float4(tex.SampleLevel(LinearClamp, v, 0).rgb, 1);
}

// ---------- fonte da layer ----------

// HAP Q guarda a cor como YCoCg escalado dentro de DXT5 (conversão da especificação do HAP)
float3 scaledYCoCgToRgb(float4 c)
{
    float scale = c.z * (255.0 / 8.0) + 1.0;
    float co = (c.x - 0.5 * 256.0 / 255.0) / scale;
    float cg = (c.y - 0.5 * 256.0 / 255.0) / scale;
    float y = c.w;
    return saturate(float3(y + co - cg, y + cg, y - co - cg));
}

// vídeo encaixado mantendo a proporção; fora da imagem fica transparente
float4 videoSource(float2 uv)
{
    float dstAspect = Resolution.x / Resolution.y;
    float2 scale = dstAspect > VideoAspect
        ? float2(dstAspect / VideoAspect, 1)
        : float2(1, VideoAspect / dstAspect);
    if (FitMode > 1.5) scale = float2(1, 1);                       // esticar
    else if (FitMode > 0.5) scale = dstAspect > VideoAspect         // preencher (corta as sobras)
        ? float2(1, VideoAspect / dstAspect)
        : float2(dstAspect / VideoAspect, 1);
    // zoom, largura/altura, giro e posição do clip
    float2 size = max(Zoom, 0.01) * (ClipScaleX == 0 && ClipScaleY == 0 ? float2(1, 1) : max(abs(float2(ClipScaleX, ClipScaleY)), 0.01));
    uv = untransform(uv, float2(OffsetX, OffsetY), ClipRotation, size);
    float2 v = (uv - 0.5) * scale + 0.5;
    if (any(v < 0) || any(v > 1)) return float4(0, 0, 0, 0);
    float2 shown = Resolution / scale * size; // tamanho da imagem na tela, em pixels
    float mask = roundedMask(v * shown, shown, ClipRadius);
    float4 c = Tex0.SampleLevel(LinearClamp, v * UvScale, 0);
    if (ColorMode > 2.5) return c.a > 0.0001 ? float4(c.rgb / c.a, c.a) : float4(0, 0, 0, 0); // pré-multiplicado (letras)
    if (ColorMode > 1.5) return float4(scaledYCoCgToRgb(c), mask);
    if (ColorMode > 0.5) return float4(c.rgb, c.a * mask);
    return float4(c.rgb, mask);
}

float4 PSSource(VSOut i) : SV_Target
{
    float t = Time * Speed;
    if (SourceKind < 0.5) return videoSource(i.uv);
    if (SourceKind < 1.5) return float4(generator(i.uv, Resolution, Time * Speed * GenSpeed), 1);
    if (SourceKind < 2.5) return float4(testCard(i.uv, Resolution, t), 1);
    // testes do edge blending: branco, cinza 50% e grade para alinhar os projetores
    if (SourceKind < 3.5) return float4(1, 1, 1, 1);
    if (SourceKind < 4.5) return float4(0.5, 0.5, 0.5, 1);
    float2 px = i.uv * Resolution;
    float2 g = abs(frac(px / 60 + 0.5) - 0.5) * 60;
    float lines = (min(g.x, g.y) < 1.2) ? 1 : 0;
    float2 c = abs(px - Resolution * 0.5);
    float cross = (min(c.x, c.y) < 2.5) ? 1 : 0;
    return float4(lerp(float3(0, 0, 0), float3(1, 1, 1), max(lines * 0.8, cross)), 1);
}

// ---------- transição entre o clip que entra (Tex0) e o que sai (Tex1) ----------

float4 sampleIn(Texture2D t, float2 uv)
{
    return any(uv < 0) || any(uv > 1) ? float4(0, 0, 0, 0) : t.SampleLevel(LinearClamp, uv, 0);
}

float4 blur9(Texture2D t, float2 uv, float r)
{
    float4 acc = 0;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
            acc += t.SampleLevel(LinearClamp, uv + float2(x, y) * float2(r * 9.0 / 16.0, r), 0);
    return acc / 9;
}

float4 PSTransition(VSOut i) : SV_Target
{
    float p = saturate(TransitionMix);
    float e = p * p * (3 - 2 * p);       // começa e termina devagar: mais confortável de ver
    int kind = (int)(TransitionKind + 0.5);
    float2 uv = i.uv;
    if (kind == 4)                       // deslizar: o novo empurra o anterior para a esquerda
        return uv.x < 1 - e ? sampleIn(Tex1, uv + float2(e, 0)) : sampleIn(Tex0, uv - float2(1 - e, 0));
    if (kind == 5)                       // aproximar: o anterior cresce um pouco e some, o novo assenta no lugar
    {
        float4 o = sampleIn(Tex1, (uv - 0.5) / (1 + 0.08 * e) + 0.5);
        float4 n = sampleIn(Tex0, (uv - 0.5) / (1.08 - 0.08 * e) + 0.5);
        return lerp(o, n, e);
    }
    if (kind == 6)                       // desfocar: os dois desfocam no meio da troca
    {
        float r = sin(3.14159 * p) * 0.01;
        return lerp(blur9(Tex1, uv, r), blur9(Tex0, uv, r), e);
    }
    float4 inc = Tex0.SampleLevel(LinearClamp, uv, 0);
    float4 outc = Tex1.SampleLevel(LinearClamp, uv, 0);
    if (kind == 7) return lerp(outc, inc, p); // mistura fixa (os dois clips de uma transição interrompida)
    if (kind == 3)                       // cortina: o novo entra da esquerda para a direita, com borda suave
    {
        float edge = e * 1.12 - 0.06;
        return lerp(outc, inc, 1 - smoothstep(edge - 0.06, edge + 0.06, uv.x));
    }
    if (kind == 2)                       // escurecer: apaga o anterior e acende o novo
    {
        float a = saturate(p * 2), b = saturate(p * 2 - 1);
        a = a * a * (3 - 2 * a);
        b = b * b * (3 - 2 * b);
        return p < 0.5 ? float4(outc.rgb * (1 - a), outc.a) : float4(inc.rgb * b, inc.a);
    }
    return lerp(outc, inc, e);           // crossfade
}
// ---------- mistura na composição ----------

float lum(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }
float3 clipColor(float3 c)
{
    float l = lum(c), n = min(min(c.r, c.g), c.b), x = max(max(c.r, c.g), c.b);
    if (n < 0) c = l + (c - l) * l / max(l - n, 1e-5);
    if (x > 1) c = l + (c - l) * (1 - l) / max(x - l, 1e-5);
    return c;
}
float3 setLum(float3 c, float l) { return clipColor(c + (l - lum(c))); }
float satOf(float3 c) { return max(max(c.r, c.g), c.b) - min(min(c.r, c.g), c.b); }
float3 setSat(float3 c, float s)
{
    float mx = max(max(c.r, c.g), c.b), mn = min(min(c.r, c.g), c.b);
    return mx > mn ? (c - mn) * s / (mx - mn) : float3(0, 0, 0);
}

float3 blend(float3 b, float3 s, int mode)
{
    b = saturate(b);
    s = saturate(s);
    switch (mode)
    {
        case 8: return b - s;                                                   // Subtrair
        case 9: return b + s - 2 * b * s;                                       // Exclusão
        case 10: return s < 0.5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s);        // Luz forte
        case 11: return (1 - 2 * s) * b * b + 2 * s * b;                        // Luz suave
        case 12: return s >= 1 ? float3(1, 1, 1) : saturate(b / (1 - s));       // Subexpor cor
        case 13: return s <= 0 ? float3(0, 0, 0) : 1 - saturate((1 - b) / s);   // Superexpor cor
        case 14: return b + 2 * s - 1;                                          // Luz linear
        case 15: return b / max(s, 0.001);                                      // Dividir
        case 16: return setLum(setSat(s, satOf(b)), lum(b));                    // Matiz
        case 17: return setLum(setSat(b, satOf(s)), lum(b));                    // Saturação
        case 18: return setLum(s, lum(b));                                      // Cor
        case 19: return setLum(b, lum(s));                                      // Luminosidade
        case 1: return b + s;                                   // Add
        case 2: return b * s;                                   // Multiply
        case 3: return 1 - (1 - b) * (1 - s);                   // Screen
        case 4: return b < 0.5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s); // Overlay
        case 5: return abs(b - s);                              // Difference
        case 6: return max(b, s);                               // Lighten
        case 7: return min(b, s);                               // Darken
        default: return s;                                      // Normal
    }
}

float4 PSBlend(VSOut i) : SV_Target
{
    float3 base = Tex0.SampleLevel(LinearClamp, i.uv, 0).rgb;
    // posição, tamanho, giro e cantos da layer inteira
    float2 size = LayerScaleX == 0 && LayerScaleY == 0 ? float2(1, 1) : max(abs(float2(LayerScaleX, LayerScaleY)), 0.01);
    float2 luv = untransform(i.uv, float2(LayerX, LayerY), LayerRotation, size);
    float4 src = any(luv < 0) || any(luv > 1) ? float4(0, 0, 0, 0) : Tex1.SampleLevel(LinearClamp, luv, 0);
    src.a *= roundedMask(luv * Resolution * size, Resolution * size, LayerRadius);
    float3 mixed = blend(base, src.rgb, (int)Blend);
    return float4(saturate(lerp(base, mixed, Opacity * saturate(src.a))), 1);
}

// ---------- saída: slices do mapeamento ----------

struct SliceIn
{
    float2 pos   : POSITION;   // 0..1 na tela de saída
    float2 uv    : TEXCOORD0;  // 0..1 na composição
    float2 st    : TEXCOORD1;  // 0..1 dentro do slice
    float4 edges : TEXCOORD2;  // soft-edge: esquerda, direita, cima, baixo
    float2 extra : TEXCOORD3;  // gamma do soft-edge, opacidade
    float3 blend : TEXCOORD4;  // edge blending: forma da curva, gamma do projetor, subida do preto
};

struct SliceOut
{
    float4 pos   : SV_Position;
    float2 uv    : TEXCOORD0;
    float2 st    : TEXCOORD1;
    float4 edges : TEXCOORD2;
    float2 extra : TEXCOORD3;
    float3 blend : TEXCOORD4;
};

SliceOut VSSlice(SliceIn v)
{
    SliceOut o;
    o.pos = float4(v.pos.x * 2 - 1, 1 - v.pos.y * 2, 0, 1);
    o.uv = v.uv;
    o.st = v.st;
    o.edges = v.edges;
    o.extra = v.extra;
    o.blend = v.blend;
    return o;
}

// rampa do edge blending (curva de Paul Bourke): f(x) + f(1-x) = 1 para qualquer forma p,
// então os dois projetores somam sempre 100% de luz no meio da emenda
float ramp(float x, float width, float p)
{
    if (width <= 0) return 1;
    float r = saturate(x / width);
    return r < 0.5 ? 0.5 * pow(2 * r, p) : 1 - 0.5 * pow(2 * (1 - r), p);
}

float4 PSSlice(SliceOut i) : SV_Target
{
    float3 col = Tex0.SampleLevel(LinearClamp, i.uv, 0).rgb;
    float p = max(i.blend.x, 1);
    float edge = ramp(i.st.x, i.edges.x, p) * ramp(1 - i.st.x, i.edges.y, p)
               * ramp(i.st.y, i.edges.z, p) * ramp(1 - i.st.y, i.edges.w, p);
    // o projetor transforma o valor do pixel em luz com gamma (~2,2): a rampa é em luz, o pixel vai com 1/gamma
    float k = pow(edge, 1 / max(i.blend.y, 1)) ;
    k = pow(k, i.extra.x) * i.extra.y * Brightness;
    // preto: onde dois projetores se somam, o preto fica mais claro; sobe o preto do resto para igualar
    float outside = edge >= 0.9999 ? 1 : 0;
    return float4(saturate(col * k + i.blend.z * outside), 1);
}

float4 PSMask(SliceOut i) : SV_Target
{
    return float4(0, 0, 0, 1);
}

// cópia simples (preview reduzido da composição)
float4 PSBlit(VSOut i) : SV_Target
{
    return float4(Tex0.SampleLevel(LinearClamp, i.uv, 0).rgb, 1);
}

// ---------- ataques (por cima de tudo, antes das telas) ----------

cbuffer AttackParams : register(b3)
{
    float  AType;      // 0 flash, 1 strobe, 2 zoom, 3 tremor, 4 inverter, 5 onda, 6 varredura
    float  AEnv;       // 0..1 força agora (entra, segura, sai)
    float  AIntensity;
    float  AAmount;
    float3 AColor;
    float  AProgress;  // 0..1 desde o disparo
    float  ACount;
    float  AElapsed;
    float  ASeed;
    float  _aPad;
};

float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

float4 PSAttack(VSOut i) : SV_Target
{
    float2 uv = i.uv;
    float k = AEnv * AIntensity;
    int type = (int)AType;

    if (type == 2)           // zoom: a imagem "salta" para perto e volta
        uv = (uv - 0.5) / (1 + AAmount * AEnv) + 0.5;
    else if (type == 3)      // tremor: deslocamento aleatório que muda rápido
    {
        float t = floor(AElapsed * 40);
        uv += (float2(hash(float2(t, ASeed)), hash(float2(ASeed, t))) - 0.5) * 2 * AAmount * AEnv;
    }

    float3 c = Tex0.SampleLevel(LinearClamp, uv, 0).rgb;

    if (type == 0)           // flash: cor por cima (branco, preto = blackout, qualquer cor)
        c = lerp(c, AColor, k);
    else if (type == 1)      // strobe: piscadas da cor
        c = lerp(c, AColor, k * step(frac(AProgress * ACount), 0.5));
    else if (type == 2)      // zoom: um leve clarão junto
        c = lerp(c, AColor, k * 0.5);
    else if (type == 4)      // inverter
        c = lerp(c, 1 - c, k);
    else if (type == 5)      // onda: anel de cor que abre do centro
    {
        float aspect = Resolution.x / Resolution.y;
        float d = length((i.uv - 0.5) * float2(aspect, 1));
        float r = AProgress * 1.2;
        float ring = 1 - smoothstep(0, max(0.01, AAmount), abs(d - r));
        c = lerp(c, AColor, ring * k);
    }
    else if (type == 6)      // varredura: faixa de cor que atravessa a tela
    {
        float x = AProgress * (1 + 2 * AAmount) - AAmount;
        float band = 1 - smoothstep(0, max(0.01, AAmount), abs(i.uv.x - x));
        c = lerp(c, AColor, band * k);
    }
    return float4(c, 1);
}
// ---------- letras: pedaços (palavras, linhas, caixa) que se movem sozinhos ----------

cbuffer SpriteParams : register(b4)
{
    float4 SDest;       // x, y, largura, altura (pixels da composição)
    float4 SSrc;        // mesmo retângulo na imagem de origem (0..1)
    float2 STranslate;  // deslocamento (pixels)
    float  SRotation;   // radianos
    float  SScale;
    float2 SPivot;      // centro do giro/escala (pixels)
    float  SOpacity;
    float  SBrightness;
    float4 SClip;       // retângulo original da palavra (0..1): o efeito não pega nada de fora dele
    float  SFx;         // 0 nenhum, 1 ondular, 2 óculos 3D
    float  SFxForce;
    float  SFxTime;
    float  SAspect;     // largura / altura da imagem
};

struct SpriteOut
{
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

SpriteOut VSSprite(uint id : SV_VertexID)
{
    // dois triângulos: 0 1 2 / 2 1 3
    static const float2 corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
    float2 k = corners[id];
    float2 p = SDest.xy + k * SDest.zw - SPivot;
    float s = sin(SRotation), c = cos(SRotation);
    p = float2(p.x * c - p.y * s, p.x * s + p.y * c) * SScale + SPivot + STranslate;
    SpriteOut o;
    o.pos = float4(p.x / CompSize.x * 2 - 1, 1 - p.y / CompSize.y * 2, 0, 1);
    o.uv = SSrc.xy + k * SSrc.zw;
    return o;
}

float SpriteHash(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

// lê a palavra sem pegar nada de fora do retângulo dela
float4 SpriteTex(float2 uv)
{
    float2 d = uv - SClip.xy;
    if (d.x < 0 || d.y < 0 || d.x > SClip.z || d.y > SClip.w) return 0;
    return Tex0.SampleLevel(LinearClamp, uv, 0);
}

// ondas descendo pelas letras: as hastes retas ficam onduladas (o tamanho acompanha a altura do texto)
float2 Ondular(float2 uv)
{
    const float TAU = 6.2831853;
    float h = SClip.w;
    float y = (uv.y - SClip.y) / h;                 // 0..1 na altura do texto
    float x = (uv.x - SClip.x) * SAspect / h;       // em "alturas do texto"
    float amp = 0.045 * SFxForce;
    float dx = sin(TAU * (2.2 * y + 0.35 * x - SFxTime)) * amp * h / SAspect;
    float dy = sin(TAU * (0.9 * x - SFxTime * 0.7)) * amp * 0.4 * h;
    return uv - float2(dx, dy);
}

// óculos 3D: vermelho para um lado, ciano para o outro, tremendo, com faixas que escapam para o lado
float4 Oculos3d(float2 uv)
{
    float k = floor(SFxTime * 14);
    float h = SClip.w;
    float baseX = 0.05 * SFxForce * h / SAspect;
    float off = baseX * (0.6 + 0.8 * SpriteHash(k));
    float band = floor((uv.y - SClip.y) / h * 9);
    float slice = SpriteHash(band + k * 7.1) > 0.72 ? (SpriteHash(band + k * 3.3 + 5) - 0.5) * baseX * 3 : 0;
    float2 u = uv + float2(slice, 0);
    float4 r = SpriteTex(u + float2(off, 0));
    float4 c = SpriteTex(u - float2(off, 0));
    float4 m = SpriteTex(u);
    float3 p = float3(1, 0.1, 0.2) * r.a;
    float a = r.a;
    p = p * (1 - c.a) + float3(0, 0.95, 1) * c.a;
    a = a + c.a * (1 - a);
    p = p * (1 - m.a) + m.rgb * m.a;
    a = a + m.a * (1 - a);
    return float4(saturate(p / max(a, 0.001) * SBrightness), a * SOpacity);
}

float4 PSSprite(SpriteOut i) : SV_Target
{
    if (SFx > 1.5) return Oculos3d(i.uv);
    float2 uv = SFx > 0.5 ? Ondular(i.uv) : i.uv;
    float4 c = SFx > 0.5 ? SpriteTex(uv) : Tex0.SampleLevel(LinearClamp, uv, 0);
    return float4(saturate(c.rgb * SBrightness), c.a * SOpacity);
}