import { useEffect, useRef, useState } from 'react'
import { send, type Display, type MappingSetup, type Mask, type OutputScreen, type Pt, type Slice } from './bridge'
import { defaultPoints, resampleGrid, warpLines, warpOutline } from './mappingMath'

type Props = {
  mapping: MappingSetup
  onChange: (m: MappingSetup) => void
  displays: Display[]
  preview: string | null
  testCard: boolean
  onTestCard: (on: boolean) => void
  presets: string[]
}

type Sel = { kind: 'slice' | 'mask'; id: string } | null

const COMP_ASPECT = 16 / 9
const GRID_SIZES = [2, 3, 4, 5]

const newId = () => Math.random().toString(16).slice(2, 10)
const clamp01 = (v: number) => Math.min(1, Math.max(0, v))

function newSlice(n: number): Slice {
  return {
    id: newId(), name: `Slice ${n}`, enabled: true, opacity: 1,
    input: { x: 0, y: 0, w: 1, h: 1 }, cols: 2, rows: 2, points: defaultPoints(2, 2, 0.1),
    softEdge: { left: 0, right: 0, top: 0, bottom: 0, gamma: 1 },
  }
}

function newMask(n: number): Mask {
  return { id: newId(), name: `Máscara ${n}`, enabled: true, points: [{ x: 0.4, y: 0.4 }, { x: 0.6, y: 0.4 }, { x: 0.6, y: 0.6 }, { x: 0.4, y: 0.6 }] }
}

/** Converte a posição do ponteiro em coordenadas 0..1 do SVG. */
function toNorm(svg: SVGSVGElement, e: { clientX: number; clientY: number }): Pt {
  const r = svg.getBoundingClientRect()
  return { x: (e.clientX - r.left) / r.width, y: (e.clientY - r.top) / r.height }
}

/** Arrasto genérico: chama onMove com o deslocamento normalizado desde o início. */
function useDrag() {
  const drag = useRef<{ svg: SVGSVGElement; start: Pt; onMove: (d: Pt, p: Pt) => void } | null>(null)
  const begin = (e: React.PointerEvent, svg: SVGSVGElement | null, onMove: (d: Pt, p: Pt) => void) => {
    if (!svg) return
    e.stopPropagation()
    ;(e.target as Element).setPointerCapture(e.pointerId)
    drag.current = { svg, start: toNorm(svg, e), onMove }
  }
  const move = (e: React.PointerEvent) => {
    const d = drag.current
    if (!d) return
    const p = toNorm(d.svg, e)
    d.onMove({ x: p.x - d.start.x, y: p.y - d.start.y }, p)
  }
  const end = () => { drag.current = null }
  return { begin, move, end }
}

