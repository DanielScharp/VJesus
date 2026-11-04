import { useEffect, useRef, useState } from 'react'
import { send, type UpdateState } from './bridge'
import { comboLabel, keysOf, matches, subscribe } from './shortcuts'
import { PerformanceDialog } from './PerformanceDialog'

const ITEMS = [
  { label: 'Novo', action: 'projeto.novo', type: 'projectNew' },
  { label: 'Abrir…', action: 'projeto.abrir', type: 'projectOpen' },
  { label: 'Salvar', action: 'projeto.salvar', type: 'projectSave' },
  { label: 'Salvar como…', action: 'projeto.salvarComo', type: 'projectSaveAs' },
] as const

/** Menu "Projeto" no topo (Novo, Abrir, Salvar, Salvar como) com atalhos de teclado. */
export function ProjectMenu({ project, onShortcuts, update, mappingOpen, onMapping }: { project: string | null; onShortcuts: () => void; update: UpdateState | null; mappingOpen: boolean; onMapping: () => void }) {
  const [open, setOpen] = useState(false)
  const [perf, setPerf] = useState(false)
  const [, force] = useState(0)
  useEffect(() => subscribe(() => force((n) => n + 1)), [])
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
      const type = matches(e, 'projeto.novo') ? 'projectNew' : matches(e, 'projeto.abrir') ? 'projectOpen'
        : matches(e, 'projeto.salvar') ? 'projectSave' : matches(e, 'projeto.salvarComo') ? 'projectSaveAs' : null
      if (!type) return
      e.preventDefault()
      send({ type })
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  return (
    <div className="project-menu" ref={ref}>
      <button className={`project-btn ${open || mappingOpen ? 'active' : ''}`} onClick={() => setOpen(!open)} title={project ?? 'Projeto sem título'}>
        <span className="project-label">Menu</span>
        <span className="project-name">{project ?? 'Sem título'}</span>
        <span className="caret">▾</span>
      </button>
      {open && (
        <div className="dropdown">
          {ITEMS.map((it) => (
            <button key={it.type} onClick={() => { send({ type: it.type }); setOpen(false) }}>
              <span>{it.label}</span><kbd>{keysOf(it.action).map(comboLabel).join(' · ')}</kbd>
            </button>
          ))}
          <div className="dropdown-sep" />
          <button onClick={() => { onMapping(); setOpen(false) }}><span>Mapeamento{mappingOpen ? ' ●' : ''}</span><kbd /></button>
          <button onClick={() => { onShortcuts(); setOpen(false) }}><span>Atalhos de teclado…</span><kbd /></button>
          <button onClick={() => { setPerf(true); setOpen(false) }}><span>Desempenho do vídeo…</span><kbd /></button>
          <button disabled={update?.status === 'checking' || update?.status === 'downloading'} onClick={() => send({ type: 'updateCheck' })}>
            <span>{update?.status === 'checking' ? 'Conferindo…' : 'Verificar atualizações'}</span><kbd />
          </button>
          {update && (
            <div className="dropdown-note">
              Versão {update.current}
              {update.status === 'none' && ' · é a mais nova'}
              {update.status === 'available' && ` · ${update.latest} disponível`}
              {update.status === 'error' && update.error && <div className="warn-text">{update.error}</div>}
            </div>
          )}
          <div className="dropdown-note">Salvamento automático a cada minuto</div>
        </div>
      )}
      {perf && <PerformanceDialog onClose={() => setPerf(false)} />}
    </div>
  )
}
