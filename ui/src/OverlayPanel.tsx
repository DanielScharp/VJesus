import { useEffect, useState } from 'react'
import { send, type LyricStyle, type OverlaySettings, type OverlayState, type StageTimer } from './bridge'
import { StylePanel } from './LyricsPage'
import { SlidePreview } from './SlidePreview'

const newId = () => Math.random().toString(16).slice(2, 12)

function remaining(t: StageTimer) {
  const elapsed = t.accumulated + (t.running ? (Date.now() - t.startedAt) / 1000 : 0)
  return t.mode === 'countdown' ? Math.max(0, t.durationSeconds - elapsed) : elapsed
}

export function formatTime(seconds: number) {
  const s = Math.ceil(seconds)
  const mm = String(Math.floor(s / 60) % 60).padStart(2, '0'), ss = String(s % 60).padStart(2, '0')
  return s >= 3600 ? `${Math.floor(s / 3600)}:${mm}:${ss}` : `${mm}:${ss}`
}

/** Segundos até um horário ("19:30"); se já passou hoje, conta até amanhã. */
function secondsUntil(time: string) {
  const [h, m] = time.split(':').map(Number)
  const target = new Date()
  target.setHours(h, m, 0, 0)
  if (target.getTime() <= Date.now()) target.setDate(target.getDate() + 1)
  return Math.round((target.getTime() - Date.now()) / 1000)
}

