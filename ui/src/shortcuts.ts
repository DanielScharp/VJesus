import { send } from './bridge'

/**
 * Central de atalhos de teclado: a lista de ações (com o que cada uma faz), as teclas padrão e as
 * teclas escolhidas pela pessoa (salvas em %AppData%\VJApp\atalhos.json pelo motor).
 *
 * Onde vale cada atalho: "global" vale em todo o programa; os outros só na aba indicada.
 * A mesma tecla não pode estar em duas ações que valem ao mesmo tempo (global + qualquer aba,
 * ou duas da mesma aba); em abas diferentes ela pode se repetir (ex.: → nas Letras e na Bíblia).
 */
export type Scope = 'global' | 'letras' | 'biblia' | 'mapeamento'

export type ActionDef = { id: string; label: string; hint?: string; scope: Scope; defaults: string[] }

export const SCOPE_LABEL: Record<Scope, string> = {
  global: 'Em qualquer aba',
  letras: 'Aba Letras',
  biblia: 'Aba Bíblia',
  mapeamento: 'Aba Mapeamento',
}

export const ACTIONS: ActionDef[] = [
  { id: 'projeto.novo', label: 'Novo projeto', hint: 'Começa um projeto vazio (grade limpa).', scope: 'global', defaults: ['Ctrl+N'] },
  { id: 'projeto.abrir', label: 'Abrir projeto…', hint: 'Abre um arquivo .vjproj.', scope: 'global', defaults: ['Ctrl+O'] },
  { id: 'projeto.salvar', label: 'Salvar projeto', hint: 'Salva no arquivo atual (pergunta o nome se for novo).', scope: 'global', defaults: ['Ctrl+S'] },
  { id: 'projeto.salvarComo', label: 'Salvar projeto como…', hint: 'Salva com outro nome ou em outra pasta.', scope: 'global', defaults: ['Ctrl+Shift+S'] },
  { id: 'tempo.tap', label: 'TAP do BPM', hint: 'Toque no ritmo da música para acertar o BPM.', scope: 'global', defaults: ['T'] },
  { id: 'telao.encerrar', label: 'Encerrar letra / versículo', hint: 'Tira a letra ou o versículo do telão e da tela de retorno (fim da música).', scope: 'global', defaults: ['Shift+Escape', 'Delete'] },
  { id: 'ataques.parar', label: 'Parar todos os ataques', hint: 'Interrompe na hora flash, strobe, tremor…', scope: 'global', defaults: [] },

  { id: 'letras.proximo', label: 'Próximo slide', hint: 'Passa a letra para o slide seguinte (ou projeta o primeiro).', scope: 'letras', defaults: ['ArrowRight', 'ArrowDown', 'PageDown', 'Space'] },
  { id: 'letras.anterior', label: 'Slide anterior', hint: 'Volta a letra um slide.', scope: 'letras', defaults: ['ArrowLeft', 'ArrowUp', 'PageUp'] },
  { id: 'letras.branco', label: 'Tela em branco (liga/desliga)', hint: 'Esconde a letra do telão sem perder a posição.', scope: 'letras', defaults: ['B', 'Escape'] },

  { id: 'biblia.proximo', label: 'Próximo versículo no telão', hint: 'Projeta o versículo seguinte (passa de capítulo sozinho).', scope: 'biblia', defaults: ['ArrowRight', 'PageDown', 'Space'] },
  { id: 'biblia.anterior', label: 'Versículo anterior no telão', hint: 'Projeta o versículo anterior.', scope: 'biblia', defaults: ['ArrowLeft', 'PageUp'] },
  { id: 'biblia.abaixo', label: 'Escolher versículo de baixo', hint: 'Move a seleção na lista (sem projetar).', scope: 'biblia', defaults: ['ArrowDown'] },
  { id: 'biblia.acima', label: 'Escolher versículo de cima', hint: 'Move a seleção na lista (sem projetar).', scope: 'biblia', defaults: ['ArrowUp'] },
  { id: 'biblia.projetar', label: 'Projetar o versículo escolhido', scope: 'biblia', defaults: ['Enter'] },
  { id: 'biblia.buscar', label: 'Ir para a busca', hint: 'Coloca o cursor na caixa de referência/pesquisa.', scope: 'biblia', defaults: ['/', 'Ctrl+F'] },

  { id: 'mapa.esquerda', label: 'Mover ponto para a esquerda', hint: '1 pixel (com Shift: 10 pixels). Precisa de um ponto selecionado.', scope: 'mapeamento', defaults: ['ArrowLeft'] },
  { id: 'mapa.direita', label: 'Mover ponto para a direita', hint: '1 pixel (com Shift: 10 pixels).', scope: 'mapeamento', defaults: ['ArrowRight'] },
  { id: 'mapa.cima', label: 'Mover ponto para cima', hint: '1 pixel (com Shift: 10 pixels).', scope: 'mapeamento', defaults: ['ArrowUp'] },
  { id: 'mapa.baixo', label: 'Mover ponto para baixo', hint: '1 pixel (com Shift: 10 pixels).', scope: 'mapeamento', defaults: ['ArrowDown'] },
]

