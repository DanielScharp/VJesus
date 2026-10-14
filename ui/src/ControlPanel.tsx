import { useEffect, useState } from 'react'
import { send, type ControlInfo, type StreamingInfo } from './bridge'

type Props = {
  info: ControlInfo | null
  learn: boolean
  onLearn: (on: boolean) => void
}

/**
 * Controle externo. Com "MIDI learn" ligado, clicar em qualquer controle marcado da tela
 * (clip, coluna, opacidade, slider de efeito, master) e depois mexer num knob/pad MIDI liga os dois.
 */
export function ControlPanel({ info, learn, onLearn }: Props) {
  const bound = new Set(info?.midiBindings.map((b) => b.address) ?? [])

  // modo learn: intercepta o clique nos elementos com data-address antes da ação normal
  useEffect(() => {
    document.body.classList.toggle('learn', learn)
    if (!learn) return
    const pick = (e: Event) => {
      const el = (e.target as HTMLElement).closest<HTMLElement>('[data-address]')
      if (!el) return
      e.preventDefault()
      e.stopPropagation()
      if (e.type === 'pointerdown') send({ type: 'learnStart', address: el.dataset.address! })
    }
    document.addEventListener('pointerdown', pick, true)
    document.addEventListener('click', pick, true)
    document.addEventListener('contextmenu', pick, true)
    return () => {
      document.removeEventListener('pointerdown', pick, true)
      document.removeEventListener('click', pick, true)
      document.removeEventListener('contextmenu', pick, true)
    }
  }, [learn])

  // marca na tela o que já tem MIDI e o que está esperando um controle
  useEffect(() => {
    document.querySelectorAll<HTMLElement>('[data-address]').forEach((el) => {
      el.classList.toggle('midi-bound', bound.has(el.dataset.address!))
      el.classList.toggle('midi-learning', info?.learning === el.dataset.address)
    })
  })

  if (!info) return null
  return (
    <section>
      <h2>Controle externo</h2>
      {info.error && <div className="meta warn">{info.error}</div>}
      <div className="control-grid">
        <span>OSC</span>
        <span>{info.oscPort ? <>UDP porta <strong>{info.oscPort}</strong> (endereços no README)</> : 'desligado'}</span>
        <span>Celular</span>
        <span>{info.remoteUrl ? <a href={info.remoteUrl} target="_blank" rel="noreferrer">{info.remoteUrl}</a> : 'desligado'}</span>
        <span>MIDI</span>
        <span>{info.midiDevices.length ? info.midiDevices.join(', ') : 'nenhum dispositivo encontrado'}</span>
      </div>
      <div className="row">
        <button className={learn ? 'active' : ''} onClick={() => { if (learn) send({ type: 'learnCancel' }); onLearn(!learn) }}>
          {learn ? 'Concluir MIDI learn' : 'MIDI learn'}
        </button>
        <button className="small" disabled={!info.midiBindings.length} onClick={() => {
          if (window.confirm('Apagar todos os mapeamentos MIDI?')) send({ type: 'midiClear' })
        }}>Limpar MIDI ({info.midiBindings.length})</button>
      </div>
      {learn && (
        <p className="meta">
          {info.learning
            ? <>Mexa no controle MIDI para ligar a <code>{info.learning}</code>…</>
            : 'Clique num clip, número de coluna, slider de opacidade, efeito ou master; depois mexa no controle MIDI.'}
        </p>
      )}
    </section>
  )
}

/** Spout (outros programas no mesmo PC, ex.: OBS com plugin Spout) e NDI (pela rede). */
export function StreamingPanel({ info }: { info: StreamingInfo }) {
  const [spoutName, setSpoutName] = useState(info.spout ?? 'VJApp')
  const [ndiName, setNdiName] = useState(info.ndi ?? 'VJApp')
  const apply = (spout: string | null, ndi: string | null) => send({ type: 'setStreaming', spout, ndi })

  return (
    <section>
      <h2>Spout e NDI</h2>
      <div className="stream-row">
        <label className="check">
          <input type="checkbox" checked={!!info.spout} onChange={(e) => apply(e.target.checked ? spoutName : null, info.ndi)} />
          Spout
        </label>
        <input className="name" value={spoutName} onChange={(e) => setSpoutName(e.target.value)}
          onBlur={() => info.spout && spoutName !== info.spout && apply(spoutName, info.ndi)} />
      </div>
      <div className="stream-row">
        <label className="check">
          <input type="checkbox" checked={!!info.ndi} disabled={!info.ndiAvailable}
            onChange={(e) => apply(info.spout, e.target.checked ? ndiName : null)} />
          NDI
        </label>
        <input className="name" value={ndiName} onChange={(e) => setNdiName(e.target.value)}
          onBlur={() => info.ndi && ndiName !== info.ndi && apply(info.spout, ndiName)} />
      </div>
      <p className="meta">
        {info.ndiAvailable
          ? 'Envia a composição 1920×1080. Spout: mesmo PC, sem atraso. NDI: pela rede, 30 fps.'
          : 'Para NDI, instale o NDI Tools ou NDI Runtime (gratuito, ndi.video/tools) e reabra o app.'}
      </p>
    </section>
  )
}