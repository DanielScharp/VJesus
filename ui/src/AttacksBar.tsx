import { useEffect, useRef, useState } from 'react'
import { send, type AttackPreset, type AttackType } from './bridge'

const TYPES: { id: AttackType; name: string; hint: string; amount?: [string, number, number, number] }[] = [
  { id: 'flash', name: 'Flash de cor', hint: 'A tela vira a cor escolhida e volta (preto = blackout).' },
  { id: 'strobe', name: 'Strobe', hint: 'Pisca a cor várias vezes.' },
  { id: 'zoom', name: 'Soco de zoom', hint: 'A imagem salta para perto e volta, com um leve clarão.', amount: ['Zoom', 0.02, 0.6, 0.01] },
  { id: 'shake', name: 'Tremor', hint: 'A imagem treme.', amount: ['Força', 0.005, 0.1, 0.005] },
  { id: 'invert', name: 'Inverter', hint: 'Inverte as cores e volta.' },
  { id: 'ring', name: 'Onda de cor', hint: 'Um anel de cor abre do centro.', amount: ['Largura', 0.02, 0.4, 0.01] },
  { id: 'sweep', name: 'Varredura', hint: 'Uma faixa de cor atravessa a tela.', amount: ['Largura', 0.02, 0.5, 0.01] },
]

// teclas que já têm outra função no app
const RESERVED = ['t', 'T', 'b', 'B', 'Escape', 'Delete', ' ', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Enter', 'Tab']

const newId = () => Math.random().toString(16).slice(2, 10)
const keyLabel = (k?: string | null) => (!k ? '' : k === ' ' ? 'Espaço' : k.length === 1 ? k.toUpperCase() : k)

function isTyping(e: KeyboardEvent) {
  const t = e.target as HTMLElement
  return t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable
}

/**
 * Ataques: botões em todas as abas (clique ou tecla de atalho). Os de "segurar" ficam enquanto
 * o botão/tecla está pressionado. "Editar" abre as cores, tempos e teclas de cada um.
 */
export function AttacksBar({ attacks }: { attacks: AttackPreset[] }) {
  const [editing, setEditing] = useState(false)
  const [lit, setLit] = useState<string | null>(null)
  const listRef = useRef(attacks)
  listRef.current = attacks

  const fire = (a: AttackPreset) => {
    send({ type: 'attackFire', id: a.id })
    setLit(a.id)
    if (!a.holdWhilePressed) setTimeout(() => setLit((x) => (x === a.id ? null : x)), Math.max(150, a.attackMs + a.holdMs))
  }
  const release = (a: AttackPreset) => {
    if (!a.holdWhilePressed) return
    send({ type: 'attackRelease', id: a.id })
    setLit((x) => (x === a.id ? null : x))
  }

  // atalhos de teclado (em qualquer aba, menos digitando)
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (isTyping(e) || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return
      const a = listRef.current.find((x) => x.key && (x.key === e.key || x.key.toLowerCase() === e.key.toLowerCase()))
      if (!a) return
      e.preventDefault()
      fire(a)
    }
    const up = (e: KeyboardEvent) => {
      const a = listRef.current.find((x) => x.key && (x.key === e.key || x.key.toLowerCase() === e.key.toLowerCase()))
      if (a) release(a)
    }
    window.addEventListener('keydown', down)
    window.addEventListener('keyup', up)
    return () => { window.removeEventListener('keydown', down); window.removeEventListener('keyup', up) }
  }, [])

  return (
    <div className="attacks">
      <div className="attacks-row">
        <strong className="attacks-title">Ataques</strong>
        {attacks.map((a, i) => (
          <button key={a.id} className={`attack-btn ${lit === a.id ? 'lit' : ''}`}
            style={{ borderColor: a.color, ['--attack-color' as string]: a.color }}
            data-address={`/attack/${i + 1}`}
            title={`${TYPES.find((t) => t.id === a.type)?.name ?? a.type}${a.holdWhilePressed ? ' · segure para manter' : ''}`}
            onPointerDown={() => fire(a)} onPointerUp={() => release(a)} onPointerLeave={() => release(a)}>
            <span className="swatch" style={{ background: a.type === 'invert' ? 'linear-gradient(90deg,#fff 50%,#000 50%)' : a.color }} />
            {a.name}
            {a.key && <kbd>{keyLabel(a.key)}</kbd>}
          </button>
        ))}
        <span className="spacer" />
        <button className="small" onClick={() => send({ type: 'attackStopAll' })} title="Para todos os ataques na hora">Parar</button>
        <button className={`small ${editing ? 'active' : ''}`} onClick={() => setEditing(!editing)}>{editing ? 'Fechar' : 'Editar ataques'}</button>
      </div>
      {editing && <AttackEditor attacks={attacks} onTest={fire} />}
    </div>
  )
}