/** Teclas escolhidas (só as que mudaram do padrão; lista vazia = sem atalho). */
let custom: Record<string, string[]> = {}
const listeners = new Set<() => void>()

export function setCustom(map: Record<string, string[]> | null | undefined) {
  custom = map ?? {}
  listeners.forEach((f) => f())
}
export function subscribe(fn: () => void) { listeners.add(fn); return () => { listeners.delete(fn) } }
export function getCustom() { return custom }

export function keysOf(id: string): string[] {
  return custom[id] ?? ACTIONS.find((a) => a.id === id)?.defaults ?? []
}

/** Salva as teclas (manda para o motor guardar em atalhos.json). */
export function saveCustom(next: Record<string, string[]>) {
  setCustom(next)
  send({ type: 'shortcutsSave', shortcuts: next })
}

const NAMED: Record<string, string> = { ' ': 'Space', Spacebar: 'Space', Esc: 'Escape' }

/** Normaliza uma tecla solta (como guardada nos ataques) para o formato dos atalhos. */
export function normalizeKey(k: string): string {
  if (NAMED[k]) return NAMED[k]
  return k.length === 1 ? k.toUpperCase() : k
}

/** Combinação de um evento de teclado: "Ctrl+Shift+S", "Shift+Escape", "ArrowRight", "T", "1". */
export function comboOf(e: KeyboardEvent, ignoreShift = false): string | null {
  if (['Control', 'Shift', 'Alt', 'Meta', 'AltGraph', 'CapsLock', 'Dead', 'Unidentified'].includes(e.key)) return null
  const key = normalizeKey(e.key)
  const parts: string[] = []
  if (e.ctrlKey || e.metaKey) parts.push('Ctrl')
  if (e.altKey) parts.push('Alt')
  // Shift só conta com teclas com nome ou junto de Ctrl/Alt ("?" já é o próprio caractere)
  if (e.shiftKey && !ignoreShift && (key.length > 1 || e.ctrlKey || e.altKey || e.metaKey)) parts.push('Shift')
  parts.push(key)
  return parts.join('+')
}

/** O evento aciona esta ação? (allowShift: Shift muda só a intensidade, como nas setas do mapeamento) */
let paused = false
/** Enquanto a janela de atalhos está aberta, nenhum atalho dispara. */
export function pauseShortcuts(on: boolean) { paused = on }
export function shortcutsPaused() { return paused }

export function matches(e: KeyboardEvent, id: string, allowShift = false): boolean {
  if (paused) return false
  const c = comboOf(e, allowShift)
  return !!c && keysOf(id).includes(c)
}

/** Digitando num campo? (aí os atalhos simples não disparam) */
export function isTyping(e: KeyboardEvent) {
  const t = e.target as HTMLElement
  return t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable
}

const LABELS: Record<string, string> = {
  ArrowRight: '→', ArrowLeft: '←', ArrowUp: '↑', ArrowDown: '↓', Space: 'Espaço', Escape: 'Esc', Delete: 'Delete',
  PageUp: 'Page Up', PageDown: 'Page Down', Enter: 'Enter', Backspace: 'Backspace', Tab: 'Tab',
}
export function comboLabel(c: string) {
  return c.split('+').map((p) => LABELS[p] ?? p).join(' + ')
}

export function scopesOverlap(a: Scope, b: Scope) {
  return a === 'global' || b === 'global' || a === b
}
