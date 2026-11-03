import { useEffect, useState } from 'react'
import { send, type CarouselRun, type CarouselSettings, type Composition } from './bridge'

const DEFAULT: CarouselSettings = { seconds: 8, allLayers: true, layers: [], skipEmpty: true }

/**
 * Carrossel da aba aberta: passa de coluna em coluna sozinho, no tempo escolhido, em todas as layers
 * da aba ou só nas marcadas. Cada aba tem o seu (e pode estar rodando enquanto outra aba está aberta).
 */
export function CarouselPanel({ comp, running }: { comp: Composition; running: CarouselRun[] }) {
  const deck = comp.decks[comp.currentDeck]
  const s = { ...DEFAULT, ...(deck?.carousel ?? {}) }
  const run = running.find((r) => r.deckId === deck?.id)
  const [, tick] = useState(0)
  const [secondsText, setSecondsText] = useState<string | null>(null)
  useEffect(() => {
    if (!run) return
    const t = setInterval(() => tick((n) => n + 1), 250)
    return () => clearInterval(t)
  }, [run])
  if (!deck) return null

  const save = (patch: Partial<CarouselSettings>) => send({ type: 'carouselSet', deckId: deck.id, settings: { ...s, ...patch } })
  const toggleLayer = (i: number) => {
    const set = new Set(s.allLayers ? comp.layers.map((_, k) => k) : s.layers)
    if (set.has(i)) set.delete(i); else set.add(i)
    save({ allLayers: false, layers: [...set].sort((a, b) => a - b) })
  }
  const included = (i: number) => s.allLayers || s.layers.includes(i)
  const left = run ? Math.max(0, Math.ceil((run.nextAt - Date.now()) / 1000)) : 0
  const colName = (c: number) => comp.columnNames?.[c] || `Coluna ${c + 1}`
  const others = running.filter((r) => r.deckId !== deck.id).map((r) => comp.decks.find((d) => d.id === r.deckId)?.name).filter(Boolean)

  return (
    <section className="carousel-panel" onClick={(e) => e.stopPropagation()}>
      <div className="effects-head">
        <h2>Carrossel da aba <span className="fx-target">{deck.name}</span></h2>
        <span className="row">
          {run && <button className="small" onClick={() => send({ type: 'carouselStep', deckId: deck.id, direction: -1 })} title="Coluna anterior">◀</button>}
          {run && <button className="small" onClick={() => send({ type: 'carouselStep', deckId: deck.id, direction: 1 })} title="Próxima coluna agora">▶</button>}
          <button className={run ? 'onair-btn' : 'primary'} disabled={!s.allLayers && s.layers.length === 0}
            onClick={() => send({ type: 'carouselRun', deckId: deck.id, on: !run })}>
            {run ? '■ Parar carrossel' : '▶ Iniciar carrossel'}
          </button>
        </span>
      </div>
      {run && <p className="carousel-status">No ar: <b>{run.column >= 0 ? colName(run.column) : '...'}</b> · próxima coluna em {left} s</p>}
      <label>Tempo em cada coluna <output>{s.seconds} s</output>
        <input type="range" min={1} max={120} step={1} value={Math.min(120, s.seconds)} onChange={(e) => save({ seconds: Number(e.target.value) })} />
      </label>
      <div className="row carousel-seconds">
        {[3, 5, 8, 10, 15, 30, 60].map((v) => <button key={v} className={`small ${s.seconds === v ? 'active' : ''}`} onClick={() => save({ seconds: v })}>{v} s</button>)}
        <input className="timer-min" value={secondsText ?? String(s.seconds)} title="Segundos (pode digitar, ex.: 2.5 ou 300)"
          onChange={(e) => setSecondsText(e.target.value)}
          onBlur={() => { const v = Number((secondsText ?? '').replace(',', '.')); if (secondsText && v >= 0.5) save({ seconds: v }); setSecondsText(null) }}
          onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur() }} />
      </div>
      <div className="carousel-layers">
        <label className="check"><input type="checkbox" checked={s.allLayers} onChange={(e) => save({ allLayers: e.target.checked, layers: e.target.checked ? [] : comp.layers.map((_, i) => i) })} /> Todas as layers da aba</label>
        <div className="row">
          {comp.layers.map((_, i) => i).reverse().map((i) => (
            <label key={i} className={`check layer-chip ${included(i) ? 'on' : ''}`}>
              <input type="checkbox" checked={included(i)} onChange={() => toggleLayer(i)} /> {comp.layers[i].name}
            </label>
          ))}
        </div>
        <label className="check"><input type="checkbox" checked={s.skipEmpty} onChange={(e) => save({ skipEmpty: e.target.checked })} /> Pular colunas sem clip nessas layers</label>
      </div>
      <p className="meta">
        Passa da coluna 1 até a última e recomeça. Nas layers escolhidas, uma coluna sem clip apaga aquela layer.
        {others.length > 0 && ` Também rodando: ${others.join(', ')}.`}
      </p>
    </section>
  )
}
