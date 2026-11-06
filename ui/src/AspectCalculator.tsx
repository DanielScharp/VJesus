import { useState } from 'react'
import { createPortal } from 'react-dom'
import { PanelSplitter, type SplitPart } from './PanelSplitter'

type Field = 'rw' | 'rh' | 'pw' | 'ph'
/** O que foi digitado: a proporção (R), a largura (W) ou a altura (H) em pixels. */
type Unit = 'R' | 'W' | 'H'
type Values = Record<Field, string>

const PRESETS: [number, number, string][] = [
  [16, 9, 'telão, TV'], [9, 16, 'vertical, celular'], [4, 3, 'projetor antigo'], [3, 4, 'vertical 3:4'],
  [21, 9, 'ultrawide'], [32, 9, 'LED largo'], [1, 1, 'quadrado'], [2, 1, 'faixa 2:1'],
]

/** Aceita vírgula ou ponto. */
const num = (s: string) => {
  const n = parseFloat(s.replace(/\s/g, '').replace(',', '.'))
  return isFinite(n) && n > 0 ? n : null
}
/** Até 2 casas, com vírgula (5333,33); inteiro fica inteiro. */
const fmt = (n: number) => {
  const r = Math.round(n * 100) / 100
  return Number.isInteger(r) ? String(r) : r.toFixed(2).replace(/0$/, '').replace('.', ',')
}
const gcd = (a: number, b: number): number => (b ? gcd(b, a % b) : a)

/** Proporção a partir dos pixels: simplificada (1920×1080 → 16:9) e a mais próxima das comuns. */
function ratioOf(w: number, h: number) {
  let rw = w, rh = h
  if (Number.isInteger(w) && Number.isInteger(h)) { const g = gcd(w, h); rw = w / g; rh = h / g }
  else { rw = Math.round(w * 100) / 100; rh = Math.round(h * 100) / 100 }
  const near = PRESETS.map(([a, b]) => ({ a, b, d: Math.abs(a / b - w / h) / (a / b) })).sort((x, y) => x.d - y.d)[0]
  return { rw, rh, near: near.d < 0.02 && !(near.a === rw && near.b === rh) ? `${near.a}:${near.b}` : null }
}

/**
 * Calculadora de proporção (aspect ratio): preencha dois campos e ela completa o resto.
 * Proporção + largura → altura; proporção + altura → largura; largura + altura → proporção.
 */
