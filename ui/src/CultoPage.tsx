import { useEffect, useMemo, useState } from 'react'
import { onMessage, send, type BibleBookInfo, type Composition, type LyricsState, type OverlayState, type PreviewRequest, type ServiceItem, type ServiceKind, type ServiceState, type Song, type StageTimer } from './bridge'
import { parseRef, refLabel } from './bible'
import { normalize, splitSlides, stripTags } from './lyrics'
import { formatTime, OverlayPanel } from './OverlayPanel'
import { isTyping, matches } from './shortcuts'
import { SlidePreview } from './SlidePreview'

const newId = () => Math.random().toString(16).slice(2, 12)

const KIND_LABEL: Record<ServiceKind, string> = {
  song: 'Música', bible: 'Bíblia', text: 'Texto', clip: 'Clip', column: 'Coluna', notice: 'Aviso', timer: 'Contagem', clear: 'Encerrar',
}

type Props = {
  service: ServiceState
  songs: Song[]
  comp: Composition | null
  state: LyricsState
  preview: string | null
  thumbs: Record<string, string>
  overlay: OverlayState | null
  timer: StageTimer | null
  fonts: string[]
}

/** O que o item mostra primeiro, para a prévia. */
function itemPreview(item: ServiceItem | undefined): PreviewRequest | null {
  if (!item) return null
  switch (item.kind) {
    case 'song': return item.songId ? { kind: 'song', songId: item.songId, slide: 0 } : null
    case 'bible': return { kind: 'bible', book: item.book ?? '', chapter: item.chapter ?? 1, verse: item.verse ?? 1, verseEnd: item.verse ?? 1 }
    case 'text': return item.text ? { kind: 'text', text: item.text } : null
    case 'notice': return item.text ? { kind: 'notice', text: item.text } : null
    case 'timer': return { kind: 'timer', seconds: item.seconds ?? 300 }
    default: return null
  }
}

/**
 * Roteiro do culto: a lista em ordem. Uma tecla avança tudo: dentro da música passa o slide e, no
 * fim dela, entra o próximo item. A prévia mostra o que vem a seguir antes de ir ao ar.
 */
