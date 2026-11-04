import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { onMessage, send, type CheckState } from './bridge'

const ICON = { erro: '✖', aviso: '⚠', info: 'ℹ', ok: '✔' } as const

/** Checagem antes do culto: confere tudo de uma vez e mostra o que resolver antes de começar. */
export function PreflightDialog({ onClose }: { onClose: () => void }) {
  const [st, setSt] = useState<CheckState | null>(null)
  useEffect(() => {
    const off = onMessage((m) => { if (m.type === 'check') setSt(m) })
    send({ type: 'checkRun' })
    return off
  }, [])

  const items = st?.items ?? []
  const verdict = !st || st.running ? null
    : (st.errors ?? 0) > 0 ? { cls: 'erro', text: `${st.errors} problema(s) para resolver antes do culto` }
    : (st.warnings ?? 0) > 0 ? { cls: 'aviso', text: `Tudo pronto, com ${st.warnings} ponto(s) de atenção` }
    : { cls: 'ok', text: 'Tudo pronto para o culto' }

  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal check-dialog" onMouseDown={(e) => e.stopPropagation()}>
        <div className="check-head">
          <h3>Checagem antes do culto</h3>
          <button disabled={st?.running} onClick={() => send({ type: 'checkRun' })}>{st?.running ? 'Conferindo…' : 'Conferir de novo'}</button>
        </div>
        <p className="meta">Confere o roteiro, os arquivos da grade, os vídeos pesados, as telas de saída, as apresentações, o som e a tela de pânico.</p>
        {verdict && <div className={`check-verdict ${verdict.cls}`}>{verdict.text}{st?.when ? ` · conferido às ${st.when}` : ''}</div>}
        {st?.running && <div className="meta">Conferindo… (lendo os vídeos, pode levar alguns segundos)</div>}
        <div className="check-list">
          {items.map((it, i) => (
            <div key={i} className={`check-item ${it.level}`}>
              <span className="check-icon">{ICON[it.level]}</span>
              <div className="check-text">
                <div><span className="check-area">{it.area}</span> {it.title}</div>
                {it.detail && <div className="meta">{it.detail}</div>}
              </div>
              {it.fix === 'relink' && it.path && <button onClick={() => send({ type: 'checkRelink', path: it.path! })}>{it.fixLabel}</button>}
              {it.fix === 'searchFolder' && <button onClick={() => send({ type: 'checkSearchFolder' })}>{it.fixLabel}</button>}
            </div>
          ))}
        </div>
        <div className="modal-actions"><button onClick={onClose}>Fechar</button></div>
      </div>
    </div>,
    document.body,
  )
}
