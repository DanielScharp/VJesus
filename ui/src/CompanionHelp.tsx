import { useEffect, useState } from 'react'
import { onMessage } from './bridge'

type Cmd = { address: string; what: string; hold?: boolean }

/** Comandos que o VJesus entende por OSC (números de layer, coluna e ataque começam em 1). */
const GROUPS: { title: string; cmds: Cmd[] }[] = [
  {
    title: 'Culto e letras',
    cmds: [
      { address: '/culto/next', what: 'Próximo item/slide do roteiro (igual ao botão Próximo)' },
      { address: '/culto/prev', what: 'Anterior do roteiro' },
      { address: '/lyrics/next', what: 'Próximo slide da letra' },
      { address: '/lyrics/prev', what: 'Slide anterior da letra' },
      { address: '/lyrics/clear', what: 'Encerra a letra/Bíblia (igual ao Esc)' },
      { address: '/lyrics/blank', what: 'Tela em branco: 1 liga, 0 desliga', hold: true },
      { address: '/apresentacao/next', what: 'Próximo slide da apresentação (PowerPoint/PDF)' },
      { address: '/apresentacao/prev', what: 'Slide anterior da apresentação' },
    ],
  },
  {
    title: 'Grid de clips',
    cmds: [
      { address: '/composition/layers/1/clips/1/connect', what: 'Toca o clip da layer 1, coluna 1 (troque os números)' },
      { address: '/composition/columns/1/connect', what: 'Dispara a coluna 1 inteira' },
      { address: '/composition/layers/1/clear', what: 'Para a layer 1' },
      { address: '/composition/clear', what: 'Para todas as layers' },
      { address: '/composition/layers/1/video/opacity', what: 'Opacidade da layer 1: valor de 0 a 1 (ex.: 0.5)' },
      { address: '/composition/layers/1/audio/volume', what: 'Volume da layer 1: valor de 0 a 1' },
    ],
  },
  {
    title: 'Ataques, BPM e master',
    cmds: [
      { address: '/attack/1', what: 'Ataque 1 (até 8, na ordem da barra de ataques). Mande 1 ao apertar e 0 ao soltar', hold: true },
      { address: '/composition/tempocontroller/tempotap', what: 'TAP do BPM' },
      { address: '/composition/tempocontroller/resync', what: 'Ressincroniza a batida' },
      { address: '/composition/tempocontroller/tempo', what: 'BPM: mande o número (ex.: 120)' },
      { address: '/composition/video/brightness', what: 'Brilho do master: 0 a 1' },
      { address: '/composition/audio/volume', what: 'Volume geral: 0 a 1' },
      { address: '/composition/testcard', what: 'Test card: 1 mostra, 0 esconde', hold: true },
    ],
  },
  {
    title: 'Retorno (palco)',
    cmds: [
      { address: '/stage/timer/toggle', what: 'Cronômetro: inicia/pausa' },
      { address: '/stage/timer/start', what: 'Cronômetro: inicia' },
      { address: '/stage/timer/pause', what: 'Cronômetro: pausa' },
      { address: '/stage/timer/reset', what: 'Cronômetro: zera' },
      { address: '/stage/alert/clear', what: 'Tira o aviso do retorno' },
      { address: '/stage/mirror', what: 'Retorno espelhando o telão: 1 liga, 0 desliga', hold: true },
    ],
  },
]

function Copy({ text }: { text: string }) {
  const [ok, setOk] = useState(false)
  return (
    <button className="small copy-btn" title="Copiar" onClick={() => {
      void navigator.clipboard.writeText(text).then(() => { setOk(true); setTimeout(() => setOk(false), 1200) })
    }}>{ok ? '✓ Copiado' : 'Copiar'}</button>
  )
}

