import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { send, type Display, type MappingSetup, type OutputScreen, type Slice } from './bridge'

type Arr = 'lado' | 'pilha'
type Settings = {
  count: number
  arr: Arr
  overlap: number      // % da largura (ou altura) de cada projetor que fica sobreposta ao vizinho
  fit: 'proporcao' | 'esticar'
  displays: number[]
  curve: number
  gamma: number
  black: number
  source: string | null
}

const PREFIX = 'blend-'
const COLORS = ['#ffb020', '#4fc3f7', '#66bb6a', '#ef5350']
const COMP_ASPECT = 16 / 9

/** Lê de volta as telas que o assistente já criou (para reabrir com os mesmos ajustes). */
function fromMapping(m: MappingSetup, displays: Display[]): Settings {
  const screens = m.screens.filter((s) => s.id.startsWith(PREFIX)).sort((a, b) => a.id.localeCompare(b.id))
  const free = displays.filter((d) => !d.primary).map((d) => d.index)
  const defaultDisplays = [...free, ...displays.map((d) => d.index)].filter((v, i, a) => a.indexOf(v) === i)
  if (screens.length < 2) {
    return { count: 2, arr: 'lado', overlap: 15, fit: 'proporcao', displays: defaultDisplays.slice(0, 4), curve: 2, gamma: 2.2, black: 0, source: null }
  }
  const s0 = screens[0].slices[0], e = s0.softEdge
  const arr: Arr = e.bottom > 0 || (e.right === 0 && e.top === 0 && e.bottom === 0 && s0.input.h < 1 && s0.input.w === 1) ? 'pilha' : 'lado'
  const o = arr === 'lado' ? e.right : e.bottom
  return {
    count: screens.length, arr, overlap: Math.round(o * 1000) / 10,
    fit: (arr === 'lado' ? s0.input.h : s0.input.w) < 0.999 ? 'proporcao' : 'esticar',
    displays: screens.map((s) => s.display), curve: e.curve ?? 2, gamma: e.projectorGamma ?? 2.2, black: e.blackLift ?? 0, source: s0.source ?? null,
  }
}

/** As telas (uma por projetor), cada uma com um slice que pega o seu pedaço da imagem e com as bordas da emenda. */
function buildScreens(st: Settings, displays: Display[], previous: OutputScreen[] = []): OutputScreen[] {
  const n = st.count, o = st.overlap / 100
  // cada projetor cobre "span" da imagem; os vizinhos se sobrepõem em o × span
  const span = 1 / (n - (n - 1) * o)
  const d0 = displays.find((d) => d.index === st.displays[0])
  const projAspect = d0 ? d0.width / d0.height : 16 / 9
  // imagem inteira dos projetores juntos (ex.: 2 projetores 16:9 com 15% = 3,3:1)
  const totalAspect = st.arr === 'lado' ? projAspect / span : projAspect * span
  // manter a proporção: usa uma faixa da composição com o formato da imagem dos projetores (corta em cima e embaixo, ou dos lados)
  const band = st.fit === 'esticar' ? { x: 0, y: 0, w: 1, h: 1 }
    : totalAspect >= COMP_ASPECT ? { x: 0, y: (1 - COMP_ASPECT / totalAspect) / 2, w: 1, h: COMP_ASPECT / totalAspect }
    : { x: (1 - totalAspect / COMP_ASPECT) / 2, y: 0, w: totalAspect / COMP_ASPECT, h: 1 }
  return Array.from({ length: n }, (_, i) => {
    const start = i * span * (1 - o)
    const input = st.arr === 'lado'
      ? { x: band.x + start * band.w, y: band.y, w: span * band.w, h: band.h }
      : { x: band.x, y: band.y + start * band.h, w: band.w, h: span * band.h }
    const first = i === 0, last = i === n - 1
    // cantos ajustados à mão no mapeamento (perspectiva de cada projetor) continuam como estavam
    const old = previous.find((s) => s.id === `${PREFIX}${i}`)?.slices[0]
    const slice: Slice = {
      id: `${PREFIX}s${i}`, name: `Projetor ${i + 1}`, enabled: true, opacity: 1, input, cols: old?.cols ?? 2, rows: old?.rows ?? 2,
      points: old?.points ?? [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }],
      softEdge: {
        left: st.arr === 'lado' && !first ? o : 0, right: st.arr === 'lado' && !last ? o : 0,
        top: st.arr === 'pilha' && !first ? o : 0, bottom: st.arr === 'pilha' && !last ? o : 0,
        gamma: 1, curve: st.curve, projectorGamma: st.gamma, blackLift: st.black,
      },
      source: st.source,
    }
    return { id: `${PREFIX}${i}`, name: `Projetor ${i + 1}`, display: st.displays[i] ?? 0, fullscreen: true, slices: [slice], masks: [], led: null }
  })
}

