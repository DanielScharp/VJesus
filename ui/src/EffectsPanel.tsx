import { useState } from 'react'
import { send, type EffectDef, type EffectInstance } from './bridge'

type Props = {
  library: EffectDef[]
  target: number // índice da layer, ou -1 para o master
  targetName: string
  chain: EffectInstance[]
  onChange: (chain: EffectInstance[]) => void // atualização otimista
}

function formatValue(v: number, step: number) {
  return step >= 1 ? v.toFixed(0) : v.toFixed(2)
}

export function EffectsPanel({ library, target, targetName, chain, onChange }: Props) {
  const [adding, setAdding] = useState('')
  const byId = Object.fromEntries(library.map((d) => [d.id, d]))

  const update = (index: number, patch: Partial<EffectInstance>) =>
    onChange(chain.map((e, i) => (i === index ? { ...e, ...patch } : e)))

  return (
    <section className="effects">
      <div className="effects-head">
        <h2>Efeitos de: <span className="fx-target">{targetName}</span></h2>
        <select value={adding} onChange={(e) => {
          if (e.target.value) send({ type: 'effectAdd', target, effect: e.target.value })
          setAdding('')
        }}>
          <option value="">+ Adicionar efeito…</option>
          {library.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
        </select>
      </div>

      {chain.length === 0 && (
        <div className="meta">Nenhum efeito em {targetName}. Para editar outra layer, clique no nome dela na grade; para o master, use "Efeitos do master".</div>
      )}

      <div className="effect-list">
        {chain.map((fx, i) => {
          const def = byId[fx.effect]
          return (
            <div key={i} className={`effect ${fx.enabled ? '' : 'off'}`}>
              <div className="effect-title">
                <label className="check">
                  <input type="checkbox" checked={fx.enabled} onChange={(e) => {
                    update(i, { enabled: e.target.checked })
                    send({ type: 'effectToggle', target, index: i, enabled: e.target.checked })
                  }} />
                  <strong>{def?.name ?? `${fx.effect} (não encontrado)`}</strong>
                </label>
                <span className="effect-actions">
                  <button className="small" disabled={i === 0} title="Subir"
                    onClick={() => send({ type: 'effectMove', target, index: i, direction: -1 })}>↑</button>
                  <button className="small" disabled={i === chain.length - 1} title="Descer"
                    onClick={() => send({ type: 'effectMove', target, index: i, direction: 1 })}>↓</button>
                  <button className="small" title="Remover"
                    onClick={() => send({ type: 'effectRemove', target, index: i })}>✕</button>
                </span>
              </div>
              {def?.params.map((p) => {
                const value = fx.values[p.id] ?? p.default
                return (
                  <label key={p.id} data-address={target < 0
                    ? `/composition/effects/${i + 1}/${p.id}`
                    : `/composition/layers/${target + 1}/effects/${i + 1}/${p.id}`}>
                    {p.label} <output>{formatValue(value, p.step)}</output>
                    <input type="range" min={p.min} max={p.max} step={p.step > 0 ? p.step : (p.max - p.min) / 500}
                      value={value}
                      onDoubleClick={() => {
                        update(i, { values: { ...fx.values, [p.id]: p.default } })
                        send({ type: 'effectParam', target, index: i, param: p.id, value: p.default })
                      }}
                      onChange={(e) => {
                        const v = Number(e.target.value)
                        update(i, { values: { ...fx.values, [p.id]: v } })
                        send({ type: 'effectParam', target, index: i, param: p.id, value: v })
                      }} />
                  </label>
                )
              })}
            </div>
          )
        })}
      </div>
    </section>
  )
}
