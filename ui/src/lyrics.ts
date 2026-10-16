// Formato das letras — mesma regra de Lyrics.Slides (C#):
// linha em branco separa slides; no começo de cada slide podem vir
//   [Refrão]                                   rótulo da seção (não vai para o telão)
//   {cena: Destaque}                           cena só deste slide
//   {estilo: tamanho=110; cor=#FFCC00; ...}    ajustes só deste slide
// e dentro do texto: <b> <i> <u> <cor #RRGGBB> <tam 130%> formatam trechos.

export type SlideOverrides = Record<string, string>
export type Slide = { label: string | null; scene: string | null; overrides: SlideOverrides; text: string }

const LABEL = /^\s*\[(.+?)\]\s*$/
const DIRECTIVE = /^\s*\{\s*([^:}]+?)\s*:\s*([^}]*)\}\s*$/

export function normalize(s: string) {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

/** Blocos brutos separados por linha em branco (inclusive os sem texto). */
function splitBlocks(text: string): string[] {
  return text.replace(/\r\n/g, '\n').split(/\n\s*\n/)
}

export function parseBlock(block: string): Slide | null {
  const lines = block.split('\n').map((l) => l.trimEnd()).filter((l) => l.length > 0)
  let label: string | null = null
  let scene: string | null = null
  const overrides: SlideOverrides = {}
  while (lines.length > 0) {
    let m: RegExpExecArray | null
    if ((m = LABEL.exec(lines[0]))) label = m[1]
    else if ((m = DIRECTIVE.exec(lines[0]))) {
      const key = normalize(m[1].trim())
      if (key === 'cena') scene = m[2].trim()
      else if (key === 'estilo')
        for (const part of m[2].split(';')) {
          const [k, ...v] = part.split('=')
          if (k.trim() && v.length) overrides[k.trim()] = v.join('=').trim()
        }
    } else break
    lines.shift()
  }
  if (lines.length === 0) return null
  return { label, scene, overrides, text: lines.join('\n') }
}

export function splitSlides(text: string): Slide[] {
  return splitBlocks(text).map(parseBlock).filter((s): s is Slide => s !== null)
}

export function serializeSlide(s: Slide): string {
  const lines: string[] = []
  if (s.label) lines.push(`[${s.label}]`)
  if (s.scene) lines.push(`{cena: ${s.scene}}`)
  const o = Object.entries(s.overrides).filter(([, v]) => v !== '')
  if (o.length) lines.push(`{estilo: ${o.map(([k, v]) => `${k}=${v}`).join('; ')}}`)
  lines.push(s.text.trim())
  return lines.join('\n')
}

/** Troca o slide de número <index> na letra, mantendo o resto do texto como está. */
export function replaceSlide(text: string, index: number, slide: Slide): string {
  const blocks = splitBlocks(text)
  let n = -1
  for (let i = 0; i < blocks.length; i++) {
    if (parseBlock(blocks[i]) === null) continue
    if (++n === index) { blocks[i] = serializeSlide(slide); break }
  }
  return blocks.map((b) => b.trim()).filter(Boolean).join('\n\n')
}

/** Texto sem marcações (para busca e prévias simples). */
export function stripTags(text: string) {
  return text.replace(/<\/?(b|i|u|cor|tam|anim|fonte)(\s+[^>]*)?>/gi, '')
}

/** Transições de slide (mesmos nomes do motor). */
export const SLIDE_TRANSITIONS: [string, string][] = [
  ['fade', 'Fade (suave)'], ['corte', 'Corte seco'], ['subir', 'Subir'], ['descer', 'Descer'],
  ['esquerda', 'Entrar pela direita'], ['direita', 'Entrar pela esquerda'], ['zoom', 'Zoom (crescer)'],
  ['aproximar', 'Aproximar (diminuir)'], ['palavra', 'Palavra por palavra'], ['linha', 'Linha por linha'],
  ['maquina', 'Máquina de escrever'],
]

/** Animações de trecho (<anim tipo intensidade velocidade>). */
export const WORD_ANIMS: [string, string][] = [
  ['balancar', 'Balançar'], ['flutuar', 'Flutuar'], ['pulsar', 'Pulsar'], ['onda', 'Onda'],
  ['brilhar', 'Brilhar'], ['tremer', 'Tremer'], ['piscar', 'Piscar'], ['girar', 'Girar'],
]

export type RichAnim = { type: string; intensity: number; speed: number }
export type RichRun = { text: string; bold: boolean; italic: boolean; underline: boolean; color: string | null; scale: number; anim: RichAnim | null; font: string | null }

/** Lê as marcações de trecho em pedaços com formatação (para a prévia dos slides na UI). */
export function parseRich(text: string): RichRun[] {
  const runs: RichRun[] = []
  let bold = false, italic = false, underline = false
  let color: string | null = null, scale = 1
  let anim: RichAnim | null = null
  let font: string | null = null
  const fonts: (string | null)[] = []
  const colors: (string | null)[] = [], scales: number[] = [], anims: (RichAnim | null)[] = []
  const re = /<(\/?)(b|i|u|cor|tam|anim|fonte)(?:\s+([^>]*))?>/gi
  let pos = 0
  let m: RegExpExecArray | null
  const emit = (s: string) => { if (s) runs.push({ text: s, bold, italic, underline, color, scale, anim, font }) }
  while ((m = re.exec(text))) {
    emit(text.slice(pos, m.index))
    pos = m.index + m[0].length
    const close = m[1] === '/'
    const arg = (m[3] ?? '').trim()
    switch (m[2].toLowerCase()) {
      case 'b': bold = !close; break
      case 'i': italic = !close; break
      case 'u': underline = !close; break
      case 'cor': if (close) color = colors.pop() ?? null; else { colors.push(color); color = arg } break
      case 'tam':
        if (close) scale = scales.pop() ?? 1
        else { scales.push(scale); const v = parseFloat(arg); if (!isNaN(v)) scale = arg.endsWith('%') ? v / 100 : v }
        break
      case 'fonte': if (close) font = fonts.pop() ?? null; else { fonts.push(font); font = arg.replace(/"/g, '') || null } break
      case 'anim':
        if (close) anim = anims.pop() ?? null
        else {
          anims.push(anim)
          const [t, i, s] = arg.split(/\s+/)
          anim = { type: normalize(t || 'balancar'), intensity: parseFloat(i) || 1, speed: parseFloat(s) || 1 }
        }
        break
    }
  }
  emit(text.slice(pos))
  return runs
}
