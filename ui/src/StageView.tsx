import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { StageElement, StageState, StageTimer } from './bridge'
import { RichText } from './RichText'

export const STAGE_ELEMENTS: { id: string; name: string }[] = [
  { id: 'current', name: 'Slide atual' },
  { id: 'next', name: 'Próximo slide' },
  { id: 'clock', name: 'Relógio' },
  { id: 'timer', name: 'Cronômetro' },
  { id: 'alert', name: 'Alerta' },
  { id: 'message', name: 'Mensagem' },
  { id: 'program', name: 'Telão (imagem/vídeo)' },
]

/** Segundos do cronômetro agora (positivo; na contagem regressiva pode ficar negativo = estourou). */
export function timerSeconds(t: StageTimer, now: number) {
  const elapsed = t.accumulated + (t.running ? (now - t.startedAt) / 1000 : 0)
  return t.mode === 'countdown' ? t.durationSeconds - elapsed : elapsed
}

/** mm:ss (ou h:mm:ss). Na contagem regressiva arredonda para cima, para o 00:00 aparecer só no fim. */
export function formatTimer(sec: number, countdown = false) {
  const neg = sec < 0
  const s = neg ? Math.floor(-sec) : countdown ? Math.ceil(sec) : Math.floor(sec)
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60
  const body = h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}` : `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`
  return (neg ? '-' : '') + body
}

export function useNow(intervalMs: number) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])
  return now
}

type Props = {
  state: StageState
  /** Editor da aba Retorno: mostra caixas para arrastar/redimensionar e exemplos onde não há conteúdo. */
  editable?: boolean
  selected?: string | null
  onSelect?: (id: string) => void
  onMove?: (id: string, el: StageElement) => void
  /** Imagem do telão (preview da composição), para o item "Telão". */
  preview?: string | null
}

/** A tela de retorno: usada em tela cheia na janela dos músicos e em miniatura no editor. */
export function StageView({ state, editable, selected, onSelect, onMove, preview }: Props) {
  const { config, live, timer, alert, message, program } = state
  const programEl = config.elements['program']
  // imagem do telão em tela cheia: a letra sai; relógio, cronômetro, alerta e mensagem ficam por cima
  const programFull = !editable && program?.active && config.programFull && programEl?.visible
  const now = useNow(200)
  const root = useRef<HTMLDivElement>(null)
  const drag = useRef<{ id: string; mode: 'move' | 'resize'; x0: number; y0: number; el: StageElement } | null>(null)

  const content = (id: string): React.ReactNode => {
    switch (id) {
      case 'current': {
        const text = live.current
        return (
          <>
            {config.showSongTitle && (live.title || live.label) && (
              <div className="stage-caption">{[live.title, live.label].filter(Boolean).join(' · ')}</div>
            )}
            {text ? <FitText className={live.blank ? 'stage-blanked' : ''} text={text} />
              : editable ? <div className="stage-sample">Slide atual aparece aqui</div> : null}
            {live.blank && text && <div className="stage-tag">TELA EM BRANCO</div>}
          </>
        )
      }
      case 'next':
        return live.next ? (
          <>
            <div className="stage-caption">{config.nextLabel}</div>
            <FitText text={live.next} />
          </>
        ) : editable ? <div className="stage-sample">{config.nextLabel}: próximo slide</div> : null
      case 'clock': {
        const d = new Date(now)
        return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: config.clockSeconds ? '2-digit' : undefined })
      }
      case 'timer': {
        const sec = timerSeconds(timer, now)
        return <span className={sec < 0 ? 'stage-overtime' : timer.running ? '' : 'stage-paused'}>{formatTimer(sec, timer.mode === 'countdown')}</span>
      }
      case 'alert':
        return alert ? <div className="stage-alert-text">{alert}</div> : editable ? <div className="stage-sample">Alerta</div> : null
      case 'message':
        return message ?? (editable ? <span className="stage-sample">Mensagem para os músicos</span> : null)
      case 'program':
        if (program?.active && preview) return <img className="stage-program-img" src={preview} alt="" draggable={false} />
        return editable ? <div className="stage-sample">Imagem do telão (aparece só quando houver algo para mostrar)</div> : null
    }
    return null
  }

  const startDrag = (e: React.PointerEvent, id: string, mode: 'move' | 'resize') => {
    if (!editable) return
    e.stopPropagation()
    ;(e.target as Element).setPointerCapture(e.pointerId)
    onSelect?.(id)
    drag.current = { id, mode, x0: e.clientX, y0: e.clientY, el: { ...config.elements[id] } }
  }
  const moveDrag = (e: React.PointerEvent) => {
    const d = drag.current
    if (!d || !root.current) return
    const r = root.current.getBoundingClientRect()
    const dx = (e.clientX - d.x0) / r.width, dy = (e.clientY - d.y0) / r.height
    const c = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))
    const el = d.mode === 'move'
      ? { ...d.el, x: c(d.el.x + dx, 0, 1 - d.el.w), y: c(d.el.y + dy, 0, 1 - d.el.h) }
      : { ...d.el, w: c(d.el.w + dx, 0.04, 1 - d.el.x), h: c(d.el.h + dy, 0.04, 1 - d.el.y) }
    onMove?.(d.id, el)
  }

  return (
    <div ref={root} className={`stage ${editable ? 'editable' : ''}`} style={{ background: config.background }}
      onPointerMove={moveDrag} onPointerUp={() => { drag.current = null }}>
      {programFull && preview && <img className="stage-program-full" src={preview} alt="" draggable={false} />}
      {STAGE_ELEMENTS.map(({ id, name }) => {
        const el = config.elements[id]
        if (!el) return null
        const body = content(id)
        if (!el.visible && !editable) return null
        if (id === 'alert' && !alert && !editable) return null
        if (id === 'program' && !editable && (!program?.active || programFull)) return null
        if (programFull && (id === 'current' || id === 'next')) return null
        return (
          <div key={id}
            className={`stage-el stage-${id} ${el.visible ? '' : 'hidden-el'} ${selected === id ? 'selected' : ''} ${id === 'alert' && alert ? 'flash' : ''}`}
            style={{
              left: `${el.x * 100}%`, top: `${el.y * 100}%`, width: `${el.w * 100}%`, height: `${el.h * 100}%`,
              fontSize: `${el.fontSize}cqh`, color: el.color, textAlign: el.align as 'left' | 'center' | 'right',
              justifyContent: el.align === 'left' ? 'flex-start' : el.align === 'right' ? 'flex-end' : 'center',
            }}
            onPointerDown={(e) => startDrag(e, id, 'move')}>
            <div className="stage-body">{body}</div>
            {editable && <span className="stage-name">{name}{el.visible ? '' : ' (oculto)'}</span>}
            {editable && <span className="stage-resize" onPointerDown={(e) => startDrag(e, id, 'resize')} />}
          </div>
        )
      })}
    </div>
  )
}

/** Texto que diminui sozinho até caber na caixa (versículos e slides longos no retorno). */
function FitText({ text, className }: { text: string; className?: string }) {
  const ref = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const el = ref.current
    const box = el?.closest('.stage-el') as HTMLElement | null
    if (!el || !box) return
    let scale = 1
    el.style.fontSize = '1em'
    while (scale > 0.3 && box.scrollHeight > box.clientHeight + 1) {
      scale *= 0.9
      el.style.fontSize = `${scale}em`
    }
  })
  return <div ref={ref} className={className}><RichText text={text} /></div>
}
