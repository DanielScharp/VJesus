import { useState } from 'react'
import { send, type Composition, type Display, type LyricsState, type StageState } from './bridge'
import { stripTags } from './lyrics'
import { StageView } from './StageView'

type Props = {
  preview: string | null
  stagePreview: string | null
  stage: StageState | null
  lyrics: LyricsState
  comp: Composition | null
  displays: Display[]
}

/**
 * Monitores do operador, visíveis em todas as abas: o que está no TELÃO (a composição que vai para
 * as telas) e o que está no RETORNO (a mesma tela que os músicos veem), com o resumo do que está no ar.
 */
export function MonitorsBar({ preview, stagePreview, stage, lyrics, comp, displays }: Props) {
  const [open, setOpen] = useState(() => {
    try { return localStorage.getItem('monitors') !== 'closed' } catch { return true }
  })
  const toggle = () => {
    setOpen(!open)
    try { localStorage.setItem('monitors', open ? 'closed' : 'open') } catch { /* sem armazenamento */ }
  }

  // o que está tocando agora em cada layer (de cima para baixo)
  const playing = comp
    ? comp.layers.map((l, i) => ({ l, i })).reverse()
      .filter(({ l }) => !!l.playing && l.opacity > 0)
      .map(({ l }) => `${l.name}: ${l.playing?.name ?? '?'}${l.opacity < 1 ? ` (${Math.round(l.opacity * 100)}%)` : ''}`)
    : []
  const lyricsStatus = lyrics.blank ? 'Letra: tela em branco' : lyrics.text ? `Letra: “${stripTags(lyrics.text).split('\n')[0]}”` : 'Letra: nada projetado'

  // há letra projetada mas nenhuma layer está tocando o clip Letras (ou está com opacidade 0)
  const lyricsOffAir = !!lyrics.text && !lyrics.blank && !!comp &&
    !comp.layers.some((l) => !!l.playing && l.opacity > 0 && l.playing.kind === 'lyrics')

  const stageDisplay = stage ? displays[stage.config.display] : undefined
  const stageAspect = stageDisplay ? stageDisplay.width / stageDisplay.height : 16 / 9

  return (
    <div className={`monitors ${open ? '' : 'closed'}`}>
      <button className="small monitors-toggle" onClick={toggle} title="Mostrar/ocultar os monitores">
        {open ? '▾' : '▸'} Monitores
      </button>
      {open && (
        <>
          <div className="monitor">
            <div className="monitor-head"><span className="onair">NO AR</span> TELÃO</div>
            <div className="monitor-screen" style={{ aspectRatio: '16 / 9' }}>
              {preview ? <img src={preview} alt="Telão" /> : <span className="meta">sem imagem</span>}
            </div>
            <div className="monitor-info">
              {playing.length ? playing.map((p) => <div key={p}>{p}</div>) : <div>Nenhuma layer tocando</div>}
              <div className={lyrics.blank ? 'warn-text' : ''}>{lyricsStatus}</div>
              {lyricsOffAir && <div className="warn-text">A letra não está no telão: o clip Letras está parado</div>}
              {lyrics.text && (
                <button className="small" title="Fim da música: tira a letra do telão e do retorno" onClick={() => send({ type: 'lyricsClear' })}>
                  Encerrar letra
                </button>
              )}
            </div>
          </div>
          <div className="monitor">
            <div className="monitor-head">
              {stage?.config.enabled ? <span className="onair">NO AR</span> : <span className="off">DESLIGADO</span>} RETORNO
              {stageDisplay && stage?.config.enabled && <span className="meta"> · {stageDisplay.name}</span>}
            </div>
            <div className="monitor-screen" style={{ aspectRatio: String(stageAspect) }}>
              {stage ? <StageView state={stage} preview={stagePreview} /> : null}
            </div>
            <div className="monitor-info">
              {stage && (
                <button className={`small mirror-btn ${stage.program.mirror ? 'on' : ''}`} onClick={() => send({ type: 'stageMirror' })}
                  title="Mostra para os músicos tudo o que está no telão">
                  {stage.program.mirror ? '● Espelhando o telão' : 'Espelhar telão no retorno'}
                </button>
              )}
              <div className={stage?.program.active ? 'warn-text' : ''}>
                Imagem no retorno: {stage?.program.mirror ? 'telão inteiro' : stage?.program.active ? 'layer de avisos tocando' : 'não'}
              </div>
              {stage?.alert && <div className="warn-text">Alerta: {stage.alert}</div>}
              {stage?.message && <div>Mensagem: {stage.message}</div>}
              {!stage?.alert && !stage?.message && <div>Sem alerta nem mensagem</div>}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