export function AspectCalculator({ onClose, startTab = 'proporcao', split }: {
  onClose: () => void
  startTab?: 'proporcao' | 'dividir'
  /** No Mapeamento: tamanho da saída, partes atuais do LED e como aplicar o resultado. */
  split?: { w: number; h: number; parts?: { name: string; frac: string }[]; onApply: (parts: SplitPart[]) => void; label: string }
}) {
  const [tab, setTab] = useState<'proporcao' | 'dividir'>(startTab)
  const [v, setV] = useState<Values>({ rw: '16', rh: '9', pw: '', ph: '' })
  // travada: os pixels seguem a proporção (muda um, calcula o outro); destravada: largura + altura dão a proporção
  const [lock, setLock] = useState(true)
  const [lastPix, setLastPix] = useState<Unit | null>(null)
  const [target, setTarget] = useState<Unit | null>(null)
  const [near, setNear] = useState<string | null>(null)

  function solve(next: Values, locked: boolean, last: Unit | null) {
    const rw = num(next.rw), rh = num(next.rh), pw = num(next.pw), ph = num(next.ph)
    let tg: Unit | null = null, n: string | null = null
    if (locked && rw && rh) {
      if (last === 'H' && ph) { next.pw = fmt(ph * rw / rh); tg = 'W' }
      else if (pw) { next.ph = fmt(pw * rh / rw); tg = 'H' }
      else if (ph) { next.pw = fmt(ph * rw / rh); tg = 'W' }
    } else if (!locked && pw && ph) {
      const r = ratioOf(pw, ph); next.rw = fmt(r.rw); next.rh = fmt(r.rh); n = r.near; tg = 'R'
    }
    setV(next); setLock(locked); setLastPix(last); setTarget(tg); setNear(n)
  }

  const change = (field: Field, text: string) => {
    const next = { ...v, [field]: text }
    if (field === 'rw' || field === 'rh') {
      // digitou a proporção: ela passa a mandar (vazia: volta a descobrir pelos pixels)
      solve(next, !!(num(next.rw) && num(next.rh)), lastPix)
    } else solve(next, lock, field === 'pw' ? 'W' : 'H')
  }
  const preset = (w: number, h: number) => solve({ ...v, rw: String(w), rh: String(h) }, true, lastPix)
  const toggleLock = () => solve({ ...v }, !lock, lastPix)
  const swap = () => {
    const flip = (u: Unit | null): Unit | null => (u === 'W' ? 'H' : u === 'H' ? 'W' : u)
    setV({ rw: v.rh, rh: v.rw, pw: v.ph, ph: v.pw }); setLastPix(flip(lastPix)); setTarget(flip(target))
  }
  const clear = () => { setV({ rw: '', rh: '', pw: '', ph: '' }); setLock(false); setLastPix(null); setTarget(null); setNear(null) }
  const auto = target === 'R' ? 'ratio' : target === 'W' ? 'pw' : target === 'H' ? 'ph' : null
  const pw = num(v.pw), ph = num(v.ph)
  const box = (f: Field, label: string) => (
    <label className={`calc-field ${auto === f || (auto === 'ratio' && (f === 'rw' || f === 'rh')) ? 'auto' : ''}`}>
      <span>{label}</span>
      <input inputMode="decimal" value={v[f]} onChange={(e) => change(f, e.target.value)} onFocus={(e) => e.target.select()} />
    </label>
  )

  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal calc-dialog" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Calculadora de proporção</h3>
        <div className="calc-tabs">
          <button className={tab === 'proporcao' ? 'active' : ''} onClick={() => setTab('proporcao')}>Proporção e pixels</button>
          <button className={tab === 'dividir' ? 'active' : ''} onClick={() => setTab('dividir')}>Dividir painel em partes</button>
        </div>
        {tab === 'dividir' && <PanelSplitter initialW={split?.w} initialH={split?.h} initialParts={split?.parts} onApply={split?.onApply} applyLabel={split?.label} />}
        {tab === 'proporcao' && <>
        <p className="meta">Com a proporção travada, digite a largura ou a altura em pixels e a outra é calculada. Para descobrir a proporção, clique em 🔒 (ou apague a proporção) e digite largura e altura. O campo calculado fica em destaque.</p>
        <div className="calc-row">
          <div className="calc-group">
            <div className="calc-title">Proporção</div>
            <div className="calc-pair">{box('rw', 'Largura')}<b>:</b>{box('rh', 'Altura')}</div>
            <button className={`small calc-lock ${lock ? 'on' : ''}`} onClick={toggleLock}
              title={lock ? 'Os pixels seguem esta proporção. Clique para descobrir a proporção a partir da largura e da altura.' : 'Digite largura e altura para descobrir a proporção. Clique para travar a proporção.'}>
              {lock ? '🔒 Proporção travada' : '🔓 Descobrindo a proporção'}
            </button>
          </div>
          <button className="small calc-swap" title="Trocar largura e altura (deitado ↔ em pé)" onClick={swap}>⇄</button>
          <div className="calc-group">
            <div className="calc-title">Pixels</div>
            <div className="calc-pair">{box('pw', 'Largura')}<b>×</b>{box('ph', 'Altura')}</div>
          </div>
        </div>
        <div className="calc-presets">
          {PRESETS.map(([w, h, hint]) => (
            <button key={`${w}:${h}`} className={`small ${num(v.rw) === w && num(v.rh) === h ? 'active' : ''}`} title={hint} onClick={() => preset(w, h)}>{w}:{h}</button>
          ))}
        </div>
        <AspectPreview w={pw && ph ? pw : num(v.rw)} h={pw && ph ? ph : num(v.rh)} label={pw && ph ? `${fmt(pw)} × ${fmt(ph)} px` : num(v.rw) && num(v.rh) ? `${v.rw} : ${v.rh}` : ''} ratio={num(v.rw) && num(v.rh) ? `${v.rw} : ${v.rh}` : ''} />
        <div className="calc-result">
          {pw && ph
            ? <>
                <b>{fmt(pw)} × {fmt(ph)} px</b>
                {(!Number.isInteger(pw) || !Number.isInteger(ph)) && <span> · arredondado: {Math.round(pw)} × {Math.round(ph)} px</span>}
                <span> · proporção {fmt(pw / ph)} : 1</span>
                {near && <span> · quase {near}</span>}
              </>
            : <span className="meta">Ex.: 9 : 16 com largura 3000 → altura 5333,33.</span>}
        </div>
        </>}
        <div className="modal-actions">
          <button onClick={clear}>Limpar</button>
          <button onClick={onClose}>Fechar</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}

/**
 * Desenho da proporção: o retângulo no tamanho certo dentro de um quadro fixo, com a tela 16:9
 * pontilhada atrás para comparar (ex.: um 9:16 aparece em pé, bem mais estreito que a TV).
 */
function AspectPreview({ w, h, label, ratio }: { w: number | null; h: number | null; label: string; ratio: string }) {
  const BOX_W = 300, BOX_H = 170
  if (!w || !h) return <div className="calc-visual empty"><span className="meta">Digite uma proporção ou as medidas para ver o desenho.</span></div>
  const a = w / h
  // cabe no quadro mantendo a proporção (bem largo ou bem alto não some: mínimo de 4 px)
  const fw = Math.max(4, Math.min(BOX_W, BOX_H * a)), fh = Math.max(4, Math.min(BOX_H, BOX_W / a))
  const rw = Math.min(BOX_W, BOX_H * 16 / 9), rh = rw * 9 / 16
  const shape = Math.abs(a - 1) < 0.02 ? 'quadrado' : a > 1 ? 'deitado (horizontal)' : 'em pé (vertical)'
  return (
    <div className="calc-visual">
      <div className="calc-canvas" style={{ width: BOX_W, height: BOX_H }}>
        <div className="calc-ref" style={{ width: rw, height: rh }} title="Referência: tela 16:9"><span>16:9</span></div>
        <div className="calc-shape" style={{ width: fw, height: fh }}>
          {fw > 70 && fh > 34 && <span>{ratio || label}</span>}
        </div>
      </div>
      <div className="calc-visual-info">
        <b>{label}</b>
        <span>{shape}</span>
        <span>{(a).toFixed(3).replace('.', ',')} : 1</span>
        <span className="meta">pontilhado = tela 16:9, para comparar</span>
      </div>
    </div>
  )
}