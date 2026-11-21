import { ColorInput } from './ColorInput'
import { SlidePreview } from './SlidePreview'
import { useEffect, useMemo, useRef, useState } from 'react'
import {
  onMessage, send, type Composition, type BibleBookInfo, type BibleHit, type BiblePassage, type BibleSettings, type BibleVersion, type LyricScene, type ServiceState,
} from './bridge'
import { NT_START, parseRef, refLabel, shortName } from './bible'
import { BibleQuick } from './BibleQuick'
import { Splitter } from './Splitter'
import { normalize } from './lyrics'
import { matches } from './shortcuts'

type Props = { scenes: LyricScene[]; comp: Composition | null; service: ServiceState }

type Sel = { verse: number; verseEnd: number }

const remembered = (() => {
  try { return JSON.parse(localStorage.getItem('bible-pos') ?? 'null') as { book: string; chapter: number } | null } catch { return null }
})()

/**
 * Aba Bíblia (no estilo do Holyrics): digita a referência ("jo 3 16") ou palavras para pesquisar,
 * navega por livro/capítulo, compara versões lado a lado e projeta no telão com o estilo das letras.
 */
export function BiblePage({ scenes, comp, service }: Props) {
  const [installed, setInstalled] = useState<BibleVersion[]>([])
  const [downloading, setDownloading] = useState<string[]>([])
  const [yvKey, setYvKey] = useState(false)
  const [settings, setSettings] = useState<BibleSettings | null>(null)
  const [live, setLive] = useState<BiblePassage | null>(null)
  const [books, setBooks] = useState<BibleBookInfo[]>([])
  const [book, setBook] = useState(remembered?.book ?? 'JHN')
  const [chapter, setChapter] = useState(remembered?.chapter ?? 1)
  const [texts, setTexts] = useState<Record<string, string[] | null>>({})
  const [sel, setSel] = useState<Sel | null>(null)
  const [input, setInput] = useState('')
  const [search, setSearch] = useState<{ query: string; total: number; hits: BibleHit[] } | null>(null)
  const [catalogOpen, setCatalogOpen] = useState(false)
  const [catalog, setCatalog] = useState<BibleVersion[] | null>(null)
  const [catalogError, setCatalogError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const off = onMessage((msg) => {
      switch (msg.type) {
        case 'bibleState':
          setInstalled(msg.installed); setDownloading(msg.downloading); setSettings(msg.settings); setLive(msg.live); setYvKey(msg.youVersionKey)
          break
        case 'bibleBooks': setBooks(msg.books); break
        case 'bibleChapter': setTexts(msg.texts); break
        case 'bibleSearch': setSearch(msg); break
        case 'bibleCatalog': setCatalog(msg.list); setCatalogError(msg.error ?? null); break
      }
    })
    send({ type: 'bibleHello' })
    inputRef.current?.focus()
    return off
  }, [])

  const main = settings?.mainVersion ?? null
  const versionsShown = useMemo(() => {
    if (!settings || !main) return []
    return [main, ...settings.compare.filter((v) => v !== main && installed.some((i) => i.id === v))]
  }, [settings, main, installed])

  useEffect(() => { if (main) send({ type: 'bibleBooks', version: main }) }, [main])
  useEffect(() => {
    if (versionsShown.length) send({ type: 'bibleChapter', book, chapter, versions: versionsShown })
    try { localStorage.setItem('bible-pos', JSON.stringify({ book, chapter })) } catch { /* sem armazenamento */ }
  }, [book, chapter, versionsShown])

  const bookInfo = books.find((b) => b.id === book)
  const verses = (main && texts[main]) || []
  const info = (id: string) => installed.find((v) => v.id === id)

  const updateSettings = (patch: Partial<BibleSettings>) => {
    if (!settings) return
    const s = { ...settings, ...patch }
    setSettings(s)
    send({ type: 'bibleSettings', settings: s })
  }

  const go = (b: string, c: number, v?: number, vEnd?: number) => {
    setBook(b); setChapter(c); setSearch(null)
    setSel(v ? { verse: v, verseEnd: vEnd ?? v } : null)
  }
  const project = (b: string, c: number, v: number, vEnd?: number) =>
    send({ type: 'bibleShow', book: b, chapter: c, verse: v, verseEnd: vEnd ?? v })

  // rola até o versículo selecionado
  useEffect(() => {
    if (!sel) return
    listRef.current?.querySelector(`[data-verse="${sel.verse}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [sel, texts])

  const parsed = useMemo(() => (input.trim() ? parseRef(input, books) : null), [input, books])
  const parsedName = parsed ? books.find((b) => b.id === parsed.book)?.name ?? parsed.book : null

  const submit = () => {
    if (!main) return
    if (parsed) {
      go(parsed.book, parsed.chapter, parsed.verse ?? 1, parsed.verseEnd)
      if (parsed.verse) project(parsed.book, parsed.chapter, parsed.verse, parsed.verseEnd)
      setInput('')
    } else if (input.trim().length >= 2) {
      send({ type: 'bibleSearch', version: main, query: input.trim() })
    }
  }

  // teclado na lista: ↑↓ escolhe, Enter projeta, → / ← / PageDown / PageUp passam o versículo no telão
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || catalogOpen) return
      if (matches(e, 'biblia.abaixo') || matches(e, 'biblia.acima')) {
        e.preventDefault()
        const d = matches(e, 'biblia.abaixo') ? 1 : -1
        const v = Math.min(Math.max(1, (sel?.verse ?? 0) + d), verses.length || 1)
        setSel({ verse: v, verseEnd: v })
      } else if (matches(e, 'biblia.projetar') && sel) {
        e.preventDefault()
        project(book, chapter, sel.verse, sel.verseEnd)
      } else if (matches(e, 'biblia.proximo') && live) {
        e.preventDefault(); send({ type: 'lyricsStep', direction: 1 })
      } else if (matches(e, 'biblia.anterior') && live) {
        e.preventDefault(); send({ type: 'lyricsStep', direction: -1 })
      } else if (matches(e, 'biblia.buscar')) {
        e.preventDefault(); inputRef.current?.focus(); inputRef.current?.select()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [sel, verses.length, book, chapter, live, catalogOpen])

  // segue o telão: quando o versículo no ar muda (→ / ←), mostra ele aqui
  useEffect(() => {
    if (!live) return
    if (live.book !== book || live.chapter !== chapter) { setBook(live.book); setChapter(live.chapter) }
    setSel({ verse: live.verse, verseEnd: live.verseEnd })
  }, [live?.book, live?.chapter, live?.verse, live?.verseEnd])

  // áreas redimensionáveis e versículos em linhas ou em grade (lembrados neste computador)
  const bodyRef = useRef<HTMLDivElement>(null)
  const [vView, setVView] = useState<'list' | 'grid'>(() => { try { return localStorage.getItem('biblia.versiculos.modo') === 'grid' ? 'grid' : 'list' } catch { return 'list' } })
  const [vSize, setVSize] = useState(() => { try { return Math.min(480, Math.max(180, Number(localStorage.getItem('biblia.versiculos.tamanho')) || 280)) } catch { return 280 } })
  const changeVView = (v: 'list' | 'grid') => { setVView(v); try { localStorage.setItem('biblia.versiculos.modo', v) } catch { /* sem armazenamento */ } }
  // favoritos: guardados nas configurações da Bíblia ("JHN 3:16-16"), para achar rápido durante o culto
  const favorites = settings?.favorites ?? []
  const favKey = (b: string, c: number, v: number, vEnd: number) => `${b} ${c}:${v}-${vEnd}`
  const favOf = (v: number) => favorites.find((f) => { const m = /^(\S+) (\d+):(\d+)-(\d+)$/.exec(f); return !!m && m[1] === book && +m[2] === chapter && v >= +m[3] && v <= +m[4] })
  const toggleFav = (v: number) => {
    const existing = favOf(v)
    if (existing) { updateSettings({ favorites: favorites.filter((f) => f !== existing) }); return }
    // com vários versículos escolhidos (Shift+clique) e a estrela num deles, marca o trecho inteiro
    const range = sel && v >= sel.verse && v <= sel.verseEnd ? sel : { verse: v, verseEnd: v }
    updateSettings({ favorites: [favKey(book, chapter, range.verse, range.verseEnd), ...favorites] })
  }
  const [favFilter, setFavFilter] = useState('')
  // manda o trecho escolhido para o roteiro do culto aberto (entra no fim)
  const [sentToCulto, setSentToCulto] = useState<string | null>(null)
  const sendToCulto = (b: string, c: number, v: number, vEnd: number) => {
    const name = books.find((x) => x.id === b)?.name ?? b
    const title = refLabel(name, c, v, vEnd)
    const item = { id: Math.random().toString(16).slice(2, 12), kind: 'bible' as const, title, book: b, chapter: c, verse: v, verseEnd: vEnd }
    send({ type: 'serviceSave', plan: { name: service.name, date: service.date, items: [...service.items, item] } })
    setSentToCulto(title)
    window.setTimeout(() => setSentToCulto((t) => (t === title ? null : t)), 3000)
  }
  const vStyle = { '--verse-w': `${vSize}px`, '--verse-font': `${Math.round(13 + (vSize - 180) / 30)}px` } as React.CSSProperties
  const changeVSize = (v: number) => { setVSize(v); try { localStorage.setItem('biblia.versiculos.tamanho', String(v)) } catch { /* sem armazenamento */ } }

  // digitação rápida: começar a digitar em qualquer lugar da aba abre a janela de livro/capítulo/versículo
  const [quick, setQuick] = useState<string | null>(null)
  useEffect(() => {
    if (quick !== null || catalogOpen) return
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement
      if (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable) return
      if (e.ctrlKey || e.altKey || e.metaKey || e.repeat || document.querySelector('.modal-back')) return
      if (!/^[\p{L}]$/u.test(e.key) && !/^[1-3]$/.test(e.key)) return // letras, ou 1/2/3 (1 Coríntios, 2 Reis...)
      e.preventDefault(); e.stopImmediatePropagation()
      setQuick(e.key)
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [quick, catalogOpen])

  const clickVerse = (v: number, e: React.MouseEvent) => {
    // clicar no versículo que já está escolhido confirma: vai para o telão
    if (!e.shiftKey && sel && sel.verse === v && sel.verseEnd === v && !liveHere(v)) { project(book, chapter, v); return }
    if (!e.shiftKey && sel && sel.verseEnd > sel.verse && v >= sel.verse && v <= sel.verseEnd) { project(book, chapter, sel.verse, sel.verseEnd); return }
    if (e.shiftKey && sel) setSel({ verse: Math.min(sel.verse, v), verseEnd: Math.max(sel.verseEnd, v) })
    else setSel({ verse: v, verseEnd: v })
  }

  if (!settings) return <div className="meta">Conectando ao motor…</div>

  // a Bíblia sai pelo mesmo clip "Letras" da grade
  const lyricsPlaying = !!comp?.layers.some((l) => !!l.playing && l.opacity > 0 && l.playing.kind === 'lyrics')
  const lyricsCell = (() => {
    for (let l = (comp?.layers.length ?? 0) - 1; l >= 0; l--) {
      const c = comp!.layers[l].clips.findIndex((x) => x?.kind === 'lyrics')
      if (c >= 0) return { layer: l, column: c }
    }
    return null
  })()
  const playLyricsClip = () => {
    if (!comp) return
    if (lyricsCell) send({ type: 'trigger', ...lyricsCell })
    else send({ type: 'lyricsToGrid', layer: comp.layers.length - 1, column: comp.columns - 1 })
  }

  const openCatalog = () => { setCatalogOpen(true); if (!catalog) send({ type: 'bibleCatalog' }) }

  if (installed.length === 0 && !catalogOpen) {
    return (
      <div className="bible-empty">
        <h2>Nenhuma versão da Bíblia instalada</h2>
        <p>As versões são baixadas uma vez e depois funcionam sem internet.</p>
        <div className="row">
          <button className="primary" disabled={downloading.length > 0} onClick={() => send({ type: 'bibleDownload', id: 'por_blj' })}>
            {downloading.length ? 'Baixando…' : 'Baixar a Bíblia Livre (português)'}
          </button>
          <button onClick={openCatalog}>Ver todas as versões…</button>
        </div>
      </div>
    )
  }

  const ot = books.filter((b) => books.findIndex((x) => x.id === NT_START) < 0 || books.indexOf(b) < books.findIndex((x) => x.id === NT_START))
  const nt = books.filter((b) => !ot.includes(b))
  const liveHere = (v: number) => live && live.book === book && live.chapter === chapter && v >= live.verse && v <= live.verseEnd
  const selected = (v: number) => sel && v >= sel.verse && v <= sel.verseEnd
  const onScreen = settings.onScreen.length ? settings.onScreen : main ? [main] : []
  const toggleOnScreen = (id: string) => {
    const list = onScreen.includes(id) ? onScreen.filter((x) => x !== id) : [...onScreen, id]
    updateSettings({ onScreen: list.length ? list : main ? [main] : [] })
  }

  return (
    <div className="bible">
      {quick !== null && <BibleQuick books={books} start={quick} onClose={() => setQuick(null)}
        onDone={(b, c, v, vEnd) => { setQuick(null); go(b, c, v, vEnd) }} />}
      <div className="bible-top">
        <div className="bible-search">
          <input ref={inputRef} value={input} onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') submit(); if (e.key === 'Escape') setInput('') }}
            placeholder="Digite a referência (jo 3 16 · sl 23 · 1co 13 4-7) ou palavras para pesquisar e tecle Enter" />
          <span className="meta bible-hint">
            {input.trim()
              ? parsed ? `Enter: ${refLabel(parsedName!, parsed.chapter, parsed.verse, parsed.verseEnd)}${parsed.verse ? ' (projeta)' : ''}` : 'Enter: pesquisar palavras'
              : 'Atalhos: / para buscar · ↑↓ escolhe · Enter projeta · → ← passa o versículo · Shift+clique seleciona vários'}
          </span>
        </div>
        <label className="bible-version">
          Versão
          <select value={main ?? ''} onChange={(e) => updateSettings({ mainVersion: e.target.value })}>
            {installed.map((v) => <option key={v.id} value={v.id}>{v.shortName} · {v.name}</option>)}
          </select>
        </label>
        <button onClick={openCatalog}>Versões…</button>
        <button className="danger" disabled={!live} onClick={() => send({ type: 'lyricsClear' })} title="Tira o versículo do telão e do retorno">Limpar telão</button>
      </div>

      <div className="bible-body" ref={bodyRef}>
        <section className="bible-books">
          <div className="bib-books-scroll">
          <div className="book-legend">{BOOK_GROUPS.map((g) => <span key={g.id} className={`bk-${g.id}`}>{g.name}</span>)}</div>
          <h3>Antigo Testamento</h3>
          <div className="book-grid">
            {ot.map((b) => (
              <button key={b.id} title={b.name} className={`small bk bk-${bookGroup(b.id)} ${b.id === book ? 'active' : ''}`} onClick={() => go(b.id, 1)}><span className="bk-ab">{shortName(b)}</span><span className="bk-nm">{b.name}</span></button>
            ))}
          </div>
          {nt.length > 0 && <h3>Novo Testamento</h3>}
          <div className="book-grid">
            {nt.map((b) => (
              <button key={b.id} title={b.name} className={`small bk bk-${bookGroup(b.id)} ${b.id === book ? 'active' : ''}`} onClick={() => go(b.id, 1)}><span className="bk-ab">{shortName(b)}</span><span className="bk-nm">{b.name}</span></button>
            ))}
          </div>
          </div>
          {/* capítulos e versículos sempre à vista (os livros rolam por cima), com números grandes */}
          {bookInfo && (
            <div className="bib-nav">
              <div className="bib-nav-block">
                <h3><span className={`bib-nav-book bk-${bookGroup(book)}`}>{bookInfo.name}</span> capítulo</h3>
                <div className="chapter-grid">
                  {bookInfo.chapters.map((_, i) => (
                    <button key={i} className={`num ${i + 1 === chapter ? 'active' : ''} ${live && live.book === book && live.chapter === i + 1 ? 'live' : ''}`} onClick={() => go(book, i + 1)}>{i + 1}</button>
                  ))}
                </div>
              </div>
              {!search && verses.length > 0 && (
                <div className="bib-nav-block">
                  <h3>{bookInfo.name} {chapter}: versículo</h3>
                  <div className="chapter-grid verse-grid">
                    {verses.map((t, i) => t && (
                      <button key={i} className={`num ${selected(i + 1) ? 'active' : ''} ${liveHere(i + 1) ? 'live' : ''}`}
                        title="Clique escolhe · clique de novo projeta" onClick={(e) => clickVerse(i + 1, e as unknown as React.MouseEvent)}>{i + 1}</button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </section>

        <Splitter container={bodyRef} measure={() => bodyRef.current?.querySelector('.bible-books') ?? null} cssVar="--bib-left" storageKey="tamanho.biblia.esquerda" dir={1} min={220}
          max={(w) => Math.min(640, w - (bodyRef.current?.querySelector('.bible-side')?.getBoundingClientRect().width ?? 0) - 380)} />
        <section className="bible-verses">
          {search ? (
            <>
              <div className="bible-head">
                <h2>“{search.query}”: {search.total} versículo(s){search.total > search.hits.length ? `, mostrando ${search.hits.length}` : ''}</h2>
                <button className="small" onClick={() => setSearch(null)}>Voltar ao capítulo</button>
              </div>
              <div className="slide-view">
                <span className="seg">
                  <button className={`small ${vView === 'list' ? 'active' : ''}`} onClick={() => changeVView('list')} title="Um versículo por linha">☰ Linhas</button>
                  <button className={`small ${vView === 'grid' ? 'active' : ''}`} onClick={() => changeVView('grid')} title="Versículos em quadros, lado a lado">▦ Grade</button>
                </span>
                <label title={vView === 'grid' ? 'Largura de cada quadro e tamanho do texto' : 'Tamanho do texto'}>
                  Tamanho
                  <button className="small" onClick={() => changeVSize(Math.max(180, vSize - 20))} disabled={vSize <= 180}>−</button>
                  <input type="range" min={180} max={480} step={10} value={vSize} onChange={(e) => changeVSize(Number(e.target.value))} />
                  <button className="small" onClick={() => changeVSize(Math.min(480, vSize + 20))} disabled={vSize >= 480}>+</button>
                </label>
              </div>
              <div className={`verse-list ${vView === 'grid' ? 'as-grid' : ''}`} style={vStyle}>
                {search.hits.map((h) => (
                  <div key={`${h.book}${h.chapter}:${h.verse}`} className="verse hit"
                    onClick={() => go(h.book, h.chapter, h.verse)}
                    onDoubleClick={() => { go(h.book, h.chapter, h.verse); project(h.book, h.chapter, h.verse) }}>
                    <b>{refLabel(h.bookName, h.chapter, h.verse)}</b> <Highlight text={h.text} query={search.query} />
                  </div>
                ))}
                {search.total === 0 && <p className="meta">Nada encontrado nesta versão.</p>}
              </div>
            </>
          ) : (
            <>
              <div className="bible-head">
                <button className="small" disabled={chapter <= 1} onClick={() => go(book, chapter - 1)}>◀</button>
                <h2>{bookInfo?.name ?? book} {chapter} <span className="meta">{info(main ?? '')?.shortName}</span></h2>
                <button className="small" disabled={!bookInfo || chapter >= bookInfo.chapters.length} onClick={() => go(book, chapter + 1)}>▶</button>
                <span className="meta">Digite o livro em qualquer lugar (ex.: jos 3 5) · clique escolhe · clique de novo projeta</span>
              </div>
              <div className="slide-view">
                <span className="seg">
                  <button className={`small ${vView === 'list' ? 'active' : ''}`} onClick={() => changeVView('list')} title="Um versículo por linha">☰ Linhas</button>
                  <button className={`small ${vView === 'grid' ? 'active' : ''}`} onClick={() => changeVView('grid')} title="Versículos em quadros, lado a lado">▦ Grade</button>
                </span>
                <label title={vView === 'grid' ? 'Largura de cada quadro e tamanho do texto' : 'Tamanho do texto'}>
                  Tamanho
                  <button className="small" onClick={() => changeVSize(Math.max(180, vSize - 20))} disabled={vSize <= 180}>−</button>
                  <input type="range" min={180} max={480} step={10} value={vSize} onChange={(e) => changeVSize(Number(e.target.value))} />
                  <button className="small" onClick={() => changeVSize(Math.min(480, vSize + 20))} disabled={vSize >= 480}>+</button>
                </label>
              </div>
              <div className={`verse-list ${vView === 'grid' ? 'as-grid' : ''}`} ref={listRef} style={vStyle}>
                {verses.map((t, i) => t && (
                  <div key={i} data-verse={i + 1}
                    className={`verse ${selected(i + 1) ? 'selected' : ''} ${liveHere(i + 1) ? 'live' : ''}`}
                    onClick={(e) => clickVerse(i + 1, e)}
                    >
                    <button className={`fav-star ${favOf(i + 1) ? 'on' : ''}`} title={favOf(i + 1) ? 'Tirar dos favoritos' : 'Marcar como favorito'}
                      onClick={(e) => { e.stopPropagation(); toggleFav(i + 1) }}>{favOf(i + 1) ? '★' : '☆'}</button>
                    <span className="vnum">{i + 1}</span> {t}
                  </div>
                ))}
                {verses.length === 0 && <p className="meta">{info(main ?? '')?.provider === 'youversion' ? 'Buscando na YouVersion…' : 'Este livro não existe nesta versão.'}</p>}
              </div>
              {info(main ?? '')?.copyright && <p className="meta copyright">{info(main ?? '')!.copyright}</p>}
            </>
          )}
        </section>

        <Splitter container={bodyRef} measure={() => bodyRef.current?.querySelector('.bible-side') ?? null} cssVar="--bib-right" storageKey="tamanho.biblia.direita" dir={-1} min={300}
          max={(w) => Math.min(640, w - (bodyRef.current?.querySelector('.bible-books')?.getBoundingClientRect().width ?? 0) - 380)} />
        <section className="bible-side">
          <SlidePreview label={sel ? refLabel(bookInfo?.name ?? book, chapter, sel.verse, sel.verseEnd) : 'Escolha um versículo'}
            request={sel ? { kind: 'bible', book, chapter, verse: sel.verse, verseEnd: sel.verseEnd } : null} empty="Clique num versículo para ver aqui" />
          <div className="row">
            <button className="primary" disabled={!sel} onClick={() => sel && project(book, chapter, sel.verse, sel.verseEnd)}>
              Projetar {sel ? refLabel(bookInfo?.name ?? book, chapter, sel.verse, sel.verseEnd) : ''}
            </button>
          </div>
          <div className="row bible-actions">
            <button disabled={!sel} title={`Põe no fim do roteiro do culto aberto (${service.name})`} onClick={() => sel && sendToCulto(book, chapter, sel.verse, sel.verseEnd)}>+ Enviar para o Culto</button>
            <button disabled={!sel} className={sel && favOf(sel.verse) ? 'active' : ''} title="Marcar ou tirar dos favoritos" onClick={() => sel && toggleFav(sel.verse)}>{sel && favOf(sel.verse) ? '★ Favorito' : '☆ Favoritar'}</button>
          </div>
          {sentToCulto && <p className="ok-text">✓ {sentToCulto} entrou no fim do roteiro “{service.name}”</p>}
          <h3>★ Favoritos {favorites.length > 0 && <span className="meta">({favorites.length})</span>}</h3>
          {favorites.length === 0 ? (
            <p className="meta">Clique na estrela ☆ de um versículo (ou em “Favoritar”) para guardar aqui. Shift+clique escolhe um trecho inteiro.</p>
          ) : (
            <>
              {favorites.length > 8 && <input className="fav-filter" placeholder="Filtrar favoritos…" value={favFilter} onChange={(e) => setFavFilter(e.target.value)} />}
              <div className="fav-list">
                {favorites.map((f) => {
                  const m = /^(\S+) (\d+):(\d+)-(\d+)$/.exec(f)
                  if (!m) return null
                  const label = refLabel(books.find((b) => b.id === m[1])?.name ?? m[1], +m[2], +m[3], +m[4])
                  if (favFilter && !normalize(label).includes(normalize(favFilter))) return null
                  const here = m[1] === book && +m[2] === chapter && sel?.verse === +m[3] && sel?.verseEnd === +m[4]
                  return (
                    <div key={f} className={`fav-item ${here ? 'here' : ''}`}>
                      <button className="fav-go" title="Clique abre e escolhe · duplo clique projeta" onClick={() => go(m[1], +m[2], +m[3], +m[4])}
                        onDoubleClick={() => project(m[1], +m[2], +m[3], +m[4])}>{label}</button>
                      <button className="small" title="Enviar para o Culto" onClick={() => sendToCulto(m[1], +m[2], +m[3], +m[4])}>+ Culto</button>
                      <button className="small" title="Tirar dos favoritos" onClick={() => updateSettings({ favorites: favorites.filter((x) => x !== f) })}>✕</button>
                    </div>
                  )
                })}
              </div>
            </>
          )}
          {comp && !lyricsPlaying && (
            <div className="warn-box info">
              O clip <b>Letras</b> não está tocando. Ao projetar, o VJesus liga ele sozinho.
              <button className="small" onClick={playLyricsClip}>
                {lyricsCell ? 'Tocar o clip Letras' : 'Colocar na grade e tocar'}
              </button>
            </div>
          )}
          {live && <p className="onair-line"><span className="onair">NO AR</span> {refLabel(books.find((b) => b.id === live.book)?.name ?? live.book, live.chapter, live.verse, live.verseEnd)}</p>}

          <h3>Comparar versões</h3>
          <p className="meta">Marque “telão” para projetar mais de uma versão juntas.</p>
          <div className="compare">
            {installed.map((v) => {
              const inCompare = v.id === main || settings.compare.includes(v.id)
              const t = texts[v.id]
              const passage = sel && t ? t.slice(sel.verse - 1, sel.verseEnd).filter(Boolean).join(' ') : null
              return (
                <div key={v.id} className={`compare-item ${inCompare ? '' : 'off'}`}>
                  <div className="compare-head">
                    <label className="check" title="Mostrar nesta comparação">
                      <input type="checkbox" checked={inCompare} disabled={v.id === main}
                        onChange={(e) => updateSettings({ compare: e.target.checked ? [...settings.compare, v.id] : settings.compare.filter((x) => x !== v.id) })} />
                      <b>{v.shortName}</b> <span className="meta">{v.name}</span>
                    </label>
                    <label className="check" title="Projetar esta versão no telão">
                      <input type="checkbox" checked={onScreen.includes(v.id)} onChange={() => toggleOnScreen(v.id)} /> telão
                    </label>
                  </div>
                  {inCompare && <div className="compare-text">{passage ?? (sel ? <span className="meta">—</span> : <span className="meta">escolha um versículo</span>)}</div>}
                </div>
              )
            })}
          </div>

          <h3>No telão</h3>
          <div className="bible-style">
            <label>Estilo
              <select value={settings.sceneId ?? ''} onChange={(e) => updateSettings({ sceneId: e.target.value || null })}>
                <option value="">Estilo padrão das letras</option>
                {scenes.map((s) => <option key={s.id} value={s.id}>Cena: {s.name}</option>)}
              </select>
            </label>
            <label>Referência
              <select value={settings.reference} onChange={(e) => updateSettings({ reference: e.target.value as BibleSettings['reference'] })}>
                <option value="abaixo">Abaixo do texto</option>
                <option value="acima">Acima do texto</option>
                <option value="nenhuma">Não mostrar</option>
              </select>
            </label>
            <label>Tamanho da referência <output>{Math.round(settings.referenceScale * 100)}%</output>
              <input type="range" min={0.3} max={1} step={0.05} value={settings.referenceScale}
                onChange={(e) => updateSettings({ referenceScale: Number(e.target.value) })} />
            </label>
            <label>Cor da referência
              <ColorInput value={settings.referenceColor} onChange={(e) => updateSettings({ referenceColor: e.target.value })} />
            </label>
            <label className="check">
              <input type="checkbox" checked={settings.showVersion} onChange={(e) => updateSettings({ showVersion: e.target.checked })} />
              Mostrar a sigla da versão
            </label>
          </div>
          <p className="meta">Fonte, cor, contorno, transição e posição vêm do estilo escolhido (aba Letras). O texto diminui sozinho para caber.</p>

          {settings.history.length > 0 && (
            <>
              <h3>Recentes</h3>
              <div className="history">
                {settings.history.slice(0, 12).map((h) => {
                  const m = /^(\S+) (\d+):(\d+)-(\d+)$/.exec(h)
                  if (!m) return null
                  const name = books.find((b) => b.id === m[1])?.name ?? m[1]
                  return (
                    <button key={h} className="small" onClick={() => go(m[1], +m[2], +m[3], +m[4])} onDoubleClick={() => project(m[1], +m[2], +m[3], +m[4])}>
                      {refLabel(name, +m[2], +m[3], +m[4])}
                    </button>
                  )
                })}
              </div>
            </>
          )}
        </section>
      </div>

      {catalogOpen && (
        <Catalog catalog={catalog} error={catalogError} installed={installed} downloading={downloading} yvKey={yvKey} onClose={() => setCatalogOpen(false)} />
      )}
    </div>
  )
}

function Highlight({ text, query }: { text: string; query: string }) {
  const words = normalize(query).split(/\s+/).filter(Boolean)
  const folded = normalize(text)
  const marks = new Array(text.length).fill(false)
  for (const w of words) {
    let i = folded.indexOf(w)
    while (i >= 0) { for (let k = i; k < i + w.length; k++) marks[k] = true; i = folded.indexOf(w, i + w.length) }
  }
  const parts: { t: string; m: boolean }[] = []
  for (let i = 0; i < text.length; i++) {
    if (parts.length && parts[parts.length - 1].m === marks[i]) parts[parts.length - 1].t += text[i]
    else parts.push({ t: text[i], m: marks[i] })
  }
  return <>{parts.map((p, i) => (p.m ? <mark key={i}>{p.t}</mark> : <span key={i}>{p.t}</span>))}</>
}

function Catalog({ catalog, error, installed, downloading, yvKey, onClose }: {
  catalog: BibleVersion[] | null; error: string | null; installed: BibleVersion[]; downloading: string[]; yvKey: boolean; onClose: () => void
}) {
  const [lang, setLang] = useState('por')
  const [q, setQ] = useState('')
  const [full, setFull] = useState(true)
  const languages = useMemo(() => {
    const m = new Map<string, { name: string; count: number }>()
    for (const v of catalog ?? []) {
      const e = m.get(v.language) ?? { name: v.languageName || v.language, count: 0 }
      e.count++; m.set(v.language, e)
    }
    const first = ['por', 'eng', 'spa', 'heb', 'grc', 'lat']
    return [...m.entries()].sort((a, b) => {
      const ia = first.indexOf(a[0]), ib = first.indexOf(b[0])
      if (ia >= 0 || ib >= 0) return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib)
      return a[1].name.localeCompare(b[1].name)
    })
  }, [catalog])
  const list = (catalog ?? []).filter((v) =>
    (lang === '*' || v.language === lang) && (!full || v.books >= 27) &&
    (!q || normalize(`${v.name} ${v.shortName} ${v.languageName}`).includes(normalize(q))))
    .sort((a, b) => b.books - a.books || a.name.localeCompare(b.name))
  const has = (id: string) => installed.some((v) => v.id === id)

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="modal bible-catalog" onClick={(e) => e.stopPropagation()}>
        <div className="bible-head">
          <h2>Versões da Bíblia</h2>
          <button className="small" onClick={onClose}>Fechar</button>
        </div>
        <p className="meta">
          Todas as versões livres (domínio público ou licença aberta) da Free Use Bible API, em {languages.length} idiomas.
          Baixe uma vez e use sem internet.
        </p>
        <YouVersionBox hasKey={yvKey} installed={installed} downloading={downloading} />
        <ImportBox />
        {installed.length > 0 && (
          <>
            <h3>Instaladas</h3>
            <div className="catalog-list">
              {installed.map((v) => (
                <div key={v.id} className="catalog-item">
                  <span><b>{v.shortName}</b> {v.name} <span className="meta">{v.provider === 'youversion' ? 'YouVersion (online)' : v.languageName}</span></span>
                  <button className="small danger" onClick={() => send({ type: 'bibleDelete', id: v.id })}>Remover</button>
                </div>
              ))}
            </div>
          </>
        )}
        <div className="row catalog-filter">
          <select value={lang} onChange={(e) => setLang(e.target.value)}>
            <option value="*">Todos os idiomas</option>
            {languages.map(([code, l]) => <option key={code} value={code}>{l.name} ({l.count})</option>)}
          </select>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filtrar por nome" />
          <label className="check"><input type="checkbox" checked={full} onChange={(e) => setFull(e.target.checked)} /> só Bíblias completas ou NT completo</label>
        </div>
        {error && <p className="warn-text">{error}</p>}
        {!catalog && !error && <p className="meta">Carregando a lista…</p>}
        <div className="catalog-list scroll">
          {list.map((v) => (
            <div key={v.id} className="catalog-item">
              <span>
                <b>{v.shortName}</b> {v.name}{' '}
                <span className="meta">
                  {v.languageName} · {v.books >= 66 ? 'Bíblia completa' : v.books === 27 ? 'Novo Testamento' : `${v.books} livros`}
                  {' · '}{v.publicDomain ? 'domínio público' : 'licença livre'}
                </span>
              </span>
              {has(v.id)
                ? <span className="meta">instalada</span>
                : <button className="small" disabled={downloading.includes(v.id)} onClick={() => send({ type: 'bibleDownload', id: v.id })}>
                    {downloading.includes(v.id) ? 'Baixando…' : 'Baixar'}
                  </button>}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

/** Versões com direitos autorais (ARA, ARC, NAA, NVI, NVT, NTLH...) entram por arquivo que a pessoa já tem. */
function ImportBox() {
  const [name, setName] = useState('')
  const [short, setShort] = useState('')
  return (
    <div className="import-box">
      <h3>Importar de arquivo (ARC, ARA, NAA, NVI, NVT, NTLH…)</h3>
      <p className="meta">
        Essas versões têm direitos autorais e não podem vir junto com o programa. Se você tem o arquivo da versão
        (de um programa ou aplicativo em que ela foi adquirida ou licenciada), importe aqui. Formatos aceitos:
        MySword (.bbl.mybible), MyBible (.SQLite3), Zefania XML, OSIS XML e JSON.
      </p>
      <div className="row">
        <input value={short} onChange={(e) => setShort(e.target.value)} placeholder="Sigla (ex.: NAA)" style={{ width: 120 }} />
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Nome (ex.: Nova Almeida Atualizada)" style={{ flex: 1 }} />
        <button className="primary" onClick={() => send({ type: 'bibleImport', name, shortName: short })}>Escolher arquivo…</button>
      </div>
    </div>
  )
}

/** Versões pela API da YouVersion com a chave de aplicação da pessoa (texto buscado na hora, precisa de internet). */
function YouVersionBox({ hasKey, installed, downloading }: { hasKey: boolean; installed: BibleVersion[]; downloading: string[] }) {
  const [key, setKey] = useState('')
  const [editing, setEditing] = useState(!hasKey)
  const [lang, setLang] = useState('pt')
  const [list, setList] = useState<BibleVersion[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')

  useEffect(() => onMessage((msg) => {
    if (msg.type === 'bibleYvVersions') { setList(msg.list); setError(msg.error ?? null) }
  }), [])
  useEffect(() => { if (hasKey) { setList(null); send({ type: 'bibleYvVersions', language: lang }) } }, [hasKey, lang])
  useEffect(() => { if (hasKey) setEditing(false) }, [hasKey])

  const has = (id: string) => installed.some((v) => v.id === id)
  const shown = (list ?? []).filter((v) => !q || normalize(`${v.name} ${v.shortName}`).includes(normalize(q)))

  return (
    <div className="import-box yv-box">
      <h3>YouVersion (com a sua chave de aplicação)</h3>
      {editing ? (
        <div className="row">
          <input type="password" value={key} onChange={(e) => setKey(e.target.value)} placeholder="Cole a App Key do portal da YouVersion" style={{ flex: 1 }} />
          <button className="primary" disabled={!key.trim()} onClick={() => { send({ type: 'bibleYvKey', key: key.trim(), language: lang }); setKey('') }}>Salvar chave</button>
          {hasKey && <button className="small" onClick={() => setEditing(false)}>Cancelar</button>}
        </div>
      ) : (
        <>
          <div className="row catalog-filter">
            <span className="meta">Chave salva (criptografada neste PC).</span>
            <button className="small" onClick={() => setEditing(true)}>Trocar chave</button>
            <button className="small danger-text" onClick={() => { if (window.confirm('Apagar a chave da YouVersion deste PC?')) send({ type: 'bibleYvKey', key: '' }) }}>Apagar chave</button>
            <span className="spacer" />
            <select value={lang} onChange={(e) => setLang(e.target.value)}>
              <option value="pt">Português</option>
              <option value="en">Inglês</option>
              <option value="es">Espanhol</option>
              <option value="*">Todos os idiomas</option>
            </select>
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filtrar" />
          </div>
          {error && <p className="warn-text">{error}</p>}
          {!list && !error && <p className="meta">Carregando as versões liberadas para a sua chave…</p>}
          {list && list.length === 0 && !error && <p className="meta">Nenhuma versão liberada para a sua chave neste idioma.</p>}
          <div className="catalog-list yv-list">
            {shown.map((v) => (
              <div key={v.id} className="catalog-item">
                <span><b>{v.shortName}</b> {v.name} <span className="meta">{v.languageName}{v.copyright ? ` · ${v.copyright.slice(0, 90)}` : ''}</span></span>
                {has(v.id)
                  ? <span className="meta">adicionada</span>
                  : <button className="small" disabled={downloading.includes(v.id)} onClick={() => send({ type: 'bibleYvAdd', id: v.id })}>
                      {downloading.includes(v.id) ? 'Adicionando…' : 'Adicionar'}
                    </button>}
              </div>
            ))}
          </div>
          <p className="meta">O texto vem da YouVersion na hora em que você abre o capítulo (precisa de internet); a pesquisa por palavras funciona só nas versões baixadas.</p>
        </>
      )}
    </div>
  )
}

/** Cores dos livros por grupo (divisão clássica da Bíblia, com tons próprios do VJesus). Os ids são os códigos USFM (GEN, EXO...). */
const BOOK_GROUPS = [
  { id: 'pent', name: 'Pentateuco', books: 'GEN EXO LEV NUM DEU' },
  { id: 'hist', name: 'Históricos', books: 'JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST' },
  { id: 'poet', name: 'Poéticos e sapienciais', books: 'JOB PSA PRO ECC SNG' },
  { id: 'pmai', name: 'Profetas maiores', books: 'ISA JER LAM EZK DAN' },
  { id: 'pmen', name: 'Profetas menores', books: 'HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL' },
  { id: 'evan', name: 'Evangelhos', books: 'MAT MRK LUK JHN' },
  { id: 'atos', name: 'Atos', books: 'ACT' },
  { id: 'paul', name: 'Cartas de Paulo', books: 'ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM' },
  { id: 'gera', name: 'Cartas gerais', books: 'HEB JAS 1PE 2PE 1JN 2JN 3JN JUD' },
  { id: 'apoc', name: 'Apocalipse', books: 'REV' },
]
const GROUP_OF = new Map(BOOK_GROUPS.flatMap((g) => g.books.split(' ').map((b) => [b, g.id] as const)))
const bookGroup = (id: string) => GROUP_OF.get(id.toUpperCase()) ?? 'outro'