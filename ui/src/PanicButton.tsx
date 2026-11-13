import { ColorInput } from './ColorInput'
import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { onMessage, send, type PanicState } from './bridge'
import { comboLabel, keysOf, matches, subscribe } from './shortcuts'

/**
 * Botão vermelho do topo (e a tecla de atalho): cobre o telão na hora com o logo ou a cor escolhida.
 * Apertar de novo volta para o que estava. A engrenagem abre a configuração.
 */
export function PanicButton() {
  const [st, setSt] = useState<PanicState | null>(null)
  const [open, setOpen] = useState(false)
  const [, force] = useState(0)
  useEffect(() => subscribe(() => force((n) => n + 1)), [])
  useEffect(() => onMessage((m) => { if (m.type === 'panic') setSt(m) }), [])

  // a tecla vale em qualquer aba, até com o cursor numa caixa de texto
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!matches(e, 'telao.panico')) return
      e.preventDefault()
      e.stopPropagation()
      send({ type: 'panicToggle' })
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [])

  const keys = keysOf('telao.panico').map(comboLabel).join(' · ')
  return (
    <div className="panic">
      <button className={`panic-btn ${st?.on ? 'on' : ''}`} onClick={() => send({ type: 'panicToggle' })}
        title={`Tela de pânico${keys ? ` (${keys})` : ''}: cobre o telão na hora com o logo ou a cor. Aperte de novo para voltar.`}>
        {st?.on ? 'PÂNICO NO AR · voltar' : 'PÂNICO'}{keys && <kbd>{keys}</kbd>}
      </button>
      <button className="panic-cfg" title="Configurar a tela de pânico" onClick={() => setOpen(true)}>⚙</button>
      {open && st && <PanicDialog st={st} onClose={() => setOpen(false)} />}
    </div>
  )
}

function PanicDialog({ st, onClose }: { st: PanicState; onClose: () => void }) {
  const s = st.settings
  const set = (patch: Partial<PanicState['settings']>) => send({ type: 'panicSet', settings: { ...s, ...patch } })
  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal panic-dialog" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Tela de pânico</h3>
        <p className="meta">
          Para quando algo der errado ao vivo: cobre o telão inteiro na hora, por cima de vídeos, letra e avisos.
          Aperte de novo (ou a tecla {keysOf('telao.panico').map(comboLabel).join(' / ') || 'escolhida em Atalhos'}) para voltar exatamente ao que estava.
        </p>
        <div className="panic-body">
          {st.thumb && <img className="panic-thumb" src={st.thumb} alt="Como fica no telão" />}
          <div className="panic-fields">
            <label>Imagem (logo)
              <span className="row">
                <button onClick={() => send({ type: 'panicBrowse' })}>{s.image ? 'Trocar…' : 'Escolher…'}</button>
                {s.image && <button onClick={() => set({ image: null })}>Sem imagem</button>}
              </span>
            </label>
            {st.imageName && <div className={st.imageMissing ? 'warn-text' : 'meta'}>{st.imageName}{st.imageMissing ? ' (arquivo não encontrado)' : ''}</div>}
            <label>Cor de fundo <ColorInput value={s.color} onChange={(e) => set({ color: e.target.value })} /></label>
            <label>Tamanho do logo <output>{Math.round(s.size * 100)}%</output>
              <input type="range" min={0.1} max={1} step={0.05} value={s.size} onChange={(e) => set({ size: Number(e.target.value) })} />
            </label>
            <label>Esmaecer <output>{s.fade.toFixed(1)} s</output>
              <input type="range" min={0} max={3} step={0.1} value={s.fade} onChange={(e) => set({ fade: Number(e.target.value) })} />
            </label>
          </div>
        </div>
        <p className="meta">A tecla de atalho muda em Menu → Atalhos de teclado (“Tela de pânico”). Se o VJesus fechar sem querer, ao abrir de novo ele recoloca no telão o que estava tocando.</p>
        <div className="modal-actions">
          <button className={`panic-btn ${st.on ? 'on' : ''}`} onClick={() => send({ type: 'panicToggle' })}>{st.on ? 'Voltar ao telão' : 'Testar agora'}</button>
          <button onClick={onClose}>Fechar</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