export function MappingEditor({ mapping, onChange, displays, preview, testCard, onTestCard, presets }: Props) {
  const [preset, setPreset] = useState('')
  const [screenId, setScreenId] = useState<string | undefined>(mapping.screens[0]?.id)
  const [sel, setSel] = useState<Sel>(null)
  const [point, setPoint] = useState<number | null>(null)
  const inSvg = useRef<SVGSVGElement>(null)
  const outSvg = useRef<SVGSVGElement>(null)
  const drag = useDrag()

  const screen = mapping.screens.find((s) => s.id === screenId) ?? mapping.screens[0]
  const display = displays[screen.display]
  const outAspect = display ? display.width / display.height : COMP_ASPECT
  const slice = sel?.kind === 'slice' ? screen.slices.find((s) => s.id === sel.id) : undefined
  const mask = sel?.kind === 'mask' ? screen.masks.find((m) => m.id === sel.id) : undefined

  // ---- atualizações imutáveis ----
  const setScreen = (patch: Partial<OutputScreen>) =>
    onChange({ screens: mapping.screens.map((s) => (s.id === screen.id ? { ...s, ...patch } : s)) })
  const setSlice = (id: string, patch: Partial<Slice>) =>
    setScreen({ slices: screen.slices.map((s) => (s.id === id ? { ...s, ...patch } : s)) })
  const setMask = (id: string, patch: Partial<Mask>) =>
    setScreen({ masks: screen.masks.map((m) => (m.id === id ? { ...m, ...patch } : m)) })

  // ---- setas do teclado movem o ponto selecionado (1 pixel; Shift = 10) ----
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (point === null || !(slice || mask)) return
      if ((e.target as HTMLElement).tagName === 'INPUT' || (e.target as HTMLElement).tagName === 'SELECT') return
      const px = (e.shiftKey ? 10 : 1) / (display?.width ?? 1920)
      const py = (e.shiftKey ? 10 : 1) / (display?.height ?? 1080)
      const d = { ArrowLeft: [-px, 0], ArrowRight: [px, 0], ArrowUp: [0, -py], ArrowDown: [0, py] }[e.key]
      if (!d) return
      e.preventDefault()
      const pts = (slice ?? mask)!.points.map((p, i) => (i === point ? { x: p.x + d[0], y: p.y + d[1] } : p))
      if (slice) setSlice(slice.id, { points: pts })
      else setMask(mask!.id, { points: pts })
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const select = (s: Sel) => { setSel(s); setPoint(null) }

  const addScreen = () => {
    const s: OutputScreen = { id: newId(), name: `Tela ${mapping.screens.length + 1}`, display: 0, fullscreen: false, slices: [newSlice(1)], masks: [] }
    onChange({ screens: [...mapping.screens, s] })
    setScreenId(s.id)
    select(null)
  }

  const W = 1000
  const inH = W / COMP_ASPECT
  const outH = W / outAspect
  const path = (pts: Pt[], h: number) => pts.map((p, i) => `${i ? 'L' : 'M'}${p.x * W},${p.y * h}`).join(' ') + ' Z'

  return (
    <div className="mapping">
      {/* ---------- telas ---------- */}
      <div className="row tabs">
        {mapping.screens.map((s) => (
          <button key={s.id} className={s.id === screen.id ? 'active' : ''} onClick={() => { setScreenId(s.id); select(null) }}>
            {s.name}
          </button>
        ))}
        <button onClick={addScreen}>+ Tela</button>
        <span className="spacer" />
        <span className="presets">
          Presets:
          <select value={preset} onChange={(e) => setPreset(e.target.value)}>
            <option value="">—</option>
            {presets.map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
          <button className="small" disabled={!preset} onClick={() => { send({ type: 'presetLoad', name: preset }); select(null) }}>Carregar</button>
          <button className="small" onClick={() => {
            const name = window.prompt('Nome do preset de mapeamento (ex.: Teatro Municipal):', preset || '')
            if (name?.trim()) { send({ type: 'presetSave', name: name.trim() }); setPreset(name.trim()) }
          }}>Salvar…</button>
          <button className="small danger-text" disabled={!preset} onClick={() => {
            if (window.confirm(`Apagar o preset "${preset}"?`)) { send({ type: 'presetDelete', name: preset }); setPreset('') }
          }}>Apagar</button>
        </span>
        <label className="check">
          <input type="checkbox" checked={testCard} onChange={(e) => onTestCard(e.target.checked)} />
          Test card nas saídas
        </label>
      </div>

      <section className="screen-settings">
        <input className="name" value={screen.name} onChange={(e) => setScreen({ name: e.target.value })} />
        <select value={screen.display} onChange={(e) => setScreen({ display: Number(e.target.value) })}>
          {displays.map((d) => (
            <option key={d.index} value={d.index}>{d.name} · {d.width}×{d.height}{d.primary ? ' (principal)' : ''}</option>
          ))}
        </select>
        <label className="check">
          <input type="checkbox" checked={screen.fullscreen} onChange={(e) => setScreen({ fullscreen: e.target.checked })} />
          Tela cheia
        </label>
        <span className="spacer" />
        {mapping.screens.length > 1 && (
          <button className="small danger-text" onClick={() => {
            onChange({ screens: mapping.screens.filter((s) => s.id !== screen.id) })
            setScreenId(mapping.screens.find((s) => s.id !== screen.id)?.id)
            select(null)
          }}>Remover tela</button>
        )}
      </section>

      <div className="mapping-body">
        {/* ---------- lista de slices e máscaras ---------- */}
        <section className="items">
          <div className="row">
            <button className="small" onClick={() => {
              const s = newSlice(screen.slices.length + 1)
              setScreen({ slices: [...screen.slices, s] })
              select({ kind: 'slice', id: s.id })
            }}>+ Slice</button>
            <button className="small" onClick={() => {
              const m = newMask(screen.masks.length + 1)
              setScreen({ masks: [...screen.masks, m] })
              select({ kind: 'mask', id: m.id })
            }}>+ Máscara</button>
          </div>
          {screen.slices.map((s) => (
            <div key={s.id} className={`item ${sel?.id === s.id ? 'active' : ''} ${s.enabled ? '' : 'off'}`}
              onClick={() => select({ kind: 'slice', id: s.id })}>▭ {s.name}</div>
          ))}
          {screen.masks.map((m) => (
            <div key={m.id} className={`item mask ${sel?.id === m.id ? 'active' : ''} ${m.enabled ? '' : 'off'}`}
              onClick={() => select({ kind: 'mask', id: m.id })}>◼ {m.name}</div>
          ))}
        </section>

        {/* ---------- editores ---------- */}
        <div className="editors">
          <section>
            <h2>Entrada · o que cada slice pega da composição</h2>
            <svg ref={inSvg} className="editor" viewBox={`0 0 ${W} ${inH}`} onPointerMove={drag.move} onPointerUp={drag.end}
              onPointerDown={() => select(null)}>
              {preview ? <image href={preview} width={W} height={inH} preserveAspectRatio="none" /> : <rect width={W} height={inH} className="bg" />}
              {screen.slices.map((s) => {
                const r = s.input
                const active = sel?.id === s.id
                return (
                  <g key={s.id} className={`in-rect ${active ? 'active' : ''}`}>
                    <rect x={r.x * W} y={r.y * inH} width={r.w * W} height={r.h * inH}
                      onPointerDown={(e) => {
                        select({ kind: 'slice', id: s.id })
                        const start = { ...r }
                        drag.begin(e, inSvg.current, (d) => setSlice(s.id, {
                          input: { ...start, x: clamp01(Math.min(start.x + d.x, 1 - start.w)), y: clamp01(Math.min(start.y + d.y, 1 - start.h)) },
                        }))
                      }} />
                    <text x={r.x * W + 8} y={r.y * inH + 22}>{s.name}</text>
                    {active && [[0, 0], [1, 0], [0, 1], [1, 1]].map(([cx, cy]) => (
                      <circle key={`${cx}${cy}`} className="handle" r={9} cx={(r.x + cx * r.w) * W} cy={(r.y + cy * r.h) * inH}
                        onPointerDown={(e) => {
                          const start = { ...r }
                          drag.begin(e, inSvg.current, (_, p) => {
                            // arrasta o canto, mantendo o canto oposto fixo
                            const ox = start.x + (1 - cx) * start.w, oy = start.y + (1 - cy) * start.h
                            const px = clamp01(p.x), py = clamp01(p.y)
                            setSlice(s.id, { input: { x: Math.min(ox, px), y: Math.min(oy, py), w: Math.max(0.01, Math.abs(px - ox)), h: Math.max(0.01, Math.abs(py - oy)) } })
                          })
                        }} />
                    ))}
                  </g>
                )
              })}
            </svg>
          </section>

          <section>
            <h2>Saída · {screen.name} ({display ? `${display.width}×${display.height}` : '16:9'})</h2>
            <svg ref={outSvg} className="editor" viewBox={`0 0 ${W} ${outH}`} onPointerMove={drag.move} onPointerUp={drag.end}
              onPointerDown={() => select(null)}>
              <rect width={W} height={outH} className="bg" />
              {screen.slices.map((s) => {
                const active = sel?.id === s.id
                return (
                  <g key={s.id} className={`out-slice ${active ? 'active' : ''} ${s.enabled ? '' : 'off'}`}>
                    <path className="fill" d={path(warpOutline(s), outH)}
                      onPointerDown={(e) => {
                        select({ kind: 'slice', id: s.id })
                        const start = s.points
                        drag.begin(e, outSvg.current, (d) => setSlice(s.id, { points: start.map((p) => ({ x: p.x + d.x, y: p.y + d.y })) }))
                      }} />
                    {warpLines(s).map((line, i) => (
                      <polyline key={i} className="wire" points={line.map((p) => `${p.x * W},${p.y * outH}`).join(' ')} />
                    ))}
                    <text x={s.points[0].x * W + 8} y={s.points[0].y * outH + 22}>{s.name}</text>
                    {active && s.points.map((p, i) => (
                      <circle key={i} className={`handle ${point === i ? 'picked' : ''}`} r={10} cx={p.x * W} cy={p.y * outH}
                        onPointerDown={(e) => {
                          setPoint(i)
                          drag.begin(e, outSvg.current, (_, np) =>
                            setSlice(s.id, { points: s.points.map((q, k) => (k === i ? np : q)) }))
                        }} />
                    ))}
                  </g>
                )
              })}
              {screen.masks.map((m) => {
                const active = sel?.id === m.id
                return (
                  <g key={m.id} className={`out-mask ${active ? 'active' : ''} ${m.enabled ? '' : 'off'}`}>
                    <path d={path(m.points, outH)} onPointerDown={(e) => {
                      select({ kind: 'mask', id: m.id })
                      const start = m.points
                      drag.begin(e, outSvg.current, (d) => setMask(m.id, { points: start.map((p) => ({ x: p.x + d.x, y: p.y + d.y })) }))
                    }} />
                    {active && m.points.map((p, i) => (
                      <circle key={i} className={`handle ${point === i ? 'picked' : ''}`} r={10} cx={p.x * W} cy={p.y * outH}
                        onPointerDown={(e) => {
                          setPoint(i)
                          drag.begin(e, outSvg.current, (_, np) =>
                            setMask(m.id, { points: m.points.map((q, k) => (k === i ? np : q)) }))
                        }} />
                    ))}
                  </g>
                )
              })}
            </svg>
          </section>
        </div>

        {/* ---------- propriedades ---------- */}
        <section className="props">
          {slice && (
            <>
              <h2>Slice</h2>
              <input className="name" value={slice.name} onChange={(e) => setSlice(slice.id, { name: e.target.value })} />
              <label className="check">
                <input type="checkbox" checked={slice.enabled} onChange={(e) => setSlice(slice.id, { enabled: e.target.checked })} /> Ativo
              </label>
              <label>Opacidade <output>{slice.opacity.toFixed(2)}</output>
                <input type="range" min={0} max={1} step={0.01} value={slice.opacity}
                  onChange={(e) => setSlice(slice.id, { opacity: Number(e.target.value) })} />
              </label>
              <label>Grade de warp <output />
                <select value={slice.cols} onChange={(e) => {
                  const n = Number(e.target.value)
                  setSlice(slice.id, { cols: n, rows: n, points: resampleGrid(slice, n, n) })
                  setPoint(null)
                }}>
                  {GRID_SIZES.map((n) => <option key={n} value={n}>{n === 2 ? '4 cantos (perspectiva)' : `Malha ${n}×${n}`}</option>)}
                </select>
              </label>
              <h3>Soft-edge (junção de projetores)</h3>
              {(['left', 'right', 'top', 'bottom'] as const).map((k) => (
                <label key={k}>{{ left: 'Esquerda', right: 'Direita', top: 'Cima', bottom: 'Baixo' }[k]} <output>{slice.softEdge[k].toFixed(2)}</output>
                  <input type="range" min={0} max={0.5} step={0.005} value={slice.softEdge[k]}
                    onChange={(e) => setSlice(slice.id, { softEdge: { ...slice.softEdge, [k]: Number(e.target.value) } })} />
                </label>
              ))}
              <label>Curva (gamma) <output>{slice.softEdge.gamma.toFixed(2)}</output>
                <input type="range" min={0.2} max={3} step={0.01} value={slice.softEdge.gamma}
                  onChange={(e) => setSlice(slice.id, { softEdge: { ...slice.softEdge, gamma: Number(e.target.value) } })} />
              </label>
              <div className="row">
                <button className="small" onClick={() => setSlice(slice.id, { points: defaultPoints(slice.cols, slice.rows) })}>Tela inteira</button>
                <button className="small" onClick={() => setSlice(slice.id, { input: { x: 0, y: 0, w: 1, h: 1 } })}>Entrada inteira</button>
                <button className="small" onClick={() => {
                  const copy = { ...structuredClone(slice), id: newId(), name: `${slice.name} cópia` }
                  copy.points = copy.points.map((p) => ({ x: p.x + 0.03, y: p.y + 0.03 }))
                  setScreen({ slices: [...screen.slices, copy] })
                  select({ kind: 'slice', id: copy.id })
                }}>Duplicar</button>
                <button className="small danger-text" onClick={() => {
                  setScreen({ slices: screen.slices.filter((s) => s.id !== slice.id) })
                  select(null)
                }}>Remover</button>
              </div>
            </>
          )}
          {mask && (
            <>
              <h2>Máscara</h2>
              <input className="name" value={mask.name} onChange={(e) => setMask(mask.id, { name: e.target.value })} />
              <label className="check">
                <input type="checkbox" checked={mask.enabled} onChange={(e) => setMask(mask.id, { enabled: e.target.checked })} /> Ativa
              </label>
              <div className="row">
                <button className="small" onClick={() => {
                  // insere um ponto no meio do lado depois do ponto selecionado (ou do lado mais longo)
                  const pts = mask.points
                  let i = point ?? -1
                  if (i < 0) {
                    let best = 0
                    pts.forEach((p, k) => {
                      const q = pts[(k + 1) % pts.length]
                      const len = Math.hypot(q.x - p.x, q.y - p.y)
                      if (len > best) { best = len; i = k }
                    })
                  }
                  const a = pts[i], b = pts[(i + 1) % pts.length]
                  const next = [...pts.slice(0, i + 1), { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }, ...pts.slice(i + 1)]
                  setMask(mask.id, { points: next })
                  setPoint(i + 1)
                }}>+ Ponto</button>
                <button className="small" disabled={point === null || mask.points.length <= 3} onClick={() => {
                  setMask(mask.id, { points: mask.points.filter((_, k) => k !== point) })
                  setPoint(null)
                }}>− Ponto</button>
                <button className="small danger-text" onClick={() => {
                  setScreen({ masks: screen.masks.filter((m) => m.id !== mask.id) })
                  select(null)
                }}>Remover</button>
              </div>
            </>
          )}
          {!slice && !mask && (
            <div className="meta">
              Selecione um slice ou máscara. Na saída, arraste os pontos para encaixar a imagem na superfície;
              arraste o slice pelo meio para movê-lo. Com um ponto selecionado, as setas movem 1 pixel (Shift = 10).
            </div>
          )}
        </section>
      </div>
    </div>
  )
}
