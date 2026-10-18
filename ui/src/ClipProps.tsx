import { useEffect, useState } from 'react'
import { send, type Clip, type ClipProps, type EffectDef, type EffectInstance } from './bridge'
import { EffectsPanel } from './EffectsPanel'

export const DEFAULT_PROPS: ClipProps = { speed: 1, fit: 'encaixar', zoom: 1, offsetX: 0, offsetY: 0, volume: 1, end: 'repetir', effects: [] }

/** Efeitos de clip usam este número de "alvo" nas mensagens de efeito (o motor sabe que é um clip). */
export const clipTarget = (layer: number, column: number) => 10000 + layer * 1000 + column

/**
 * Propriedades de um clip só: nome, velocidade, como ocupa a tela (encaixar/preencher/esticar, zoom e posição),
 * volume, o que fazer no fim e efeitos próprios. Muda na hora se o clip estiver tocando.
 */
export function ClipPropsPanel({ clip, layer, column, playing, library, onClose }: {
  clip: Clip; layer: number; column: number; playing: boolean; library: EffectDef[]; onClose: () => void
}) {
  const [p, setP] = useState<ClipProps>({ ...DEFAULT_PROPS, ...(clip.props ?? {}) })
  const [name, setName] = useState(clip.name)
  const [chain, setChain] = useState<EffectInstance[]>(clip.props?.effects ?? [])
  useEffect(() => { setChain(clip.props?.effects ?? []) }, [clip.props?.effects])

  const isVideo = clip.kind === 'video'
  const hasPicture = clip.kind === 'video' || clip.kind === 'camera' || clip.kind === 'spout' || clip.kind === 'ndi'
  const update = (patch: Partial<ClipProps>, newName = name) => {
    const next = { ...p, ...patch }
    setP(next)
    send({ type: 'setClipProps', layer, column, props: { ...next, name: newName } })
  }
  const slider = (label: string, key: 'speed' | 'zoom' | 'offsetX' | 'offsetY' | 'volume', min: number, max: number, step: number, fmt: (v: number) => string) => (
    <label>{label} <output>{fmt(p[key])}</output>
      <input type="range" min={min} max={max} step={step} value={p[key]} onChange={(e) => update({ [key]: Number(e.target.value) })} />
    </label>
  )

  return (
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal clip-props" onMouseDown={(e) => e.stopPropagation()}>
        <div className="bible-head">
          <h2>Propriedades do clip</h2>
          {!playing && <button className="small primary" onClick={() => send({ type: 'trigger', layer, column })}>Tocar para ver</button>}
          <button className="small" onClick={() => { update({ ...DEFAULT_PROPS, effects: p.effects }) }} title="Volta velocidade, ajuste, posição, volume e fim ao padrão">Padrão</button>
          <button className="small" onClick={onClose}>Fechar</button>
        </div>
        <label className="clip-props-name">Nome
          <input className="name" value={name} onChange={(e) => setName(e.target.value)} onBlur={() => update({}, name)}
            onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur() }} />
        </label>
        {clip.path && <p className="meta clip-path" title={clip.path}>{clip.path}</p>}

        <div className="clip-props-body">
          <div>
            {isVideo && (
              <>
                <h3>Reprodução</h3>
                {slider('Velocidade', 'speed', 0.1, 4, 0.05, (v) => v.toFixed(2) + '×')}
                <div className="row chips">
                  {[0.5, 0.75, 1, 1.5, 2].map((v) => (
                    <button key={v} className={`small ${p.speed === v ? 'active' : ''}`} onClick={() => update({ speed: v })}>{v}×</button>
                  ))}
                </div>
                {p.speed !== 1 && <p className="meta">Fora da velocidade normal o som deste clip fica mudo.</p>}
                <div className="row chips">
                  <span>No fim:</span>
                  <button className={`small ${p.end === 'repetir' ? 'active' : ''}`} onClick={() => update({ end: 'repetir' })}>Repetir</button>
                  <button className={`small ${p.end === 'parar' ? 'active' : ''}`} onClick={() => update({ end: 'parar' })}>Parar no último quadro</button>
                </div>
                {slider('Volume', 'volume', 0, 1.5, 0.01, (v) => Math.round(v * 100) + '%')}
              </>
            )}
            {hasPicture && (
              <>
                <h3>Na tela</h3>
                <div className="row chips">
                  {([['encaixar', 'Encaixar (inteiro)'], ['preencher', 'Preencher (sem bordas)'], ['esticar', 'Esticar']] as const).map(([v, l]) => (
                    <button key={v} className={`small ${p.fit === v ? 'active' : ''}`} onClick={() => update({ fit: v })}>{l}</button>
                  ))}
                </div>
                {slider('Tamanho (zoom)', 'zoom', 0.1, 4, 0.01, (v) => Math.round(v * 100) + '%')}
                {slider('Posição horizontal', 'offsetX', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
                {slider('Posição vertical', 'offsetY', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
                <div className="row chips">
                  <button className="small" onClick={() => update({ zoom: 1, offsetX: 0, offsetY: 0 })}>Centralizar e tamanho normal</button>
                </div>
              </>
            )}
            {!hasPicture && <p className="meta">Este tipo de clip não tem velocidade nem ajuste de tela; use os efeitos ao lado.</p>}
          </div>
          <div className="clip-fx">
            <EffectsPanel library={library} target={clipTarget(layer, column)} targetName={`clip ${name}`} chain={chain} onChange={setChain} />
          </div>
        </div>
      </div>
    </div>
  )
}
