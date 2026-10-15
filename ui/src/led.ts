import type { LedPart, LedWall, OutputScreen, Slice } from './bridge'

const COMP_ASPECT = 16 / 9
export const newId = () => Math.random().toString(16).slice(2, 10)

/** Tamanho da parte como ela aparece montada (girada 90/270 troca largura e altura). */
export function physSize(p: LedPart) {
  return p.rotation % 180 === 0 ? { w: p.w, h: p.h } : { w: p.h, h: p.w }
}

/** Recorte da composição com a proporção pedida, centralizado e preenchendo (sem faixas pretas). */
function coverRect(aspect: number) {
  if (aspect >= COMP_ASPECT) { const h = COMP_ASPECT / aspect; return { x: 0, y: (1 - h) / 2, w: 1, h } }
  const w = aspect / COMP_ASPECT
  return { x: (1 - w) / 2, y: 0, w, h: 1 }
}

/**
 * Cantos na saída (ordem do slice: sup.esq., sup.dir., inf.esq., inf.dir. DA IMAGEM) para a parte girada
 * no sentido horário. Ex.: 90° = a imagem "deita" para a direita no mapa da processadora.
 */
function cornerPoints(p: LedPart, outW: number, outH: number) {
  const x0 = p.x / outW, y0 = p.y / outH, x1 = (p.x + p.w) / outW, y1 = (p.y + p.h) / outH
  const TL = { x: x0, y: y0 }, TR = { x: x1, y: y0 }, BL = { x: x0, y: y1 }, BR = { x: x1, y: y1 }
  switch (((p.rotation % 360) + 360) % 360) {
    case 90: return [TR, BR, TL, BL]
    case 180: return [BR, BL, TR, TL]
    case 270: return [BL, TL, BR, TR]
    default: return [TL, TR, BL, BR]
  }
}

/** Retângulo que envolve o mapa físico. */
export function mapBounds(parts: LedPart[]) {
  if (!parts.length) return { x: 0, y: 0, w: 1, h: 1 }
  const xs = parts.flatMap((p) => [p.mapX, p.mapX + physSize(p).w])
  const ys = parts.flatMap((p) => [p.mapY, p.mapY + physSize(p).h])
  const x = Math.min(...xs), y = Math.min(...ys)
  return { x, y, w: Math.max(1, Math.max(...xs) - x), h: Math.max(1, Math.max(...ys) - y) }
}

/** Slices gerados pelo mapa de painéis: um por parte, já posicionado no mapa de pixels da processadora. */
export function ledSlices(led: LedWall, outW: number, outH: number): Slice[] {
  const b = mapBounds(led.parts)
  const whole = coverRect(b.w / b.h)
  return led.parts.map((p) => {
    const ps = physSize(p)
    const input = led.layout === 'unido'
      ? { x: whole.x + ((p.mapX - b.x) / b.w) * whole.w, y: whole.y + ((p.mapY - b.y) / b.h) * whole.h, w: (ps.w / b.w) * whole.w, h: (ps.h / b.h) * whole.h }
      : coverRect(ps.w / ps.h)
    return {
      id: `led-${p.id}`, name: p.name, enabled: true, opacity: 1, input, cols: 2, rows: 2,
      points: cornerPoints(p, outW, outH), softEdge: { left: 0, right: 0, top: 0, bottom: 0, gamma: 1 },
      source: led.layout === 'unido' ? led.source : p.source, ledPart: p.id,
    }
  })
}

/** Aplica o mapa na tela: troca os slices do LED e mantém os slices livres. */
export function applyLed(screen: OutputScreen, led: LedWall | null, outW: number, outH: number): OutputScreen {
  const free = screen.slices.filter((s) => !s.ledPart)
  return { ...screen, led, slices: led ? [...ledSlices(led, outW, outH), ...free] : free }
}

const part = (name: string, x: number, y: number, w: number, h: number, mapX: number, mapY: number, rotation = 0): LedPart =>
  ({ id: newId(), name, x, y, w, h, mapX, mapY, rotation, source: null })

/** Modelos prontos, no tamanho da saída (pixels). */
export const LED_TEMPLATES: { id: string; name: string; build: (W: number, H: number) => LedPart[] }[] = [
  { id: 'um', name: 'Painel único', build: (W, H) => [part('Painel', 0, 0, W, H, 0, 0)] },
  {
    id: 'laterais-centro', name: '3 partes: 2 laterais verticais + centro', build: (W, H) => {
      // na processadora: as 3 lado a lado; no palco: laterais em pé, centro deitado e mais baixo
      const side = Math.round(W * 0.2), center = W - 2 * side
      return [
        part('Lateral esquerda', 0, 0, side, H, 0, 0),
        part('Centro', side, 0, center, Math.round(H * 0.6), side + 100, Math.round(H * 0.2)),
        part('Lateral direita', side + center, 0, side, H, side + center + 200, 0),
      ]
    },
  },
  {
    id: 'dois', name: '2 partes lado a lado', build: (W, H) => [
      part('Esquerda', 0, 0, W / 2, H, 0, 0), part('Direita', W / 2, 0, W / 2, H, W / 2 + 100, 0),
    ],
  },
  {
    id: 'faixa', name: 'Faixa (4 partes)', build: (W, H) => [0, 1, 2, 3].map((i) =>
      part(`Parte ${i + 1}`, (W / 4) * i, 0, W / 4, H / 3, (W / 4) * i, 0)),
  },
]

/** Arranjos do mapa físico (como os painéis estão montados) sem mexer no mapa de pixels. */
export const LED_ARRANGEMENTS: { id: string; name: string; apply: (parts: LedPart[]) => LedPart[] }[] = [
  {
    id: 'faixa', name: 'Faixa (lado a lado, colados)', apply: (parts) => {
      let x = 0
      return parts.map((p) => { const r = { ...p, mapX: x, mapY: 0 }; x += physSize(p).w; return r })
    },
  },
  {
    id: 'laterais', name: 'Laterais + centro (centro na altura do meio)', apply: (parts) => {
      if (parts.length < 3) return parts
      const sides = [parts[0], parts[parts.length - 1]], mid = parts.slice(1, -1)
      const tall = Math.max(...sides.map((p) => physSize(p).h))
      const gap = 80
      let x = physSize(sides[0]).w + gap
      const out = [{ ...sides[0], mapX: 0, mapY: 0 }]
      for (const p of mid) { out.push({ ...p, mapX: x, mapY: (tall - physSize(p).h) / 2 }); x += physSize(p).w }
      out.push({ ...sides[1], mapX: x + gap, mapY: 0 })
      return out
    },
  },
  {
    id: 'quadrado', name: 'Bloco (em grade)', apply: (parts) => {
      const cols = Math.ceil(Math.sqrt(parts.length))
      const cw = Math.max(...parts.map((p) => physSize(p).w)), ch = Math.max(...parts.map((p) => physSize(p).h))
      return parts.map((p, i) => ({ ...p, mapX: (i % cols) * cw, mapY: Math.floor(i / cols) * ch }))
    },
  },
  {
    id: 'pilha', name: 'Empilhado (um em cima do outro)', apply: (parts) => {
      let y = 0
      return parts.map((p) => { const r = { ...p, mapX: 0, mapY: y }; y += physSize(p).h; return r })
    },
  },
]