function AttackEditor({ attacks, onTest }: { attacks: AttackPreset[]; onTest: (a: AttackPreset) => void }) {
  const [selId, setSelId] = useState<string | undefined>(attacks[0]?.id)
  const [capturing, setCapturing] = useState(false)
  const a = attacks.find((x) => x.id === selId) ?? attacks[0]
  const type = TYPES.find((t) => t.id === a?.type)

  const save = (list: AttackPreset[]) => send({ type: 'attacksSave', attacks: list })
  const set = (patch: Partial<AttackPreset>) => a && save(attacks.map((x) => (x.id === a.id ? { ...x, ...patch } : x)))

  // capturar a próxima tecla como atalho
  useEffect(() => {
    if (!capturing) return
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault()
      e.stopImmediatePropagation()
      setCapturing(false)
      if (e.key === 'Backspace') { set({ key: null }); return }
      if (RESERVED.includes(e.key)) { window.alert(`A tecla ${keyLabel(e.key)} já é usada pelo app. Escolha outra.`); return }
      const other = attacks.find((x) => x.id !== a?.id && x.key?.toLowerCase() === e.key.toLowerCase())
      if (other && !window.confirm(`${keyLabel(e.key)} já é do ataque "${other.name}". Passar para este?`)) return
      save(attacks.map((x) => x.id === a?.id ? { ...x, key: e.key } : other && x.id === other.id ? { ...x, key: null } : x))
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  })

  return (
    <div className="attack-editor">
      <div className="attack-list">
        {attacks.map((x) => (
          <div key={x.id} className={`attack-item ${x.id === a?.id ? 'active' : ''}`} onClick={() => setSelId(x.id)}>
            <span className="swatch" style={{ background: x.color }} /> {x.name} {x.key && <kbd>{keyLabel(x.key)}</kbd>}
          </div>
        ))}
        <div className="row">
          <button className="small" onClick={() => {
            const n: AttackPreset = { id: newId(), name: 'Novo ataque', type: 'flash', color: '#FFFFFF', attackMs: 0, holdMs: 60, releaseMs: 500, intensity: 1, amount: 0.15, count: 6, key: null, holdWhilePressed: false }
            save([...attacks, n]); setSelId(n.id)
          }}>+ Novo</button>
          <button className="small" onClick={() => { if (window.confirm('Voltar os 8 ataques de fábrica? Os seus serão substituídos.')) send({ type: 'attacksReset' }) }}>Restaurar padrão</button>
        </div>
      </div>

      {a && (
        <div className="attack-props">
          <div className="row">
            <input className="name" value={a.name} onChange={(e) => set({ name: e.target.value })} />
            <button className="active" onPointerDown={() => onTest(a)} onPointerUp={() => a.holdWhilePressed && send({ type: 'attackRelease', id: a.id })}>Testar</button>
          </div>
          <label>Tipo <output />
            <select value={a.type} onChange={(e) => set({ type: e.target.value as AttackType })}>
              {TYPES.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </label>
          <p className="meta">{type?.hint}</p>
          {a.type !== 'invert' && a.type !== 'shake' && (
            <label className="color">Cor <input type="color" value={a.color} onChange={(e) => set({ color: e.target.value.toUpperCase() })} />
              <span className="row inline quick-colors">
                {['#FFFFFF', '#000000', '#FF1744', '#FFC400', '#00E5FF', '#76FF03', '#D500F9'].map((c) => (
                  <span key={c} className="swatch big" style={{ background: c }} onClick={() => set({ color: c })} />
                ))}
              </span>
            </label>
          )}
          <label>Entrada <output>{a.attackMs} ms</output>
            <input type="range" min={0} max={1000} step={10} value={a.attackMs} onChange={(e) => set({ attackMs: Number(e.target.value) })} />
          </label>
          <label>Segura <output>{a.holdMs} ms</output>
            <input type="range" min={0} max={3000} step={10} value={a.holdMs} onChange={(e) => set({ holdMs: Number(e.target.value) })} />
          </label>
          <label>Saída suave <output>{a.releaseMs} ms</output>
            <input type="range" min={0} max={4000} step={10} value={a.releaseMs} onChange={(e) => set({ releaseMs: Number(e.target.value) })} />
          </label>
          <label>Intensidade <output>{Math.round(a.intensity * 100)}%</output>
            <input type="range" min={0} max={1} step={0.01} value={a.intensity} onChange={(e) => set({ intensity: Number(e.target.value) })} />
          </label>
          {type?.amount && (
            <label>{type.amount[0]} <output>{a.amount.toFixed(3)}</output>
              <input type="range" min={type.amount[1]} max={type.amount[2]} step={type.amount[3]} value={a.amount} onChange={(e) => set({ amount: Number(e.target.value) })} />
            </label>
          )}
          {a.type === 'strobe' && (
            <label>Piscadas <output>{a.count}</output>
              <input type="range" min={1} max={30} step={1} value={a.count} onChange={(e) => set({ count: Number(e.target.value) })} />
            </label>
          )}
          <label className="check">
            <input type="checkbox" checked={a.holdWhilePressed} onChange={(e) => set({ holdWhilePressed: e.target.checked })} />
            Segurar enquanto a tecla/botão estiver pressionado
          </label>
          <div className="row">
            <span>Tecla de atalho: <kbd>{a.key ? keyLabel(a.key) : 'nenhuma'}</kbd></span>
            <button className={`small ${capturing ? 'active' : ''}`} onClick={() => setCapturing(true)}>
              {capturing ? 'Aperte a tecla… (Backspace remove)' : 'Definir tecla'}
            </button>
          </div>
          <div className="row">
            <button className="small" disabled={attacks.indexOf(a) === 0} onClick={() => {
              const i = attacks.indexOf(a); const l = [...attacks]; [l[i - 1], l[i]] = [l[i], l[i - 1]]; save(l)
            }}>← Mover</button>
            <button className="small" disabled={attacks.indexOf(a) === attacks.length - 1} onClick={() => {
              const i = attacks.indexOf(a); const l = [...attacks]; [l[i + 1], l[i]] = [l[i], l[i + 1]]; save(l)
            }}>Mover →</button>
            <button className="small" onClick={() => { const c = { ...a, id: newId(), name: `${a.name} cópia`, key: null }; save([...attacks, c]); setSelId(c.id) }}>Duplicar</button>
            <button className="small danger-text" onClick={() => { if (window.confirm(`Excluir "${a.name}"?`)) { save(attacks.filter((x) => x.id !== a.id)); setSelId(undefined) } }}>Excluir</button>
          </div>
        </div>
      )}
    </div>
  )
}
