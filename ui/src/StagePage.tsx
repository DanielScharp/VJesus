import { useState } from 'react'
import { send, type Display, type StageConfig, type StageElement, type StageState } from './bridge'
import { formatTimer, STAGE_ELEMENTS, StageView, timerSeconds, useNow } from './StageView'

type Props = {
  state: StageState
  displays: Display[]
  onConfig: (c: StageConfig) => void // atualização otimista + envio
  preview: string | null
}

/**
 * Aba Retorno: monitor virado para os músicos. À esquerda a configuração e os itens,
 * no meio o layout (arrastar para mover, canto para redimensionar), à direita os controles ao vivo.
 */
export function StagePage({ state, displays, onConfig, preview }: Props) {
  const { config, timer } = state
  const [selected, setSelected] = useState<string | null>('current')
  const [alertText, setAlertText] = useState('')
  const [messageText, setMessageText] = useState('')
  const [minutes, setMinutes] = useState(String(Math.round(timer.durationSeconds / 60)))
  const now = useNow(200)

  const display = displays[config.display]
  const aspect = display ? display.width / display.height : 16 / 9
  const set = (patch: Partial<StageConfig>) => onConfig({ ...config, ...patch })
  const setEl = (id: string, patch: Partial<StageElement>) =>
    onConfig({ ...config, elements: { ...config.elements, [id]: { ...config.elements[id], ...patch } } })
  const el = selected ? config.elements[selected] : null

  return (
    <div className="stage-page">
      {/* ---------- configuração ---------- */}
      <section>
        <h2>Tela de retorno</h2>
        <label className="check">
          <input type="checkbox" checked={config.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
          <strong>Ativar tela de retorno</strong>
        </label>
        <label>Monitor <output />
          <select value={config.display} onChange={(e) => set({ display: Number(e.target.value) })}>
            {displays.map((d) => <option key={d.index} value={d.index}>{d.name} · {d.width}×{d.height}{d.primary ? ' (principal)' : ''}</option>)}
          </select>
        </label>
        <label className="check">
          <input type="checkbox" checked={config.fullscreen} onChange={(e) => set({ fullscreen: e.target.checked })} /> Tela cheia
        </label>
        <label className="color">Fundo <input type="color" value={config.background} onChange={(e) => set({ background: e.target.value })} /></label>
        <label className="check">
          <input type="checkbox" checked={config.clockSeconds} onChange={(e) => set({ clockSeconds: e.target.checked })} /> Relógio com segundos
        </label>
        <label className="check">
          <input type="checkbox" checked={!!config.programFull} onChange={(e) => set({ programFull: e.target.checked })} /> Imagem do telão em tela cheia quando aparecer
        </label>
        <label className="check">
          <input type="checkbox" checked={config.showSongTitle} onChange={(e) => set({ showSongTitle: e.target.checked })} /> Nome da música e seção
        </label>
        <label>Texto "próximo" <output />
          <input className="name" value={config.nextLabel} onChange={(e) => set({ nextLabel: e.target.value })} />
        </label>

        <h3>Itens (clique para ajustar)</h3>
        <div className="stage-items">
          {STAGE_ELEMENTS.map(({ id, name }) => (
            <div key={id} className={`stage-item ${selected === id ? 'active' : ''}`} onClick={() => setSelected(id)}>
              <input type="checkbox" checked={config.elements[id]?.visible ?? false} title="Aparece na tela de retorno"
                onClick={(e) => e.stopPropagation()}
                onChange={(e) => setEl(id, { visible: e.target.checked })} />
              {name}
            </div>
          ))}
        </div>

        {el && selected && (
          <div className="stage-el-props">
            <h3>{STAGE_ELEMENTS.find((x) => x.id === selected)?.name}</h3>
            <label>Tamanho do texto <output>{el.fontSize.toFixed(1)}</output>
              <input type="range" min={1} max={20} step={0.5} value={el.fontSize} onChange={(e) => setEl(selected, { fontSize: Number(e.target.value) })} />
            </label>
            <label className="color">Cor <input type="color" value={el.color} onChange={(e) => setEl(selected, { color: e.target.value })} /></label>
            <label>Alinhamento <output />
              <select value={el.align} onChange={(e) => setEl(selected, { align: e.target.value })}>
                <option value="left">Esquerda</option>
                <option value="center">Centro</option>
                <option value="right">Direita</option>
              </select>
            </label>
            <p className="meta">Posição e tamanho: arraste a caixa no layout; o canto de baixo à direita redimensiona.</p>
          </div>
        )}
        <button className="small" onClick={() => {
          if (window.confirm('Voltar o layout da tela de retorno para o padrão?'))
            send({ type: 'stageConfig', config: { ...config, elements: {} as StageConfig['elements'] } })
        }}>Layout padrão</button>
      </section>

      {/* ---------- layout ---------- */}
      <section className="stage-editor">
        <h2>Layout {display ? `(${display.name} · ${display.width}×${display.height})` : ''}</h2>
        <div className="stage-frame" style={{ aspectRatio: String(aspect) }}>
          <StageView state={state} preview={preview} editable selected={selected} onSelect={setSelected} onMove={(id, e) => setEl(id, e)} />
        </div>
        <p className="meta">É exatamente o que aparece para os músicos. Itens ocultos ficam transparentes aqui e não aparecem lá.</p>
      </section>

      {/* ---------- ao vivo ---------- */}
      <section className="stage-live">
        <h2>Imagem do telão no retorno</h2>
        <button className={`mirror-btn ${state.program.mirror ? 'on' : ''}`} onClick={() => send({ type: 'stageMirror' })}>
          {state.program.mirror ? '● Espelhando o telão' : 'Espelhar telão no retorno'}
        </button>
        <p className="meta">
          {state.program.mirror ? 'Os músicos estão vendo tudo o que está no telão.'
            : state.program.active ? 'Mostrando no retorno: layer marcada tocando.'
            : state.program.layers.length ? `Aparece sozinho quando tocar: ${state.program.layers.join(', ')}.`
            : 'Nenhuma layer marcada. Marque "Mostrar no retorno" numa layer (clique no nome dela) para os avisos aparecerem sozinhos.'}
        </p>

        <h2>Cronômetro</h2>
        <div className="stage-timer-big">{formatTimer(timerSeconds(timer, now), timer.mode === 'countdown')}</div>
        <div className="row">
          <select value={timer.mode} onChange={(e) => send({ type: 'stageTimer', mode: e.target.value as 'stopwatch' | 'countdown' })}>
            <option value="countdown">Contagem regressiva</option>
            <option value="stopwatch">Cronômetro (para cima)</option>
          </select>
          {timer.mode === 'countdown' && (
            <span className="row inline">
              <input className="name minutes" type="number" min={1} max={600} value={minutes} onChange={(e) => setMinutes(e.target.value)}
                onBlur={() => send({ type: 'stageTimer', durationSeconds: Math.max(1, Number(minutes)) * 60 })} /> min
            </span>
          )}
        </div>
        <div className="row">
          <button className={timer.running ? '' : 'active'} onClick={() => send({ type: 'stageTimer', action: 'toggle' })}>
            {timer.running ? 'Pausar' : 'Iniciar'}
          </button>
          <button onClick={() => send({ type: 'stageTimer', action: 'reset' })}>Zerar</button>
        </div>

        <h2>Alerta</h2>
        <p className="meta">Aparece piscando no meio da tela de retorno até você limpar.</p>
        <input className="name" placeholder="ex.: REPETIR O REFRÃO" value={alertText} onChange={(e) => setAlertText(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && alertText.trim()) send({ type: 'stageAlert', text: alertText }) }} />
        <div className="row">
          {['REPETIR REFRÃO', 'ÚLTIMA VEZ', 'TERMINAR', 'MAIS DEVAGAR'].map((q) => (
            <button key={q} className="small" onClick={() => { setAlertText(q); send({ type: 'stageAlert', text: q }) }}>{q}</button>
          ))}
        </div>
        <div className="row">
          <button className="active" disabled={!alertText.trim()} onClick={() => send({ type: 'stageAlert', text: alertText })}>Mostrar alerta</button>
          <button disabled={!state.alert} onClick={() => send({ type: 'stageAlert', text: null })}>Limpar</button>
        </div>

        <h2>Mensagem</h2>
        <p className="meta">Texto fixo só para os músicos (não vai para o telão).</p>
        <textarea rows={3} value={messageText} onChange={(e) => setMessageText(e.target.value)} placeholder="ex.: Próxima música em Sol" />
        <div className="row">
          <button className="active" disabled={!messageText.trim()} onClick={() => send({ type: 'stageMessage', text: messageText })}>Enviar</button>
          <button disabled={!state.message} onClick={() => send({ type: 'stageMessage', text: null })}>Limpar</button>
        </div>
      </section>
    </div>
  )
}
