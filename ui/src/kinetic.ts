// Tipografia cinética: o motor que desenha cada quadro de uma animação de letra num canvas 1920×1080.
// É uma função do tempo (o mesmo t sempre dá o mesmo quadro), usada na prévia e na gravação do vídeo.
import '@fontsource/anton/400.css'
import '@fontsource/bebas-neue/400.css'
import '@fontsource/montserrat/800.css'
import '@fontsource/montserrat/900.css'
import '@fontsource/oswald/700.css'

export const KW = 1920
export const KH = 1080

export type KEffect = 'esticar' | 'onda' | 'eco' | 'caos' | 'fatias' | 'digitar' | 'contador' | 'pular' | 'zoom' | 'subir'
export type KBgFx = 'liso' | 'estrelas' | 'faixa' | 'barras' | 'brilho'
export type KTransition = 'corte' | 'barras' | 'cortina' | 'circulo'

export type KScene = {
  id: string
  text: string
  /** segundos */
  duration: number
  effect: KEffect
  font: string
  /** 1 = tamanho automático (cabe na tela) */
  size: number
  bg: string
  color: string
  accent: string
  bgFx: KBgFx
  transition: KTransition
  upper: boolean
  /** "Espalhar palavra": a palavra que se espalha pelo fundo (vazio = a última da frase) */
  word?: string
}

export type KProject = {
  version: 1
  name: string
  /** segundos antes da primeira frase (a introdução da música) */
  lead: number
  scenes: KScene[]
  /** nome do arquivo da música usada para marcar o tempo */
  audioName?: string | null
  includeAudio?: boolean
}

export const EFFECTS: { id: KEffect; label: string; hint: string }[] = [
  { id: 'esticar', label: 'Esticar (letras altas)', hint: 'letras grandes e estreitas que crescem e ondulam na altura' },
  { id: 'onda', label: 'Onda com rastro', hint: 'as letras chegam em onda, deixando um rastro' },
  { id: 'eco', label: 'Eco', hint: 'cópias da frase se juntam até formar o texto' },
  { id: 'caos', label: 'Espalhar palavra', hint: 'a frase no meio e uma palavra espalhada girando pelo fundo' },
  { id: 'fatias', label: 'Fatias', hint: 'o texto entra em faixas que deslizam dos lados' },
  { id: 'digitar', label: 'Máquina de escrever', hint: 'letra por letra, com cursor' },
  { id: 'contador', label: 'Contador', hint: 'o número da primeira linha conta até o valor; o resto vai numa faixa' },
  { id: 'pular', label: 'Letras pulando', hint: 'cada letra aparece com um pulo, fora de ordem' },
  { id: 'zoom', label: 'Zoom em quadros', hint: 'quadros saem do centro e o texto cresce' },
  { id: 'subir', label: 'Palavra por palavra', hint: 'as palavras sobem uma de cada vez' },
]

export const BG_FX: { id: KBgFx; label: string }[] = [
  { id: 'liso', label: 'Cor lisa' },
  { id: 'estrelas', label: 'Estrelas' },
  { id: 'faixa', label: 'Faixa atrás do texto' },
  { id: 'barras', label: 'Barras pulsando' },
  { id: 'brilho', label: 'Brilho no centro' },
]

export const TRANSITIONS_K: { id: KTransition; label: string }[] = [
  { id: 'corte', label: 'Corte seco' },
  { id: 'barras', label: 'Barras subindo' },
  { id: 'cortina', label: 'Cortina (da esquerda)' },
  { id: 'circulo', label: 'Círculo' },
]

/** Fontes que vêm com o VJesus (funcionam sem internet) e o peso usado. */
export const BUILTIN_FONTS: { family: string; weight: number; label: string }[] = [
  { family: 'Anton', weight: 400, label: 'Anton (alta e estreita)' },
  { family: 'Bebas Neue', weight: 400, label: 'Bebas Neue (estreita)' },
  { family: 'Montserrat', weight: 900, label: 'Montserrat Black (larga)' },
  { family: 'Oswald', weight: 700, label: 'Oswald (estreita)' },
]
export const fontWeight = (family: string) => BUILTIN_FONTS.find((f) => f.family === family)?.weight ?? 700
const fontCss = (family: string, px: number) => `${fontWeight(family)} ${Math.max(1, px).toFixed(1)}px "${family.replace(/"/g, '')}"`

