import { onMessage, send } from './bridge'

type Props = Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type' | 'onChange' | 'value'> & {
  value: string
  onChange: (e: { target: { value: string } }) => void
}

let seq = 0

/** Abre o conta-gotas de tela inteira do VJesus e devolve a cor clicada (null = cancelado). */
export function pickScreenColor(): Promise<string | null> {
  const id = `c${++seq}`
  return new Promise((resolve) => {
    const off = onMessage((m) => {
      if (m.type === 'screenColor' && m.id === id) { off(); resolve(m.color) }
    })
    send({ type: 'pickScreenColor', id })
  })
}

/**
 * Campo de cor com conta-gotas que pega a cor de qualquer lugar das telas do computador (imagens, vídeos,
 * outros programas, outro monitor). O seletor de cor normal continua funcionando no quadradinho.
 */
export function ColorInput({ value, onChange, disabled, className, title, ...rest }: Props) {
  return (
    <span className={`color-input ${className ?? ''}`}>
      <input type="color" value={value} disabled={disabled} title={title} onChange={(e) => onChange({ target: { value: e.target.value } })} {...rest} />
      <button type="button" className="eyedrop" disabled={disabled} title="Conta-gotas: clique em qualquer ponto de qualquer tela para pegar a cor (Esc cancela)"
        onClick={async (e) => { e.preventDefault(); e.stopPropagation(); const c = await pickScreenColor(); if (c) onChange({ target: { value: c } }) }}>💧</button>
    </span>
  )
}
