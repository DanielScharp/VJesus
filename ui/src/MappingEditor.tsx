import { useEffect, useRef, useState } from 'react'
import { send, type Composition, type Display, type LayerGroup, type MappingSetup, type Mask, type OutputScreen, type Pt, type Slice } from './bridge'
import { applyLed } from './led'
import { matches } from './shortcuts'
import { LedMap, type SourceOption } from './LedMap'
import {
  defaultPoints, resampleGrid, SCREEN_TARGETS, shapeTargets, sliceCorners, snapPoint, snapShape, snapValue,
  warpLines, warpOutline, type SnapTargets,
} from './mappingMath'

type Props = {
  mapping: MappingSetup
  onChange: (m: MappingSetup) => void
  displays: Display[]
  preview: string | null
  testCard: boolean
  onTestCard: (on: boolean) => void
  presets: string[]
  comp: Composition | null
}

type Sel = { kind: 'slice' | 'mask'; id: string } | null
type Mods = { altKey: boolean }

const COMP_ASPECT = 16 / 9
const GRID_SIZES = [2, 3, 4, 5]
/** Raio do ímã em unidades do editor (o editor tem 1000 de largura). */
const SNAP = 14

const newId = () => Math.random().toString(16).slice(2, 10)
const clamp01 = (v: number) => Math.min(1, Math.max(0, v))