/** Combinações de cores no estilo de clipe de letra: fundo, texto, destaque. */
export const PALETTES: [string, string, string][] = [
  ['#05041a', '#19c8ff', '#19c8ff'],
  ['#e00000', '#ff4fbf', '#ffe600'],
  ['#1a0010', '#ff2aa8', '#ff2aa8'],
  ['#ff10a0', '#14001a', '#ffffff'],
  ['#ffe600', '#111111', '#ff10a0'],
  ['#0b0b08', '#ffe600', '#ffe600'],
  ['#05041a', '#19c8ff', '#19c8ff'],
  ['#0b0b08', '#3cff4a', '#3cff4a'],
]

export const projectDuration = (p: KProject) => p.lead + p.scenes.reduce((s, x) => s + Math.max(0.2, x.duration), 0)

export function newId() { return Math.random().toString(36).slice(2, 10) }

/** Monta as cenas a partir de uma letra colada: uma cena por linha (linhas juntas com " / " viram duas linhas na mesma cena). */
export function scenesFromLyrics(text: string, start = 0): KScene[] {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  return lines.map((line, i) => autoStyle(line.includes('/') ? line.replace(/\s*\/\s*/g, '\n') : balance(line), start + i))
}

/** Frase comprida numa linha só vira duas linhas parecidas (a letra fica maior na tela). */
function balance(line: string) {
  const words = line.split(/\s+/)
  if (line.length <= 18 || words.length < 3) return line
  let best = 1, diff = Infinity
  for (let i = 1; i < words.length; i++) {
    const d = Math.abs(words.slice(0, i).join(' ').length - words.slice(i).join(' ').length)
    if (d < diff) { diff = d; best = i }
  }
  return words.slice(0, best).join(' ') + '\n' + words.slice(best).join(' ')
}

/** Escolhe efeito, fonte e cores para uma frase (variando de uma para a outra). */
export function autoStyle(text: string, i: number): KScene {
  const words = text.split(/\s+/).filter(Boolean)
  const [bg, color, accent] = PALETTES[i % PALETTES.length]
  const plain = text.replace(/\n/g, ' ')
  let effect: KEffect
  let font = 'Montserrat'
  let bgFx: KBgFx = 'liso'
  if (/\d/.test(plain)) { effect = 'contador'; font = 'Anton'; bgFx = 'estrelas' }
  else if (words.length <= 2 && plain.length <= 14) { effect = i % 2 ? 'fatias' : 'esticar'; font = 'Anton' }
  else effect = (['onda', 'digitar', 'eco', 'caos', 'subir', 'pular', 'zoom'] as KEffect[])[i % 7]
  if (effect === 'digitar' || effect === 'zoom') bgFx = i % 2 ? 'barras' : 'liso'
  const duration = Math.round(Math.min(6, Math.max(2, 1.2 + words.length * 0.38)) * 10) / 10
  return { id: newId(), text, duration, effect, font, size: 1, bg, color, accent, bgFx, transition: i % 3 === 1 ? 'barras' : 'corte', upper: true }
}

// ---------------------------------------------------------------- utilidades

const clamp = (x: number, a = 0, b = 1) => Math.min(b, Math.max(a, x))
const easeOutCubic = (x: number) => 1 - Math.pow(1 - x, 3)
const easeInCubic = (x: number) => x * x * x
const easeOutBack = (x: number) => { const c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.pow(x - 1, 3) + c1 * Math.pow(x - 1, 2) }
const easeOutQuart = (x: number) => 1 - Math.pow(1 - x, 4)
const easeOutElastic = (x: number) => x <= 0 ? 0 : x >= 1 ? 1 : Math.pow(2, -10 * x) * Math.sin((x * 10 - 0.75) * (2 * Math.PI) / 3) + 1
/** Aleatório fixo (o mesmo quadro sempre sai igual). */
const rnd = (seed: number, i: number) => { const s = Math.sin(seed * 12.9898 + i * 78.233) * 43758.5453; return s - Math.floor(s) }
const hashStr = (s: string) => { let h = 7; for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) % 100003; return h }

