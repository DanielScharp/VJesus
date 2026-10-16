import { useEffect, useRef, useState } from 'react'
import { send, TRANSITIONS, type Layer, type Transition } from './bridge'
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
  cut: 'Corte seco', crossfade: 'Crossfade', dip: 'Escurecer', wipe: 'Cortina',
}

/** Ajustes da layer selecionada: transição ao trocar de clip e volume do áudio. */
export function LayerSettings({ layer, index, onChange }: {
  layer: Layer
  index: number
  onChange: (patch: Partial<Pick<Layer, 'transition' | 'transitionSeconds' | 'volume' | 'sendToStage'>>) => void
}) {
  return (
    <div className="layer-settings">
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
