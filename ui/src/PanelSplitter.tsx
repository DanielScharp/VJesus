import { useState } from 'react'

type Orient = 'h' | 'v' | 'livre'
type Part = { name: string; frac: string; orient: Orient }
/** Uma parte calculada, pronta para virar parte do mapa de LED (pixels da saída). */
export type SplitPart = { name: string; x: number; y: number; w: number; h: number }

const ORIENT: Record<Orient, { label: string; ratio: number | null; hint: string }> = {
  h: { label: 'Horizontal (16:9)', ratio: 16 / 9, hint: 'conteúdo deitado, como TV' },
  v: { label: 'Vertical (9:16)', ratio: 9 / 16, hint: 'conteúdo em pé, como celular' },
  livre: { label: 'Livre (o tamanho da parte)', ratio: null, hint: 'a arte é feita do tamanho exato da parte' },
}

const COLORS = ['#ffb020', '#4fc3f7', '#66bb6a', '#ef5350', '#ab47bc', '#26a69a', '#ff7043', '#8d6e63']

/** "3/5", "60%", "0,6" ou "1" → número (0..1). */
function parseFrac(s: string): number | null {
  const t = s.replace(/\s/g, '').replace(',', '.')
  let n: number
  if (/^\d+(\.\d+)?\/\d+(\.\d+)?$/.test(t)) { const [a, b] = t.split('/').map(Number); n = a / b }
  else if (t.endsWith('%')) n = parseFloat(t) / 100
  else n = parseFloat(t)
  return isFinite(n) && n > 0 ? n : null
}
const num = (s: string) => { const n = parseFloat(s.replace(',', '.')); return isFinite(n) && n > 0 ? n : null }
const fmt = (n: number) => { const r = Math.round(n * 100) / 100; return Number.isInteger(r) ? String(r) : r.toFixed(2).replace(/0$/, '').replace('.', ',') }

/**
 * Dividir um painel (ou a saída da processadora) em partes: cada uma com a fração do todo e a orientação
 * do conteúdo. Diz o tamanho exato de cada parte em pixels, onde ela começa, e o tamanho ideal da imagem
 * 16:9 ou 9:16 que cabe nela.
 */
