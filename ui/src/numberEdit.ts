/**
 * Valores dos sliders editáveis: clique no número (o <output> ao lado de um <input type=range>) e digite.
 * Enter confirma, Esc cancela, ↑/↓ somam um passo. Aceita vírgula ou ponto; o número é lido na mesma unidade mostrada (100% = 1, 45° = 45, posição ±50%).
 * Funciona para todos os sliders da interface, sem mudar cada tela.
 */
function rangeOf(output: HTMLElement): HTMLInputElement | null {
  const label = output.closest('label')
  return label?.querySelector('input[type=range]') ?? null
}

/** Muda o valor do slider como se a pessoa tivesse arrastado (o React recebe o onChange). */
function setRange(range: HTMLInputElement, value: number) {
  const min = Number(range.min || 0), max = Number(range.max || 100)
  const v = Math.min(max, Math.max(min, value))
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  setter.call(range, String(v))
  range.dispatchEvent(new Event('input', { bubbles: true }))
  range.dispatchEvent(new Event('change', { bubbles: true }))
}

/** Quantas vezes o número mostrado é maior que o valor do slider (100 em "100%" com valor 1; 50 na posição ±50%). */
const factors = new WeakMap<HTMLInputElement, number>()
const NICE = [1, 10, 50, 100, 1000]

function numberIn(text: string) {
  const n = parseFloat(text.replace(',', '.').replace(/[^\d.+-]/g, ''))
  return isFinite(n) ? n : null
}

/** Mede a escala comparando o texto mostrado com o valor real (só dá quando os dois não são zero). */
function learn(range: HTMLInputElement, shown: string) {
  const v = Number(range.value), n = numberIn(shown)
  if (!v || n === null || n === 0) return
  let f = n / v
  const nice = NICE.find((x) => Math.abs(f - x) / x < 0.05)
  if (nice) f = nice
  factors.set(range, f)
}

/** Texto digitado → valor do slider, na mesma unidade que está mostrada. */
function parse(text: string, shown: string, range: HTMLInputElement): number | null {
  const n = numberIn(text)
  if (n === null) return null
  learn(range, shown)
  let f = factors.get(range)
  if (f === undefined) {
    // valor zero agora: deduz pela unidade e pelos limites do slider
    const min = Number(range.min || 0), max = Number(range.max || 100)
    const percent = shown.trim().endsWith('%') || text.trim().endsWith('%')
    f = !percent ? 1 : min < 0 ? 100 / (max - min) : max <= 10 ? 100 : 1
  }
  return n / f
}
function edit(output: HTMLElement) {
  const range = rangeOf(output)
  if (!range || range.disabled || output.querySelector('input')) return
  const shown = output.textContent ?? ''
  const box = document.createElement('input')
  box.type = 'text'
  box.inputMode = 'decimal'
  box.className = 'output-edit'
  box.value = shown.replace(/\s*(ms|s|%)$/, '').trim()
  // a caixa fica por cima do número, presa no <body>: não mexe nos elementos que o React controla
  // (inserir algo dentro do painel fazia o React se perder e deixar cópias do painel na tela)
  const r = output.getBoundingClientRect()
  Object.assign(box.style, { position: 'fixed', left: `${r.left - 4}px`, top: `${r.top + r.height / 2 - 12}px`, height: '24px', zIndex: '1000', width: `${Math.max(56, r.width + 12)}px` })
  output.style.visibility = 'hidden'
  document.body.appendChild(box)
  box.focus()
  box.select()

  let done = false
  const finish = (apply: boolean) => {
    if (done) return
    done = true
    if (apply) {
      const v = parse(box.value, shown, range)
      if (v !== null) setRange(range, v)
    }
    box.remove()
    output.style.visibility = ''
  }
  box.addEventListener('keydown', (e) => {
    e.stopPropagation() // não dispara atalhos (setas, espaço, teclas de ataque) enquanto digita
    if (e.key === 'Enter') finish(true)
    else if (e.key === 'Escape') finish(false)
    else if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      e.preventDefault()
      const step = Number(range.step) || 1
      setRange(range, Number(range.value) + (e.key === 'ArrowUp' ? step : -step) * (e.shiftKey ? 10 : 1))
      box.value = (output.textContent ?? '').replace(/\s*(ms|s|%)$/, '').trim()
    }
  })
  box.addEventListener('blur', () => finish(true))
  // a caixa é fixa na tela: se a página rolar, confirma e some (senão ficaria fora do lugar)
  window.addEventListener('scroll', () => finish(true), { once: true, capture: true })
  box.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation() })
}

document.addEventListener('click', (e) => {
  const out = (e.target as HTMLElement).closest?.('output')
  if (!out || !rangeOf(out as HTMLElement)) return
  e.preventDefault() // o clique no <label> não deve ir para o slider
  e.stopPropagation()
  edit(out as HTMLElement)
}, true)

// marca os números editáveis (para o cursor e a dica)
const mark = () => document.querySelectorAll('label output').forEach((o) => {
  const r = rangeOf(o as HTMLElement)
  if (r) learn(r, o.textContent ?? '')
  if (!o.classList.contains('editable') && r) {
    o.classList.add('editable')
    o.setAttribute('title', 'Clique para digitar o valor')
  }
})
let queued = false
new MutationObserver(() => {
  if (queued) return
  queued = true
  requestAnimationFrame(() => { queued = false; mark() })
}).observe(document.documentElement, { childList: true, subtree: true })
