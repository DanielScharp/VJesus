import { useEffect, useMemo, useState } from 'react'
import { send, type AttackPreset } from './bridge'
import { normalize } from './lyrics'
import {
  ACTIONS, comboLabel, comboOf, getCustom, keysOf, normalizeKey, pauseShortcuts, saveCustom, SCOPE_LABEL, scopesOverlap, subscribe, type Scope,
} from './shortcuts'

/** Uma linha da lista: ação do programa ou um ataque (ataques usam uma tecla só, sem Ctrl/Alt). */
type Item = { id: string; label: string; hint?: string; scope: Scope; keys: string[]; attack?: AttackPreset; defaults?: string[] }

const GROUPS: { scope: Scope; title: string }[] = [
  { scope: 'global', title: 'Em qualquer aba' },
  { scope: 'letras', title: 'Aba Letras' },
  { scope: 'biblia', title: 'Aba Bíblia' },
  { scope: 'mapeamento', title: 'Aba Mapeamento' },
]

/**
 * Janela com todos os atalhos: o que cada um faz, as teclas, trocar, apagar e voltar ao padrão.
 * Não deixa a mesma tecla em duas ações que valem ao mesmo tempo.
 */
export function ShortcutsDialog({ attacks, onClose }: { attacks: AttackPreset[]; onClose: () => void }) {
  const [, force] = useState(0)
  useEffect(() => subscribe(() => force((n) => n + 1)), [])
  const [q, setQ] = useState('')
  const [capturing, setCapturing] = useState<string | null>(null)
  const [pending, setPending] = useState<{ target: string; combo: string; other: Item } | null>(null)
  const [warn, setWarn] = useState<string | null>(null)
  useEffect(() => { pauseShortcuts(true); return () => pauseShortcuts(false) }, [])

  const items: Item[] = useMemo(() => [
    ...ACTIONS.map((a) => ({ id: a.id, label: a.label, hint: a.hint, scope: a.scope, keys: keysOf(a.id), defaults: a.defaults })),
    ...attacks.map((a) => ({
      id: `ataque:${a.id}`, label: `Ataque: ${a.name}`, hint: 'Dispara o ataque (com "segurar", fica enquanto a tecla estiver apertada).',
      scope: 'global' as Scope, keys: a.key ? [normalizeKey(a.key)] : [], attack: a,
    })),
  ], [attacks, getCustom()])

  /** Grava as teclas de um item (ação ou ataque). */
  const apply = (changes: { item: Item; keys: string[] }[]) => {
    const next = { ...getCustom() }
    let list = attacks
    for (const { item, keys } of changes) {
      if (item.attack) list = list.map((x) => (x.id === item.attack!.id ? { ...x, key: keys[0] ? (keys[0] === 'Space' ? ' ' : keys[0].toLowerCase()) : null } : x))
      else next[item.id] = keys
    }
    saveCustom(next)
    if (list !== attacks) send({ type: 'attacksSave', attacks: list })
  }

  const assign = (target: Item, combo: string) => {
    setWarn(null)
    if (target.attack && combo.includes('+')) { setWarn('Ataques usam uma tecla só (sem Ctrl, Alt ou Shift).'); return }
    if (target.keys.includes(combo)) return
    const other = items.find((i) => i.id !== target.id && scopesOverlap(i.scope, target.scope) && i.keys.includes(combo))
    if (other) { setPending({ target: target.id, combo, other }); return }
    apply([{ item: target, keys: target.attack ? [combo] : [...target.keys, combo] }])
  }

  // captura a próxima combinação de teclas para a ação escolhida
  useEffect(() => {
    if (!capturing) return
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault()
      e.stopImmediatePropagation()
      const combo = comboOf(e)
      if (!combo) return // só Ctrl/Shift/Alt por enquanto: espera a tecla
      const target = items.find((i) => i.id === capturing)
      setCapturing(null)
      if (target) assign(target, combo)
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  })

  // Esc fecha a janela (quando não está capturando)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (!capturing && !pending && e.key === 'Escape') { e.stopImmediatePropagation(); onClose() } }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  })

  const filtered = items.filter((i) => !q || normalize(`${i.label} ${i.hint ?? ''} ${i.keys.map(comboLabel).join(' ')}`).includes(normalize(q)))
  const isDefault = (i: Item) => !i.attack && JSON.stringify(i.keys) === JSON.stringify(i.defaults ?? [])

  return (
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal shortcuts" onMouseDown={(e) => e.stopPropagation()}>
        <div className="bible-head">
          <h2>Atalhos de teclado</h2>
          <input className="sc-search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Procurar ação ou tecla" />
          <button className="small" onClick={() => {
            if (window.confirm('Voltar todos os atalhos do programa para o padrão? (as teclas dos ataques não mudam)')) saveCustom({})
          }}>Restaurar padrões</button>
          <button className="small" onClick={onClose}>Fechar</button>
        </div>
        <p className="meta">
          Clique em <b>+ tecla</b> e aperte a combinação. Uma tecla não pode estar em duas ações que valem ao mesmo tempo;
          em abas diferentes ela pode se repetir. Os atalhos não disparam enquanto você digita num campo.
        </p>
        {warn && <p className="warn-text">{warn}</p>}
        {pending && (
          <div className="sc-conflict">
            <b>{comboLabel(pending.combo)}</b> já é de “{pending.other.label}” ({SCOPE_LABEL[pending.other.scope]}).
            <button className="small primary" onClick={() => {
              const target = items.find((i) => i.id === pending.target)!
              apply([
                { item: pending.other, keys: pending.other.keys.filter((k) => k !== pending.combo) },
                { item: target, keys: target.attack ? [pending.combo] : [...target.keys, pending.combo] },
              ])
              setPending(null)
            }}>Passar para “{items.find((i) => i.id === pending.target)?.label}”</button>
            <button className="small" onClick={() => setPending(null)}>Cancelar</button>
          </div>
        )}
        <div className="sc-list">
          {GROUPS.map((g) => {
            const rows = filtered.filter((i) => i.scope === g.scope)
            if (!rows.length) return null
            return (
              <div key={g.scope}>
                <h3>{g.title}</h3>
                {rows.map((i) => (
                  <div key={i.id} className={`sc-row ${capturing === i.id ? 'capturing' : ''}`}>
                    <div className="sc-what">
                      <div className="sc-label">{i.label}</div>
                      {i.hint && <div className="meta">{i.hint}</div>}
                    </div>
                    <div className="sc-keys">
                      {i.keys.length === 0 && <span className="meta">sem atalho</span>}
                      {i.keys.map((k) => (
                        <span key={k} className="sc-key">
                          <kbd>{comboLabel(k)}</kbd>
                          <button className="small link" title="Remover esta tecla" onClick={() => apply([{ item: i, keys: i.keys.filter((x) => x !== k) }])}>✕</button>
                        </span>
                      ))}
                      {capturing === i.id
                        ? <><span className="sc-press">Aperte as teclas…</span><button className="small" onClick={() => setCapturing(null)}>Cancelar</button></>
                        : <button className="small" onClick={() => { setWarn(null); setCapturing(i.id) }}>{i.attack && i.keys.length ? 'Trocar' : '+ tecla'}</button>}
                      {!i.attack && !isDefault(i) && (
                        <button className="small link" title="Voltar ao padrão" onClick={() => {
                          // o padrão também não pode repetir tecla de outra ação
                          const defs = i.defaults ?? []
                          const taken = defs.find((d) => items.some((o) => o.id !== i.id && scopesOverlap(o.scope, i.scope) && o.keys.includes(d)))
                          if (taken) { setWarn(`Não deu para voltar ao padrão: ${comboLabel(taken)} está em outra ação.`); return }
                          const next = { ...getCustom() }; delete next[i.id]; saveCustom(next)
                        }}>padrão</button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
