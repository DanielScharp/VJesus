import { useEffect, useRef, useState } from 'react'
import { onMessage, send, type Song } from './bridge'
import { normalize, splitSlides } from './lyrics'

export type WebLyric = { id: string; title: string; artist: string; album: string; duration: number; text: string }

const newId = () => Math.random().toString(16).slice(2, 12)
const mmss = (s: number) => (s > 0 ? `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}` : '')

/**
 * Buscar letra na internet (como no Holyrics): digita música e/ou artista, escolhe o resultado,
 * confere a prévia já dividida em slides e importa para a biblioteca.
 */
export function OnlineLyrics({ songs, initialQuery, onImported, onClose }: {
  songs: Song[]; initialQuery?: string; onImported: (id: string) => void; onClose: () => void
}) {
  const [q, setQ] = useState(initialQuery ?? '')
  const [busy, setBusy] = useState(false)
  const [results, setResults] = useState<WebLyric[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [sel, setSel] = useState<WebLyric | null>(null)
  const input = useRef<HTMLInputElement>(null)

  useEffect(() => {
    input.current?.focus()
    return onMessage((msg) => {
      if (msg.type !== 'lyricsWebResults') return
      setBusy(false)
      setResults(msg.results)
      setError(msg.error ?? null)
      setSel(msg.results[0] ?? null)
    })
  }, [])
  useEffect(() => { if (initialQuery && initialQuery.trim().length >= 2) search() }, [])

  const search = () => {
    if (q.trim().length < 2) return
    setBusy(true); setError(null)
    send({ type: 'lyricsWebSearch', query: q.trim() })
  }

  const already = (r: WebLyric) => songs.some((s) => normalize(s.title) === normalize(r.title) && normalize(s.artist) === normalize(r.artist))
  const slides = sel ? splitSlides(sel.text) : []

  const importSong = (r: WebLyric) => {
    const song: Song = { id: newId(), title: r.title, artist: r.artist, text: r.text, sceneId: null }
    send({ type: 'lyricsSave', song })
    onImported(song.id)
  }

  return (
    <div className="modal-back" onMouseDown={onClose}>
      <div className="modal web-lyrics" onMouseDown={(e) => e.stopPropagation()}>
        <div className="bible-head">
          <h2>Buscar letra na internet</h2>
          <button className="small" onClick={onClose}>Fechar</button>
        </div>
        <div className="row">
          <input ref={input} className="web-q" value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') search() }}
            placeholder="Nome da música e, se quiser, o artista (ex.: Grandes Coisas Fernandinho)" />
          <button className="primary" disabled={busy || q.trim().length < 2} onClick={search}>{busy ? 'Buscando…' : 'Buscar'}</button>
        </div>
        {error && <p className="warn-text">{error}</p>}
        {results && results.length === 0 && !error && <p className="meta">Nada encontrado. Tente só o nome da música, ou o nome com o artista.</p>}
        {results && results.length > 0 && (
          <div className="web-body">
            <div className="web-list">
              {results.map((r) => (
                <div key={r.id} className={`web-item ${sel?.id === r.id ? 'active' : ''}`} onClick={() => setSel(r)}>
                  <b>{r.title}</b>
                  <span className="meta">{r.artist}{r.album ? ` · ${r.album}` : ''}{r.duration ? ` · ${mmss(r.duration)}` : ''}</span>
                  {already(r) && <span className="meta web-have">já está na biblioteca</span>}
                </div>
              ))}
            </div>
            <div className="web-preview">
              {sel && (
                <>
                  <div className="row web-preview-head">
                    <div><b>{sel.title}</b> <span className="meta">— {sel.artist} · {slides.length} slides</span></div>
                    <button className="primary" onClick={() => importSong(sel)}>Importar para a biblioteca</button>
                  </div>
                  <div className="web-slides">
                    {slides.map((s, i) => <div key={i} className="web-slide"><span className="meta">{i + 1}</span>{s.text}</div>)}
                  </div>
                </>
              )}
            </div>
          </div>
        )}
        <p className="meta">
          As letras vêm do LRCLIB (lrclib.net), uma base aberta mantida pela comunidade; confira o texto antes de projetar.
          Depois de importar, a música fica na sua biblioteca e funciona sem internet (dá para editar, dividir e formatar).
        </p>
      </div>
    </div>
  )
}
