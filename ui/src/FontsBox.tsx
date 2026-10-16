import { useEffect, useState } from 'react'
import { onMessage, send } from './bridge'

/**
 * Fontes próprias: baixar do Google Fonts pelo nome (ex.: Poppins) ou importar .ttf/.otf/.zip.
 * Ficam no VJApp (não precisa instalar no Windows) e aparecem na lista de fontes das letras e da Bíblia.
 */
export function FontsBox({ current, onUse }: { current: string; onUse: (family: string) => void }) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState<string | null>(null)
  const [imported, setImported] = useState<string[]>([])
  const [waitingFor, setWaitingFor] = useState<string | null>(null)

  useEffect(() => {
    const off = onMessage((msg) => {
      if (msg.type === 'fonts') setImported(msg.imported ?? [])
      else if (msg.type === 'fontsBusy') setBusy(msg.family)
    })
    send({ type: 'fontsList' })
    return off
  }, [])

  // assim que a fonte baixada aparece, já passa a usar
  useEffect(() => {
    if (!waitingFor) return
    const hit = imported.find((f) => f.toLowerCase() === waitingFor.toLowerCase())
    if (hit) { onUse(hit); setWaitingFor(null) }
  }, [imported, waitingFor])

  const download = () => {
    const f = name.trim()
    if (!f) return
    send({ type: 'fontsGoogle', family: f })
    setWaitingFor(f)
    setName('')
  }

  return (
    <div className="fonts-box">
      <button className="small" onClick={() => setOpen(!open)}>{open ? '▾' : '▸'} Mais fontes (Google Fonts ou arquivo)</button>
      {open && (
        <div className="fonts-panel">
          <div className="row">
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Nome no Google Fonts (ex.: Poppins)"
              onKeyDown={(e) => { if (e.key === 'Enter') download() }} />
            <button className="small primary" disabled={!name.trim() || !!busy} onClick={download}>{busy ? 'Baixando…' : 'Baixar'}</button>
          </div>
          <div className="row">
            <button className="small" onClick={() => send({ type: 'fontsImport' })}>Importar arquivo (.ttf, .otf, .zip)…</button>
            <a className="meta" href="https://fonts.google.com" target="_blank" rel="noreferrer">abrir fonts.google.com</a>
          </div>
          {imported.length > 0 && (
            <div className="fonts-list">
              {imported.map((f) => (
                <div key={f} className={`font-item ${f === current ? 'active' : ''}`}>
                  <button className="small link" onClick={() => onUse(f)} title="Usar esta fonte">{f}</button>
                  <button className="small danger-text" title="Remover do VJApp" onClick={() => {
                    if (window.confirm(`Remover a fonte "${f}" do VJApp?`)) send({ type: 'fontsDelete', family: f })
                  }}>✕</button>
                </div>
              ))}
            </div>
          )}
          <p className="meta">As fontes ficam guardadas no VJApp, sem instalar no Windows. Variações como “Poppins Medium” e “Poppins Light” aparecem como fontes separadas.</p>
        </div>
      )}
    </div>
  )
}
