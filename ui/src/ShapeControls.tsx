import { useState } from 'react'

const CORNERS = ['↖', '↗', '↘', '↙'] as const

/** Raio de cada canto (px na composição 1920×1080): ↖ ↗ ↘ ↙, com um controle para mudar todos juntos. */
export function CornerControls({ radius, onChange }: { radius: number[]; onChange: (r: number[]) => void }) {
  const r = [0, 1, 2, 3].map((i) => radius[i] ?? 0)
  const same = r.every((v) => v === r[0])
  const [separate, setSeparate] = useState(!same)
  return (
    <div className="corner-controls">
      <div className="row corner-head">
        <span>Cantos arredondados</span>
        <label className="check"><input type="checkbox" checked={separate} onChange={(e) => {
          setSeparate(e.target.checked)
          if (!e.target.checked) onChange([r[0], r[0], r[0], r[0]])
        }} /> Cada canto separado</label>
      </div>
      {!separate ? (
        <label>Todos <output>{Math.round(r[0])} px</output>
          <input type="range" min={0} max={540} step={1} value={r[0]} onChange={(e) => { const v = Number(e.target.value); onChange([v, v, v, v]) }} />
        </label>
      ) : (
        r.map((v, i) => (
          <label key={i}>Canto {CORNERS[i]} <output>{Math.round(v)} px</output>
            <input type="range" min={0} max={540} step={1} value={v} onChange={(e) => onChange(r.map((x, k) => (k === i ? Number(e.target.value) : x)))} />
          </label>
        ))
      )}
    </div>
  )
}

/** Giro em graus, com atalhos para os ângulos mais usados. */
export function RotationControl({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  return (
    <>
      <label>Rotação <output>{Math.round(value)}°</output>
        <input type="range" min={-180} max={180} step={1} value={value} onChange={(e) => onChange(Number(e.target.value))} />
      </label>
      <div className="row chips">
        {[-90, -45, 0, 45, 90, 180].map((a) => (
          <button key={a} className={`small ${value === a ? 'active' : ''}`} onClick={() => onChange(a)}>{a}°</button>
        ))}
      </div>
    </>
  )
}
