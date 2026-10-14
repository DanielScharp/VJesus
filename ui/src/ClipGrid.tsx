import { useState } from 'react'
import { EffectChips, type ToggleEffect } from './EffectChips'
import { BLEND_MODES, send, sendFiles, type EffectDef, type BlendMode, type Clip, type Composition, type LayerPlayback, type LiveSources } from './bridge'

type Props = {
  comp: Composition
  thumbs: Record<string, string>
  playback: LayerPlayback[]
  onLayerChange: (layer: number, patch: { opacity?: number; blend?: BlendMode }) => void
  selected: number
  onSelect: (layer: number) => void
  live: LiveSources | null
  library: EffectDef[]
  onToggleEffect: ToggleEffect
}

type Menu = { layer: number; column: number; x: number; y: number; filled: boolean }

const CLIP_MIME = 'application/x-vj-clip'

const BLEND_LABELS: Record<string, string> = {
  normal: 'Normal', add: 'Add', multiply: 'Multiply', screen: 'Screen',
  overlay: 'Overlay', difference: 'Difference', lighten: 'Lighten', darken: 'Darken',
}

function ClipThumb({ clip, thumb }: { clip: Clip; thumb?: string }) {
  if (clip.kind === 'video')
    return thumb ? <img src={thumb} alt="" draggable={false} /> : <div className="thumb-placeholder" />
  if (clip.kind === 'lyrics')
    return <div className="thumb-lyrics"><span>Aa</span><small>LETRAS</small></div>
  if (clip.kind === 'camera' || clip.kind === 'spout')
    return <div className="thumb-live"><span>{clip.kind === 'camera' ? 'CÂMERA AO VIVO' : 'SPOUT AO VIVO'}</span></div>
  return <div className={clip.kind === 'plasma' ? 'thumb-plasma' : 'thumb-testcard'} />
}