function newSlice(n: number): Slice {
  return {
    id: newId(), name: `Slice ${n}`, enabled: true, opacity: 1,
    input: { x: 0, y: 0, w: 1, h: 1 }, cols: 2, rows: 2, points: defaultPoints(2, 2, 0.1),
    softEdge: { left: 0, right: 0, top: 0, bottom: 0, gamma: 1 }, source: null,
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
function useDrag(onEnd: () => void) {
  const drag = useRef<{ svg: SVGSVGElement; start: Pt; onMove: (d: Pt, p: Pt, e: Mods) => void } | null>(null)
  const begin = (e: React.PointerEvent, svg: SVGSVGElement | null, onMove: (d: Pt, p: Pt, e: Mods) => void) => {
    if (!svg) return
    e.stopPropagation()
    try { (e.target as Element).setPointerCapture(e.pointerId) } catch { /* ponteiro já solto */ }
    drag.current = { svg, start: toNorm(svg, e), onMove }
  }
  const move = (e: React.PointerEvent) => {
    const d = drag.current
    if (!d) return
    const p = toNorm(d.svg, e)
    d.onMove({ x: p.x - d.start.x, y: p.y - d.start.y }, p, e)
  }
  const end = () => { drag.current = null; onEnd() }
  return { begin, move, end }
}

export function MappingEditor({ mapping, onChange, displays, preview, testCard, onTestCard, presets, comp }: Props) {
  const [preset, setPreset] = useState('')
  const [screenId, setScreenId] = useState<string | undefined>(mapping.screens[0]?.id)
  const [sel, setSel] = useState<Sel>(null)
  const [point, setPoint] = useState<number | null>(null)
  const [snapHint, setSnapHint] = useState<{ where: 'in' | 'out'; p: Pt } | null>(null)
  const [magnet, setMagnetState] = useState(() => { try { return localStorage.getItem('magnet') !== 'off' } catch { return true } })
  const inSvg = useRef<SVGSVGElement>(null)
  const outSvg = useRef<SVGSVGElement>(null)
  const drag = useDrag(() => setSnapHint(null))

  const setMagnet = (on: boolean) => {
    setMagnetState(on)
    try { localStorage.setItem('magnet', on ? 'on' : 'off') } catch { /* sem armazenamento */ }
  }
  /** Ímã ligado neste arrasto? (Alt inverte enquanto segura) */
  const magnetOn = (e: Mods) => magnet !== e.altKey

  const screen = mapping.screens.find((s) => s.id === screenId) ?? mapping.screens[0]
  const display = displays[screen.display]
  const pxW = display?.width ?? 1920, pxH = display?.height ?? 1080
  const outAspect = pxW / pxH
  const slice = sel?.kind === 'slice' ? screen.slices.find((s) => s.id === sel.id) : undefined
  const mask = sel?.kind === 'mask' ? screen.masks.find((m) => m.id === sel.id) : undefined
  const groups = mapping.groups ?? []

  // ---- atualizações imutáveis ----
  const replaceScreen = (next: OutputScreen) =>
    onChange({ ...mapping, screens: mapping.screens.map((s) => (s.id === next.id ? next : s)) })
  const setScreen = (patch: Partial<OutputScreen>) => replaceScreen({ ...screen, ...patch })
  const setSlice = (id: string, patch: Partial<Slice>) =>
    setScreen({ slices: screen.slices.map((s) => (s.id === id ? { ...s, ...patch } : s)) })
  const setMask = (id: string, patch: Partial<Mask>) =>
    setScreen({ masks: screen.masks.map((m) => (m.id === id ? { ...m, ...patch } : m)) })
  const setGroups = (g: LayerGroup[]) => onChange({ ...mapping, groups: g })

  // de onde cada slice ou parte do LED pega a imagem
  const sources: SourceOption[] = [
    { value: '', label: 'Composição inteira' },
    ...groups.map((g) => ({ value: g.id, label: `Grupo: ${g.name}` })),
    ...(comp?.layers ?? []).map((l, i) => ({ value: `layer:${i}`, label: `Só a ${l.name}` })),
  ]

  /** Alvos do ímã na saída: bordas e cantos da tela + pontos e contornos dos outros slices e máscaras. */
  const outTargets = (exceptId: string): SnapTargets => {
    const t: SnapTargets = { points: [...SCREEN_TARGETS.points], segs: [...SCREEN_TARGETS.segs] }
    for (const s of screen.slices) {
      if (s.id === exceptId || !s.enabled) continue
      const o = shapeTargets([...sliceCorners(s), ...s.points], warpOutline(s))
      t.points.push(...o.points); t.segs.push(...o.segs)
    }
    for (const m of screen.masks) {
      if (m.id === exceptId || !m.enabled) continue
      const o = shapeTargets(m.points, m.points)
      t.points.push(...o.points); t.segs.push(...o.segs)
    }
    return t
  }

  /** Ponto arrastado na saída, com ímã. */
  const snapOut = (p: Pt, exceptId: string, e: Mods): Pt => {
    if (!magnetOn(e)) { setSnapHint(null); return p }
    const r = snapPoint(p, outTargets(exceptId), SNAP, W, outH)
    setSnapHint(r.hit ? { where: 'out', p: r.p } : null)
    return r.p
  }
  /** Forma inteira arrastada na saída, com ímã nos cantos. */
  const snapOutShape = (moved: Pt[], exceptId: string, e: Mods): Pt => {
    if (!magnetOn(e)) { setSnapHint(null); return { x: 0, y: 0 } }
    const r = snapShape(moved, outTargets(exceptId), SNAP, W, outH)
    setSnapHint(r.hit ? { where: 'out', p: r.hit } : null)
    return r.d
  }
  /** Linhas da entrada: bordas da composição e dos retângulos dos outros slices. */
  const inLines = (exceptId: string) => {
    const xs = [0, 0.5, 1], ys = [0, 0.5, 1]
    for (const s of screen.slices) {
      if (s.id === exceptId) continue
      xs.push(s.input.x, s.input.x + s.input.w); ys.push(s.input.y, s.input.y + s.input.h)
    }
    return { xs, ys }
  }

  // ---- setas do teclado movem o ponto selecionado (1 pixel; Shift = 10) ----
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (point === null || !(slice || mask)) return
      if ((e.target as HTMLElement).tagName === 'INPUT' || (e.target as HTMLElement).tagName === 'SELECT') return
      const px = (e.shiftKey ? 10 : 1) / pxW
      const py = (e.shiftKey ? 10 : 1) / pxH
      const d = matches(e, 'mapa.esquerda', true) ? [-px, 0] : matches(e, 'mapa.direita', true) ? [px, 0]
        : matches(e, 'mapa.cima', true) ? [0, -py] : matches(e, 'mapa.baixo', true) ? [0, py] : null
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
    onChange({ ...mapping, screens: [...mapping.screens, s] })
    setScreenId(s.id)
    select(null)
  }

  const W = 1000
  const inH = W / COMP_ASPECT
  const outH = W / outAspect
  const path = (pts: Pt[], h: number) => pts.map((p, i) => `${i ? 'L' : 'M'}${p.x * W},${p.y * h}`).join(' ') + ' Z'
  const sourceName = (v?: string | null) => sources.find((s) => s.value === (v ?? ''))?.label ?? 'Composição inteira'

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
        <button className={`magnet ${magnet ? 'active' : ''}`} onClick={() => setMagnet(!magnet)}
          title="Ímã: ao arrastar, gruda as pontas nas bordas e cantos da tela e nos outros slices e máscaras. Segure Alt para inverter.">
          🧲 Ímã {magnet ? 'ligado' : 'desligado'}
        </button>
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
        <select value={screen.display} onChange={(e) => {
          const d = Number(e.target.value)
          const dd = displays[d]
          // o mapa de LED é em pixels: refaz os slices na resolução da nova tela
          const next = { ...screen, display: d }
          replaceScreen(next.led && dd ? applyLed(next, next.led, dd.width, dd.height) : next)
        }}>
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
            onChange({ ...mapping, screens: mapping.screens.filter((s) => s.id !== screen.id) })
            setScreenId(mapping.screens.find((s) => s.id !== screen.id)?.id)
            select(null)
          }}>Remover tela</button>
        )}
      </section>

      <LedMap screen={screen} outW={pxW} outH={pxH} sources={sources} magnet={magnet} onScreen={replaceScreen} />

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
              onClick={() => select({ kind: 'slice', id: s.id })}>
              {s.ledPart ? '▦' : '▭'} {s.name}
              {s.source && <span className="item-src"> · {sourceName(s.source).replace('Grupo: ', '')}</span>}
            </div>
          ))}
          {screen.masks.map((m) => (
            <div key={m.id} className={`item mask ${sel?.id === m.id ? 'active' : ''} ${m.enabled ? '' : 'off'}`}
              onClick={() => select({ kind: 'mask', id: m.id })}>◼ {m.name}</div>
          ))}

          <GroupsEditor groups={groups} comp={comp} onChange={setGroups} />
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
                  <g key={s.id} className={`in-rect ${active ? 'active' : ''} ${s.source ? 'other-src' : ''}`}>
                    <rect x={r.x * W} y={r.y * inH} width={r.w * W} height={r.h * inH}
                      onPointerDown={(e) => {
                        select({ kind: 'slice', id: s.id })
                        if (s.ledPart) return
                        const start = { ...r }
                        drag.begin(e, inSvg.current, (d, _, ev) => {
                          let x = clamp01(Math.min(start.x + d.x, 1 - start.w)), y = clamp01(Math.min(start.y + d.y, 1 - start.h))
                          if (magnetOn(ev)) {
                            const { xs, ys } = inLines(s.id)
                            const rx = SNAP / W, ry = SNAP / inH
                            const a = snapValue(x, xs, rx), b = snapValue(x + start.w, xs, rx)
                            const c = snapValue(y, ys, ry), dd = snapValue(y + start.h, ys, ry)
                            if (a !== null) x = a; else if (b !== null) x = b - start.w
                            if (c !== null) y = c; else if (dd !== null) y = dd - start.h
                          }
                          setSlice(s.id, { input: { ...start, x, y } })
                        })
                      }} />
                    <text x={r.x * W + 8} y={r.y * inH + 22}>{s.name}{s.source ? ` (${sourceName(s.source)})` : ''}</text>
                    {active && !s.ledPart && [[0, 0], [1, 0], [0, 1], [1, 1]].map(([cx, cy]) => (
                      <circle key={`${cx}${cy}`} className="handle" r={9} cx={(r.x + cx * r.w) * W} cy={(r.y + cy * r.h) * inH}
                        onPointerDown={(e) => {
                          const start = { ...r }
                          drag.begin(e, inSvg.current, (_, p, ev) => {
                            // arrasta o canto, mantendo o canto oposto fixo
                            const ox = start.x + (1 - cx) * start.w, oy = start.y + (1 - cy) * start.h
                            let px = clamp01(p.x), py = clamp01(p.y)
                            if (magnetOn(ev)) {
                              const { xs, ys } = inLines(s.id)
                              px = snapValue(px, xs, SNAP / W) ?? px
                              py = snapValue(py, ys, SNAP / inH) ?? py
                              setSnapHint({ where: 'in', p: { x: px, y: py } })
                            }
                            setSlice(s.id, { input: { x: Math.min(ox, px), y: Math.min(oy, py), w: Math.max(0.01, Math.abs(px - ox)), h: Math.max(0.01, Math.abs(py - oy)) } })
                          })
                        }} />
                    ))}
                  </g>
                )
              })}
              {snapHint?.where === 'in' && <circle className="snap-hint" r={14} cx={snapHint.p.x * W} cy={snapHint.p.y * inH} />}
            </svg>
          </section>

          <section>
            <h2>Saída · {screen.name} ({pxW}×{pxH})</h2>
            <svg ref={outSvg} className="editor" viewBox={`0 0 ${W} ${outH}`} onPointerMove={drag.move} onPointerUp={drag.end}
              onPointerDown={() => select(null)}>
              <rect width={W} height={outH} className="bg" />
              {screen.slices.map((s) => {
                const active = sel?.id === s.id
                return (
                  <g key={s.id} className={`out-slice ${active ? 'active' : ''} ${s.enabled ? '' : 'off'} ${s.ledPart ? 'led' : ''}`}>
                    <path className="fill" d={path(warpOutline(s), outH)}
                      onPointerDown={(e) => {
                        select({ kind: 'slice', id: s.id })
                        if (s.ledPart) return
                        const start = s.points
                        const corners = sliceCorners(s)
                        drag.begin(e, outSvg.current, (d, _, ev) => {
                          const snap = snapOutShape(corners.map((c) => ({ x: c.x + d.x, y: c.y + d.y })), s.id, ev)
                          setSlice(s.id, { points: start.map((p) => ({ x: p.x + d.x + snap.x, y: p.y + d.y + snap.y })) })
                        })
                      }} />
                    {warpLines(s).map((line, i) => (
                      <polyline key={i} className="wire" points={line.map((p) => `${p.x * W},${p.y * outH}`).join(' ')} />
                    ))}
                    <text x={sliceCorners(s).reduce((a, p) => Math.min(a, p.x), 1) * W + 8} y={sliceCorners(s).reduce((a, p) => Math.min(a, p.y), 1) * outH + 22}>{s.name}</text>
                    {active && !s.ledPart && s.points.map((p, i) => (
                      <circle key={i} className={`handle ${point === i ? 'picked' : ''}`} r={10} cx={p.x * W} cy={p.y * outH}
                        onPointerDown={(e) => {
                          setPoint(i)
                          drag.begin(e, outSvg.current, (_, np, ev) =>
                            setSlice(s.id, { points: s.points.map((q, k) => (k === i ? snapOut(np, s.id, ev) : q)) }))
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
                      drag.begin(e, outSvg.current, (d, _, ev) => {
                        const snap = snapOutShape(start.map((c) => ({ x: c.x + d.x, y: c.y + d.y })), m.id, ev)
                        setMask(m.id, { points: start.map((p) => ({ x: p.x + d.x + snap.x, y: p.y + d.y + snap.y })) })
                      })
                    }} />
                    {active && m.points.map((p, i) => (
                      <circle key={i} className={`handle ${point === i ? 'picked' : ''}`} r={10} cx={p.x * W} cy={p.y * outH}
                        onPointerDown={(e) => {
                          setPoint(i)
                          drag.begin(e, outSvg.current, (_, np, ev) =>
                            setMask(m.id, { points: m.points.map((q, k) => (k === i ? snapOut(np, m.id, ev) : q)) }))
                        }} />
                    ))}
                  </g>
                )
              })}
              {snapHint?.where === 'out' && <circle className="snap-hint" r={14} cx={snapHint.p.x * W} cy={snapHint.p.y * outH} />}
            </svg>
          </section>
        </div>

        {/* ---------- propriedades ---------- */}
        <section className="props">
          {slice && (
            <>
              <h2>Slice</h2>
              {slice.ledPart && <p className="warn-text">Slice do painel de LED: posição, entrada e imagem vêm do mapa de painéis acima.</p>}
              <input className="name" value={slice.name} disabled={!!slice.ledPart} onChange={(e) => setSlice(slice.id, { name: e.target.value })} />
              <label className="check">
                <input type="checkbox" checked={slice.enabled} onChange={(e) => setSlice(slice.id, { enabled: e.target.checked })} /> Ativo
              </label>
              {!slice.ledPart && (
                <label>Imagem <output />
                  <select value={slice.source ?? ''} onChange={(e) => setSlice(slice.id, { source: e.target.value || null })}>
                    {sources.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </label>
              )}
              <label>Opacidade <output>{slice.opacity.toFixed(2)}</output>
                <input type="range" min={0} max={1} step={0.01} value={slice.opacity}
                  onChange={(e) => setSlice(slice.id, { opacity: Number(e.target.value) })} />
              </label>
              {!slice.ledPart && (
                <label>Grade de warp <output />
                  <select value={slice.cols} onChange={(e) => {
                    const n = Number(e.target.value)
                    setSlice(slice.id, { cols: n, rows: n, points: resampleGrid(slice, n, n) })
                    setPoint(null)
                  }}>
                    {GRID_SIZES.map((n) => <option key={n} value={n}>{n === 2 ? '4 cantos (perspectiva)' : `Malha ${n}×${n}`}</option>)}
                  </select>
                </label>
              )}
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
              {!slice.ledPart && (
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
              )}
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
              Com o ímã ligado, as pontas grudam nas bordas, nos cantos e nos outros slices e máscaras (Alt inverte).
            </div>
          )}
        </section>
      </div>
    </div>
  )
}

/** Grupos de layers: composições à parte que podem ir para um slice ou parte do LED. */
function GroupsEditor({ groups, comp, onChange }: { groups: LayerGroup[]; comp: Composition | null; onChange: (g: LayerGroup[]) => void }) {
  const set = (id: string, patch: Partial<LayerGroup>) => onChange(groups.map((g) => (g.id === id ? { ...g, ...patch } : g)))
  return (
    <div className="groups">
      <h3>Grupos de layers</h3>
      <p className="meta">Junte layers para mandar a um slice ou parte do LED (ex.: “Centro” = layers 2 e 3).</p>
      {groups.map((g) => (
        <div key={g.id} className="group">
          <div className="row">
            <input className="name" value={g.name} onChange={(e) => set(g.id, { name: e.target.value })} />
            <button className="small danger-text" onClick={() => onChange(groups.filter((x) => x.id !== g.id))}>✕</button>
          </div>
          {(comp?.layers ?? []).map((l, i) => (
            <label key={i} className="check">
              <input type="checkbox" checked={g.layers.includes(i)}
                onChange={(e) => set(g.id, { layers: e.target.checked ? [...g.layers, i].sort() : g.layers.filter((x) => x !== i) })} />
              {l.name}
            </label>
          ))}
        </div>
      ))}
      <button className="small" onClick={() => onChange([...groups, { id: newId(), name: `Grupo ${groups.length + 1}`, layers: [] }])}>+ Grupo</button>
    </div>
  )
}
