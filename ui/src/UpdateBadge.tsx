import { useState } from 'react'
import { send, type UpdateState } from './bridge'

/** Aviso discreto de versão nova no topo. Nunca instala sozinho: pode haver culto em andamento. */
export function UpdateBadge({ update }: { update: UpdateState | null }) {
  const [dismissed, setDismissed] = useState(() => {
    try { return localStorage.getItem('update-dismissed') } catch { return null }
  })
  if (!update) return null
  const { status, latest, progress, canInstall, page } = update
  if (status === 'downloading') return <span className="update-badge">Baixando {progress}%</span>
  if (status === 'ready') {
    return (
      <span className="update-badge">
        Pronto para instalar
        <button className="small primary" onClick={() => {
          if (window.confirm('O VJesus vai fechar para instalar a versão nova e abrir de novo. O telão fica sem imagem nesse tempo. Instalar agora?')) send({ type: 'updateInstall' })
        }}>Instalar agora</button>
      </span>
    )
  }
  if (status === 'error' && update.error) return <span className="update-badge warn-text" title={update.error}>Atualização falhou</span>
  if (status !== 'available' || !latest || dismissed === latest) return null
  return (
    <span className="update-badge">
      Versão {latest} disponível
      {canInstall
        ? <button className="small primary" onClick={() => send({ type: 'updateDownload' })}>Baixar</button>
        : page && <a className="small-link" href={page} target="_blank" rel="noreferrer">Ver página</a>}
      <button className="small" title="Lembrar só na próxima versão" onClick={() => {
        setDismissed(latest)
        try { localStorage.setItem('update-dismissed', latest) } catch { /* sem armazenamento */ }
      }}>✕</button>
    </span>
  )
}
