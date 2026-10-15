import { parseRich } from './lyrics'

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
            ...(animate && r.anim ? { ['--ai' as string]: r.anim.intensity, ['--as' as string]: `${1 / r.anim.speed}s` } : {}),
          }}>{r.text}</span>
      ))}
    </>
  )
}