/** Contagem regressiva e avisos no telão, por cima de tudo. O tempo é o mesmo da tela de retorno. */
export function OverlayPanel({ overlay, timer, fonts }: { overlay: OverlayState; timer: StageTimer; fonts: string[] }) {
  const { settings, mode, noticeId } = overlay
  const [, tick] = useState(0)
  const [minutes, setMinutes] = useState(5)
  const [clock, setClock] = useState('')
  const [editing, setEditing] = useState<'timer' | 'notice' | null>(null)
  const [draft, setDraft] = useState('')
  useEffect(() => {
    const t = setInterval(() => tick((n) => n + 1), 250)
    return () => clearInterval(t)
  }, [])

  const save = (patch: Partial<OverlaySettings>) => send({ type: 'overlaySave', settings: { ...settings, ...patch } })
  const startFor = (seconds: number) => {
    send({ type: 'stageTimer', action: 'reset', mode: 'countdown', durationSeconds: seconds })
    send({ type: 'stageTimer', action: 'start' })
  }
  const addNotice = () => {
    if (!draft.trim()) return
    save({ notices: [...settings.notices, { id: newId(), text: draft.trim() }] })
    setDraft('')
  }
  const live = mode !== 'off'

  const targets = overlay.targets ?? []
  const chosen = settings.screens ?? []
  const toggleTarget = (id: string) => {
    const all = chosen.length === 0 ? targets.map((t) => t.id) : chosen
    const next = all.includes(id) ? all.filter((x) => x !== id) : [...all, id]
    if (next.length === 0) return // pelo menos um telão
    save({ screens: next.length === targets.length ? null : next })
  }
  return (
    <div className="overlay-panel">
      <section>
        <div className="effects-head">
          <h2>Contagem regressiva</h2>
          <button className={mode === 'timer' ? 'onair-btn' : 'primary'} onClick={() => send({ type: 'overlayTimer', on: mode !== 'timer' })}>
            {mode === 'timer' ? '● Tirar do telão' : 'Mostrar no telão'}
          </button>
        </div>
        <div className="timer-big">{formatTime(remaining(timer))}</div>
        <div className="row">
          {[5, 10, 15, 30].map((m) => <button key={m} className="small" onClick={() => startFor(m * 60)}>{m} min</button>)}
          <input className="timer-min" type="number" min={1} max={600} value={minutes} onChange={(e) => setMinutes(Number(e.target.value))} title="Minutos" />
          <button className="small" disabled={minutes <= 0} onClick={() => startFor(minutes * 60)}>Iniciar</button>
          <span className="timer-until">até <input type="time" value={clock} onChange={(e) => setClock(e.target.value)} />
            <button className="small" disabled={!clock} onClick={() => startFor(secondsUntil(clock))}>Iniciar</button></span>
        </div>
        <div className="row">
          <button className="small" onClick={() => send({ type: 'stageTimer', action: 'toggle' })}>{timer.running ? 'Pausar' : 'Continuar'}</button>
          <button className="small" onClick={() => send({ type: 'stageTimer', action: 'reset' })}>Zerar</button>
          <label className="check"><input type="checkbox" checked={settings.timerBackground} onChange={(e) => save({ timerBackground: e.target.checked })} /> Fundo preto (esconde os vídeos)</label>
          <button className="small" onClick={() => setEditing(editing === 'timer' ? null : 'timer')}>Aparência</button>
        </div>
        <label className="wide">Frase em cima
          <input className="name" value={settings.timerLabel} onChange={(e) => save({ timerLabel: e.target.value })} />
        </label>
        <label className="wide">Frase no fim (opcional)
          <input className="name" value={settings.timerEndText} placeholder="Fica 00:00 se estiver vazio" onChange={(e) => save({ timerEndText: e.target.value })} />
        </label>
        {editing === 'timer' && (
          <div className="overlay-style">
            <SlidePreview request={{ kind: 'timer', seconds: 300 }} />
            <StylePanel style={settings.timerStyle} fonts={fonts} onChange={(p: Partial<LyricStyle>) => save({ timerStyle: { ...settings.timerStyle, ...p } })} />
          </div>
        )}
      </section>

      <section>
        <div className="effects-head">
          <h2>Avisos</h2>
          <button disabled={settings.notices.length === 0} className={mode === 'notices' ? 'onair-btn' : 'primary'}
            onClick={() => send({ type: 'overlayNotices', on: mode !== 'notices' })}>
            {mode === 'notices' ? '● Parar sequência' : 'Passar todos no telão'}
          </button>
        </div>
        <div className="notice-list">
          {settings.notices.map((n, i) => (
            <div key={n.id} className={`notice-item ${live && noticeId === n.id ? 'live' : ''}`}>
              <textarea rows={2} value={n.text} onChange={(e) => save({ notices: settings.notices.map((x) => (x.id === n.id ? { ...x, text: e.target.value } : x)) })} />
              <div className="notice-actions">
                <button className="small" title="Mostra só este aviso no telão" onClick={() => send(live && noticeId === n.id && mode === 'notice' ? { type: 'overlayNotices', on: false } : { type: 'overlayNotice', id: n.id })}>
                  {live && noticeId === n.id && mode === 'notice' ? '● Tirar' : 'Mostrar'}
                </button>
                <button className="small" disabled={i === 0} onClick={() => {
                  const list = [...settings.notices]; [list[i - 1], list[i]] = [list[i], list[i - 1]]; save({ notices: list })
                }}>↑</button>
                <button className="small danger-text" onClick={() => save({ notices: settings.notices.filter((x) => x.id !== n.id) })}>Excluir</button>
              </div>
            </div>
          ))}
          {settings.notices.length === 0 && <p className="meta">Nenhum aviso ainda. Escreva abaixo.</p>}
        </div>
        <textarea rows={2} className="notice-new" placeholder="Novo aviso (ex.: Reunião de jovens no sábado às 19h)" value={draft} onChange={(e) => setDraft(e.target.value)} />
        <div className="row">
          <button className="small primary" disabled={!draft.trim()} onClick={addNotice}>Adicionar aviso</button>
          <label className="check"><input type="checkbox" checked={settings.noticeBackground} onChange={(e) => save({ noticeBackground: e.target.checked })} /> Fundo preto</label>
          <button className="small" onClick={() => setEditing(editing === 'notice' ? null : 'notice')}>Aparência</button>
        </div>
        <label>Cada aviso <output>{settings.noticeSeconds} s</output>
          <input type="range" min={3} max={60} step={1} value={settings.noticeSeconds} onChange={(e) => save({ noticeSeconds: Number(e.target.value) })} />
        </label>
        {editing === 'notice' && (
          <div className="overlay-style">
            <SlidePreview request={{ kind: 'notice', text: settings.notices[0]?.text || 'Exemplo de aviso' }} />
            <StylePanel style={settings.noticeStyle} fonts={fonts} onChange={(p: Partial<LyricStyle>) => save({ noticeStyle: { ...settings.noticeStyle, ...p } })} />
          </div>
        )}
      </section>
      {targets.length > 1 && (
        <div className="overlay-where" title="Telões do mapeamento. A tela de pânico sempre cobre todos.">
          <span className="meta">Contagem e avisos aparecem em:</span>
          {targets.map((t) => (
            <label key={t.id} className="check">
              <input type="checkbox" checked={chosen.length === 0 || chosen.includes(t.id)} onChange={() => toggleTarget(t.id)} /> {t.name}
            </label>
          ))}
        </div>
      )}
    </div>
  )
}
