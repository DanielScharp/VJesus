import { useEffect, useRef, useState } from 'react'
import { onMessage, send, type PreviewRequest } from './bridge'

let nextId = 1

/**
 * Prévia: o slide desenhado pelo mesmo motor do telão (fonte, cena, curva da bola), sobre preto.
 * Só o operador vê; nada vai ao ar. Animações aparecem paradas.
 */
export function SlidePreview({ request, label, empty }: { request: PreviewRequest | null; label?: string; empty?: string }) {
  const [image, setImage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const pending = useRef<string | null>(null)
  const key = request ? JSON.stringify(request) : null

  useEffect(() => onMessage((msg) => {
    if (msg.type !== 'slidePreview' || msg.id !== pending.current) return
    setImage(msg.data)
    setError(msg.error ?? null)
  }), [])

  useEffect(() => {
    if (!request) { pending.current = null; setImage(null); setError(null); return }
    // espera um instante: passar rápido pela lista não desenha todos os slides
    const t = setTimeout(() => {
      const id = `p${nextId++}`
      pending.current = id
      send({ type: 'previewRender', id, ...request })
    }, 90)
    return () => clearTimeout(t)
  }, [key])

  return (
    <div className="slide-monitor">
      {label && <div className="monitor-head"><span className="next-tag">PRÉVIA</span> {label}</div>}
      <div className="monitor-screen" style={{ aspectRatio: '16 / 9' }}>
        {image ? <img src={image} alt="Prévia do slide" /> : <span className="meta">{error ?? empty ?? 'Nada para mostrar'}</span>}
      </div>
    </div>
  )
}