export function PanelSplitter({ initialW = 1920, initialH = 1080, initialParts, onApply, applyLabel }: {
  initialW?: number; initialH?: number; initialParts?: { name: string; frac: string; orient?: Orient }[]
  onApply?: (parts: SplitPart[]) => void; applyLabel?: string
}) {
  const [W, setW] = useState(String(initialW))
  const [H, setH] = useState(String(initialH))
  const [dir, setDir] = useState<'lado' | 'pilha'>('lado')
  const [parts, setParts] = useState<Part[]>(initialParts?.length ? initialParts.map((p) => ({ name: p.name, frac: p.frac, orient: p.orient ?? 'livre' })) : [
    { name: 'Esquerda', frac: '1/5', orient: 'v' },
    { name: 'Centro', frac: '3/5', orient: 'h' },
    { name: 'Direita', frac: '1/5', orient: 'v' },
  ])
  const [applied, setApplied] = useState(false)
  const set = (i: number, p: Partial<Part>) => setParts(parts.map((x, k) => (k === i ? { ...x, ...p } : x)))

  const w = num(W), h = num(H)
  const fracs = parts.map((p) => parseFrac(p.frac))
  const sum = fracs.reduce<number>((s, f) => s + (f ?? 0), 0)
  const ok = !!w && !!h && fracs.every((f) => f !== null) && sum > 0
  const total = dir === 'lado' ? w ?? 0 : h ?? 0

  // tamanho de cada parte em pixels inteiros (a última fica com o que sobra, para somar exatamente o total)
  const rows = ok ? (() => {
    let start = 0
    return parts.map((p, i) => {
      const f = fracs[i]! / sum
      const isLast = i === parts.length - 1
      const size = isLast ? total - start : Math.round(total * f)
      const pw = dir === 'lado' ? size : w!, ph = dir === 'lado' ? h! : size
      const x = dir === 'lado' ? start : 0, y = dir === 'lado' ? 0 : start
      start += size
      const r = ORIENT[p.orient].ratio
      // maior imagem com a proporção escolhida que cabe na parte (centralizada)
      const fw = r ? Math.min(pw, ph * r) : pw, fh = r ? fw / r : ph
      return { ...p, f, pw, ph, x, y, fw: Math.round(fw), fh: Math.round(fh), fx: Math.round(x + (pw - fw) / 2), fy: Math.round(y + (ph - fh) / 2), color: COLORS[i % COLORS.length] }
    })
  })() : []

  // desenho: o painel inteiro numa caixa de até 560×220
  const BW = 560, BH = 220
  const k = ok ? Math.min(BW / w!, BH / h!) : 1

  return (
    <div className="split">
      <p className="meta">Informe o tamanho total (a saída da processadora ou o painel inteiro), como as partes ficam e a fração de cada uma. A calculadora diz o tamanho exato de cada parte e o tamanho ideal da imagem para ela.</p>
      <div className="split-top">
        <label className="calc-field"><span>Largura total (px)</span><input inputMode="decimal" value={W} onChange={(e) => setW(e.target.value)} /></label>
        <b className="split-x">×</b>
        <label className="calc-field"><span>Altura total (px)</span><input inputMode="decimal" value={H} onChange={(e) => setH(e.target.value)} /></label>
        <div className="split-dir">
          <span className="calc-title">As partes ficam</span>
          <div className="row">
            <button className={`small ${dir === 'lado' ? 'active' : ''}`} onClick={() => setDir('lado')}>lado a lado</button>
            <button className={`small ${dir === 'pilha' ? 'active' : ''}`} onClick={() => setDir('pilha')}>uma em cima da outra</button>
          </div>
        </div>
      </div>

      <div className="split-parts">
        <div className="split-head"><span>Parte</span><span>Fração</span><span>Conteúdo</span><span /></div>
        {parts.map((p, i) => (
          <div key={i} className="split-part">
            <span className="split-dot" style={{ background: COLORS[i % COLORS.length] }} />
            <input className="split-name" value={p.name} onChange={(e) => set(i, { name: e.target.value })} />
            <input className={`split-frac ${fracs[i] === null ? 'bad' : ''}`} value={p.frac} placeholder="1/3" title="Ex.: 1/3, 3/5, 20%, 0,25" onChange={(e) => set(i, { frac: e.target.value })} />
            <select value={p.orient} onChange={(e) => set(i, { orient: e.target.value as Orient })} title={ORIENT[p.orient].hint}>
              {(Object.keys(ORIENT) as Orient[]).map((o) => <option key={o} value={o}>{ORIENT[o].label}</option>)}
            </select>
            <button className="small" disabled={parts.length <= 1} title="Tirar esta parte" onClick={() => setParts(parts.filter((_, k2) => k2 !== i))}>✕</button>
          </div>
        ))}
        <div className="row">
          <button className="small" onClick={() => setParts([...parts, { name: `Parte ${parts.length + 1}`, frac: `1/${parts.length + 1}`, orient: 'h' }])}>+ Parte</button>
          <button className="small" onClick={() => setParts(parts.map((p) => ({ ...p, frac: `1/${parts.length}` })))}>Partes iguais</button>
          {ok && Math.abs(sum - 1) > 0.001 && (
            <span className="warn-text">As frações somam {fmt(sum)} (não 1): a calculadora reparte o total na mesma proporção entre elas.</span>
          )}
        </div>
      </div>

      {ok && (
        <>
          <div className="split-canvas" style={{ width: w! * k, height: h! * k }}>
            {rows.map((r, i) => (
              <div key={i} className="split-area" style={{ left: r.x * k, top: r.y * k, width: r.pw * k, height: r.ph * k, borderColor: r.color }}>
                <div className="split-fit" style={{ left: (r.fx - r.x) * k, top: (r.fy - r.y) * k, width: r.fw * k, height: r.fh * k, background: r.color + '33', borderColor: r.color }} />
                <span style={{ color: r.color }}>{r.name}<br />{r.pw}×{r.ph}</span>
              </div>
            ))}
          </div>
          <table className="split-table">
            <thead><tr><th>Parte</th><th>Fração</th><th>Tamanho da parte</th><th>Começa em</th><th>Imagem ideal</th></tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  <td><span className="split-dot" style={{ background: r.color }} /> {r.name}</td>
                  <td>{fmt(r.f * 100)}%</td>
                  <td><b>{r.pw} × {r.ph} px</b><span className="meta"> ({fmt(r.pw / r.ph)} : 1)</span></td>
                  <td>X {r.x} · Y {r.y}</td>
                  <td>
                    {r.orient === 'livre'
                      ? <>arte com <b>{r.pw} × {r.ph} px</b></>
                      : <><b>{r.fw} × {r.fh} px</b>{(r.fw < r.pw || r.fh < r.ph) && <span className="meta"> (sobra {r.pw - r.fw > 0 ? `${r.pw - r.fw} px na largura` : `${r.ph - r.fh} px na altura`}; para preencher tudo, faça a arte com {r.pw} × {r.ph})</span>}</>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {onApply && (
            <div className="split-apply">
              <button className="primary" onClick={() => { onApply(rows.map((r) => ({ name: r.name, x: r.x, y: r.y, w: r.pw, h: r.ph }))); setApplied(true) }}>{applyLabel ?? 'Aplicar'}</button>
              <span className="meta">{applied ? '✔ Aplicado. Confira o mapa de painéis e a saída abaixo.' : 'Troca as partes do mapa de LED desta tela por estas (as imagens escolhidas para cada parte continuam, na mesma ordem).'}</span>
            </div>
          )}
          <p className="meta">Pontilhado/colorido claro = onde a imagem {`16:9`} ou {`9:16`} inteira cabe dentro da parte. Os valores de "Começa em" e "Tamanho da parte" são os que vão na processadora (ou no mapa de LED do VJesus).</p>
        </>
      )}
    </div>
  )
}
