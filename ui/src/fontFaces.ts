export type FontFace = { family: string; url: string; bold: boolean; italic: boolean }

/** Carrega na interface as fontes importadas no VJesus (as prévias usam as mesmas fontes do telão). */
export function injectFontFaces(faces: FontFace[] | undefined) {
  if (!faces) return
  let el = document.getElementById('vj-fonts') as HTMLStyleElement | null
  if (!el) {
    el = document.createElement('style')
    el.id = 'vj-fonts'
    document.head.appendChild(el)
  }
  el.textContent = faces.map((f) =>
    `@font-face{font-family:"${f.family.replace(/"/g, '')}";src:url("${f.url}");font-weight:${f.bold ? 700 : 400};font-style:${f.italic ? 'italic' : 'normal'};font-display:swap}`,
  ).join('\n')
}
