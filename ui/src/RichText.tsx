import { parseRich } from './lyrics'

/** Texto com as marcações de trecho (<b>, <i>, <u>, <cor>, <tam>) desenhado como no telão. */
export function RichText({ text }: { text: string }) {
  return (
    <>
      {parseRich(text).map((r, i) => (
        <span key={i} style={{
          fontWeight: r.bold ? 700 : undefined, fontStyle: r.italic ? 'italic' : undefined,
          textDecoration: r.underline ? 'underline' : undefined, color: r.color ?? undefined,
          fontSize: r.scale !== 1 ? `${r.scale}em` : undefined,
        }}>{r.text}</span>
      ))}
    </>
  )
}
