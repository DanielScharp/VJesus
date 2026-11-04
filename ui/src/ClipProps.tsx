import { useEffect, useState } from 'react'
import { send, type Clip, type ClipNext, type ClipProps, type Composition, type EffectDef, type EffectInstance, type LayerPlayback } from './bridge'
import { EffectsPanel } from './EffectsPanel'
import { CornerControls, RotationControl } from './ShapeControls'

export const DEFAULT_PROPS: ClipProps = { speed: 1, fit: 'encaixar', zoom: 1, offsetX: 0, offsetY: 0, volume: 1, end: 'repetir', rotation: 0, width: 1, height: 1, radius: [0, 0, 0, 0], effects: [] }

/** Efeitos de clip usam este número de "alvo" nas mensagens de efeito (o motor sabe que é um clip). */
export const clipTarget = (layer: number, column: number) => 10000 + layer * 1000 + column

/**
 * Propriedades de um clip só: nome, velocidade, como ocupa a tela (encaixar/preencher/esticar, zoom e posição),
 * volume, o que fazer no fim e efeitos próprios. Muda na hora se o clip estiver tocando.
 */
/** 83,5 → "1:23.5" */
export function fmtTime(s: number) {
  if (!isFinite(s) || s < 0) s = 0
  const m = Math.floor(s / 60), r = s - m * 60
  return `${m}:${r.toFixed(1).padStart(4, '0')}`
}

/**
 * Linha do tempo do vídeo: posição (arrastável quando está no ar), play/pause/stop/do início, mudo da
 * layer e os pontos de corte (entrada e saída), marcados pelos controles ou "aqui" no ponto em que está tocando.
 */
function VideoTimeline({ layer, playing, pb, duration, inPoint, outPoint, muted, onMute, onRange }: {
  layer: number; playing: boolean; pb: LayerPlayback; duration: number; inPoint: number; outPoint: number
  muted: boolean; onMute: (m: boolean) => void; onRange: (inPoint: number, outPoint: number) => void
}) {
  const [drag, setDrag] = useState<number | null>(null)
  const out = outPoint > 0 ? outPoint : duration
  const live = playing && pb && pb.duration > 0
  const pos = drag ?? (live ? pb!.position : inPoint)
  const pct = (v: number) => (duration > 0 ? `${(v / duration) * 100}%` : '0%')
  return (
    <div className="video-timeline">
      <h3>Linha do tempo e corte</h3>
      {duration <= 0 ? (
        <p className="meta">Toque o clip uma vez para o VJesus saber a duração; depois dá para marcar a entrada e a saída mesmo sem ele tocar.</p>
      ) : (
        <>
          <div className="timeline-bar">
            <div className="timeline-range" style={{ left: pct(inPoint), width: `calc(${pct(out)} - ${pct(inPoint)})` }} />
            {live && <div className="timeline-head" style={{ left: pct(pos) }} />}
            <input type="range" min={0} max={duration} step={0.01} value={pos} disabled={!live}
              title={live ? 'Arraste para ir a outro ponto do vídeo' : 'Toque o clip para mudar a posição'}
              onChange={(e) => setDrag(Number(e.target.value))}
              onPointerUp={() => { if (drag !== null) send({ type: 'seek', layer, position: drag }); setDrag(null) }}
              onKeyUp={() => { if (drag !== null) send({ type: 'seek', layer, position: drag }); setDrag(null) }} />
          </div>
          <div className="row timeline-info">
            <span className="timeline-time">{fmtTime(pos)} / {fmtTime(duration)}</span>
            {live && (
              <>
                <button className="small" onClick={() => send({ type: 'transport', layer, action: 'toggle' })}>{pb!.playing ? '⏸ Pausar' : '▶ Tocar'}</button>
                <button className="small" title="Para e volta para a entrada" onClick={() => send({ type: 'transport', layer, action: 'stop' })}>⏹ Parar</button>
                <button className="small" title="Volta para a entrada e toca" onClick={() => send({ type: 'transport', layer, action: 'restart' })}>⏮ Do início</button>
              </>
            )}
            <button className={`small ${muted ? 'active' : ''}`} title="Desliga o som da layer (o volume continua guardado)" onClick={() => onMute(!muted)}>{muted ? '🔇 Mudo' : '🔊 Som'}</button>
          </div>
          <label>Entrada <output>{fmtTime(inPoint)}</output>
            <input type="range" min={0} max={duration} step={0.01} value={inPoint}
              onChange={(e) => { const v = Number(e.target.value); onRange(Math.min(v, out - 0.1), outPoint) }} />
          </label>
          <label>Saída <output>{fmtTime(out)}</output>
            <input type="range" min={0} max={duration} step={0.01} value={out}
              onChange={(e) => { const v = Number(e.target.value); onRange(inPoint, v >= duration - 0.01 ? 0 : Math.max(v, inPoint + 0.1)) }} />
          </label>
          <div className="row chips">
            {live && <button className="small" title="A entrada vira o ponto que está tocando agora" onClick={() => onRange(Math.min(pb!.position, out - 0.1), outPoint)}>Entrada aqui</button>}
            {live && <button className="small" title="A saída vira o ponto que está tocando agora" onClick={() => onRange(inPoint, Math.max(pb!.position, inPoint + 0.1))}>Saída aqui</button>}
            <button className="small" disabled={inPoint === 0 && outPoint === 0} onClick={() => onRange(0, 0)}>Sem corte</button>
          </div>
          {(inPoint > 0 || outPoint > 0) && <p className="meta">Toca só de {fmtTime(inPoint)} até {fmtTime(out)} ({fmtTime(out - inPoint)}); “No fim” abaixo decide se repete o trecho ou para.</p>}
        </>
      )}
    </div>
  )
}