type Char = { ch: string; x: number; w: number; k: number }
type Line = { text: string; width: number; y: number; chars: Char[] }
type Layout = { size: number; lines: Line[]; height: number; left: number; right: number; top: number; bottom: number; n: number }

const layoutCache = new Map<string, Layout>()
let measureCtx: CanvasRenderingContext2D | null = null

/**
 * Posição de cada letra (centralizada). O tamanho da fonte é o maior que cabe em maxW × maxH,
 * vezes o ajuste da cena. As posições vêm da largura dos começos da linha (respeita o espaçamento da fonte).
 */
function layout(text: string, family: string, maxW: number, maxH: number, mul: number, cy = KH / 2, gap = 1.12): Layout {
  const key = `${text}|${family}|${maxW}|${maxH}|${mul}|${cy}|${gap}`
  const hit = layoutCache.get(key)
  if (hit) return hit
  if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d')!
  const ctx = measureCtx
  const lines = text.split('\n')
  ctx.font = fontCss(family, 100)
  const w100 = lines.map((l) => ctx.measureText(l).width)
  const widest = Math.max(1, ...w100)
  let size = Math.min((maxW / widest) * 100, maxH / (lines.length * gap)) * mul
  size = Math.min(size, 900)
  ctx.font = fontCss(family, size)
  const lineH = size * gap
  const top0 = cy - (lines.length * lineH) / 2
  let k = 0
  const out: Line[] = lines.map((l, li) => {
    const width = ctx.measureText(l).width
    const x0 = KW / 2 - width / 2
    const chars: Char[] = []
    const glyphs = Array.from(l)
    let prefix = ''
    for (const ch of glyphs) {
      const before = ctx.measureText(prefix).width
      prefix += ch
      const after = ctx.measureText(prefix).width
      chars.push({ ch, x: x0 + before, w: after - before, k: k++ })
    }
    return { text: l, width, y: top0 + lineH * (li + 0.5), chars }
  })
  const widestPx = Math.max(...out.map((l) => l.width))
  const res: Layout = {
    size, lines: out, height: lines.length * lineH, n: k,
    left: KW / 2 - widestPx / 2, right: KW / 2 + widestPx / 2, top: top0, bottom: top0 + lines.length * lineH,
  }
  if (layoutCache.size > 400) layoutCache.clear()
  layoutCache.set(key, res)
  return res
}

/** Desenha uma letra centrada em (cx, cy), com escala e giro. */
function glyph(ctx: CanvasRenderingContext2D, ch: string, cx: number, cy: number, sx = 1, sy = 1, rot = 0) {
  if (ch === ' ' || sx === 0 || sy === 0) return
  ctx.save()
  ctx.translate(cx, cy)
  if (rot) ctx.rotate(rot)
  if (sx !== 1 || sy !== 1) ctx.scale(sx, sy)
  ctx.fillText(ch, 0, 0)
  ctx.restore()
}

function star(ctx: CanvasRenderingContext2D, x: number, y: number, r: number) {
  ctx.beginPath()
  for (let i = 0; i < 10; i++) {
    const a = -Math.PI / 2 + (i * Math.PI) / 5
    const rr = i % 2 ? r * 0.45 : r
    ctx.lineTo(x + Math.cos(a) * rr, y + Math.sin(a) * rr)
  }
  ctx.closePath()
  ctx.fill()
}

