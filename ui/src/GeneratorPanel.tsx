import { useEffect, useState } from 'react'
import { send, type GeneratorSettings } from './bridge'

export const GEN_TYPES: [string, string, string][] = [
  ['ondas', 'Ondas suaves', 'Ondulação lenta misturando as três cores.'],
  ['nuvens', 'Nuvens', 'Névoa que escorre devagar, como fumaça de palco.'],
  ['aurora', 'Aurora', 'Faixa de luz ondulando, como aurora boreal.'],
  ['luzes', 'Luzes desfocadas', 'Bolinhas de luz (bokeh) subindo devagar.'],
  ['gradiente', 'Gradiente', 'Degradê que gira e "respira" bem devagar.'],
  ['feixes', 'Feixes de luz', 'Raios de luz vindo de cima.'],
  ['arcoiris', 'Arco-íris', 'O plasma colorido clássico (ignora as cores).'],
]

export const DEFAULT_GEN: GeneratorSettings = {
  type: 'ondas', speed: 0.4, intensity: 0.8, scale: 1, detail: 0.5, angle: 90, colors: ['#0A1030', '#1E4A9A', '#8CC8FF'],
}

/** Modelos prontos de fundos leves para o culto. */
export const GEN_PRESETS: { name: string; gen: GeneratorSettings }[] = [
  { name: 'Azul calmo', gen: DEFAULT_GEN },
  { name: 'Aurora', gen: { type: 'aurora', speed: 0.5, intensity: 0.9, scale: 1, detail: 0.4, angle: 0, colors: ['#02040A', '#0E6B5C', '#7CF5C8'] } },
  { name: 'Pôr do sol', gen: { type: 'ondas', speed: 0.3, intensity: 0.85, scale: 1.4, detail: 0.3, angle: 0, colors: ['#1A0A1E', '#B8405E', '#FFC07A'] } },
  { name: 'Nuvens douradas', gen: { type: 'nuvens', speed: 0.35, intensity: 0.8, scale: 1, detail: 0.6, angle: 20, colors: ['#120C04', '#7A5A1E', '#FFE2A0'] } },
  { name: 'Adoração roxa', gen: { type: 'luzes', speed: 0.5, intensity: 0.85, scale: 1, detail: 0.5, angle: 90, colors: ['#0B0618', '#4B2A8C', '#D9B8FF'] } },
  { name: 'Luz do céu', gen: { type: 'feixes', speed: 0.3, intensity: 0.9, scale: 1, detail: 0.4, angle: 0, colors: ['#02030A', '#30406A', '#FFF4D6'] } },
  { name: 'Névoa cinza', gen: { type: 'nuvens', speed: 0.25, intensity: 0.6, scale: 1.3, detail: 0.5, angle: 0, colors: ['#050506', '#2A2D35', '#9AA3B5'] } },
  { name: 'Natal', gen: { type: 'luzes', speed: 0.4, intensity: 0.9, scale: 1, detail: 0.6, angle: 90, colors: ['#06140A', '#B3202A', '#FFD27A'] } },
  { name: 'Gradiente quente', gen: { type: 'gradiente', speed: 0.3, intensity: 0.8, scale: 1, detail: 0.5, angle: 45, colors: ['#2A0A12', '#C2412D', '#F6B04A'] } },
  { name: 'Arco-íris', gen: { type: 'arcoiris', speed: 1, intensity: 1, scale: 1, detail: 0.5, angle: 0, colors: ['#000000', '#000000', '#000000'] } },
]

/** Cores do clip para a miniatura na grade. */
export function genThumbStyle(g?: GeneratorSettings | null): React.CSSProperties | undefined {
  if (!g || g.type === 'arcoiris') return undefined
  return { background: `linear-gradient(135deg, ${g.colors[0]}, ${g.colors[1]} 55%, ${g.colors[2]})` }
}

/**
 * Painel do fundo animado (Plasma): tipo de ondulação, 3 cores, velocidade, intensidade, escala,
 * detalhe e direção. As mudanças valem na hora no telão se o clip estiver tocando.
 */
export function GeneratorPanel({ layer, column, initial, playing, onClose }: {
  layer: number; column: number; initial?: GeneratorSettings | null; playing: boolean; onClose: () => void
}) {
  const [g, setG] = useState<GeneratorSettings>(initial ?? { ...DEFAULT_GEN, type: 'arcoiris' })
  useEffect(() => { send({ type: 'setGeneratorParams', layer, column, gen: g }) }, [g])

  const set = (patch: Partial<GeneratorSettings>) => setG({ ...g, ...patch })
  const setColor = (i: number, c: string) => set({ colors: g.colors.map((x, k) => (k === i ? c.toUpperCase() : x)) })
  const slider = (label: string, key: 'speed' | 'intensity' | 'scale' | 'detail' | 'angle', min: number, max: number, step: number, fmt: (v: number) => string) => (
    <label>{label} <output>{fmt(g[key])}</output>
      <input type="range" min={min} max={max} step={step} value={g[key]} onChange={(e) => set({ [key]: Number(e.target.value) })} />
    </label>
  )

  return (
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal gen-panel" onMouseDown={(e) => e.stopPropagation()}>
        <div className="bible-head">
          <h2>Fundo animado</h2>
          {!playing && <button className="small primary" onClick={() => send({ type: 'trigger', layer, column })}>Tocar no telão para ver</button>}
          <button className="small" onClick={onClose}>Fechar</button>
        </div>
        <p className="meta">Fundos leves gerados pelo próprio VJesus (não precisa de vídeo). As mudanças aparecem na hora no telão.</p>

        <h3>Modelos prontos</h3>
        <div className="gen-presets">
          {GEN_PRESETS.map((p) => (
            <button key={p.name} className="gen-preset" onClick={() => setG(p.gen)} title={p.name}>
              <span className="gen-swatch" style={genThumbStyle(p.gen) ?? { background: 'conic-gradient(from 90deg, #f0f, #00f, #0ff, #0f0, #ff0, #f00, #f0f)' }} />
              {p.name}
            </button>
          ))}
        </div>

        <h3>Tipo de ondulação</h3>
        <div className="gen-types">
          {GEN_TYPES.map(([id, name, hint]) => (
            <button key={id} className={`small ${g.type === id ? 'active' : ''}`} title={hint} onClick={() => set({ type: id })}>{name}</button>
          ))}
        </div>
        <p className="meta">{GEN_TYPES.find((t) => t[0] === g.type)?.[2]}</p>

        <div className="gen-body">
          <div>
            <h3>Cores</h3>
            <div className="gen-colors">
              {['Fundo', 'Meio', 'Brilho'].map((l, i) => (
                <label key={l} className="gen-color">
                  <input type="color" value={g.colors[i]} disabled={g.type === 'arcoiris'} onChange={(e) => setColor(i, e.target.value)} />
                  <span>{l}</span>
                </label>
              ))}
            </div>
          </div>
          <div className="gen-sliders">
            {slider('Velocidade', 'speed', 0, 3, 0.05, (v) => v.toFixed(2) + '×')}
            {slider('Intensidade', 'intensity', 0, 1.5, 0.01, (v) => Math.round(v * 100) + '%')}
            {slider('Escala', 'scale', 0.2, 4, 0.05, (v) => v.toFixed(2) + '×')}
            {slider('Detalhe', 'detail', 0, 1, 0.01, (v) => Math.round(v * 100) + '%')}
            {slider('Direção', 'angle', 0, 360, 1, (v) => Math.round(v) + '°')}
          </div>
        </div>
      </div>
    </div>
  )
}