/**
 * Próxima exibição: quando este vídeo acabar, entra outro clip (qualquer aba e layer), a próxima coluna
 * desta layer, ou a layer é limpa. A troca usa a transição da layer de destino.
 */
function NextPicker({ comp, layer, column, next, onChange }: {
  comp: Composition; layer: number; column: number; next: ClipNext; onChange: (n: ClipNext) => void
}) {
  const deck = comp.decks.find((d) => d.id === next.deckId) ?? comp.decks[comp.currentDeck]
  const row = deck?.clips?.[next.layer] ?? []
  const colName = (c: number) => deck?.columnNames?.[c] || `Coluna ${c + 1}`
  const options = row.map((c, i) => ({ c, i })).filter(({ c, i }) => c && !(deck?.id === comp.decks[comp.currentDeck]?.id && next.layer === layer && i === column))
  const chosen = row[next.column]
  return (
    <div className="next-picker">
      <div className="row chips">
        {([['clip', 'Tocar outro clip'], ['proxima', 'Próxima coluna desta layer'], ['limpar', 'Limpar a layer']] as const).map(([v, l]) => (
          <button key={v} className={`small ${next.action === v ? 'active' : ''}`} onClick={() => onChange({ ...next, action: v })}>{l}</button>
        ))}
      </div>
      {next.action === 'clip' && (
        <>
          <label>Aba <output />
            <select value={deck?.id ?? ''} onChange={(e) => onChange({ ...next, deckId: e.target.value, column: -1 })}>
              {comp.decks.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </label>
          <label>Layer <output />
            <select value={next.layer} onChange={(e) => onChange({ ...next, layer: Number(e.target.value), column: -1 })}>
              {comp.layers.map((_, i) => i).reverse().map((i) => <option key={i} value={i}>{comp.layers[i].name}{i === layer ? ' (esta)' : ''}</option>)}
            </select>
          </label>
          <label>Clip <output />
            <select value={next.column} onChange={(e) => onChange({ ...next, column: Number(e.target.value) })}>
              <option value={-1}>Escolha…</option>
              {options.map(({ c, i }) => <option key={i} value={i}>{c!.name} ({colName(i)})</option>)}
            </select>
          </label>
          {next.column < 0 && <p className="warn-text">Escolha o clip que entra quando este vídeo acabar.</p>}
          {next.column >= 0 && !chosen && <p className="warn-text">Essa célula ficou vazia; escolha outro clip.</p>}
          {chosen && next.layer !== layer && <p className="meta">Quando acabar, este vídeo sai e “{chosen.name}” entra na {comp.layers[next.layer]?.name}.</p>}
        </>
      )}
      {next.action === 'proxima' && <p className="meta">Toca o próximo clip à direita nesta layer; se não houver, limpa a layer.</p>}
      {next.action === 'limpar' && <p className="meta">O vídeo some com a transição da layer e fica o que estiver embaixo.</p>}
      <p className="meta">Enquanto o próximo não entra, o último quadro fica parado na tela, sem piscar.</p>
    </div>
  )
}

export function ClipPropsPanel({ clip, layer, column, playing, library, onClose, pb, muted, onMute, comp }: {
  clip: Clip; layer: number; column: number; playing: boolean; library: EffectDef[]; onClose: () => void
  pb: LayerPlayback; muted: boolean; onMute: (m: boolean) => void; comp?: Composition
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
  const slider = (label: string, key: 'speed' | 'zoom' | 'offsetX' | 'offsetY' | 'volume' | 'width' | 'height', min: number, max: number, step: number, fmt: (v: number) => string) => (
    <label>{label} <output>{fmt(p[key] ?? 1)}</output>
      <input type="range" min={min} max={max} step={step} value={p[key] ?? 1} onChange={(e) => update({ [key]: Number(e.target.value) })} />
    </label>
  )

  return (
    <section className="clip-props" onClick={(e) => e.stopPropagation()}>
      <div>
        <div className="bible-head">
          <h2>Propriedades do clip: <span className="fx-target">{name}</span> <span className="meta">· layer {layer + 1}, coluna {column + 1}{playing ? ' · no ar' : ' · não está tocando'}</span></h2>
          {!playing && <button className="small" onClick={() => send({ type: 'trigger', layer, column })} title="Toca este clip no telão">Tocar no telão</button>}
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
              <VideoTimeline layer={layer} playing={playing} pb={pb} duration={pb?.duration || clip.duration || 0}
                inPoint={p.inPoint ?? 0} outPoint={p.outPoint ?? 0} muted={muted} onMute={onMute}
                onRange={(inPoint, outPoint) => update({ inPoint, outPoint })} />
            )}
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
                  <button className={`small ${p.end === 'seguir' ? 'active' : ''}`} title="Quando o vídeo acabar, entra sozinho o que você programar"
                    onClick={() => update({ end: 'seguir', next: p.next ?? { action: 'clip', deckId: comp?.decks[comp.currentDeck]?.id ?? null, layer, column: -1 } })}>Depois, ir para…</button>
                </div>
                {p.end === 'seguir' && comp && (
                  <NextPicker comp={comp} layer={layer} column={column} next={p.next ?? { action: 'clip', layer, column: -1 }} onChange={(next) => update({ next })} />
                )}
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
                {slider('Escala', 'zoom', 0.1, 4, 0.01, (v) => Math.round(v * 100) + '%')}
                {slider('Largura', 'width', 0.05, 3, 0.01, (v) => Math.round(v * 100) + '%')}
                {slider('Altura', 'height', 0.05, 3, 0.01, (v) => Math.round(v * 100) + '%')}
                {slider('Posição horizontal', 'offsetX', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
                {slider('Posição vertical', 'offsetY', -1, 1, 0.005, (v) => (v * 50).toFixed(0) + '%')}
                <RotationControl value={p.rotation ?? 0} onChange={(v) => update({ rotation: v })} />
                <CornerControls radius={p.radius ?? [0, 0, 0, 0]} onChange={(r) => update({ radius: r })} />
                <div className="row chips">
                  <button className="small" onClick={() => update({ zoom: 1, offsetX: 0, offsetY: 0, width: 1, height: 1, rotation: 0, radius: [0, 0, 0, 0] })}>Voltar ao normal</button>
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
    </section>
  )
}
