import { useEffect, useRef, useState } from 'react'
import { BLEND_MODES, send, TRANSITIONS, type BlendMode, type Layer, type LayerTransform, type Transition } from './bridge'
import { BLEND_LABELS } from './ClipGrid'
import { CornerControls, RotationControl } from './ShapeControls'
import { matches } from './shortcuts'

/** BPM no topo: número editável, TAP, ressincronizar e 4 luzes que acompanham a batida. */
export function TempoBar({ bpm, beat }: { bpm: number; beat: number }) {
  const [draft, setDraft] = useState<string | null>(null)
  // a batida chega a cada 100 ms; entre uma mensagem e outra a UI estima localmente
  const ref = useRef({ beat, bpm, at: performance.now() })
  const [now, setNow] = useState(beat)
  useEffect(() => { ref.current = { beat, bpm, at: performance.now() } }, [beat, bpm])
  useEffect(() => {
    let id = 0
    const tick = () => {
      const r = ref.current
      setNow(r.beat + ((performance.now() - r.at) / 1000) * (r.bpm / 60))
      id = requestAnimationFrame(tick)
    }
    id = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(id)
  }, [])

  // tecla T = tap (quando não está digitando)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName
      if (matches(e, 'tempo.tap') && tag !== 'INPUT' && tag !== 'SELECT' && tag !== 'TEXTAREA' && !e.repeat) send({ type: 'tap' })
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const current = Math.floor(now) % 4
  const flash = now - Math.floor(now) < 0.15
  return (
    <div className="tempo">
      <input className="bpm" type="text" inputMode="decimal" maxLength={6}
        value={draft ?? bpm.toFixed(1)} title="BPM (20 a 300)"
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const v = Number((draft ?? '').replace(',', '.'))
          if (draft && v >= 20 && v <= 300) send({ type: 'setBpm', bpm: v })
          setDraft(null)
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
          if (e.key === 'Escape') { setDraft(null); (e.target as HTMLInputElement).blur() }
        }} />
      <span className="bpm-label">BPM</span>
      <span className="beats">
        {[0, 1, 2, 3].map((i) => <i key={i} className={i === current ? (flash ? 'on flash' : 'on') : ''} />)}
      </span>
      <button className="small" title="Toque no ritmo (ou aperte T)" data-address="/composition/tempocontroller/tempotap"
        onClick={() => send({ type: 'tap' })}>TAP</button>
      <button className="small" title="A batida 1 começa agora" data-address="/composition/tempocontroller/resync"
        onClick={() => send({ type: 'beatResync' })}>⟲</button>
    </div>
  )
}

const TRANSITION_LABELS: Record<Transition, string> = {
  cut: 'Corte seco', crossfade: 'Crossfade (suave)', dip: 'Passar pelo preto', wipe: 'Cortina',
  push: 'Deslizar', zoom: 'Aproximar', blur: 'Desfocar',
}

/** Tempos mais usados: curto para música, médio para trocas calmas, longo para fundos. */
const DURATIONS = [0.3, 0.5, 0.8, 1, 1.5, 2, 3]

const IDENTITY: LayerTransform = { x: 0, y: 0, scale: 1, width: 1, height: 1, rotation: 0, radius: [0, 0, 0, 0] }

