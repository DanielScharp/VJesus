import { useEffect, useMemo, useRef, useState } from 'react'
import {
  onMessage, send, type Composition, type BibleBookInfo, type BibleHit, type BiblePassage, type BibleSettings, type BibleVersion, type LyricScene,
} from './bridge'
import { NT_START, parseRef, refLabel, shortName } from './bible'
import { normalize } from './lyrics'

type Props = { scenes: LyricScene[]; comp: Composition | null }

type Sel = { verse: number; verseEnd: number }

const remembered = (() => {
  try { return JSON.parse(localStorage.getItem('bible-pos') ?? 'null') as { book: string; chapter: number } | null } catch { return null }
})()

/**
 * Aba Bíblia (no estilo do Holyrics): digita a referência ("jo 3 16") ou palavras para pesquisar,
 * navega por livro/capítulo, compara versões lado a lado e projeta no telão com o estilo das letras.
 */
export function BiblePage({ scenes, comp }: Props) {
  const [installed, setInstalled] = useState<BibleVersion[]>([])
  const [downloading, setDownloading] = useState<string[]>([])
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
          setInstalled(msg.installed); setDownloading(msg.downloading); setSettings(msg.settings); setLive(msg.live)
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
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault()
        const d = e.key === 'ArrowDown' ? 1 : -1
        const v = Math.min(Math.max(1, (sel?.verse ?? 0) + d), verses.length || 1)
        setSel({ verse: v, verseEnd: v })
      } else if (e.key === 'Enter' && sel) {
        e.preventDefault()
        project(book, chapter, sel.verse, sel.verseEnd)
      } else if (['ArrowRight', 'PageDown', ' '].includes(e.key) && live) {
        e.preventDefault(); send({ type: 'lyricsStep', direction: 1 })
      } else if (['ArrowLeft', 'PageUp'].includes(e.key) && live) {
        e.preventDefault(); send({ type: 'lyricsStep', direction: -1 })
      } else if (e.key === '/' || (e.ctrlKey && e.key.toLowerCase() === 'f')) {
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

  const clickVerse = (v: number, e: React.MouseEvent) => {
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

      <div className="bible-body">
        <section className="bible-books">
          <h3>Antigo Testamento</h3>
          <div className="book-grid">
            {ot.map((b) => (
              <button key={b.id} title={b.name} className={`small ${b.id === book ? 'active' : ''}`} onClick={() => go(b.id, 1)}>{shortName(b)}</button>
            ))}
          </div>
          {nt.length > 0 && <h3>Novo Testamento</h3>}
          <div className="book-grid">
            {nt.map((b) => (
              <button key={b.id} title={b.name} className={`small ${b.id === book ? 'active' : ''}`} onClick={() => go(b.id, 1)}>{shortName(b)}</button>
            ))}
          </div>
          {bookInfo && (
            <>
              <h3>{bookInfo.name}: capítulos</h3>
              <div className="chapter-grid">
                {bookInfo.chapters.map((_, i) => (
                  <button key={i} className={`small ${i + 1 === chapter ? 'active' : ''}`} onClick={() => go(book, i + 1)}>{i + 1}</button>
                ))}
              </div>
            </>
          )}
        </section>

        <section className="bible-verses">
          {search ? (
            <>
              <div className="bible-head">
                <h2>“{search.query}”: {search.total} versículo(s){search.total > search.hits.length ? `, mostrando ${search.hits.length}` : ''}</h2>
                <button className="small" onClick={() => setSearch(null)}>Voltar ao capítulo</button>
              </div>
              <div className="verse-list">
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
                <span className="meta">Clique escolhe · duplo clique projeta</span>
              </div>
              <div className="verse-list" ref={listRef}>
                {verses.map((t, i) => t && (
                  <div key={i} data-verse={i + 1}
                    className={`verse ${selected(i + 1) ? 'selected' : ''} ${liveHere(i + 1) ? 'live' : ''}`}
                    onClick={(e) => clickVerse(i + 1, e)}
                    onDoubleClick={() => { setSel({ verse: i + 1, verseEnd: i + 1 }); project(book, chapter, i + 1) }}>
                    <span className="vnum">{i + 1}</span> {t}
                  </div>
                ))}
                {verses.length === 0 && <p className="meta">Este livro não existe nesta versão.</p>}
              </div>
            </>
          )}
        </section>

        <section className="bible-side">
          <div className="row">
            <button className="primary" disabled={!sel} onClick={() => sel && project(book, chapter, sel.verse, sel.verseEnd)}>
              Projetar {sel ? refLabel(bookInfo?.name ?? book, chapter, sel.verse, sel.verseEnd) : ''}
            </button>
          </div>
          {comp && !lyricsPlaying && (
            <div className="warn-box">
              Para o versículo aparecer no telão, o clip <b>Letras</b> precisa estar tocando na grade.
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
              <input type="color" value={settings.referenceColor} onChange={(e) => updateSettings({ referenceColor: e.target.value })} />
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
        <Catalog catalog={catalog} error={catalogError} installed={installed} downloading={downloading} onClose={() => setCatalogOpen(false)} />
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

function Catalog({ catalog, error, installed, downloading, onClose }: {
  catalog: BibleVersion[] | null; error: string | null; installed: BibleVersion[]; downloading: string[]; onClose: () => void
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
        <ImportBox />
        {installed.length > 0 && (
          <>
            <h3>Instaladas</h3>
            <div className="catalog-list">
              {installed.map((v) => (
                <div key={v.id} className="catalog-item">
                  <span><b>{v.shortName}</b> {v.name} <span className="meta">{v.languageName}</span></span>
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
