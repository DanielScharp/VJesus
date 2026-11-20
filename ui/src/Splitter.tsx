import { useEffect, useRef } from 'react'

/**
 * Divisória arrastável entre duas áreas (grid e biblioteca, lista e telão...). Arrastar muda a largura de uma
 * das áreas, guardada numa variável CSS do contêiner e lembrada entre as aberturas. Duplo clique volta ao padrão.
 * - measure: a área cuja largura muda; dir: 1 = arrastar para a direita aumenta, -1 = diminui.
 * - min / max: limites em pixels (max pode depender da largura do contêiner).
 */
export function Splitter({ container, measure, cssVar, storageKey, dir, min, max, title }: {
  container: React.RefObject<HTMLElement | null>
  measure: () => HTMLElement | null
  cssVar: string
  storageKey: string
  dir: 1 | -1
  min: number
  max: (containerWidth: number) => number
  title?: string
}) {
  const drag = useRef<{ x: number; w: number } | null>(null)

  const apply = (px: number | null) => {
    const el = container.current
    if (!el) return
    if (px == null) el.style.removeProperty(cssVar)
    else el.style.setProperty(cssVar, `${Math.round(px)}px`)
  }
  const clamp = (w: number) => {
    const cw = container.current?.getBoundingClientRect().width ?? 1200
    return Math.max(min, Math.min(Math.max(min, max(cw)), w))
  }

  // medida guardada
  useEffect(() => {
    try {
      const v = Number(localStorage.getItem(storageKey))
      if (v > 0) apply(clamp(v))
    } catch { /* sem armazenamento */ }
  }, [])

  return (
    <div className="splitter" title={title ?? 'Arraste para mudar o tamanho · duplo clique volta ao padrão'}
      onPointerDown={(e) => {
        const m = measure()
        if (!m) return
        e.preventDefault()
        ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
        drag.current = { x: e.clientX, w: m.getBoundingClientRect().width }
        document.body.classList.add('resizing')
      }}
      onPointerMove={(e) => {
        if (!drag.current) return
        apply(clamp(drag.current.w + dir * (e.clientX - drag.current.x)))
      }}
      onPointerUp={() => {
        if (!drag.current) return
        drag.current = null
        document.body.classList.remove('resizing')
        const m = measure()
        try { if (m) localStorage.setItem(storageKey, String(Math.round(m.getBoundingClientRect().width))) } catch { /* sem armazenamento */ }
      }}
      onDoubleClick={() => { apply(null); try { localStorage.removeItem(storageKey) } catch { /* sem armazenamento */ } }}>
      <span />
    </div>
  )
}