/** Ajustes da layer selecionada: mistura, posição/tamanho/giro/cantos, transição ao trocar de clip e volume. */
export function LayerSettings({ layer, index, onChange }: {
  layer: Layer
  index: number
  onChange: (patch: Partial<Pick<Layer, 'blend' | 'opacity' | 'transition' | 'transitionSeconds' | 'transitionIn' | 'transitionBetween' | 'transitionOut' | 'volume' | 'sendToStage' | 'transform'>>) => void
}) {
  const t = { ...IDENTITY, ...(layer.transform ?? {}) }
  const setT = (patch: Partial<LayerTransform>) => onChange({ transform: { ...t, ...patch } })
  const slide = (label: string, key: 'x' | 'y' | 'scale' | 'width' | 'height', min: number, max: number, step: number, fmt: (v: number) => string) => (
    <label>{label} <output>{fmt(t[key])}</output>
      <input type="range" min={min} max={max} step={step} value={t[key]} onChange={(e) => setT({ [key]: Number(e.target.value) })} />
    </label>
  )
  const changed = JSON.stringify(t) !== JSON.stringify(IDENTITY)
  return (
    <div className="layer-settings">
      <label>
        Mistura (blend) <output />
        <select value={layer.blend} onChange={(e) => onChange({ blend: e.target.value as BlendMode })}>
          {BLEND_MODES.map((b) => <option key={b} value={b}>{BLEND_LABELS[b] ?? b}</option>)}
        </select>
      </label>
      <label>
        Opacidade <output>{Math.round(layer.opacity * 100)}%</output>
        <input type="range" min={0} max={1} step={0.01} value={layer.opacity} onChange={(e) => onChange({ opacity: Number(e.target.value) })} />
      </label>
      <details className="layer-shape" open={changed || undefined}>
        <summary>Posição, tamanho, rotação e cantos{changed ? ' ●' : ''}</summary>
        {slide('Escala', 'scale', 0.05, 3, 0.01, (v) => Math.round(v * 100) + '%')}
        {slide('Largura', 'width', 0.05, 3, 0.01, (v) => Math.round(v * 100) + '%')}
        {slide('Altura', 'height', 0.05, 3, 0.01, (v) => Math.round(v * 100) + '%')}
        {slide('Posição horizontal', 'x', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
        {slide('Posição vertical', 'y', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
        <RotationControl value={t.rotation} onChange={(v) => setT({ rotation: v })} />
        <CornerControls radius={t.radius} onChange={(r) => setT({ radius: r })} />
        <button className="small" disabled={!changed} onClick={() => onChange({ transform: { ...IDENTITY } })}>Voltar ao normal</button>
      </details>
      <label>
        Transição <output />
        <select value={layer.transition} onChange={(e) => onChange({ transition: e.target.value as Transition })}>
          {TRANSITIONS.map((t) => <option key={t} value={t}>{TRANSITION_LABELS[t]}</option>)}
        </select>
      </label>
      {layer.transition !== 'cut' && (
        <label>
          Duração <output>{layer.transitionSeconds.toFixed(1)} s</output>
          <input type="range" min={0.1} max={5} step={0.1} value={layer.transitionSeconds}
            onChange={(e) => onChange({ transitionSeconds: Number(e.target.value) })} />
        </label>
      )}
      {layer.transition !== 'cut' && (
        <div className="row chips duration-chips">
          {DURATIONS.map((d) => (
            <button key={d} className={`small ${Math.abs(layer.transitionSeconds - d) < 0.01 ? 'active' : ''}`} onClick={() => onChange({ transitionSeconds: d })}>{d.toString().replace('.', ',')} s</button>
          ))}
        </div>
      )}
      {layer.transition !== 'cut' && (
        <div className="transition-when">
          <span>Usar a transição:</span>
          <label className="check" title="De uma coluna sem clip nesta layer para uma com clip">
            <input type="checkbox" checked={layer.transitionIn ?? true} onChange={(e) => onChange({ transitionIn: e.target.checked })} /> na entrada
          </label>
          <label className="check" title="De um clip para outro clip">
            <input type="checkbox" checked={layer.transitionBetween ?? true} onChange={(e) => onChange({ transitionBetween: e.target.checked })} /> ao trocar de clip
          </label>
          <label className="check" title="De um clip para uma coluna sem clip nesta layer (ou ao limpar a layer)">
            <input type="checkbox" checked={layer.transitionOut ?? true} onChange={(e) => onChange({ transitionOut: e.target.checked })} /> na saída
          </label>
        </div>
      )}
      <label className="check stage-flag" title="Para avisos: quando um clip desta layer estiver tocando, ele aparece também na tela de retorno">
        <input type="checkbox" checked={!!layer.sendToStage} onChange={(e) => onChange({ sendToStage: e.target.checked })} />
        Mostrar no retorno quando tocar (avisos)
      </label>
      <label data-address={`/composition/layers/${index + 1}/audio/volume`}>
        Volume {layer.hasAudio ? '🔊' : ''} <output>{Math.round(layer.volume * 100)}%</output>
        <input type="range" min={0} max={1.5} step={0.01} value={layer.volume}
          onChange={(e) => onChange({ volume: Number(e.target.value) })} />
      </label>
    </div>
  )
}