/** Stream Deck / Bitfocus Companion: dados da conexão, passo a passo, lista de comandos e o último comando que chegou. */
export function CompanionHelp({ port, host }: { port: number | null; host: string | null }) {
  const [last, setLast] = useState<{ address: string; value: number; known: boolean; at: number } | null>(null)
  const [, tick] = useState(0)
  const [layer, setLayer] = useState(1)
  const [column, setColumn] = useState(1)

  useEffect(() => onMessage((m) => {
    if (m.type === 'oscIn') setLast({ address: m.address, value: m.value, known: m.known, at: Date.now() })
  }), [])
  useEffect(() => {
    if (!last) return
    const t = setInterval(() => tick((n) => n + 1), 1000)
    return () => clearInterval(t)
  }, [last])

  const ago = last ? Math.round((Date.now() - last.at) / 1000) : 0
  const clipAddr = `/composition/layers/${layer}/clips/${column}/connect`

  return (
    <details className="midi-help companion-help">
      <summary>Stream Deck / Companion (botões físicos por OSC)</summary>

      <div className={`osc-status ${last ? (last.known ? 'ok' : 'warn') : ''}`}>
        {!port ? 'OSC desligado: a porta pode estar em uso por outro programa. Feche o outro programa e reabra o VJesus.'
          : !last ? 'Aguardando comando… Aperte um botão no Stream Deck: ele aparece aqui na hora.'
            : <>Último comando recebido: <code>{last.address}</code> {last.value !== 1 && <>valor {+last.value.toFixed(3)} </>}
              {ago < 2 ? 'agora' : `há ${ago} s`}{!last.known && ' · endereço não reconhecido: confira se está escrito igual à lista abaixo'}</>}
      </div>

      <h4>Dados da conexão</h4>
      <div className="control-grid">
        <span>Protocolo</span><span><strong>OSC por UDP</strong></span>
        <span>Porta</span><span><strong>{port ?? 7000}</strong> <Copy text={String(port ?? 7000)} /></span>
        <span>IP</span>
        <span>
          <strong>127.0.0.1</strong> se o Companion está neste computador <Copy text="127.0.0.1" />
          {host && <><br /><strong>{host}</strong> se está em outro computador da rede <Copy text={host} /></>}
        </span>
      </div>

      <h4>Passo a passo no Bitfocus Companion</h4>
      <ol>
        <li>Instale o <b>Bitfocus Companion</b> (gratuito, bitfocus.io/companion) e ligue o Stream Deck no USB. Feche o app da Elgato: os dois não usam o Stream Deck ao mesmo tempo.</li>
        <li>No Companion, aba <b>Connections</b>: procure <b>OSC</b> e adicione <b>Generic: OSC</b>.</li>
        <li>Na conexão, preencha <b>Target IP</b> e <b>Target Port</b> com os dados acima e salve.</li>
        <li>Aba <b>Buttons</b>: clique num botão, dê um nome e, em <b>Press actions</b>, adicione <b>Send message without arguments</b> (ou <b>Send integer</b> com valor 1) e cole o endereço da lista abaixo em <b>OSC Path</b>.</li>
        <li>Para os comandos marcados como <b>segurar</b>: em Press mande <b>1</b> e em <b>Release actions</b> mande <b>0</b>.</li>
        <li>Para valores (opacidade, volume, brilho) use <b>Send float</b> com um número de 0 a 1.</li>
        <li>Aperte o botão: o último comando recebido aparece no quadro do topo desta seção. Se não aparecer, veja "Não funcionou?" no fim.</li>
      </ol>

      <h4>Sem Companion</h4>
      <ul>
        <li><b>App da Elgato:</b> instale um plugin que envia OSC (procure "OSC" na Stream Deck Store) e use os mesmos IP, porta e endereços.</li>
        <li><b>Outras marcas</b> (Ulanzi, Mirabox, Loupedeck…): se o aparelho funciona no Companion, siga o passo a passo acima. Se ele envia MIDI, use o <b>MIDI learn</b> logo acima, sem endereços.</li>
        <li><b>Aparelho que só aperta teclas:</b> use os atalhos de teclado do VJesus (Menu → Atalhos de teclado). A janela do VJesus precisa estar em foco.</li>
      </ul>

      <h4>Montar endereço de um clip</h4>
      <div className="row osc-builder">
        <label>Layer <input type="number" min={1} max={32} value={layer} onChange={(e) => setLayer(Math.max(1, Number(e.target.value) || 1))} /></label>
        <label>Coluna <input type="number" min={1} max={999} value={column} onChange={(e) => setColumn(Math.max(1, Number(e.target.value) || 1))} /></label>
        <code>{clipAddr}</code> <Copy text={clipAddr} />
      </div>
      <p className="meta">Layer 1 é a de baixo no grid; coluna 1 é a primeira à esquerda. Vale para a aba aberta.</p>

      <h4>Comandos</h4>
      {GROUPS.map((g) => (
        <div key={g.title} className="osc-group">
          <h5>{g.title}</h5>
          <table className="osc-table">
            <tbody>
              {g.cmds.map((c) => (
                <tr key={c.address}>
                  <td><code>{c.address}</code></td>
                  <td>{c.what}{c.hold && <span className="tag-hold">segurar</span>}</td>
                  <td><Copy text={c.address} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}

      <h4>Não funcionou?</h4>
      <ul>
        <li>Confira se a conexão no Companion está verde e com a porta <b>{port ?? 7000}</b>.</li>
        <li>Companion em outro computador: os dois precisam estar na mesma rede, e o Firewall do Windows precisa permitir o VJesus em <b>redes privadas</b> (Painel de Controle → Firewall → Permitir um aplicativo).</li>
        <li>Se o quadro do topo mostra "endereço não reconhecido", o endereço tem algum erro de digitação: copie da lista.</li>
        <li>Clip não toca: confira se existe clip naquela layer e coluna da aba aberta.</li>
      </ul>
    </details>
  )
}