export function ClipGrid({ comp, thumbs, playback, onLayerChange, selected, onSelect, live, library, onToggleEffect }: Props) {
  const [menu, setMenu] = useState<Menu | null>(null)
  const [dropTarget, setDropTarget] = useState<string | null>(null)

  // layer de cima aparece primeiro, como no Resolume
  const order = comp.layers.map((_, i) => i).reverse()
  const columns = Array.from({ length: comp.columns }, (_, i) => i)

  const onDrop = (e: React.DragEvent, layer: number, column: number) => {
    e.preventDefault()
    setDropTarget(null)
    const internal = e.dataTransfer.getData(CLIP_MIME)
    if (internal) {
      const [fromLayer, fromColumn] = internal.split(':').map(Number)
      send({ type: 'moveClip', fromLayer, fromColumn, layer, column })
    } else if (e.dataTransfer.files.length > 0) {
      sendFiles({ type: 'dropFiles', layer, column }, e.dataTransfer.files)
    }
  }

  return (
    <div className="grid" onClick={() => setMenu(null)}>
      <div className="grid-row grid-head">
        <div className="layer-head">
          <button className="small" onClick={() => send({ type: 'clearAll' })}>Limpar tudo</button>
        </div>
        {columns.map((c) => (
          <button key={c} className="column-head" title="Disparar a coluna inteira" data-address={`/composition/columns/${c + 1}/connect`}
            onClick={() => send({ type: 'triggerColumn', column: c })}>
            {c + 1}
          </button>
        ))}
      </div>

      {order.map((li) => {
        const layer = comp.layers[li]
        const pb = playback[li]
        return (
          <div key={li} className="grid-row">
            <div className={`layer-head ${layer.active >= 0 ? 'live' : ''} ${selected === li ? 'selected' : ''}`}
              title="Clique para editar os efeitos desta layer" onClick={() => onSelect(li)}>
              <div className="layer-title">
                <span>{layer.name}{layer.hasAudio && layer.active >= 0 && <span title="Este clip tem áudio"> 🔊</span>}{layer.sendToStage && <em className="ret-badge" title="Aparece na tela de retorno quando tocar">RET</em>}</span>
                <button className="small" title="Parar a layer" data-address={`/composition/layers/${li + 1}/clear`} onClick={() => send({ type: 'clearLayer', layer: li })}>✕</button>
              </div>
              <input type="range" min={0} max={1} step={0.01} value={layer.opacity} title="Opacidade"
                data-address={`/composition/layers/${li + 1}/video/opacity`}
                onChange={(e) => onLayerChange(li, { opacity: Number(e.target.value) })} />
              <select value={layer.blend}
                onChange={(e) => onLayerChange(li, { blend: e.target.value as BlendMode })}>
                {BLEND_MODES.map((b) => <option key={b} value={b}>{BLEND_LABELS[b]}</option>)}
              </select>
              <EffectChips chain={layer.effects} target={li} library={library} onToggle={onToggleEffect} />
            </div>

            {columns.map((c) => {
              const clip = layer.clips[c]
              const key = `${li}:${c}`
              const active = layer.active === c
              return (
                <div key={c} data-address={clip ? `/composition/layers/${li + 1}/clips/${c + 1}/connect` : undefined}
                  className={`cell ${clip ? 'filled' : 'empty'} ${active ? 'active' : ''} ${dropTarget === key ? 'drop' : ''}`}
                  draggable={!!clip}
                  onDragStart={(e) => e.dataTransfer.setData(CLIP_MIME, key)}
                  onDragOver={(e) => { e.preventDefault(); setDropTarget(key) }}
                  onDragLeave={() => setDropTarget((t) => (t === key ? null : t))}
                  onDrop={(e) => onDrop(e, li, c)}
                  onClick={(e) => {
                    e.stopPropagation()
                    if (clip) send({ type: 'trigger', layer: li, column: c })
                    else { send({ type: 'listLiveSources' }); setMenu({ layer: li, column: c, x: e.clientX, y: e.clientY, filled: false }) }
                  }}
                  onContextMenu={(e) => {
                    e.preventDefault()
                    e.stopPropagation()
                    send({ type: 'listLiveSources' })
                    setMenu({ layer: li, column: c, x: e.clientX, y: e.clientY, filled: !!clip })
                  }}>
                  {clip ? (
                    <>
                      <ClipThumb clip={clip} thumb={clip.path ? thumbs[clip.path] : undefined} />
                      <span className="clip-name">{clip.name}</span>
                      {active && pb && pb.duration > 0 && (
                        <div className="progress"><div style={{ width: `${(pb.position / pb.duration) * 100}%` }} /></div>
                      )}
                    </>
                  ) : (
                    <span className="plus">+</span>
                  )}
                </div>
              )
            })}
          </div>
        )
      })}

      {menu && (
        <div className="menu" style={{ left: menu.x, top: menu.y }} onClick={(e) => e.stopPropagation()}>
          {(() => {
            const cell = { layer: menu.layer, column: menu.column }
            const pick = (fn: () => void) => () => { fn(); setMenu(null) }
            return (
              <>
                <button onClick={pick(() => send({ type: 'browseClip', ...cell }))}>Vídeo…</button>
                <button onClick={pick(() => send({ type: 'setGenerator', kind: 'plasma', ...cell }))}>Gerador: Plasma</button>
                <button onClick={pick(() => send({ type: 'setGenerator', kind: 'testCard', ...cell }))}>Gerador: Test card</button>
                <button onClick={pick(() => send({ type: 'setGenerator', kind: 'lyrics', ...cell }))}>Letras (aba Letras)</button>
                <div className="menu-label">Entrada ao vivo</div>
                {!live && <div className="menu-empty">procurando…</div>}
                {live?.cameras.map((name) => (
                  <button key={'c' + name} onClick={pick(() => send({ type: 'setLiveClip', kind: 'camera', name, ...cell }))}>Câmera: {name}</button>
                ))}
                {live?.spout.map((name) => (
                  <button key={'s' + name} onClick={pick(() => send({ type: 'setLiveClip', kind: 'spout', name, ...cell }))}>Spout: {name}</button>
                ))}
                {live && live.cameras.length + live.spout.length === 0 && <div className="menu-empty">nenhuma câmera ou sender Spout</div>}
                {menu.filled && <button className="danger" onClick={pick(() => send({ type: 'removeClip', ...cell }))}>Remover</button>}
              </>
            )
          })()}
        </div>
      )}
    </div>
  )
}
