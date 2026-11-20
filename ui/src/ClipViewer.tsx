import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { onMessage, send } from './bridge'

type Info = { kind: 'video' | 'image' | 'slides' | 'none'; name: string; clipKind?: string | null; url?: string | null; slides: string[]; path?: string | null }

/**
 * "Ver": mostra o conteúdo de um clip só aqui, no monitor do operador (nada vai para os telões).
 * Vídeo toca com controles (começa sem som, para não sair nas caixas da igreja), imagem e slides aparecem inteiros.
 */
export function ClipViewer({ layer, column, path, onClose }: { layer?: number; column?: number; path?: string; onClose: () => void }) {
  const [info, setInfo] = useState<Info | null>(null)
  const [muted, setMuted] = useState(true)
  const [slide, setSlide] = useState(0)
  const [failed, setFailed] = useState(false)
  const [frames, setFrames] = useState<string[] | null>(null)
  const [frame, setFrame] = useState(0)
  const id = useRef(Math.random().toString(36).slice(2))

  useEffect(() => {
    const off = onMessage((m) => {
      if (m.type === 'clipPreviewInfo' && m.id === id.current) setInfo(m as unknown as Info)
      if (m.type === 'libraryPreview' && info?.path && m.path === info.path) setFrames(m.frames)
    })
    return off
  }, [info?.path])
  useEffect(() => { send({ type: 'clipPreviewInfo', id: id.current, layer: layer ?? -1, column: column ?? -1, path: path ?? null }) }, [])
  // formato que o navegador não toca (HAP, ProRes...): mostra quadros do vídeo
  useEffect(() => { if (failed && info?.path) send({ type: 'libraryPreview', path: info.path }) }, [failed])
  useEffect(() => {
    if (!frames?.length) return
    const t = setInterval(() => setFrame((f) => (f + 1) % frames.length), 450)
    return () => clearInterval(t)
  }, [frames])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.stopImmediatePropagation(); onClose() }
      if (info?.kind === 'slides' && e.key === 'ArrowRight') setSlide((s) => Math.min(info.slides.length - 1, s + 1))
      if (info?.kind === 'slides' && e.key === 'ArrowLeft') setSlide((s) => Math.max(0, s - 1))
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [info])

  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal viewer" onMouseDown={(e) => e.stopPropagation()}>
        <div className="viewer-head">
          <h3>👁 {info?.name || 'Ver'}</h3>
          <span className="meta">Só aqui no seu monitor · não vai para os telões</span>
          <span className="spacer" />
          {info?.kind === 'video' && !failed && (
            <button className={`small ${muted ? '' : 'active'}`} onClick={() => setMuted(!muted)}
              title="O som sai na saída de áudio do computador (pode ser a mesma das caixas da igreja)">{muted ? '🔇 Sem som' : '🔊 Com som'}</button>
          )}
          <button className="small" onClick={onClose}>Fechar (Esc)</button>
        </div>
        <div className="viewer-stage">
          {!info && <span className="meta">Abrindo…</span>}
          {info?.kind === 'video' && !failed && (
            <video key={info.url} src={info.url!} controls autoPlay loop muted={muted} onError={() => setFailed(true)} />
          )}
          {info?.kind === 'video' && failed && (
            frames?.length
              ? <><img src={frames[frame]} alt="" /><span className="viewer-note">Este formato não toca aqui na prévia; mostrando quadros do vídeo.</span></>
              : <span className="meta">Este formato de vídeo não toca na prévia. Carregando quadros…</span>
          )}
          {info?.kind === 'image' && <img src={info.url!} alt="" />}
          {info?.kind === 'slides' && <img src={info.slides[slide]} alt="" />}
          {info?.kind === 'none' && <span className="meta">Este tipo de clip ({info.clipKind ?? 'sem arquivo'}) não tem como ver separado: ele é gerado ou vem ao vivo. Use o monitor de prévia.</span>}
        </div>
        {info?.kind === 'slides' && (
          <div className="viewer-foot">
            <button className="small" disabled={slide <= 0} onClick={() => setSlide(slide - 1)}>◀</button>
            <span>{slide + 1} de {info.slides.length}</span>
            <button className="small" disabled={slide >= info.slides.length - 1} onClick={() => setSlide(slide + 1)}>▶</button>
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}
