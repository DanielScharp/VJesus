// Mesma matemática de warp do motor (MappingMesh.cs), usada para desenhar e reamostrar no editor.
import type { Pt, Slice } from './bridge'

type V = { x: number; y: number }

/** Homografia do quadrado unitário para o quadrilátero (0 sup.esq., 1 sup.dir., 2 inf.esq., 3 inf.dir.). */
function homography(p: Pt[]): (s: number, t: number) => V {
  const x0 = p[0].x, y0 = p[0].y, x1 = p[1].x, y1 = p[1].y
  const x2 = p[3].x, y2 = p[3].y, x3 = p[2].x, y3 = p[2].y
  const dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3
  const dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3
  const den = dx1 * dy2 - dx2 * dy1
  let a, b, c, d, e, f, g, h
  if ((Math.abs(dx3) < 1e-9 && Math.abs(dy3) < 1e-9) || Math.abs(den) < 1e-12) {
    a = x1 - x0; b = x2 - x1; c = x0; d = y1 - y0; e = y2 - y1; f = y0; g = 0; h = 0
  } else {
    g = (dx3 * dy2 - dx2 * dy3) / den
    h = (dx1 * dy3 - dx3 * dy1) / den
    a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0
    d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0
  }
  return (s, t) => {
    const w = g * s + h * t + 1
    return { x: (a * s + b * t + c) / w, y: (d * s + e * t + f) / w }
  }
}

function catmullRom(p0: V, p1: V, p2: V, p3: V, t: number): V {
  const t2 = t * t, t3 = t2 * t
  const f = (a: number, b: number, c: number, d: number) =>
    0.5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (3 * b - a - 3 * c + d) * t3)
  return { x: f(p0.x, p1.x, p2.x, p3.x), y: f(p0.y, p1.y, p2.y, p3.y) }
}

function surface(slice: Slice, s: number, t: number): V {
  const P = (c: number, r: number) => {
    c = Math.min(Math.max(c, 0), slice.cols - 1)
    r = Math.min(Math.max(r, 0), slice.rows - 1)
    return slice.points[r * slice.cols + c]
  }
  const u = s * (slice.cols - 1), w = t * (slice.rows - 1)
  const ci = Math.min(Math.floor(u), slice.cols - 2), ri = Math.min(Math.floor(w), slice.rows - 2)
  const lu = u - ci, lw = w - ri
  const rows = [-1, 0, 1, 2].map((k) => catmullRom(P(ci - 1, ri + k), P(ci, ri + k), P(ci + 1, ri + k), P(ci + 2, ri + k), lu))
  return catmullRom(rows[0], rows[1], rows[2], rows[3], lw)
}

/** Posição na tela de saída do ponto (s,t) do slice. */
export function warpEvaluator(slice: Slice): (s: number, t: number) => V {
  return slice.cols === 2 && slice.rows === 2 ? homography(slice.points) : (s, t) => surface(slice, s, t)
}

/** Troca o tamanho da grade de controle mantendo a deformação atual. */
export function resampleGrid(slice: Slice, cols: number, rows: number): Pt[] {
  const ev = warpEvaluator(slice)
  const pts: Pt[] = []
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++) pts.push(ev(c / (cols - 1), r / (rows - 1)))
  return pts
}

export function defaultPoints(cols: number, rows: number, margin = 0): Pt[] {
  const pts: Pt[] = []
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++)
      pts.push({ x: margin + ((1 - 2 * margin) * c) / (cols - 1), y: margin + ((1 - 2 * margin) * r) / (rows - 1) })
  return pts
}

/** Linhas da grade deformada (para desenhar o slice no editor de saída). */
export function warpLines(slice: Slice, divisions = 8, samples = 24): V[][] {
  const ev = warpEvaluator(slice)
  const lines: V[][] = []
  for (let k = 0; k <= divisions; k++) {
    const a = k / divisions
    const h: V[] = [], v: V[] = []
    for (let i = 0; i <= samples; i++) {
      const b = i / samples
      h.push(ev(b, a))
      v.push(ev(a, b))
    }
    lines.push(h, v)
  }
  return lines
}

/** Contorno do slice (para clicar e arrastar o slice inteiro). */
export function warpOutline(slice: Slice, samples = 24): V[] {
  const ev = warpEvaluator(slice)
  const out: V[] = []
  for (let i = 0; i < samples; i++) out.push(ev(i / samples, 0))
  for (let i = 0; i < samples; i++) out.push(ev(1, i / samples))
  for (let i = samples; i > 0; i--) out.push(ev(i / samples, 1))
  for (let i = samples; i > 0; i--) out.push(ev(0, i / samples))
  return out
}