export function CultoPage({ service, songs, comp, state, preview, thumbs, overlay, timer, fonts }: Props) {
  const { items, current } = service
  const [view, setView] = useState<'roteiro' | 'telao'>('roteiro')
  const [addTab, setAddTab] = useState<'songs' | 'bible' | 'clips' | 'other'>('songs')
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [dragging, setDragging] = useState<string | null>(null)
  const [refText, setRefText] = useState('')
  const [text, setText] = useState('')
  const [minutes, setMinutes] = useState(5)
  const [books, setBooks] = useState<BibleBookInfo[]>([])
  const [mainVersion, setMainVersion] = useState<string | null | undefined>(undefined)

  const save = (next: ServiceItem[], name = service.name) => send({ type: 'serviceSave', plan: { name, items: next } })
  const add = (item: Omit<ServiceItem, 'id'>) => {
    const created = { ...item, id: newId() }
    // entra depois do item selecionado, ou no fim
    const at = selected ? items.findIndex((i) => i.id === selected) + 1 : items.length
    save([...items.slice(0, at), created, ...items.slice(at)])
    setSelected(created.id)
  }
  const move = (id: string, to: number) => {
    const from = items.findIndex((i) => i.id === id)
    if (from < 0 || to < 0 || to >= items.length) return
    const list = [...items]
    const [it] = list.splice(from, 1)
    list.splice(to, 0, it)
    save(list)
  }
  const patch = (id: string, p: Partial<ServiceItem>) => save(items.map((i) => (i.id === id ? { ...i, ...p } : i)))

  // livros da versão principal, para entender "jo 3 16"
  useEffect(() => {
    const off = onMessage((msg) => {
      if (msg.type === 'bibleState') setMainVersion(msg.settings.mainVersion)
      else if (msg.type === 'bibleBooks') setBooks(msg.books)
    })
    send({ type: 'bibleHello' })
    return off
  }, [])
  useEffect(() => { if (mainVersion) send({ type: 'bibleBooks', version: mainVersion }) }, [mainVersion])
  const parsed = useMemo(() => (refText.trim() ? parseRef(refText, books) : null), [refText, books])
  const parsedName = parsed ? books.find((b) => b.id === parsed.book)?.name ?? parsed.book : ''

  const curIndex = items.findIndex((i) => i.id === current)
  const curItem = items[curIndex]
  const curSong = curItem?.kind === 'song' ? songs.find((s) => s.id === curItem.songId) : undefined
  const curSlides = useMemo(() => (curSong ? splitSlides(curSong.text) : []), [curSong])
  const onSong = !!curSong && state.songId === curSong.id
  const hasNextSlide = onSong && state.slide + 1 < curSlides.length
  const nextVerse = curItem?.kind === 'bible' && service.verse < (curItem.verseEnd ?? 0) ? service.verse + 1 : null
  const nextItem = curIndex < 0 ? items[0] : items[curIndex + 1]
  const selItem = items.find((i) => i.id === selected)

  // prévia: o item escolhido na lista; sem escolha, o que entra ao apertar "próximo"
  const showSelected = !!selItem && selItem.id !== current
  const nextRequest: PreviewRequest | null = showSelected ? itemPreview(selItem)
    : hasNextSlide ? { kind: 'song', songId: curSong!.id, slide: state.slide + 1 }
      : nextVerse ? { kind: 'bible', book: curItem!.book ?? '', chapter: curItem!.chapter ?? 1, verse: nextVerse, verseEnd: nextVerse }
        : itemPreview(nextItem)
  const nextLabel = showSelected ? selItem!.title
    : hasNextSlide ? `${curSong!.title}, slide ${state.slide + 2}`
      : nextVerse ? `${curItem!.title}, versículo ${nextVerse}`
        : nextItem?.title ?? 'Fim do roteiro'
  const previewItem = showSelected ? selItem : hasNextSlide || nextVerse ? undefined : nextItem
  const clipThumb = (() => {
    if (previewItem?.kind !== 'clip' || !comp) return undefined
    if (previewItem.deckId && comp.decks[comp.currentDeck]?.id !== previewItem.deckId) return undefined
    const path = comp.layers[previewItem.layer ?? 0]?.clips[previewItem.column ?? 0]?.path
    return path ? thumbs[path] : undefined
  })()

  // teclas (configuráveis em Menu → Atalhos de teclado)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (isTyping(e) || view !== 'roteiro') return
      if (matches(e, 'culto.proximo')) { e.preventDefault(); send({ type: 'serviceStep', direction: 1 }); setSelected(null) }
      else if (matches(e, 'culto.anterior')) { e.preventDefault(); send({ type: 'serviceStep', direction: -1 }); setSelected(null) }
      else if (matches(e, 'culto.branco')) { e.preventDefault(); send({ type: 'lyricsBlank' }) }
      else if (matches(e, 'culto.ir') && selected) { e.preventDefault(); send({ type: 'serviceGo', id: selected }); setSelected(null) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [view, selected])

  const filtered = useMemo(() => {
    const q = normalize(query.trim())
    return q ? songs.filter((s) => normalize(`${s.title} ${s.artist} ${stripTags(s.text)}`).includes(q)) : songs
  }, [songs, query])

  const deckId = comp?.decks[comp.currentDeck]?.id ?? null
  const columnName = (c: number) => comp?.columnNames?.[c] || `Coluna ${c + 1}`
  const missing = (i: ServiceItem) => i.kind === 'song' && !songs.some((s) => s.id === i.songId)
  const detail = (i: ServiceItem) => {
    if (i.kind === 'song') {
      const s = songs.find((x) => x.id === i.songId)
      return s ? `${splitSlides(s.text).length} slides${s.artist ? ` · ${s.artist}` : ''}` : 'Música não está mais na biblioteca'
    }
    if (i.kind === 'notice' || i.kind === 'text') return stripTags(i.text ?? '').split('\n')[0]
    if (i.kind === 'timer') return formatTime(i.seconds ?? 0)
    if (i.kind === 'clear') return 'Tira a letra, a contagem e o aviso do telão'
    return ''
  }

  const lyricsMissing = !comp?.layers.some((l) => l.clips.some((c) => c?.kind === 'lyrics')) && items.some((i) => ['song', 'bible', 'text'].includes(i.kind))

  return (
    <div className="culto">
      <div className="culto-switch row">
        <button className={view === 'roteiro' ? 'active' : ''} onClick={() => setView('roteiro')}>Roteiro</button>
        <button className={view === 'telao' ? 'active' : ''} onClick={() => setView('telao')}>Contagem e avisos{overlay && overlay.mode !== 'off' ? ' ●' : ''}</button>
      </div>

      {view === 'telao' && overlay && timer && <OverlayPanel overlay={overlay} timer={timer} fonts={fonts} />}

      {view === 'roteiro' && (
        <div className="culto-body">
          <section className="culto-add">
            <div className="row culto-tabs">
              {([['songs', 'Músicas'], ['bible', 'Bíblia'], ['clips', 'Clips'], ['other', 'Outros']] as const).map(([k, l]) => (
                <button key={k} className={`small ${addTab === k ? 'active' : ''}`} onClick={() => setAddTab(k)}>{l}</button>
              ))}
            </div>
            {addTab === 'songs' && (
              <>
                <input className="name" placeholder="Buscar música…" value={query} onChange={(e) => setQuery(e.target.value)} />
                <div className="song-list culto-pick">
                  {filtered.map((s) => (
                    <div key={s.id} className="song" title="Adicionar ao roteiro" onClick={() => add({ kind: 'song', title: s.title, songId: s.id })}>
                      <strong>{s.title}</strong>{s.artist && <span>{s.artist}</span>}
                    </div>
                  ))}
                  {filtered.length === 0 && <p className="meta">Nenhuma música. Importe ou busque na aba Letras.</p>}
                </div>
              </>
            )}
            {addTab === 'bible' && (
              mainVersion === null || (mainVersion !== undefined && books.length === 0)
                ? <p className="meta">Instale uma versão na aba Bíblia para usar aqui.</p>
                : (
                  <>
                    <input className="name" placeholder="jo 3 16-18 · sl 23 · 1co 13 4-7" value={refText} onChange={(e) => setRefText(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key !== 'Enter' || !parsed) return
                        e.stopPropagation()
                        const v = parsed.verse ?? 1
                        add({ kind: 'bible', title: refLabel(parsedName, parsed.chapter, v, parsed.verseEnd), book: parsed.book, chapter: parsed.chapter, verse: v, verseEnd: parsed.verseEnd ?? v })
                        setRefText('')
                      }} />
                    <p className="meta">{parsed ? `Enter adiciona: ${refLabel(parsedName, parsed.chapter, parsed.verse ?? 1, parsed.verseEnd)}` : 'Digite a referência e tecle Enter.'}</p>
                    <p className="meta">Com vários versículos, cada aperto de “próximo” mostra um.</p>
                  </>
                )
            )}
            {addTab === 'clips' && comp && (
              <div className="song-list culto-pick">
                {Array.from({ length: comp.columns }, (_, c) => c).filter((c) => comp.layers.some((l) => l.clips[c])).map((c) => (
                  <div key={`c${c}`} className="song" title="A coluna inteira entra junto" onClick={() => add({ kind: 'column', title: columnName(c), deckId, column: c })}>
                    <strong>▦ {columnName(c)}</strong><span>coluna inteira</span>
                  </div>
                ))}
                {comp.layers.flatMap((l, li) => l.clips.map((clip, c) => clip && clip.kind !== 'lyrics' && (
                  <div key={`${li}-${c}`} className="song" onClick={() => add({ kind: 'clip', title: clip.name, deckId, layer: li, column: c })}>
                    <strong>{clip.name}</strong><span>{l.name} · {columnName(c)}</span>
                  </div>
                )))}
                <p className="meta">Clips da aba aberta na grade.</p>
              </div>
            )}
            {addTab === 'other' && (
              <>
                <textarea rows={3} placeholder="Texto de um aviso ou de um slide" value={text} onChange={(e) => setText(e.target.value)} />
                <div className="row">
                  <button className="small" disabled={!text.trim()} title="Entra como um slide de letra" onClick={() => { add({ kind: 'text', title: stripTags(text).split('\n')[0].slice(0, 50), text: text.trim() }); setText('') }}>+ Texto no telão</button>
                  <button className="small" disabled={!text.trim()} title="Entra por cima de tudo, com a aparência dos avisos" onClick={() => { add({ kind: 'notice', title: stripTags(text).split('\n')[0].slice(0, 50), text: text.trim() }); setText('') }}>+ Aviso</button>
                </div>
                <div className="row">
                  <input className="timer-min" type="number" min={1} max={600} value={minutes} onChange={(e) => setMinutes(Number(e.target.value))} title="Minutos" />
                  <button className="small" disabled={minutes <= 0} onClick={() => add({ kind: 'timer', title: `Contagem de ${minutes} min`, seconds: minutes * 60 })}>+ Contagem regressiva</button>
                </div>
                <button className="small" onClick={() => add({ kind: 'clear', title: 'Encerrar telão' })}>+ Encerrar telão</button>
              </>
            )}
          </section>

          <section className="culto-list">
            <div className="effects-head">
              <input className="name culto-name" value={service.name} onChange={(e) => save(items, e.target.value)} title="Nome do roteiro" />
              <span className="row">
                <button className="small" disabled={!current} onClick={() => send({ type: 'serviceReset' })} title="Volta ao começo (não muda o que está no telão)">Voltar ao início</button>
                <button className="small danger-text" disabled={items.length === 0} onClick={() => {
                  if (window.confirm('Apagar todos os itens deste roteiro?')) save([])
                }}>Limpar</button>
              </span>
            </div>
            {lyricsMissing && (
              <div className="warn-box">Músicas, versículos e textos só aparecem com o clip <b>Letras</b> na grade.
                {comp && <button className="small" onClick={() => send({ type: 'lyricsToGrid', layer: comp.layers.length - 1, column: comp.columns - 1 })}>Colocar na grade</button>}
              </div>
            )}
            <div className="service-items">
              {items.map((i, idx) => (
                <div key={i.id} draggable
                  className={`service-item ${i.id === current ? 'live' : ''} ${i.id === selected ? 'selected' : ''} ${i.id === nextItem?.id && !hasNextSlide && !nextVerse ? 'next' : ''} ${missing(i) ? 'broken' : ''}`}
                  onClick={() => setSelected(i.id === selected ? null : i.id)}
                  onDoubleClick={() => { send({ type: 'serviceGo', id: i.id }); setSelected(null) }}
                  onDragStart={() => setDragging(i.id)} onDragEnd={() => setDragging(null)}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={(e) => { e.preventDefault(); if (dragging) move(dragging, idx); setDragging(null) }}>
                  <span className="service-num">{idx + 1}</span>
                  <em className="badge">{KIND_LABEL[i.kind]}</em>
                  <div className="service-text">
                    <strong>{i.title}</strong>
                    <span className="meta">{detail(i)}</span>
                    {i.id === selected && (
                      <input className="name" placeholder="Anotação (só você vê)" value={i.note ?? ''} onClick={(e) => e.stopPropagation()} onChange={(e) => patch(i.id, { note: e.target.value })} />
                    )}
                    {i.id !== selected && i.note && <span className="service-note">{i.note}</span>}
                  </div>
                  <span className="service-actions" onClick={(e) => e.stopPropagation()} onDoubleClick={(e) => e.stopPropagation()}>
                    <button className="small" onClick={() => { send({ type: 'serviceGo', id: i.id }); setSelected(null) }}>▶ Ir</button>
                    <button className="small" disabled={idx === 0} onClick={() => move(i.id, idx - 1)}>↑</button>
                    <button className="small" disabled={idx === items.length - 1} onClick={() => move(i.id, idx + 1)}>↓</button>
                    <button className="small danger-text" title="Tirar do roteiro" onClick={() => { save(items.filter((x) => x.id !== i.id)); if (selected === i.id) setSelected(null) }}>✕</button>
                  </span>
                </div>
              ))}
              {items.length === 0 && <p className="meta">Roteiro vazio. Adicione músicas, versículos, clips e avisos pela coluna ao lado.</p>}
            </div>
            <p className="meta">Clique escolhe e mostra a prévia · duplo clique ou ▶ Ir manda ao ar · arraste para reordenar · → / espaço próximo · ← anterior · Esc tela em branco</p>
          </section>

          <section className="culto-live">
            <div className="row culto-go">
              <button onClick={() => { send({ type: 'serviceStep', direction: -1 }); setSelected(null) }}>◀ Anterior</button>
              <button className="primary culto-next" disabled={items.length === 0} onClick={() => { send({ type: 'serviceStep', direction: 1 }); setSelected(null) }}>Próximo ▶</button>
              <button className={state.blank ? 'active' : ''} onClick={() => send({ type: 'lyricsBlank' })}>{state.blank ? 'Mostrar' : 'Em branco'}</button>
            </div>
            <div className="slide-monitor">
              <div className="monitor-head"><span className="onair">NO AR</span> {curItem?.title ?? 'Nada do roteiro'}{onSong ? ` · slide ${state.slide + 1} de ${curSlides.length}` : ''}</div>
              <div className="monitor-screen" style={{ aspectRatio: '16 / 9' }}>
                {preview ? <img src={preview} alt="Telão" /> : <span className="meta">sem imagem</span>}
              </div>
            </div>
            {clipThumb
              ? <div className="slide-monitor"><div className="monitor-head"><span className="next-tag">PRÉVIA</span> {nextLabel}</div><div className="monitor-screen" style={{ aspectRatio: '16 / 9' }}><img src={clipThumb} alt="Clip" /></div></div>
              : <SlidePreview request={nextRequest} label={nextLabel} empty={previewItem ? previewItem.kind === 'clear' ? 'Telão sem letra nem avisos' : previewItem.title : 'Fim do roteiro'} />}
            {showSelected && <button className="primary" onClick={() => { send({ type: 'serviceGo', id: selected! }); setSelected(null) }}>Enviar ao telão</button>}
            {curSong && curSlides.length > 0 && (
              <>
                <h3>Slides de {curSong.title}</h3>
                <div className="culto-slides">
                  {curSlides.map((sl, i) => (
                    <div key={i} className={`slide ${onSong && state.slide === i ? (state.blank ? 'live blank' : 'live') : ''}`}
                      onClick={() => send({ type: 'lyricsShow', songId: curSong.id, slide: i })}>
                      <span className="slide-num">{i + 1}{sl.label && ` · ${sl.label}`}</span>
                      <span className="slide-text">{stripTags(sl.text)}</span>
                    </div>
                  ))}
                </div>
              </>
            )}
          </section>
        </div>
      )}
    </div>
  )
}
