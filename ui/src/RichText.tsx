import { parseRich } from './lyrics'

// filtro SVG usado pela prévia da animação "Ondular letras" (no telão quem faz é o shader)
if (!document.getElementById('anim-ondular')) {
  document.body.insertAdjacentHTML('beforeend', `<svg width="0" height="0" style="position:absolute" aria-hidden="true"><filter id="anim-ondular" x="-10%" y="-20%" width="120%" height="140%"><feTurbulence type="turbulence" baseFrequency="0.004 0.05" numOctaves="1" seed="3"><animate attributeName="baseFrequency" dur="2s" values="0.004 0.05;0.006 0.07;0.004 0.05" repeatCount="indefinite"/></feTurbulence><feDisplacementMap in="SourceGraphic" scale="10" xChannelSelector="R" yChannelSelector="G"/></filter></svg>`)
}

/**
 * Texto com as marcações de trecho (<b>, <i>, <u>, <cor>, <tam>, <anim>) desenhado como no telão.
 * Com animate, os trechos com <anim> se mexem (prévia na aba Letras).
 */
export function RichText({ text, animate }: { text: string; animate?: boolean }) {
  return (
    <>
      {parseRich(text).map((r, i) => (
        <span key={i}
          className={animate && r.anim ? `anim anim-${r.anim.type}` : undefined}
          style={{
            fontWeight: r.bold ? 700 : undefined, fontStyle: r.italic ? 'italic' : undefined,
            textDecoration: r.underline ? 'underline' : undefined, color: r.color ?? undefined,
            fontSize: r.scale !== 1 ? `${r.scale}em` : undefined,
            fontFamily: r.font ? `"${r.font}", system-ui, sans-serif` : undefined,
            ...(animate && r.anim ? { ['--ai' as string]: r.anim.intensity, ['--as' as string]: `${1 / r.anim.speed}s` } : {}),
          }}>{r.text}</span>
      ))}
    </>
  )
}