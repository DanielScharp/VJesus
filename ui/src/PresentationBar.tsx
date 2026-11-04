import { useEffect, useRef } from 'react'
import { send, type PresentationState } from './bridge'
import { isTyping, matches } from './shortcuts'

/**
 * Apresentações (PowerPoint) tocando: o que está no ar, ◀ ▶ e as miniaturas para pular. Slides com
 * animação viram etapas (uma por clique, como no PowerPoint): "2.1", "2.2"…
 * Na aba Clips, as setas / Page Up / Page Down passam as etapas (configurável em Atalhos).
 */
export function PresentationBar({ list, keys }: { list: PresentationState[]; keys: boolean }) {
  const strips = useRef<Record<number, HTMLDivElement | null>>({})

  useEffect(() => {
    if (!keys || list.length === 0) return
    const top = list[list.length - 1]
    const onKey = (e: KeyboardEvent) => {
      if (isTyping(e)) return
      if (matches(e, 'apresentacao.proximo')) { e.preventDefault(); send({ type: 'presentationStep', layer: top.layer, direction: 1 }) }
      else if (matches(e, 'apresentacao.anterior')) { e.preventDefault(); send({ type: 'presentationStep', layer: top.layer, direction: -1 }) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [keys, list])

  // mantém a etapa no ar visível na fileira de miniaturas
  useEffect(() => {
    for (const p of list) strips.current[p.layer]?.querySelector(`[data-slide="${p.slide}"]`)?.scrollIntoView({ block: 'nearest', inline: 'center' })
  }, [list])

  if (list.length === 0) return null
  return (
    <>
      {list.map((p) => (
        <section key={p.layer} className="presentation-bar">
          <div className="effects-head">
            <h2>Apresentação: <span className="fx-target">{p.name}</span> <span className="meta">· {p.layerName} · {p.current || `slide ${p.slide + 1}`} · {p.slides} slides</span></h2>
            <span className="row">
              <button disabled={p.slide <= 0} onClick={() => send({ type: 'presentationStep', layer: p.layer, direction: -1 })}>◀ Anterior</button>
              <button className="primary" disabled={p.slide >= p.count - 1} onClick={() => send({ type: 'presentationStep', layer: p.layer, direction: 1 })}>Próximo ▶</button>
            </span>
          </div>
          <div className="pres-strip" ref={(el) => { strips.current[p.layer] = el }}>
            {p.images.map((src, i) => (
              <button key={i} data-slide={i} className={`pres-thumb ${i === p.slide ? 'live' : ''} ${p.labels[i]?.includes('.') ? 'step' : ''}`}
                title={p.labels[i]?.includes('.') ? `Slide ${p.labels[i].split('.')[0]}, etapa ${p.labels[i].split('.')[1]}` : `Slide ${p.labels[i] ?? i + 1}`}
                onClick={() => send({ type: 'presentationGo', layer: p.layer, slide: i })}>
                <img src={src} alt="" loading="lazy" />
                <span>{p.labels[i] ?? i + 1}</span>
              </button>
            ))}
          </div>
        </section>
      ))}
    </>
  )
}
