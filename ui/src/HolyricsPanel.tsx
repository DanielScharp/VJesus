import { useEffect, useState } from 'react'
import { onMessage, send, type HolyricsState } from './bridge'

/** Menu → Conexões → Holyrics: o VJesus mostra no telão a letra/versículo que o Holyrics está projetando. */
export function HolyricsPanel() {
  const [st, setSt] = useState<HolyricsState | null>(null)
  const [draft, setDraft] = useState<HolyricsState['settings'] | null>(null)
  useEffect(() => {
    const off = onMessage((m) => { if (m.type === 'holyrics') { setSt(m); setDraft((d) => d ?? m.settings) } })
    send({ type: 'holyricsGet' })
    return off
  }, [])
  if (!st || !draft) return null
  const changed = JSON.stringify(draft) !== JSON.stringify(st.settings)
  const save = (s = draft) => send({ type: 'holyricsSet', settings: s })

  return (
    <section className="holyrics">
      <h2>Holyrics</h2>
      <p className="meta">Passe os slides no Holyrics como sempre: a letra e o versículo que ele projeta aparecem no telão do VJesus, por cima dos vídeos, com a aparência das Letras (versículo: a da Bíblia).</p>
      <label className="check">
        <input type="checkbox" checked={draft.enabled} onChange={(e) => { const s = { ...draft, enabled: e.target.checked }; setDraft(s); save(s) }} />
        Receber do Holyrics
        <span className={`hr-status ${st.ok ? 'ok' : st.settings.enabled ? 'erro' : ''}`}>{st.status}</span>
      </label>
      <div className="hr-fields">
        <label>IP <output /><input value={draft.host} onChange={(e) => setDraft({ ...draft, host: e.target.value })} placeholder="127.0.0.1 = este computador" /></label>
        <label>Porta <output /><input value={draft.port} inputMode="numeric" onChange={(e) => setDraft({ ...draft, port: Number(e.target.value.replace(/\D/g, '')) || 0 })} /></label>
        <label>Token <output /><input value={draft.token} onChange={(e) => setDraft({ ...draft, token: e.target.value })} placeholder="token criado no Holyrics" /></label>
      </div>
      <button className="small" disabled={!changed} onClick={() => save()}>Salvar e conectar</button>
      <details className="midi-help">
        <summary>Como ligar no Holyrics</summary>
        <ol>
          <li>No Holyrics, abra <b>Arquivo → Configurações → API Server</b> e ative o servidor. Anote a <b>porta</b> (normalmente 8091).</li>
          <li>Em <b>Gerenciar permissões</b>, crie um <b>token</b> e marque a permissão <b>GetCurrentPresentation</b>.</li>
          <li>Aqui, coloque o IP do computador do Holyrics (se for este mesmo, deixe 127.0.0.1), a porta e o token, marque <b>Receber do Holyrics</b>.</li>
          <li>Quando aparecer <b>conectado</b>, é só passar os slides no Holyrics.</li>
        </ol>
        <p>Tela em branco, papel de parede ou fim da música no Holyrics tiram o texto do telão do VJesus. Se o Holyrics estiver em outro computador, libere a porta dele no Firewall do Windows desse computador.</p>
        <p>Dica: no Holyrics, use uma saída que não vai para o telão (ou desligue a projeção dele) para o texto não aparecer duas vezes.</p>
      </details>
    </section>
  )
}
