import type { Composition, EffectDef, EffectInstance } from './bridge'

export type ToggleEffect = (target: number, index: number, enabled: boolean) => void

/** Etiquetas dos efeitos de uma cadeia: clique liga/desliga (desligado aparece riscado). */
export function EffectChips({ chain, target, library, onToggle }: {
  chain: EffectInstance[]
  target: number
  library: EffectDef[]
  onToggle: ToggleEffect
}) {
  if (chain.length === 0) return null
  const name = (id: string) => library.find((d) => d.id === id)?.name ?? id
  return (
    <div className="fx-chips">
      {chain.map((fx, i) => (
        <button key={i} className={`fx-chip ${fx.enabled ? 'on' : 'off'}`}
          title={fx.enabled ? 'Ligado: clique para desligar' : 'Desligado: clique para ligar'}
          onClick={(e) => { e.stopPropagation(); onToggle(target, i, !fx.enabled) }}>
          {name(fx.effect)}
        </button>
      ))}
    </div>
  )
}

/** Faixa acima da grade: mostra todo efeito ligado (layers e master) e permite desligar na hora. */
export function ActiveEffectsBar({ comp, library, onToggle, onShow }: {
  comp: Composition
  library: EffectDef[]
  onToggle: ToggleEffect
  onShow: (target: number) => void
}) {
  const name = (id: string) => library.find((d) => d.id === id)?.name ?? id
  const groups = [
    ...comp.layers.map((l, i) => ({ target: i, label: l.name, chain: l.effects })),
    { target: -1, label: 'Master', chain: comp.masterEffects },
  ].map((g) => ({ ...g, on: g.chain.map((fx, i) => ({ fx, i })).filter((x) => x.fx.enabled) }))
    .filter((g) => g.on.length > 0)

  if (groups.length === 0) return <div className="active-fx none">Nenhum efeito ligado.</div>

  return (
    <div className="active-fx">
      <strong>Efeitos ligados:</strong>
      {groups.map((g) => (
        <span key={g.target} className="active-fx-group">
          <button className="link" title="Abrir os efeitos desta parte" onClick={() => onShow(g.target)}>{g.label}</button>
          {g.on.map(({ fx, i }) => (
            <button key={i} className="fx-chip on" title="Clique para desligar" onClick={() => onToggle(g.target, i, false)}>
              {name(fx.effect)} ✕
            </button>
          ))}
        </span>
      ))}
      <span className="spacer" />
      <button className="small" onClick={() => groups.forEach((g) => g.on.forEach(({ i }) => onToggle(g.target, i, false)))}>
        Desligar todos
      </button>
    </div>
  )
}
