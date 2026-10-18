import { useState } from 'react'
import { EffectChips, type ToggleEffect } from './EffectChips'
import { DEFAULT_GEN, GeneratorPanel, genThumbStyle } from './GeneratorPanel'
import { ClipPropsPanel } from './ClipProps'
import { BLEND_MODES, send, sendFiles, type EffectDef, type BlendMode, type Clip, type Composition, type GeneratorSettings, type LayerPlayback, type LiveSources } from './bridge'

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

/**
 * Posição do menu de botão direito: na metade de baixo da tela ele abre para cima, perto da borda direita
 * abre para a esquerda; a altura máxima é o espaço que sobra (e rola se precisar).
 */
function menuPos(x: number, y: number): React.CSSProperties {
  const up = y > window.innerHeight / 2
  const left = x > window.innerWidth - 280
  return {
    ...(up ? { bottom: window.innerHeight - y, maxHeight: y - 8 } : { top: y, maxHeight: window.innerHeight - y - 8 }),
    ...(left ? { right: window.innerWidth - x } : { left: x }),
  }
}

/** Menus de botão direito: se ainda assim não couber na tela, puxa para dentro. */
function fitMenu(el: HTMLDivElement | null) {
  if (!el) return
  requestAnimationFrame(() => {
    const r = el.getBoundingClientRect()
    if (r.bottom > window.innerHeight - 8) el.style.top = `px`
    if (r.right > window.innerWidth - 8) el.style.left = `px`
  })
}

// resolução e fps pedidos à câmera/placa de captura (se o aparelho não aceitar, abre no padrão dele)
const CAPTURE_SIZES: [string, string][] = [['', 'Resolução padrão'], ['1280x720', '1280×720 (HD)'], ['1920x1080', '1920×1080 (Full HD)'], ['2560x1440', '2560×1440'], ['3840x2160', '3840×2160 (4K)'], ['720x480', '720×480 (SD)']]
const CAPTURE_FPS: [number, string][] = [[0, 'FPS padrão'], [25, '25 fps'], [30, '30 fps'], [50, '50 fps'], [60, '60 fps']]

const BLEND_LABELS: Record<string, string> = {
  normal: 'Normal', add: 'Add', multiply: 'Multiply', screen: 'Screen',
  overlay: 'Overlay', difference: 'Difference', lighten: 'Lighten', darken: 'Darken',
}

function ClipThumb({ clip, thumb }: { clip: Clip; thumb?: string }) {
  if (clip.kind === 'video')
    return thumb ? <img src={thumb} alt="" draggable={false} /> : <div className="thumb-placeholder" />
  if (clip.kind === 'lyrics')
    return <div className="thumb-lyrics"><span>Aa</span><small>LETRAS</small></div>
  if (clip.kind === 'camera' || clip.kind === 'spout' || clip.kind === 'ndi')
    return <div className="thumb-live"><span>{clip.kind === 'camera' ? ['AO VIVO', clip.captureSize, clip.captureFps ? `${clip.captureFps} fps` : null].filter(Boolean).join(' · ') : clip.kind === 'ndi' ? 'NDI AO VIVO' : 'SPOUT AO VIVO'}</span></div>
  return <div className={clip.kind === 'plasma' ? 'thumb-plasma' : 'thumb-testcard'} style={clip.kind === 'plasma' ? genThumbStyle(clip.gen) : undefined} />
}