/**
 * Edge blending: vários projetores formando uma imagem só, com a emenda invisível.
 * Escolha quantos projetores, como estão (lado a lado ou um em cima do outro), em que saída está cada um
 * e quanto eles se sobrepõem. As telas são montadas na hora e cada ajuste aparece direto nos projetores.
 */
export function EdgeBlendDialog({ mapping, displays, onChange, sources, onClose }: {
  mapping: MappingSetup; displays: Display[]; onChange: (m: MappingSetup) => void
  sources: { value: string; label: string }[]; onClose: () => void
}) {
  const [st, setSt] = useState<Settings>(() => fromMapping(mapping, displays))
  const [applied, setApplied] = useState(mapping.screens.some((s) => s.id.startsWith(PREFIX)))
  const [test, setTest] = useState<0 | 1 | 2 | 3>(0)
  useEffect(() => () => send({ type: 'blendTest', mode: 0 }), [])

  const apply = (next: Settings) => {
    const used = new Set(next.displays.slice(0, next.count))
    // as telas do assistente são refeitas; outras telas nas mesmas saídas saem (senão duas imagens brigam pelo projetor)
    const keep = mapping.screens.filter((s) => !s.id.startsWith(PREFIX) && !used.has(s.display))
    onChange({ ...mapping, screens: [...keep, ...buildScreens(next, displays, mapping.screens.filter((s) => s.id.startsWith(PREFIX)))] })
    setApplied(true)
  }
  const set = (patch: Partial<Settings>) => {
    const next = { ...st, ...patch }
    setSt(next)
    if (applied) apply(next) // depois de montado, cada ajuste vai na hora para os projetores
  }
  const remove = () => {
    onChange({ ...mapping, screens: mapping.screens.filter((s) => !s.id.startsWith(PREFIX)).length ? mapping.screens.filter((s) => !s.id.startsWith(PREFIX)) : mapping.screens })
    setApplied(false)
  }
  const testMode = (m: 0 | 1 | 2 | 3) => { const v = test === m ? 0 : m; setTest(v); send({ type: 'blendTest', mode: v }) }

  const n = st.count, o = st.overlap / 100, span = 1 / (n - (n - 1) * o)
  const d0 = displays.find((d) => d.index === st.displays[0])
  const pw = d0?.width ?? 1920, ph = d0?.height ?? 1080
  const total = st.arr === 'lado' ? { w: Math.round(pw * (n - (n - 1) * o)), h: ph } : { w: pw, h: Math.round(ph * (n - (n - 1) * o)) }
  const overlapPx = Math.round((st.arr === 'lado' ? pw : ph) * o)
  const dupDisplays = useMemo(() => st.displays.slice(0, n).some((d, i, a) => a.indexOf(d) !== i), [st.displays, n])

  // desenho: a imagem inteira dos projetores, com cada projetor e as emendas
  const BW = 520, BH = 150, aspect = total.w / total.h
  const dw = Math.min(BW, BH * aspect), dh = dw / aspect

  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal blend-dialog" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Edge blending: vários projetores numa imagem só</h3>
        <p className="meta">Os projetores se sobrepõem um pouco e, na faixa da emenda, cada um vai escurecendo suavemente enquanto o vizinho clareia. Bem ajustado, a emenda some. Monte, projete o teste em cinza e ajuste até não ver a faixa.</p>

        <div className="blend-grid">
          <div className="blend-step">
            <b>1. Projetores</b>
            <div className="row">
              {[2, 3, 4].map((c) => <button key={c} className={`small ${n === c ? 'active' : ''}`} onClick={() => set({ count: c })}>{c} projetores</button>)}
            </div>
            <div className="row">
              <button className={`small ${st.arr === 'lado' ? 'active' : ''}`} onClick={() => set({ arr: 'lado' })}>lado a lado</button>
              <button className={`small ${st.arr === 'pilha' ? 'active' : ''}`} onClick={() => set({ arr: 'pilha' })}>um em cima do outro</button>
            </div>
            {Array.from({ length: n }, (_, i) => (
              <label key={i} className="blend-proj">
                <span><i style={{ background: COLORS[i] }} /> Projetor {i + 1}{st.arr === 'lado' ? (i === 0 ? ' (esquerda)' : i === n - 1 ? ' (direita)' : '') : (i === 0 ? ' (cima)' : i === n - 1 ? ' (baixo)' : '')}</span>
                <select value={st.displays[i] ?? ''} onChange={(e) => { const ds = [...st.displays]; ds[i] = Number(e.target.value); set({ displays: ds }) }}>
                  {displays.map((d) => <option key={d.index} value={d.index}>{d.name} · {d.width}×{d.height}{d.primary ? ' (principal)' : ''}</option>)}
                </select>
              </label>
            ))}
            {dupDisplays && <p className="warn-text">Dois projetores na mesma saída: cada projetor precisa estar ligado numa saída diferente do computador (ou num divisor de vídeo, como um Datapath/Matrox).</p>}
          </div>

          <div className="blend-step">
            <b>2. Sobreposição</b>
            <label>Quanto se sobrepõem <output>{st.overlap.toFixed(1)}%</output>
              <input type="range" min={5} max={40} step={0.5} value={st.overlap} onChange={(e) => set({ overlap: Number(e.target.value) })} />
            </label>
            <p className="meta">≈ {overlapPx} px de cada projetor. Ajuste a posição/zoom dos projetores até a grade se encaixar e depois este valor até as linhas da grade baterem na emenda. 10% a 20% é o comum.</p>
            <label>Imagem <output />
              <select value={st.fit} onChange={(e) => set({ fit: e.target.value as Settings['fit'] })}>
                <option value="proporcao">Manter a proporção (corta um pouco da imagem)</option>
                <option value="esticar">Esticar a composição inteira</option>
              </select>
            </label>
            <label>Fonte <output />
              <select value={st.source ?? ''} onChange={(e) => set({ source: e.target.value || null })}>
                {sources.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </label>
            <p className="meta">Tamanho total da imagem dos projetores: <b>{total.w} × {total.h} px</b> ({(total.w / total.h).toFixed(2).replace('.', ',')} : 1). Faça os vídeos nessa proporção para não cortar nada (no clip, use o encaixe "Encaixar"). A composição do VJesus tem 1920 px de largura: em telas tão largas a imagem é ampliada para os projetores.</p>
          </div>

          <div className="blend-step">
            <b>3. Acabamento da emenda</b>
            <label title="O quanto o projetor converte o sinal em luz. 2,2 serve para quase todos; se a emenda ficar mais clara, aumente; mais escura, diminua.">Gamma do projetor <output>{st.gamma.toFixed(2)}</output>
              <input type="range" min={1} max={3} step={0.05} value={st.gamma} onChange={(e) => set({ gamma: Number(e.target.value) })} />
            </label>
            <label title="Forma da rampa: 2 é a curva suave padrão; valores maiores deixam o meio da emenda mais firme">Curva <output>{st.curve.toFixed(1)}</output>
              <input type="range" min={1} max={4} step={0.1} value={st.curve} onChange={(e) => set({ curve: Number(e.target.value) })} />
            </label>
            <label title="Na emenda, o preto dos dois projetores se soma e fica mais claro. Suba até o preto fora da emenda ficar igual ao de dentro (teste em fundo preto, com a sala escura).">Nivelar o preto <output>{Math.round(st.black * 100)}%</output>
              <input type="range" min={0} max={0.15} step={0.005} value={st.black} onChange={(e) => set({ black: Number(e.target.value) })} />
            </label>
          </div>
        </div>

        <div className="blend-canvas" style={{ width: dw, height: dh }}>
          {Array.from({ length: n }, (_, i) => {
            const a = i * span * (1 - o)
            const style = st.arr === 'lado' ? { left: a * dw, top: 0, width: span * dw, height: dh } : { left: 0, top: a * dh, width: dw, height: span * dh }
            return <div key={i} className="blend-proj-box" style={{ ...style, borderColor: COLORS[i], background: COLORS[i] + '22' }}><span style={{ color: COLORS[i] }}>{i + 1}</span></div>
          })}
          {Array.from({ length: n - 1 }, (_, i) => {
            const a = (i + 1) * span * (1 - o)
            const style = st.arr === 'lado' ? { left: a * dw, top: 0, width: span * o * dw, height: dh } : { left: 0, top: a * dh, width: dw, height: span * o * dh }
            return <div key={'e' + i} className="blend-zone" style={style} title="emenda" />
          })}
        </div>

        <div className="blend-actions">
          {!applied
            ? <button className="primary" disabled={dupDisplays} onClick={() => apply(st)}>Montar nos projetores</button>
            : <span className="ok-text">✔ Montado: os ajustes vão direto para os projetores.</span>}
          <span className="blend-tests">
            Teste:
            <button className={`small ${test === 3 ? 'active' : ''}`} onClick={() => testMode(3)} title="Grade para alinhar os projetores e conferir a sobreposição">Grade</button>
            <button className={`small ${test === 2 ? 'active' : ''}`} onClick={() => testMode(2)} title="Cinza: a emenda não deve aparecer nem mais clara nem mais escura">Cinza</button>
            <button className={`small ${test === 1 ? 'active' : ''}`} onClick={() => testMode(1)} title="Branco: confere o brilho da emenda">Branco</button>
          </span>
          {applied && <button className="small danger-text" onClick={remove}>Desfazer (tirar os projetores)</button>}
          <button onClick={onClose}>Fechar</button>
        </div>
        <details className="midi-help">
          <summary>Passo a passo para uma emenda perfeita</summary>
          <ol>
            <li>Ligue cada projetor numa saída do computador (no Windows: "Estender estas telas"). Escolha acima qual saída é cada projetor.</li>
            <li>Clique em <b>Montar nos projetores</b> e ligue o teste <b>Grade</b>.</li>
            <li>Nos projetores, ajuste posição, zoom e foco até as duas imagens ficarem do mesmo tamanho e as linhas da grade se encontrarem na emenda. Se precisar, corrija a perspectiva arrastando os cantos do slice de cada projetor no mapeamento.</li>
            <li>Ajuste a <b>sobreposição</b> até as linhas da grade dentro da emenda baterem certinho (sem linhas duplas).</li>
            <li>Ligue o teste <b>Cinza</b>: se a emenda estiver mais clara, aumente o gamma do projetor; se estiver mais escura, diminua. A faixa deve sumir.</li>
            <li>Com a sala escura e uma imagem preta, use <b>Nivelar o preto</b> até o preto fora da emenda ficar igual ao de dentro.</li>
            <li>Desligue o teste. Dica: projetores do mesmo modelo, com o mesmo brilho e o mesmo modo de cor, dão o melhor resultado.</li>
          </ol>
        </details>
      </div>
    </div>,
    document.body,
  )
}