const withAlpha = (hex: string, a: number) => {
  const h = hex.replace('#', '')
  const n = parseInt(h.length === 3 ? h.split('').map((c) => c + c).join('') : h.slice(0, 6), 16)
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${clamp(a)})`
}

// ---------------------------------------------------------------- quadro

/** Qual cena está no tempo t (e o tempo dentro dela). */
export function sceneAt(p: KProject, t: number): { index: number; local: number } {
  let start = p.lead
  if (t < start || p.scenes.length === 0) return { index: -1, local: t - start }
  for (let i = 0; i < p.scenes.length; i++) {
    const d = Math.max(0.2, p.scenes[i].duration)
    if (t < start + d || i === p.scenes.length - 1) return { index: i, local: t - start }
    start += d
  }
  return { index: -1, local: 0 }
}

export function sceneStart(p: KProject, index: number) {
  let s = p.lead
  for (let i = 0; i < index; i++) s += Math.max(0.2, p.scenes[i].duration)
  return s
}

/** Desenha o quadro do tempo t (segundos) no canvas (qualquer tamanho; o desenho é em 1920×1080). */
export function drawFrame(ctx: CanvasRenderingContext2D, p: KProject, t: number) {
  const cw = ctx.canvas.width, ch = ctx.canvas.height
  ctx.setTransform(cw / KW, 0, 0, ch / KH, 0, 0)
  ctx.globalAlpha = 1
  const { index, local } = sceneAt(p, t)
  if (index < 0) {
    // introdução: o fundo da primeira cena, sem texto
    ctx.fillStyle = p.scenes[0]?.bg ?? '#000'
    ctx.fillRect(0, 0, KW, KH)
    return
  }
  const s = p.scenes[index]
  const TR = 0.45
  const prev = index > 0 ? p.scenes[index - 1] : null
  if (prev && s.transition !== 'corte' && local < TR) {
    // o fundo da cena anterior e a nova entrando por cima, recortada pela forma da transição
    ctx.fillStyle = prev.bg
    ctx.fillRect(0, 0, KW, KH)
    const q = easeOutCubic(clamp(local / TR))
    ctx.save()
    ctx.beginPath()
    if (s.transition === 'cortina') ctx.rect(0, 0, KW * q, KH)
    else if (s.transition === 'circulo') ctx.arc(KW / 2, KH / 2, Math.hypot(KW, KH) * 0.5 * q, 0, Math.PI * 2)
    else {
      const cols = 12, w = KW / cols
      for (let i = 0; i < cols; i++) {
        const qi = easeOutCubic(clamp((local - i * 0.018) / (TR - 0.2)))
        ctx.rect(i * w - 1, KH * (1 - qi), w + 2, KH * qi)
      }
    }
    ctx.clip()
    drawScene(ctx, s, local)
    ctx.restore()
    return
  }
  drawScene(ctx, s, local)
}

function drawScene(ctx: CanvasRenderingContext2D, s: KScene, t: number) {
  const D = Math.max(0.2, s.duration)
  ctx.fillStyle = s.bg
  ctx.fillRect(0, 0, KW, KH)
  const text = (s.upper ? s.text.toLocaleUpperCase('pt-BR') : s.text).trim()
  const seed = hashStr(s.id)
  const outDur = Math.min(0.35, D * 0.2)
  const pOut = clamp((t - (D - outDur)) / outDur)

  // fundo
  drawBgFx(ctx, s, t, seed, text)

  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  // com faixa atrás, o texto vai na cor do fundo (senão some na faixa)
  if (s.bgFx === 'faixa') s = { ...s, color: s.bg, accent: s.bg }
  ctx.fillStyle = s.color
  if (!text) return
  switch (s.effect) {
    case 'esticar': return fxStretch(ctx, s, text, t, pOut)
    case 'onda': return fxWave(ctx, s, text, t, pOut)
    case 'eco': return fxEcho(ctx, s, text, t, pOut)
    case 'caos': return fxChaos(ctx, s, text, t, pOut, seed)
    case 'fatias': return fxSlices(ctx, s, text, t, pOut)
    case 'digitar': return fxType(ctx, s, text, t, D, pOut)
    case 'contador': return fxCounter(ctx, s, text, t, D, pOut)
    case 'pular': return fxPop(ctx, s, text, t, pOut, seed)
    case 'zoom': return fxZoom(ctx, s, text, t, pOut)
    case 'subir': return fxWords(ctx, s, text, t, pOut)
  }
}

function drawBgFx(ctx: CanvasRenderingContext2D, s: KScene, t: number, seed: number, text: string) {
  switch (s.bgFx) {
    case 'estrelas': {
      ctx.fillStyle = s.accent
      for (let i = 0; i < 110; i++) {
        const x = ((rnd(seed, i) * KW - t * (8 + rnd(seed, i + 500) * 22)) % KW + KW) % KW
        const y = rnd(seed, i + 1000) * KH
        const tw = 0.5 + 0.5 * Math.sin(t * (1.5 + rnd(seed, i + 2000) * 3) + i)
        ctx.globalAlpha = 0.12 + 0.3 * tw
        star(ctx, x, y, 6 + rnd(seed, i + 3000) * 12)
      }
      ctx.globalAlpha = 1
      break
    }
    case 'faixa': {
      const L = layoutFor(s, text)
      const q = easeOutCubic(clamp(t / 0.45))
      const pad = L.size * 0.35
      ctx.fillStyle = s.accent
      ctx.fillRect(KW / 2 - (KW / 2) * q, L.top - pad, KW * q, L.height + pad * 2)
      break
    }
    case 'barras': {
      ctx.fillStyle = s.accent
      const cols = 16, w = KW / cols
      for (let i = 0; i < cols; i++) {
        const h1 = (0.04 + 0.1 * (0.5 + 0.5 * Math.sin(t * 3.1 + i * 1.7 + seed))) * KH
        const h2 = (0.04 + 0.1 * (0.5 + 0.5 * Math.sin(t * 2.7 + i * 2.3 + seed * 2))) * KH
        if (rnd(seed, i) > 0.35) ctx.fillRect(i * w + w * 0.1, 0, w * 0.8, h1)
        if (rnd(seed, i + 77) > 0.35) ctx.fillRect(i * w + w * 0.1, KH - h2, w * 0.8, h2)
      }
      break
    }
    case 'brilho': {
      const g = ctx.createRadialGradient(KW / 2, KH / 2, 0, KW / 2, KH / 2, KW * 0.55)
      const pulse = 0.28 + 0.08 * Math.sin(t * 2)
      g.addColorStop(0, withAlpha(s.accent, pulse))
      g.addColorStop(1, withAlpha(s.accent, 0))
      ctx.fillStyle = g
      ctx.fillRect(0, 0, KW, KH)
      break
    }
  }
}

/** Layout padrão da cena: até 84% da largura e 60% da altura. */
function layoutFor(s: KScene, text: string, maxW = KW * 0.84, maxH = KH * 0.6, cy = KH / 2) {
  return layout(text, s.font, maxW, maxH, s.size || 1, cy)
}

function setFont(ctx: CanvasRenderingContext2D, s: KScene, px: number) { ctx.font = fontCss(s.font, px) }

// ---------------------------------------------------------------- efeitos

function fxStretch(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  const L = layoutFor(s, text, KW * 0.94, KH * 0.72)
  setFont(ctx, s, L.size)
  const hold = clamp((t - 0.6) / 0.6) * (1 - pOut)
  for (const line of L.lines) {
    const base = line.y + L.size * 0.36 // apoiado na linha de base: cresce para cima
    for (const c of line.chars) {
      const pin = easeOutBack(clamp((t - c.k * 0.035) / 0.4))
      const po = easeInCubic(clamp((pOut * 1.4 - (L.n - c.k) * 0.02)))
      const wave = 1 + 0.28 * Math.sin(t * 3.2 + c.k * 0.8) * hold
      const sy = Math.max(0, pin * wave * (1 - po))
      if (sy <= 0.001) continue
      ctx.save()
      ctx.translate(c.x + c.w / 2, base)
      ctx.scale(1, sy)
      ctx.fillText(c.ch, 0, -L.size * 0.36)
      ctx.restore()
    }
  }
}

function fxWave(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  const L = layoutFor(s, text)
  setFont(ctx, s, L.size)
  // posição de uma letra num instante (entrada em onda vindo de baixo; saída subindo)
  const at = (c: Char, tt: number) => {
    const pin = clamp((tt - c.k * 0.03) / 0.6)
    const e = easeOutCubic(pin)
    const outStart = s.duration - Math.min(0.35, s.duration * 0.2)
    const po = easeInCubic(clamp((tt - outStart - c.k * 0.01) / 0.35))
    const dy = (1 - e) * KH * 0.35 + Math.sin(pin * Math.PI) * -L.size * 0.6 - po * KH * 0.4
    const rot = (1 - e) * 0.6 - po * 0.4
    return { dy, rot, a: Math.min(e * 1.4, 1) * (1 - po), moving: pin < 1 || po > 0 }
  }
  for (const line of L.lines) for (const c of line.chars) {
    const now = at(c, t)
    if (now.moving) {
      // rastro: cópias de instantes anteriores, cada vez mais apagadas
      for (let j = 6; j >= 1; j--) {
        const p = at(c, t - j * 0.022)
        ctx.globalAlpha = p.a * 0.32 * (1 - j / 7)
        glyph(ctx, c.ch, c.x + c.w / 2, line.y + p.dy, 1, 1, p.rot)
      }
    }
    ctx.globalAlpha = now.a
    glyph(ctx, c.ch, c.x + c.w / 2, line.y + now.dy, 1, 1, now.rot)
  }
  ctx.globalAlpha = 1
  void pOut
}

function fxEcho(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  const L = layoutFor(s, text)
  setFont(ctx, s, L.size)
  const pin = easeOutCubic(clamp(t / 0.7))
  const spread = (1 - pin) + easeInCubic(pOut)
  const copies = 7
  for (let j = copies; j >= 1; j--) {
    const off = j * L.size * 0.42 * spread
    const a = (1 - j / (copies + 1)) * 0.55 * Math.min(1, spread * 3)
    if (a <= 0.01) continue
    ctx.globalAlpha = a
    for (const sign of [-1, 1]) {
      for (const line of L.lines) for (const c of line.chars)
        glyph(ctx, c.ch, c.x + c.w / 2, line.y + sign * off, 1, 0.55)
    }
  }
  // o texto principal: se revela da esquerda para a direita enquanto as cópias se juntam
  ctx.globalAlpha = 1 - easeInCubic(pOut)
  const reveal = clamp(t / 0.55)
  for (const line of L.lines) for (const c of line.chars) {
    if (c.k / Math.max(1, L.n) > reveal) continue
    glyph(ctx, c.ch, c.x + c.w / 2, line.y)
  }
  ctx.globalAlpha = 1
}

function fxChaos(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number, seed: number) {
  const words = text.replace(/\n/g, ' ').split(/\s+/).filter(Boolean)
  const word = ((s.word ?? '').trim() || words[words.length - 1] || '').replace(/[,.;:!?]+$/, '')
  const word2 = (s.upper ? word.toLocaleUpperCase('pt-BR') : word)
  const L = layoutFor(s, text, KW * 0.6, KH * 0.32)
  // palavras espalhadas (fora do miolo onde fica a frase)
  const n = 70
  for (let i = 0; i < n; i++) {
    const appear = 0.15 + i * 0.018
    const p = easeOutBack(clamp((t - appear) / 0.3))
    if (p <= 0) continue
    let x = rnd(seed, i) * KW, y = rnd(seed, i + 100) * KH
    if (Math.abs(x - KW / 2) < (L.right - L.left) / 2 + 60 && Math.abs(y - KH / 2) < L.height / 2 + 40) y = y < KH / 2 ? L.top - 60 - rnd(seed, i + 9) * 200 : L.bottom + 60 + rnd(seed, i + 9) * 200
    const out = easeInCubic(pOut)
    x += (x - KW / 2) * out * 1.5
    y += (y - KH / 2) * out * 1.5
    const rot = (rnd(seed, i + 200) - 0.5) * Math.PI * 1.4 + Math.sin(t * 0.8 + i) * 0.1
    const px = 26 + rnd(seed, i + 300) * 34
    setFont(ctx, s, px)
    ctx.globalAlpha = 0.95 * (1 - out)
    ctx.fillStyle = s.color
    glyphWord(ctx, word2, x, y, p, rot)
  }
  ctx.globalAlpha = 1
  // a frase
  setFont(ctx, s, L.size)
  for (const line of L.lines) for (const c of line.chars) {
    const p = easeOutBack(clamp((t - c.k * 0.025) / 0.35))
    const out = easeInCubic(pOut)
    glyph(ctx, c.ch, c.x + c.w / 2, line.y, p * (1 + out * 0.6), p * (1 - out))
  }
}

function glyphWord(ctx: CanvasRenderingContext2D, w: string, x: number, y: number, sc: number, rot: number) {
  ctx.save()
  ctx.translate(x, y)
  ctx.rotate(rot)
  ctx.scale(sc, sc)
  ctx.fillText(w, 0, 0)
  ctx.restore()
}

function fxSlices(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  const L = layoutFor(s, text, KW * 0.84, KH * 0.56)
  setFont(ctx, s, L.size)
  const top = L.top - L.size * 0.1, h = L.height + L.size * 0.2
  const n = 14, sh = h / n
  for (let i = 0; i < n; i++) {
    const pin = easeOutCubic(clamp((t - i * 0.025) / 0.45))
    const po = easeInCubic(clamp(pOut * 1.3 - (n - i) * 0.015))
    const dir = i % 2 ? 1 : -1
    const dx = dir * ((1 - pin) * KW * 0.8 + po * KW * 0.8)
    ctx.save()
    ctx.beginPath()
    ctx.rect(0, top + i * sh, KW, sh + 0.5)
    ctx.clip()
    ctx.translate(dx, 0)
    for (const line of L.lines) ctx.fillText(line.text, KW / 2, line.y)
    ctx.restore()
  }
  // linhas finas embaixo, como no estilo "scanline"
  const lp = easeOutCubic(clamp((t - 0.3) / 0.5)) * (1 - pOut)
  if (lp > 0) {
    ctx.fillStyle = withAlpha(s.color, 0.55 * lp)
    for (let i = 0; i < 4; i++) ctx.fillRect(L.left, L.bottom + 12 + i * 12, (L.right - L.left) * lp, 3)
    ctx.fillStyle = s.color
  }
}

function fxType(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, D: number, pOut: number) {
  const L = layoutFor(s, text)
  setFont(ctx, s, L.size)
  const typeDur = Math.min(L.n * 0.06, D * 0.55)
  const shown = Math.floor(clamp(t / Math.max(0.01, typeDur)) * L.n)
  // saída: apaga de trás para a frente
  const kept = pOut > 0 ? Math.floor(L.n * (1 - pOut)) : shown
  let last: { x: number; y: number } | null = null
  for (const line of L.lines) for (const c of line.chars) {
    if (c.k >= kept) continue
    glyph(ctx, c.ch, c.x + c.w / 2, line.y)
    last = { x: c.x + c.w, y: line.y }
  }
  if (!last && L.lines[0]) last = { x: KW / 2 - L.lines[0].width / 2, y: L.lines[0].y }
  if (last && Math.floor(t * 2.5) % 2 === 0 && pOut < 1) {
    ctx.fillStyle = s.accent
    ctx.fillRect(last.x + L.size * 0.08, last.y + L.size * 0.32, L.size * 0.5, L.size * 0.1)
  }
}

function fxCounter(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, D: number, pOut: number) {
  const [first, ...rest] = text.split('\n')
  const digits = first.replace(/[^\d]/g, '')
  if (!digits) return fxWords(ctx, s, text, t, pOut)
  const target = Number(digits)
  const q = easeOutQuart(clamp(t / Math.max(0.5, D * 0.6)))
  const value = Math.round(target * q)
  const label = first.replace(/[\d.,]+/, value.toLocaleString('pt-BR'))
  // o número no tamanho do valor final (não muda de tamanho enquanto conta)
  const finalLabel = first.replace(/[\d.,]+/, target.toLocaleString('pt-BR'))
  const cy = rest.length ? KH * 0.4 : KH / 2
  const L = layout(finalLabel, s.font, KW * 0.86, KH * 0.34, s.size || 1, cy)
  setFont(ctx, s, L.size)
  ctx.globalAlpha = 1 - easeInCubic(pOut)
  ctx.fillStyle = s.color
  ctx.fillText(label, KW / 2, cy)
  if (rest.length) {
    // faixa com o resto do texto
    const bandTop = L.bottom + KH * 0.02
    const L2 = layout(rest.join('\n'), 'Montserrat', KW * 0.8, KH * 0.16, 1, 0)
    const bh = L2.height + L2.size * 0.6
    const bp = easeOutCubic(clamp((t - 0.2) / 0.5))
    ctx.fillStyle = s.accent
    ctx.fillRect(0, bandTop, KW * bp, bh)
    ctx.fillStyle = s.bg
    ctx.font = fontCss('Montserrat', L2.size)
    ctx.save()
    ctx.beginPath(); ctx.rect(0, bandTop, KW * bp, bh); ctx.clip()
    L2.lines.forEach((ln, i) => ctx.fillText(ln.text, KW / 2, bandTop + L2.size * 0.3 + L2.size * 1.12 * (i + 0.5)))
    ctx.restore()
  }
  ctx.globalAlpha = 1
}

function fxPop(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number, seed: number) {
  const L = layoutFor(s, text, KW * 0.8, KH * 0.5)
  const order = Array.from({ length: L.n }, (_, i) => i).sort((a, b) => rnd(seed, a) - rnd(seed, b))
  const rank = new Map(order.map((k, i) => [k, i]))
  for (const line of L.lines) for (const c of line.chars) {
    const r = rank.get(c.k) ?? 0
    const p = easeOutElastic(clamp((t - r * 0.05) / 0.6))
    if (p <= 0.001) continue
    const big = rnd(seed, c.k + 40) > 0.82
    const po = easeInCubic(clamp(pOut * 1.4 - rnd(seed, c.k + 9) * 0.4))
    setFont(ctx, s, L.size * (big ? 1.18 : 1))
    ctx.fillStyle = big ? s.accent : s.color
    const rot = (rnd(seed, c.k + 60) - 0.5) * 0.3 * (1 - clamp(t - r * 0.05 - 0.4))
    ctx.globalAlpha = 1 - po
    glyph(ctx, c.ch, c.x + c.w / 2, line.y + po * KH * 0.5, p, p, rot + po * (rnd(seed, c.k) - 0.5) * 2)
  }
  ctx.globalAlpha = 1
}

function fxZoom(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  // quadros saindo do centro
  ctx.strokeStyle = s.accent
  for (let i = 0; i < 4; i++) {
    const q = clamp((t - i * 0.09) / 0.8)
    if (q <= 0 || q >= 1) continue
    const e = easeInCubic(q)
    const w = KW * 1.3 * e, h = KH * 1.3 * e
    ctx.lineWidth = 18 + 40 * e
    ctx.globalAlpha = 1 - q * 0.3
    ctx.strokeRect(KW / 2 - w / 2, KH / 2 - h / 2, w, h)
  }
  ctx.globalAlpha = 1
  const L = layoutFor(s, text)
  setFont(ctx, s, L.size)
  const p = easeOutBack(clamp((t - 0.3) / 0.5))
  const po = easeInCubic(pOut)
  const sc = (0.3 + 0.7 * p) * (1 + po * 2)
  if (p <= 0) return
  ctx.save()
  ctx.translate(KW / 2, KH / 2)
  ctx.scale(sc, sc)
  ctx.globalAlpha = clamp(p * 2) * (1 - po)
  for (const line of L.lines) ctx.fillText(line.text, 0, line.y - KH / 2)
  ctx.restore()
  ctx.globalAlpha = 1
}

function fxWords(ctx: CanvasRenderingContext2D, s: KScene, text: string, t: number, pOut: number) {
  const L = layoutFor(s, text)
  setFont(ctx, s, L.size)
  // agrupa as letras em palavras
  let w = 0
  for (const line of L.lines) {
    let wordIdx = -1
    let inWord = false
    for (const c of line.chars) {
      if (c.ch === ' ') { inWord = false; continue }
      if (!inWord) { inWord = true; wordIdx = w++ }
      const p = easeOutCubic(clamp((t - wordIdx * 0.14) / 0.4))
      const po = easeInCubic(pOut)
      ctx.globalAlpha = p * (1 - po)
      const fresh = clamp((t - wordIdx * 0.14) / 0.5)
      ctx.fillStyle = fresh < 1 ? s.accent : s.color
      glyph(ctx, c.ch, c.x + c.w / 2, line.y + (1 - p) * L.size * 0.8 - po * L.size * 0.6)
    }
  }
  ctx.globalAlpha = 1
}

/** Espera as fontes da animação ficarem prontas (antes da prévia e da gravação). */
export async function ensureFonts(p: KProject) {
  const fams = new Set(p.scenes.map((s) => s.font).concat('Montserrat'))
  await Promise.all([...fams].map((f) => document.fonts.load(fontCss(f, 100), 'AÉÇÃ0').catch(() => [])))
  layoutCache.clear()
}
