import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { BibleBookInfo } from './bridge'
import { bookCandidates } from './bible'

type Stage = 'book' | 'chapter' | 'verse'

/**
 * Digitação rápida (como no Holyrics): na aba Bíblia, começar a digitar em qualquer lugar abre esta janela.
 * Livro: as primeiras letras já filtram ("jo" mostra Josué, Joel, Jonas, João); quando só sobra um
 * ("jos" = Josué) ele passa sozinho para o capítulo. Capítulo e versículo: números (Enter ou espaço avança;
 * o capítulo avança sozinho quando não cabe mais um dígito). No versículo dá para usar "4-7".
 * Enter no fim só escolhe o versículo (pré-seleção); clicar nele (ou Enter de novo) projeta.
 */
export function BibleQuick({ books, start, onDone, onClose }: {
  books: BibleBookInfo[]
  start: string
  onDone: (book: string, chapter: number, verse?: number, verseEnd?: number) => void
  onClose: () => void
}) {
  const [stage, setStage] = useState<Stage>('book')
  const [text, setText] = useState(start)
  const [book, setBook] = useState<BibleBookInfo | null>(null)
  const [chapter, setChapter] = useState('')
  const [verse, setVerse] = useState('')
  const typed = useRef('') // o que foi digitado do livro (para o Backspace voltar)
  const state = useRef({ stage, text, book, chapter, verse })
  state.current = { stage, text, book, chapter, verse }

  const { list, exact } = bookCandidates(text, books)

  const pickBook = (b: BibleBookInfo, firstDigit = '') => {
    typed.current = state.current.text
    setBook(b); setText('')
    if (b.chapters.length === 1) { setChapter('1'); setStage('verse'); setVerse(firstDigit); return }
    setStage('chapter'); setChapter(firstDigit)
  }

  // só um livro combina: já vai para o capítulo
  useEffect(() => {
    if (stage === 'book' && list.length === 1 && text.length >= 2) pickBook(list[0])
  }, [text])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const s = state.current
      if (e.ctrlKey || e.altKey || e.metaKey) return
      // a janela fica com o teclado: atalhos (ataques, TAP, Enter que projeta...) não disparam por baixo
      e.preventDefault(); e.stopImmediatePropagation()
      const k = e.key
      if (k === 'Escape') { onClose(); return }
      if (s.stage === 'book') {
        const c = bookCandidates(s.text, books)
        if (k === 'Backspace') { if (s.text.length <= 1) onClose(); else setText(s.text.slice(0, -1)); return }
        if (k === 'Enter' || k === ' ' || k === 'Tab') { const b = c.exact ?? c.list[0]; if (b) pickBook(b); return }
        // número depois das letras ("jo3"): escolhe o livro e já começa o capítulo
        if (/^\d$/.test(k) && /[a-z]/i.test(s.text)) { const b = c.exact ?? c.list[0]; if (b) pickBook(b, k); return }
        if (k.length === 1 && /[\p{L}\d]/u.test(k)) setText(s.text + k)
        return
      }
      const b = s.book!
      if (s.stage === 'chapter') {
        if (k === 'Backspace') { if (s.chapter) setChapter(s.chapter.slice(0, -1)); else { setStage('book'); setBook(null); setText(typed.current.slice(0, -1) || typed.current.slice(0, 1)) } return }
        const advance = (ch: string) => { const n = Number(ch); if (n >= 1 && n <= b.chapters.length) { setChapter(String(n)); setStage('verse'); setVerse('') } }
        if (k === 'Enter' || k === ' ' || k === ':' || k === '.' || k === 'Tab') { if (s.chapter) advance(s.chapter); else onDone(b.id, 1); return }
        if (/^\d$/.test(k)) {
          const next = s.chapter + k
          const n = Number(next)
          if (n < 1 || n > b.chapters.length) return
          setChapter(next)
          if (n * 10 > b.chapters.length) advance(next) // não cabe mais um dígito
        }
        return
      }
      // versículo
      const ch = Number(s.chapter)
      const max = b.chapters[ch - 1] ?? 999
      if (k === 'Backspace') { if (s.verse) setVerse(s.verse.slice(0, -1)); else { setStage('chapter'); setChapter(s.chapter.slice(0, -1)) } return }
      if (k === 'Enter' || k === 'Tab') {
        const [a, z] = s.verse.split('-').map((x) => Number(x))
        if (!a) { onDone(b.id, ch); return }
        const v = Math.min(Math.max(1, a), max)
        onDone(b.id, ch, v, z && z > v ? Math.min(z, max) : v)
        return
      }
      if (k === '-' && s.verse && !s.verse.includes('-')) { setVerse(s.verse + '-'); return }
      if (/^\d$/.test(k)) {
        const part = (s.verse.split('-')[1] ?? s.verse) + k
        if (Number(part) > max) return
        setVerse(s.verse + k)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [books])

  const ch = Number(chapter)
  return createPortal(
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal bible-quick" onMouseDown={(e) => e.stopPropagation()}>
        <span className="bq-esc">Esc para cancelar · Backspace volta</span>
        <div className={`bq-step ${stage === 'book' ? 'on' : ''}`}>
          <span className="bq-label">Livro</span>
          <span className="bq-value">{book ? book.name : <>{text}<i className="bq-caret">_</i></>}</span>
          {stage === 'book' && (
            <span className="bq-cands">
              {list.length === 0 ? 'Nenhum livro começa assim'
                : list.slice(0, 8).map((b) => (
                  <button key={b.id} className={`link ${b === (exact ?? list[0]) ? 'first' : ''}`} onClick={() => pickBook(b)}>{b.name}</button>
                ))}
              {list.length > 1 && <span className="meta"> · Enter escolhe o destacado</span>}
            </span>
          )}
        </div>
        <div className={`bq-step ${stage === 'chapter' ? 'on' : ''}`}>
          <span className="bq-label">Capítulo</span>
          <span className="bq-value">{chapter}{stage === 'chapter' && <i className="bq-caret">_</i>}</span>
          {stage === 'chapter' && book && <span className="meta">Capítulos: {book.chapters.length}</span>}
        </div>
        <div className={`bq-step ${stage === 'verse' ? 'on' : ''}`}>
          <span className="bq-label">Versículo</span>
          <span className="bq-value">{verse}{stage === 'verse' && <i className="bq-caret">_</i>}</span>
          {stage === 'verse' && book && <span className="meta">Versículos: {book.chapters[ch - 1] ?? '?'} · Enter escolhe (dá para fazer 4-7) · Enter sem número abre o capítulo</span>}
        </div>
      </div>
    </div>,
    document.body,
  )
}
