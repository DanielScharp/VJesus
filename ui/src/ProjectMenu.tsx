import { useEffect, useRef, useState } from 'react'
import { send } from './bridge'

const ITEMS = [
  { label: 'Novo', keys: 'Ctrl+N', type: 'projectNew' },
  { label: 'Abrir…', keys: 'Ctrl+O', type: 'projectOpen' },
  { label: 'Salvar', keys: 'Ctrl+S', type: 'projectSave' },
  { label: 'Salvar como…', keys: 'Ctrl+Shift+S', type: 'projectSaveAs' },
] as const

/** Menu "Projeto" no topo (Novo, Abrir, Salvar, Salvar como) com atalhos de teclado. */
export function ProjectMenu({ project }: { project: string | null }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  // fecha ao clicar fora ou com Esc
  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('mousedown', close)
    window.addEventListener('keydown', esc)
    return () => { window.removeEventListener('mousedown', close); window.removeEventListener('keydown', esc) }
  }, [open])

  // atalhos: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!e.ctrlKey || e.altKey) return
      const k = e.key.toLowerCase()
      const type = k === 'n' ? 'projectNew' : k === 'o' ? 'projectOpen' : k === 's' ? (e.shiftKey ? 'projectSaveAs' : 'projectSave') : null
      if (!type) return
      e.preventDefault()
      send({ type })
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  return (
    <div className="project-menu" ref={ref}>
      <button className={`project-btn ${open ? 'active' : ''}`} onClick={() => setOpen(!open)} title={project ?? 'Projeto sem título'}>
        <span className="project-label">Projeto</span>
        <span className="project-name">{project ?? 'Sem título'}</span>
        <span className="caret">▾</span>
      </button>
      {open && (
        <div className="dropdown">
          {ITEMS.map((it) => (
            <button key={it.type} onClick={() => { send({ type: it.type }); setOpen(false) }}>
              <span>{it.label}</span><kbd>{it.keys}</kbd>
            </button>
          ))}
          <div className="dropdown-note">Salvamento automático a cada minuto</div>
        </div>
      )}
    </div>
  )
}
