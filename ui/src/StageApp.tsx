import { useEffect, useState } from 'react'
import { onMessage, send, type StageState } from './bridge'
import { StageView } from './StageView'

/** Página que roda na janela da tela de retorno (index.html#stage): só a tela, em tela cheia. */
export function StageApp() {
  const [state, setState] = useState<StageState | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  useEffect(() => {
    document.body.classList.add('stage-window')
    const off = onMessage((msg) => {
      if (msg.type === 'stage') setState(msg)
      else if (msg.type === 'stagePreview') setPreview(msg.data)
    })
    send({ type: 'stageHello' })
    return off
  }, [])
  if (!state) return null
  return <StageView state={state} preview={preview} />
}
