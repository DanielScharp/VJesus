import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { onMessage, send, type DecodeMode, type PerformanceState } from './bridge'

const MODES: { id: DecodeMode; label: string; hint: string }[] = [
  { id: 'auto', label: 'Automático (recomendado)', hint: 'Vídeos pesados (maiores que Full HD, HEVC, AV1, VP9) vão para a placa de vídeo; os leves ficam no processador, que abre sem esforço.' },
  { id: 'gpu', label: 'Placa de vídeo', hint: 'Todos os vídeos que a placa aceita vão para ela (HAP continua como está, já é leve).' },
  { id: 'cpu', label: 'Processador', hint: 'Como era antes. Use se algum driver de vídeo der problema.' },
]

const NAME: Record<DecodeMode, string> = { auto: 'Automático', gpu: 'Placa de vídeo', cpu: 'Processador' }

/** Menu → Desempenho do vídeo: onde decodificar os vídeos e um teste para descobrir o melhor neste PC. */
export function PerformanceDialog({ onClose }: { onClose: () => void }) {
  const [st, setSt] = useState<PerformanceState | null>(null)
  useEffect(() => {
    const off = onMessage((m) => { if (m.type === 'performance') setSt(m) })
    send({ type: 'performanceGet' })
    const timer = setInterval(() => send({ type: 'performanceGet' }), 2000)
    return () => { off(); clearInterval(timer) }
  }, [])

  const test = st?.test
  // fora do topo da tela (lá o texto não quebra linha e a posição é relativa à barra)
  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal perf-dialog" onMouseDown={(e) => e.stopPropagation()}>
        <h3>Desempenho do vídeo</h3>
        <p className="meta">Onde os vídeos são decodificados neste computador. A escolha fica salva neste PC e vale para os vídeos que começarem a tocar depois.</p>
        <div className="perf-modes">
          {MODES.map((m) => (
            <label key={m.id} className={`perf-mode ${st?.decode === m.id ? 'on' : ''}`}>
              <input type="radio" name="decode" checked={st?.decode === m.id} onChange={() => send({ type: 'performanceSet', decode: m.id })} />
              <span><b>{m.label}</b><small>{m.hint}</small></span>
            </label>
          ))}
        </div>

        <div className="perf-test">
          <button disabled={!st || st.testing} onClick={() => send({ type: 'performanceTest' })}>
            {st?.testing ? 'Testando… (uns 15 segundos)' : 'Testar este computador'}
          </button>
          <span className="meta">Toca os vídeos mais pesados da grade por alguns segundos, sem aparecer no telão, dos dois jeitos.</span>
        </div>
        {test && 'error' in test && <p className="warn-text">{test.error}</p>}
        {test && 'cpu' in test && (
          <div className="perf-result">
            <table>
              <thead><tr><th /><th>Fluidez</th><th>Processador usado</th></tr></thead>
              <tbody>
                <tr><td>Processador</td><td>{test.cpu.smooth}%</td><td>{test.cpu.usage}%</td></tr>
                <tr>
                  <td>Placa de vídeo{test.gpu.count !== test.gpu.total ? ` (${test.gpu.count} de ${test.gpu.total} vídeos)` : ''}</td>
                  <td>{test.gpu.count ? `${test.gpu.smooth}%` : '—'}</td><td>{test.gpu.count ? `${test.gpu.usage}%` : '—'}</td>
                </tr>
              </tbody>
            </table>
            <p><b>Sugestão: {NAME[test.suggestion]}.</b> {test.why}</p>
            {st && st.decode !== test.suggestion && (
              <button onClick={() => send({ type: 'performanceSet', decode: test.suggestion })}>Usar {NAME[test.suggestion]}</button>
            )}
            <p className="meta">Vídeos testados: {test.files.join(', ')}</p>
          </div>
        )}

        {st && st.layers.length > 0 && (
          <div className="perf-layers">
            <div className="meta">Tocando agora:</div>
            {st.layers.map((l, i) => (
              <div key={i} className="perf-layer">
                <span>{l.layer}: {l.clip}</span>
                <span className="meta">{l.size} · {l.codec}</span>
                <span className={l.gpu ? 'perf-tag gpu' : 'perf-tag'}>{l.gpu ? 'placa de vídeo' : 'processador'}</span>
              </div>
            ))}
          </div>
        )}
        <div className="modal-actions"><button onClick={onClose}>Fechar</button></div>
      </div>
    </div>,
    document.body,
  )
}