export function ClipGrid({ comp, thumbs, playback, onLayerChange, selected, onSelect, live, library, onToggleEffect }: Props) {
  const [menu, setMenu] = useState<Menu | null>(null)
  const [dropTarget, setDropTarget] = useState<string | null>(null)
  const [colMenu, setColMenu] = useState<{ column: number; x: number; y: number } | null>(null)
  const [layerMenu, setLayerMenu] = useState<{ layer: number; x: number; y: number } | null>(null)
  const [genEdit, setGenEdit] = useState<{ layer: number; column: number; gen: GeneratorSettings | null } | null>(null)
  const [propsEdit, setPropsEdit] = useState<{ layer: number; column: number } | null>(null)
  const [deckMenu, setDeckMenu] = useState<{ index: number; x: number; y: number } | null>(null)
  const [renaming, setRenaming] = useState<{ kind: 'deck' | 'layer' | 'column'; index: number } | null>(null)

  const finishRename = (value: string) => {
    const name = value.trim()
    if (renaming?.kind === 'column') { send({ type: 'columnRename', column: renaming.index, name }); setRenaming(null); return }
    if (renaming && name) {
      if (renaming.kind === 'deck') send({ type: 'deckRename', index: renaming.index, name })
      else send({ type: 'layerParams', layer: renaming.index, name })
    }
    setRenaming(null)
  }
  const renameKeys = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') finishRename(e.currentTarget.value)
    if (e.key === 'Escape') setRenaming(null)
  }

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
    <div className="grid-wrap" onClick={() => { setMenu(null); setColMenu(null); setDeckMenu(null); setLayerMenu(null) }}>
      {/* ---------- abas de clips ---------- */}
      <div className="deck-tabs">
        {comp.decks.map((d, i) => (
          renaming?.kind === 'deck' && renaming.index === i ? (
            <input key={d.id} className="rename" autoFocus defaultValue={d.name}
              onBlur={(e) => finishRename(e.target.value)} onKeyDown={renameKeys} />
          ) : (
            <button key={d.id} className={`deck-tab ${i === comp.currentDeck ? 'active' : ''}`}
              title="Clique para abrir · duplo clique para renomear · botão direito para mais opções"
              onClick={() => i !== comp.currentDeck && send({ type: 'deckSelect', index: i })}
              onDoubleClick={() => setRenaming({ kind: 'deck', index: i })}
              onContextMenu={(e) => { e.preventDefault(); e.stopPropagation(); setDeckMenu({ index: i, x: e.clientX, y: e.clientY }) }}>
              {d.name}
              {i !== comp.currentDeck && comp.layers.some((l) => l.playingDeck === d.id) && <span className="deck-live" title="Há clips desta aba tocando">●</span>}
            </button>
          )
        ))}
        <button className="small" title="Nova aba de clips" onClick={() => send({ type: 'deckAdd' })}>+ Aba</button>
      </div>

    <div className="grid">
      <div className="grid-row grid-head">
        <div className="layer-head">
          <button className="small" onClick={() => send({ type: 'clearAll' })}>Limpar tudo</button>
        </div>
        {columns.map((c) => (
          renaming?.kind === 'column' && renaming.index === c ? (
            <input key={c} className="rename column-rename" autoFocus defaultValue={comp.columnNames?.[c] ?? ''} placeholder={`Coluna ${c + 1}`}
              onBlur={(e) => finishRename(e.target.value || ' ')} onKeyDown={renameKeys} />
          ) : (
            <button key={c} className="column-head" title={`Disparar a coluna inteira · botão direito: renomear, inserir ou remover`} data-address={`/composition/columns/${c + 1}/connect`}
              onClick={() => send({ type: 'triggerColumn', column: c })}
              onContextMenu={(e) => { e.preventDefault(); e.stopPropagation(); setColMenu({ column: c, x: e.clientX, y: e.clientY }) }}>
              <span className="col-num">{c + 1}</span>
              {comp.columnNames?.[c] && <span className="col-name">{comp.columnNames[c]}</span>}
            </button>
          )
        ))}
        <button className="column-head column-add" title="Adicionar coluna" onClick={() => send({ type: 'columnAdd' })}>+</button>
      </div>

      {order.map((li) => {
        const layer = comp.layers[li]
        const pb = playback[li]
        return (
          <div key={li} className="grid-row">
            <div className={`layer-head ${layer.playing ? 'live' : ''} ${selected === li ? 'selected' : ''}`}
              title="Clique para editar os efeitos desta layer · botão direito: inserir, duplicar, mover ou remover"
              onClick={() => onSelect(li)}
              onContextMenu={(e) => { e.preventDefault(); e.stopPropagation(); setLayerMenu({ layer: li, x: e.clientX, y: e.clientY }) }}>
              <div className="layer-title">
                {renaming?.kind === 'layer' && renaming.index === li
                  ? <input className="rename" autoFocus defaultValue={layer.name} onClick={(e) => e.stopPropagation()}
                      onBlur={(e) => finishRename(e.target.value)} onKeyDown={renameKeys} />
                  : <span title="Duplo clique para renomear" onDoubleClick={(e) => { e.stopPropagation(); setRenaming({ kind: 'layer', index: li }) }}>{layer.name}{layer.hasAudio && layer.playing && <span title="Este clip tem áudio"> 🔊</span>}{layer.sendToStage && <em className="ret-badge" title="Aparece na tela de retorno quando tocar">RET</em>}</span>}
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
                  className={`cell ${clip ? 'filled' : 'empty'} ${active ? 'active' : ''} ${dropTarget === key ? 'drop' : ''} ${propsEdit?.layer === li && propsEdit?.column === c ? 'editing' : ''}`}
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
                      <span className={`clip-name ${propsEdit?.layer === li && propsEdit?.column === c ? 'editing' : ''}`} title="Clique no nome para ver e mudar as propriedades (sem tocar o clip)"
                        onClick={(e) => { e.stopPropagation(); setMenu(null); setPropsEdit(propsEdit?.layer === li && propsEdit?.column === c ? null : { layer: li, column: c }) }}>{clip.name}</span>
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

      <div className="grid-row layer-add-row">
        <button className="small" title="Nova layer no topo (fica por cima das outras)" onClick={() => send({ type: 'layerAdd' })}>+ Layer</button>
        <span className="meta">{comp.layers.length} layers · botão direito no nome de uma layer para inserir, duplicar, mover ou remover</span>
      </div>

      {menu && (
        <div className="menu" ref={fitMenu} style={menuPos(menu.x, menu.y)} onClick={(e) => e.stopPropagation()}>
          {(() => {
            const cell = { layer: menu.layer, column: menu.column }
            const pick = (fn: () => void) => () => { fn(); setMenu(null) }
            const current = comp.layers[menu.layer]?.clips[menu.column] ?? null
            return (
              <>
                {menu.filled && <button className="danger" onClick={pick(() => send({ type: 'removeClip', ...cell }))}>Remover este clip</button>}
                {menu.filled && <button onClick={pick(() => setPropsEdit(cell))}>Propriedades do clip… (velocidade, tamanho, efeitos)</button>}
                {current?.kind === 'plasma' && <button onClick={pick(() => setGenEdit({ ...cell, gen: current.gen ?? null }))}>Ajustar fundo animado…</button>}
                {menu.filled && <div className="menu-label">Trocar por</div>}
                <button onClick={pick(() => send({ type: 'browseClip', ...cell }))}>Vídeo ou imagem…</button>
                <button onClick={pick(() => { send({ type: 'setGenerator', kind: 'plasma', ...cell, gen: DEFAULT_GEN }); setGenEdit({ ...cell, gen: DEFAULT_GEN }) })}>Fundo animado (ajustável)…</button>
                <button onClick={pick(() => send({ type: 'setGenerator', kind: 'testCard', ...cell }))}>Gerador: Test card</button>
                <button onClick={pick(() => send({ type: 'setGenerator', kind: 'lyrics', ...cell }))}>Letras (aba Letras)</button>
                {current?.kind === 'camera' && (
                  <>
                    <div className="menu-label">Captura: {current.name}</div>
                    <div className="menu-inline">
                      <select value={current.captureSize ?? ''} onChange={(e) => send({ type: 'setCapture', ...cell, size: e.target.value || null, fps: current.captureFps ?? null })}>
                        {CAPTURE_SIZES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                      </select>
                      <select value={current.captureFps ?? 0} onChange={(e) => send({ type: 'setCapture', ...cell, size: current.captureSize ?? null, fps: Number(e.target.value) || null })}>
                        {CAPTURE_FPS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                      </select>
                    </div>
                  </>
                )}
                <div className="menu-label">Câmeras e placas de captura</div>
                {!live && <div className="menu-empty">procurando…</div>}
                {live?.cameras.map((name) => (
                  <button key={'c' + name} onClick={pick(() => send({ type: 'setLiveClip', kind: 'camera', name, ...cell }))}>{name}</button>
                ))}
                {live && live.cameras.length === 0 && <div className="menu-empty">nenhuma câmera ou placa de captura conectada</div>}
                <div className="menu-label">NDI (pela rede)</div>
                {live && live.ndiAvailable === false && <div className="menu-empty">instale o NDI Tools (ndi.video/tools) e reabra o VJesus</div>}
                {live?.ndiAvailable && !live.ndi?.length && <div className="menu-empty">procurando fontes NDI na rede…</div>}
                {live?.ndi?.map((name) => (
                  <button key={'n' + name} onClick={pick(() => send({ type: 'setLiveClip', kind: 'ndi', name, ...cell }))}>{name}</button>
                ))}
                <div className="menu-label">Spout (outros programas neste PC)</div>
                {live?.spout.map((name) => (
                  <button key={'s' + name} onClick={pick(() => send({ type: 'setLiveClip', kind: 'spout', name, ...cell }))}>{name}</button>
                ))}
                {live && live.spout.length === 0 && <div className="menu-empty">nenhum programa enviando por Spout</div>}
              </>
            )
          })()}
        </div>
      )}
    </div>

      {propsEdit && comp.layers[propsEdit.layer]?.clips[propsEdit.column] && (
        <ClipPropsPanel key={propsEdit.layer + ':' + propsEdit.column} clip={comp.layers[propsEdit.layer].clips[propsEdit.column]!}
          layer={propsEdit.layer} column={propsEdit.column} playing={comp.layers[propsEdit.layer].active === propsEdit.column}
          library={library} onClose={() => setPropsEdit(null)} />
      )}

      {genEdit && (
        <GeneratorPanel key={genEdit.layer + ':' + genEdit.column} layer={genEdit.layer} column={genEdit.column} initial={genEdit.gen}
          playing={comp.layers[genEdit.layer]?.active === genEdit.column} onClose={() => setGenEdit(null)} />
      )}

      {layerMenu && (() => {
        const l = layerMenu.layer
        const close = () => setLayerMenu(null)
        return (
          <div className="menu" ref={fitMenu} style={menuPos(layerMenu.x, layerMenu.y)} onClick={(e) => e.stopPropagation()}>
            <div className="menu-label">{comp.layers[l]?.name}</div>
            <button onClick={() => { setRenaming({ kind: 'layer', index: l }); close() }}>Renomear</button>
            <button onClick={() => { send({ type: 'layerAdd', layer: l }); close() }}>Inserir layer acima</button>
            <button onClick={() => { send({ type: 'layerAdd', layer: l - 1 }); close() }}>Inserir layer abaixo</button>
            <button onClick={() => { send({ type: 'layerDuplicate', layer: l }); close() }}>Duplicar (com os clips e efeitos)</button>
            <button disabled={l >= comp.layers.length - 1} onClick={() => { send({ type: 'layerMove', layer: l, direction: 1 }); close() }}>Mover para cima</button>
            <button disabled={l <= 0} onClick={() => { send({ type: 'layerMove', layer: l, direction: -1 }); close() }}>Mover para baixo</button>
            <button className="danger" disabled={comp.layers.length <= 1} onClick={() => {
              const has = comp.layers[l].clips.some(Boolean) || comp.layers[l].playing
              if (!has || window.confirm(`Remover a layer "${comp.layers[l].name}" e os clips dela (em todas as abas)?`)) send({ type: 'layerRemove', layer: l })
              close()
            }}>Remover layer</button>
          </div>
        )
      })()}

      {colMenu && (
        <div className="menu" ref={fitMenu} style={menuPos(colMenu.x, colMenu.y)} onClick={(e) => e.stopPropagation()}>
          <div className="menu-label">Coluna {colMenu.column + 1}{comp.columnNames?.[colMenu.column] ? ` · ${comp.columnNames[colMenu.column]}` : ''}</div>
          <button onClick={() => { setRenaming({ kind: 'column', index: colMenu.column }); setColMenu(null) }}>Renomear coluna</button>
          <button onClick={() => { send({ type: 'columnAdd', after: colMenu.column - 1 }); setColMenu(null) }}>Inserir coluna antes</button>
          <button onClick={() => { send({ type: 'columnAdd', after: colMenu.column }); setColMenu(null) }}>Inserir coluna depois</button>
          <button className="danger" disabled={comp.columns <= 1} onClick={() => {
            const filled = comp.layers.some((l) => l.clips[colMenu.column])
            if (!filled || window.confirm(`Remover a coluna ${colMenu.column + 1} e os clips dela?`)) send({ type: 'columnRemove', column: colMenu.column })
            setColMenu(null)
          }}>Remover coluna</button>
        </div>
      )}

      {deckMenu && (
        <div className="menu" ref={fitMenu} style={menuPos(deckMenu.x, deckMenu.y)} onClick={(e) => e.stopPropagation()}>
          <div className="menu-label">{comp.decks[deckMenu.index]?.name}</div>
          <button onClick={() => { setRenaming({ kind: 'deck', index: deckMenu.index }); setDeckMenu(null) }}>Renomear</button>
          <button disabled={deckMenu.index === 0} onClick={() => { send({ type: 'deckMove', index: deckMenu.index, direction: -1 }); setDeckMenu(null) }}>Mover para a esquerda</button>
          <button disabled={deckMenu.index === comp.decks.length - 1} onClick={() => { send({ type: 'deckMove', index: deckMenu.index, direction: 1 }); setDeckMenu(null) }}>Mover para a direita</button>
          <button className="danger" disabled={comp.decks.length <= 1} onClick={() => {
            if (window.confirm(`Apagar a aba "${comp.decks[deckMenu.index].name}" e os clips dela?`)) send({ type: 'deckRemove', index: deckMenu.index })
            setDeckMenu(null)
          }}>Apagar aba</button>
        </div>
      )}
    </div>
  )
}
