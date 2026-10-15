import { useRef, useState } from 'react'
import type { LedPart, LedWall, OutputScreen } from './bridge'
import { applyLed, LED_ARRANGEMENTS, LED_TEMPLATES, mapBounds, newId, physSize } from './led'
import { snapValue } from './mappingMath'

export type SourceOption = { value: string; label: string }

type Props = {
  screen: OutputScreen
  outW: number
  outH: number
  sources: SourceOption[]
  magnet: boolean
  onScreen: (s: OutputScreen) => void
}

const COLORS = ['#ffb020', '#4fc3f7', '#81c784', '#e57373', '#ba68c8', '#fff176', '#4db6ac', '#ff8a65']

/**
 * Mapa de painéis de LED: cada parte tem onde fica no mapa de pixels da processadora (X, Y, largura,
 * altura e giro) e onde está montada no palco (mapa físico, arrastável). Gera os slices sozinho.
 */
export function LedMap({ screen, outW, outH, sources, magnet, onScreen }: Props) {
  const led = screen.led
  const svg = useRef<SVGSVGElement>(null)
  const drag = useRef<{ id: string; x0: number; y0: number; mx: number; my: number; scale: number } | null>(null)
  const [selected, setSelected] = useState<string | null>(null)

  const set = (next: LedWall | null) => onScreen(applyLed(screen, next, outW, outH))
  const setPart = (id: string, patch: Partial<LedPart>) =>
    led && set({ ...led, parts: led.parts.map((p) => (p.id === id ? { ...p, ...patch } : p)) })

  if (!led) {
    return (
      <section className="led-map">
        <h2>Painel de LED</h2>
        <p className="meta">
          Use quando a saída vai para uma processadora de LED com o painel dividido em partes (ex.: 2 laterais em pé e
          1 central deitada). Você desenha onde cada parte está no mapa de pixels e como está montada no palco; os slices são
          criados sozinhos e cada parte pode receber uma layer, um grupo de layers ou a composição inteira.
        </p>
        <div className="row">
          {LED_TEMPLATES.map((t) => (
            <button key={t.id} className="small" onClick={() => set({ layout: 'separado', source: null, parts: t.build(outW, outH) })}>{t.name}</button>
          ))}
        </div>
      </section>
    )
  }

  // ---- mapa físico ----
  const b = mapBounds(led.parts)
  const pad = Math.max(b.w, b.h) * 0.08
  const vb = { x: b.x - pad, y: b.y - pad, w: b.w + 2 * pad, h: b.h + 2 * pad }

  const onDown = (e: React.PointerEvent, p: LedPart) => {
    e.stopPropagation()
    setSelected(p.id)
    const r = svg.current!.getBoundingClientRect()
    ;(e.target as Element).setPointerCapture(e.pointerId)
    drag.current = { id: p.id, x0: e.clientX, y0: e.clientY, mx: p.mapX, my: p.mapY, scale: vb.w / r.width }
  }
  const onMove = (e: React.PointerEvent) => {
    const d = drag.current
    if (!d) return
    const p = led.parts.find((x) => x.id === d.id)!
    let mx = d.mx + (e.clientX - d.x0) * d.scale
    let my = d.my + (e.clientY - d.y0) * d.scale
    if (magnet !== e.altKey) {
      // ímã: encosta as bordas desta parte nas bordas das outras
      const ps = physSize(p), radius = 14 * d.scale
      const xs = led.parts.filter((q) => q.id !== p.id).flatMap((q) => [q.mapX, q.mapX + physSize(q).w])
      const ys = led.parts.filter((q) => q.id !== p.id).flatMap((q) => [q.mapY, q.mapY + physSize(q).h])
      const sx = snapValue(mx, xs, radius), sx2 = snapValue(mx + ps.w, xs, radius)
      if (sx !== null) mx = sx; else if (sx2 !== null) mx = sx2 - ps.w
      const yMid = led.parts.filter((q) => q.id !== p.id).map((q) => q.mapY + physSize(q).h / 2)
      const sy = snapValue(my, ys, radius), sy2 = snapValue(my + ps.h, ys, radius), sym = snapValue(my + ps.h / 2, yMid, radius)
      if (sy !== null) my = sy; else if (sy2 !== null) my = sy2 - ps.h; else if (sym !== null) my = sym - ps.h / 2
    }
    setPart(d.id, { mapX: Math.round(mx), mapY: Math.round(my) })
  }

  const sel = led.parts.find((p) => p.id === selected)
  const srcLabel = (v: string | null) => sources.find((s) => s.value === (v ?? ''))?.label ?? 'Composição inteira'

  return (
    <section className="led-map">
      <div className="row led-head">
        <h2>Painel de LED · {led.parts.length} partes</h2>
        <span className="spacer" />
        <label className="check"><input type="radio" checked={led.layout === 'separado'} onChange={() => set({ ...led, layout: 'separado' })} /> Separados (cada parte com sua imagem)</label>
        <label className="check"><input type="radio" checked={led.layout === 'unido'} onChange={() => set({ ...led, layout: 'unido' })} /> Unido (uma imagem atravessa todas)</label>
        {led.layout === 'unido' && (
          <select value={led.source ?? ''} onChange={(e) => set({ ...led, source: e.target.value || null })}>
            {sources.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        )}
        <button className="small danger-text" onClick={() => { if (window.confirm('Desligar o mapa de painéis e apagar os slices dele?')) set(null) }}>Desligar</button>
      </div>

      <div className="led-body">
        <div>
          <h3>Como está montado no palco (arraste as partes)</h3>
          <svg ref={svg} className="editor led-phys" viewBox={`${vb.x} ${vb.y} ${vb.w} ${vb.h}`}
            onPointerMove={onMove} onPointerUp={() => { drag.current = null }} onPointerDown={() => setSelected(null)}>
            <rect x={vb.x} y={vb.y} width={vb.w} height={vb.h} className="bg" />
            {led.parts.map((p, i) => {
              const s = physSize(p)
              const fs = Math.max(vb.w, vb.h) * 0.028
              return (
                <g key={p.id} className={`led-part ${selected === p.id ? 'active' : ''}`} onPointerDown={(e) => onDown(e, p)}>
                  <rect x={p.mapX} y={p.mapY} width={s.w} height={s.h} style={{ fill: COLORS[i % COLORS.length] + '33', stroke: COLORS[i % COLORS.length] }} />
                  <text x={p.mapX + s.w / 2} y={p.mapY + s.h / 2} style={{ fontSize: fs }} textAnchor="middle">{p.name}</text>
                  <text x={p.mapX + s.w / 2} y={p.mapY + s.h / 2 + fs * 1.3} style={{ fontSize: fs * 0.75 }} textAnchor="middle" className="sub">
                    {led.layout === 'unido' ? `${Math.round(p.w)}×${Math.round(p.h)}` : srcLabel(p.source)}
                  </text>
                </g>
              )
            })}
          </svg>
          <div className="row">
            Arrumar:
            {LED_ARRANGEMENTS.map((a) => (
              <button key={a.id} className="small" onClick={() => set({ ...led, parts: a.apply(led.parts) })}>{a.name}</button>
            ))}
          </div>
        </div>

        <div className="led-parts">
          <h3>Partes · mapa de pixels da saída ({outW}×{outH})</h3>
          <table>
            <thead>
              <tr><th>Nome</th><th>X</th><th>Y</th><th>Larg.</th><th>Alt.</th><th>Giro</th>{led.layout === 'separado' && <th>Imagem</th>}<th /></tr>
            </thead>
            <tbody>
              {led.parts.map((p, i) => (
                <tr key={p.id} className={selected === p.id ? 'active' : ''} onClick={() => setSelected(p.id)}>
                  <td><span className="dot" style={{ background: COLORS[i % COLORS.length] }} />
                    <input value={p.name} onChange={(e) => setPart(p.id, { name: e.target.value })} /></td>
                  {(['x', 'y', 'w', 'h'] as const).map((k) => (
                    <td key={k}><input type="number" value={Math.round(p[k])} min={k === 'w' || k === 'h' ? 1 : 0}
                      onChange={(e) => setPart(p.id, { [k]: Math.max(k === 'w' || k === 'h' ? 1 : 0, Number(e.target.value)) })} /></td>
                  ))}
                  <td>
                    <select value={p.rotation} onChange={(e) => setPart(p.id, { rotation: Number(e.target.value) })}>
                      {[0, 90, 180, 270].map((r) => <option key={r} value={r}>{r}°</option>)}
                    </select>
                  </td>
                  {led.layout === 'separado' && (
                    <td>
                      <select value={p.source ?? ''} onChange={(e) => setPart(p.id, { source: e.target.value || null })}>
                        {sources.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                      </select>
                    </td>
                  )}
                  <td><button className="small danger-text" title="Remover parte"
                    onClick={(e) => { e.stopPropagation(); set({ ...led, parts: led.parts.filter((x) => x.id !== p.id) }) }}>✕</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="row">
            <button className="small" onClick={() => {
              const last = led.parts[led.parts.length - 1]
              const np: LedPart = { id: newId(), name: `Parte ${led.parts.length + 1}`, x: 0, y: 0, w: Math.round(outW / 4), h: Math.round(outH / 4),
                mapX: last ? last.mapX + physSize(last).w : 0, mapY: last?.mapY ?? 0, rotation: 0, source: null }
              set({ ...led, parts: [...led.parts, np] })
              setSelected(np.id)
            }}>+ Parte</button>
            <span className="meta">Modelos:</span>
            {LED_TEMPLATES.map((t) => (
              <button key={t.id} className="small" onClick={() => {
                if (window.confirm(`Trocar as partes pelo modelo "${t.name}"?`)) set({ ...led, parts: t.build(outW, outH) })
              }}>{t.name}</button>
            ))}
          </div>
          {sel && <p className="meta">“{sel.name}”: no palco fica com {physSize(sel).w}×{physSize(sel).h} px{sel.rotation ? `, girada ${sel.rotation}° na processadora` : ''}.</p>}
          <p className="meta">
            X, Y, largura e altura são os pixels que a processadora usa para essa parte (veja no software da processadora).
            Os slices do LED aparecem na Saída abaixo e são refeitos quando o mapa muda; para ajustes finos, use slices livres.
          </p>
        </div>
      </div>
    </section>
  )
}
