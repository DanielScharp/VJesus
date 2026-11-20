/**
 * Sliders no estilo do Resolume: a parte até o valor fica preenchida com cor. O CSS desenha o preenchimento
 * com a variável --p (0%..100%); aqui ela acompanha o valor de cada <input type=range> da interface, quando a
 * pessoa arrasta e quando o valor muda sozinho (MIDI, outra tela, desfazer).
 */
function update(r: HTMLInputElement) {
  const min = Number(r.min || 0), max = Number(r.max || 100), v = Number(r.value)
  const p = max > min ? ((v - min) / (max - min)) * 100 : 0
  const s = `${Math.max(0, Math.min(100, p)).toFixed(2)}%`
  if (r.style.getPropertyValue('--p') !== s) r.style.setProperty('--p', s)
}

document.addEventListener('input', (e) => {
  const t = e.target as HTMLElement
  if (t instanceof HTMLInputElement && t.type === 'range') update(t)
}, true)

// valores que mudam sem a pessoa mexer: confere todos de tempos em tempos (é barato: só os da tela)
function sweep() {
  if (document.hidden) return // janela minimizada: ninguém está vendo
  document.querySelectorAll<HTMLInputElement>('input[type=range]').forEach(update)
}
setInterval(sweep, 200)
let queued = false
new MutationObserver(() => { if (queued) return; queued = true; requestAnimationFrame(() => { queued = false; sweep() }) }).observe(document.documentElement, { childList: true, subtree: true })
