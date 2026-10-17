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
    float2 _layerPad;
};

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
    float2 v = (uv - 0.5) * scale + 0.5;
    if (any(v < 0) || any(v > 1)) return float4(0, 0, 0, 0);
    float4 c = Tex0.SampleLevel(LinearClamp, v * UvScale, 0);
    if (ColorMode > 2.5) return c.a > 0.0001 ? float4(c.rgb / c.a, c.a) : float4(0, 0, 0, 0); // pré-multiplicado (letras)
    if (ColorMode > 1.5) return float4(scaledYCoCgToRgb(c), 1);
    if (ColorMode > 0.5) return c;
    return float4(c.rgb, 1);
}

float4 PSSource(VSOut i) : SV_Target
{
    float t = Time * Speed;
    if (SourceKind < 0.5) return videoSource(i.uv);
    if (SourceKind < 1.5) return float4(generator(i.uv, Resolution, Time * Speed * GenSpeed), 1);
    return float4(testCard(i.uv, Resolution, t), 1);
}

// ---------- transição entre o clip que entra (Tex0) e o que sai (Tex1) ----------

float4 PSTransition(VSOut i) : SV_Target
{
    float4 inc = Tex0.SampleLevel(LinearClamp, i.uv, 0);
    float4 outc = Tex1.SampleLevel(LinearClamp, i.uv, 0);
    float p = saturate(TransitionMix);
    if (TransitionKind > 2.5)            // cortina: o novo entra da esquerda para a direita, com borda suave
    {
        float edge = p * 1.06 - 0.03;
        return lerp(outc, inc, 1 - smoothstep(edge - 0.03, edge + 0.03, i.uv.x));
    }
    if (TransitionKind > 1.5)            // escuro: apaga o anterior e acende o novo
        return p < 0.5 ? float4(outc.rgb * (1 - 2 * p), outc.a)
                       : float4(inc.rgb * (2 * p - 1), inc.a);
    return lerp(outc, inc, p);           // crossfade
}
// ---------- mistura na composição ----------

float3 blend(float3 b, float3 s, int mode)
{
    switch (mode)
    {
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
    float4 src = Tex1.SampleLevel(LinearClamp, i.uv, 0);
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
};

struct SliceOut
{
    float4 pos   : SV_Position;
    float2 uv    : TEXCOORD0;
    float2 st    : TEXCOORD1;
    float4 edges : TEXCOORD2;
    float2 extra : TEXCOORD3;
};

SliceOut VSSlice(SliceIn v)
{
    SliceOut o;
    o.pos = float4(v.pos.x * 2 - 1, 1 - v.pos.y * 2, 0, 1);
    o.uv = v.uv;
    o.st = v.st;
    o.edges = v.edges;
    o.extra = v.extra;
    return o;
}

float ramp(float x, float width)
{
    if (width <= 0) return 1;
    float r = saturate(x / width);
    return r * r * (3 - 2 * r);
}

float4 PSSlice(SliceOut i) : SV_Target
{
    float3 col = Tex0.SampleLevel(LinearClamp, i.uv, 0).rgb;
    float edge = ramp(i.st.x, i.edges.x) * ramp(1 - i.st.x, i.edges.y)
               * ramp(i.st.y, i.edges.z) * ramp(1 - i.st.y, i.edges.w);
    float k = pow(edge, i.extra.x) * i.extra.y * Brightness;
    return float4(col * k, 1);
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

float4 PSSprite(SpriteOut i) : SV_Target
{
    float4 c = Tex0.SampleLevel(LinearClamp, i.uv, 0);
    return float4(saturate(c.rgb * SBrightness), c.a * SOpacity);
}