import { useEffect, useMemo, useRef, useState } from 'react'
import { send, type Composition, type LyricScene, type LyricStyle, type LyricsState, type Song } from './bridge'
import { normalize, replaceSlide, SLIDE_TRANSITIONS, splitSlides, stripTags, WORD_ANIMS, type Slide } from './lyrics'
import { RichText } from './RichText'

type Props = {
  songs: Song[]
  style: LyricStyle | null
  scenes: LyricScene[]
  state: LyricsState
  fonts: string[]
  preview: string | null
  comp: Composition | null
  onStyleLocal: (s: LyricStyle) => void
  onScenesLocal: (s: LyricScene[]) => void
}

const newId = () => Math.random().toString(16).slice(2, 12)



/**
 * Aba Letras (no estilo do Holyrics): biblioteca, editor, slides clicáveis, cenas (estilos com nome)
 * para a música inteira, ajustes de um slide só e formatação de trechos. A letra vai para o telão
 * por um clip "Letras" na grade, misturada com os vídeos, efeitos e o mapeamento.
 */
export function LyricsPage({ songs, style, scenes, state, fonts, preview, comp, onStyleLocal, onScenesLocal }: Props) {
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(state.songId)
  const [editing, setEditing] = useState<Song | null>(null)
  const [editingSlide, setEditingSlide] = useState<number | null>(null)
  const [quickText, setQuickText] = useState('')
  const [gridLayer, setGridLayer] = useState(3)
  const [gridColumn, setGridColumn] = useState(7)
  const [styleTarget, setStyleTarget] = useState<string>('') // '' = padrão, senão id da cena

  const filtered = useMemo(() => {
    const q = normalize(query.trim())
    if (!q) return songs
    return songs.filter((s) => normalize(`${s.title} ${s.artist} ${stripTags(s.text)}`).includes(q))
  }, [songs, query])

  const song = songs.find((s) => s.id === selectedId) ?? null
  const slides = useMemo(() => (song ? splitSlides(song.text) : []), [song])
  const liveHere = song != null && state.songId === song.id
  const songScene = scenes.find((s) => s.id === song?.sceneId)
  const lyricsInGrid = comp?.layers.some((l) => l.clips.some((c) => c?.kind === 'lyrics')) ?? false

  // atalhos: → / PageDown / espaço = próximo, ← / PageUp = anterior, B ou Esc = tela em branco
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName
      if (editing || editingSlide !== null || tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
      if (['ArrowRight', 'ArrowDown', 'PageDown', ' '].includes(e.key)) {
        e.preventDefault()
        if (state.songId) send({ type: 'lyricsStep', direction: 1 })
        else if (song) send({ type: 'lyricsShow', songId: song.id, slide: 0 })
      } else if (['ArrowLeft', 'ArrowUp', 'PageUp'].includes(e.key)) {
        e.preventDefault()
        send({ type: 'lyricsStep', direction: -1 })
      } else if (e.key === 'Delete' || (e.key === 'Escape' && e.shiftKey)) {
        send({ type: 'lyricsClear' }) // fim da música: sai do telão e do retorno
      } else if (e.key === 'b' || e.key === 'B' || e.key === 'Escape') {
        send({ type: 'lyricsBlank' })
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [editing, editingSlide, state.songId, song])

  // estilo que o painel "Aparência" está editando: o padrão ou o de uma cena
  const target = scenes.find((s) => s.id === styleTarget)
  const editedStyle = target?.style ?? style
  const setStyle = (patch: Partial<LyricStyle>) => {
    if (!editedStyle) return
    const next = { ...editedStyle, ...patch }
    if (target) onScenesLocal(scenes.map((s) => (s.id === target.id ? { ...s, style: next } : s)))
    else onStyleLocal(next)
    send({ type: 'lyricsStyle', style: next, sceneId: target?.id ?? null })
  }

  const saveSong = (s: Song) => send({ type: 'lyricsSave', song: s })

  return (
    <div className="lyrics">
      {/* ---------- biblioteca ---------- */}
      <section className="lyrics-library">
        <input className="name" placeholder="Buscar título, artista ou trecho…" value={query} onChange={(e) => setQuery(e.target.value)} />
        <div className="row">
          <button className="small" onClick={() => setEditing({ id: newId(), title: '', artist: '', text: '', sceneId: null })}>+ Nova música</button>
          <button className="small" onClick={() => send({ type: 'lyricsImport' })}>Importar .txt…</button>
        </div>
        <div className="song-list">
          {filtered.map((s) => (
            <div key={s.id} className={`song ${s.id === selectedId ? 'active' : ''} ${s.id === state.songId ? 'live' : ''}`}
              onClick={() => { setSelectedId(s.id); setEditing(null); setEditingSlide(null) }}
              onDoubleClick={() => send({ type: 'lyricsShow', songId: s.id, slide: 0 })}>
              <strong>{s.title}</strong>
              {s.artist && <span>{s.artist}</span>}
            </div>
          ))}
          {filtered.length === 0 && <div className="meta">Nenhuma música encontrada.</div>}
        </div>
        <p className="meta">{songs.length} música(s). Duplo clique projeta o primeiro slide.</p>
      </section>

      {/* ---------- slides, editor da música ou editor de slide ---------- */}
      <section className="lyrics-main">
        {editing ? (
          <SongEditor song={editing} onCancel={() => setEditing(null)} onSave={(s) => {
            saveSong(s)
            setSelectedId(s.id)
            setEditing(null)
          }} />
        ) : song && editingSlide !== null && slides[editingSlide] ? (
          <SlideEditor index={editingSlide} slide={slides[editingSlide]} scenes={scenes}
            songSceneName={songScene?.name ?? 'Padrão'}
            onCancel={() => setEditingSlide(null)}
            onSave={(sl, project) => {
              saveSong({ ...song, text: replaceSlide(song.text, editingSlide, sl) })
              if (project) send({ type: 'lyricsShow', songId: song.id, slide: editingSlide })
              else setEditingSlide(null)
            }} />
        ) : song ? (
          <>
            <div className="effects-head">
              <h2><span className="fx-target">{song.title}</span>{song.artist && <> · {song.artist}</>}</h2>
              <span className="row">
                <button className="small" onClick={() => setEditing({ ...song })}>Editar letra</button>
                <button className="small danger-text" onClick={() => {
                  if (window.confirm(`Excluir "${song.title}"?`)) { send({ type: 'lyricsDelete', id: song.id }); setSelectedId(null) }
                }}>Excluir</button>
              </span>
            </div>
            <div className="song-scene">
              Cena da música:
              <select value={song.sceneId ?? ''} onChange={(e) => saveSong({ ...song, sceneId: e.target.value || null })}>
                <option value="">Padrão</option>
                {scenes.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              <span className="meta">vale para todos os slides; cada slide pode ter a sua (✎)</span>
            </div>
            <div className="slides">
              {slides.map((sl, i) => {
                const custom = Object.keys(sl.overrides).length > 0
                return (
                  <div key={i} className={`slide ${liveHere && state.slide === i ? (state.blank ? 'live blank' : 'live') : ''}`}
                    onClick={() => send({ type: 'lyricsShow', songId: song.id, slide: i })}>
                    <span className="slide-num">
                      {i + 1}{sl.label && ` · ${sl.label}`}
                      {sl.scene && <em className="badge">{sl.scene}</em>}
                      {custom && <em className="badge">ajustado</em>}
                      <button className="small edit" title="Editar só este slide"
                        onClick={(e) => { e.stopPropagation(); setEditingSlide(i) }}>✎</button>
                    </span>
                    <span className="slide-text"><RichText text={sl.text} animate /></span>
                  </div>
                )
              })}
              {slides.length === 0 && <div className="meta">Esta música ainda não tem letra. Clique em Editar letra.</div>}
            </div>
            <p className="meta">Clique num slide para projetar · ✎ edita só aquele slide · → / espaço próximo · ← anterior · Esc tela em branco · Shift+Esc encerra nas duas telas</p>
          </>
        ) : (
          <div className="meta">Escolha uma música na lista, crie uma nova ou importe arquivos .txt.</div>
        )}
      </section>

      {/* ---------- telão, cenas e aparência ---------- */}
      <section className="lyrics-side">
        <h2>No telão</h2>
        {preview && <img className="preview" src={preview} alt="Preview da composição" />}
        <div className="live-text">{state.blank ? '(tela em branco)' : state.text ? <RichText text={state.text} /> : '(nada projetado)'}</div>
        <div className="row">
          <button className={state.blank ? 'active' : ''} onClick={() => send({ type: 'lyricsBlank' })}>
            {state.blank ? 'Mostrar letra' : 'Tela em branco'}
          </button>
          <button onClick={() => send({ type: 'lyricsStep', direction: -1 })}>◀</button>
          <button onClick={() => send({ type: 'lyricsStep', direction: 1 })}>▶</button>
        </div>
        <button className="clear-lyrics" disabled={!state.text} title="Fim da música: tira a letra do telão e da tela de retorno (Shift+Esc ou Delete)"
          onClick={() => send({ type: 'lyricsClear' })}>Encerrar letra (telão e retorno)</button>
        <p className="meta">Esc / B: tira só do telão (os músicos continuam vendo). Shift+Esc / Delete: tira das duas telas.</p>

        {!lyricsInGrid && (
          <div className="grid-hint">
            <p>Para a letra aparecer, ela precisa estar na grade de clips (assim ela se mistura com os vídeos).</p>
            <div className="row">
              <select value={gridLayer} onChange={(e) => setGridLayer(Number(e.target.value))}>
                {comp?.layers.map((l, i) => <option key={i} value={i}>{l.name}</option>)}
              </select>
              <select value={gridColumn} onChange={(e) => setGridColumn(Number(e.target.value))}>
                {Array.from({ length: comp?.columns ?? 8 }, (_, c) => <option key={c} value={c}>Coluna {c + 1}</option>)}
              </select>
              <button className="small active" onClick={() => send({ type: 'lyricsToGrid', layer: gridLayer, column: gridColumn })}>
                Colocar na grade
              </button>
            </div>
          </div>
        )}

        <h3>Texto rápido</h3>
        <textarea rows={3} placeholder="Avisos, versículos, nomes…" value={quickText} onChange={(e) => setQuickText(e.target.value)} />
        <button className="small" disabled={!quickText.trim()} onClick={() => send({ type: 'lyricsText', text: quickText.trim() })}>Projetar texto</button>

        <h3>Cenas</h3>
        <div className="row scenes-row">
          <select value={styleTarget} onChange={(e) => setStyleTarget(e.target.value)} title="O que o painel Aparência está editando">
            <option value="">Editando: Padrão</option>
            {scenes.map((s) => <option key={s.id} value={s.id}>Editando: {s.name}</option>)}
          </select>
          <button className="small" title="Cria uma cena a partir do estilo que está aberto" onClick={() => {
            const name = window.prompt('Nome da cena (ex.: Refrão grande, Rodapé, Destaque):')
            if (!name?.trim() || !editedStyle) return
            const scene = { id: newId(), name: name.trim(), style: { ...editedStyle } }
            send({ type: 'lyricsSceneSave', scene })
            setStyleTarget(scene.id)
          }}>+ Nova cena</button>
          {target && (
            <>
              <button className="small" onClick={() => {
                const name = window.prompt('Novo nome da cena:', target.name)
                if (name?.trim()) send({ type: 'lyricsSceneSave', scene: { ...target, name: name.trim() } })
              }}>Renomear</button>
              <button className="small danger-text" onClick={() => {
                if (window.confirm(`Excluir a cena "${target.name}"? Músicas que usam ela voltam para o Padrão.`)) {
                  send({ type: 'lyricsSceneDelete', id: target.id })
                  setStyleTarget('')
                }
              }}>Excluir</button>
            </>
          )}
        </div>

        {editedStyle && <StylePanel style={editedStyle} fonts={fonts} onChange={setStyle} />}
      </section>
    </div>
  )
}

function StylePanel({ style, fonts, onChange }: { style: LyricStyle; fonts: string[]; onChange: (p: Partial<LyricStyle>) => void }) {
  return (
    <>
      <label>Fonte <output />
        <select value={style.fontFamily} onChange={(e) => onChange({ fontFamily: e.target.value })}>
          {(fonts.includes(style.fontFamily) ? fonts : [style.fontFamily, ...fonts]).map((f) => <option key={f}>{f}</option>)}
        </select>
      </label>
      <label>Tamanho <output>{Math.round(style.fontSize)}</output>
        <input type="range" min={24} max={200} step={1} value={style.fontSize} onChange={(e) => onChange({ fontSize: Number(e.target.value) })} />
      </label>
      <label>Posição <output />
        <select value={style.position} onChange={(e) => onChange({ position: e.target.value as LyricStyle['position'] })}>
          <option value="top">Em cima</option>
          <option value="center">No meio</option>
          <option value="bottom">Embaixo</option>
        </select>
      </label>
      <label>Alinhamento <output />
        <select value={style.align ?? 'center'} onChange={(e) => onChange({ align: e.target.value as LyricStyle['align'] })}>
          <option value="left">Esquerda</option>
          <option value="center">Centro</option>
          <option value="right">Direita</option>
        </select>
      </label>
      <label>Margem <output>{Math.round(style.margin)}</output>
        <input type="range" min={0} max={400} step={5} value={style.margin} onChange={(e) => onChange({ margin: Number(e.target.value) })} />
      </label>
      <div className="style-row">
        <label className="check"><input type="checkbox" checked={style.bold} onChange={(e) => onChange({ bold: e.target.checked })} /> Negrito</label>
        <label className="check"><input type="checkbox" checked={!!style.italic} onChange={(e) => onChange({ italic: e.target.checked })} /> Itálico</label>
        <label className="check"><input type="checkbox" checked={style.uppercase} onChange={(e) => onChange({ uppercase: e.target.checked })} /> MAIÚSCULAS</label>
        <label className="check"><input type="checkbox" checked={style.shadow} onChange={(e) => onChange({ shadow: e.target.checked })} /> Sombra</label>
      </div>
      <div className="style-row">
        <label className="color">Texto <input type="color" value={style.color} onChange={(e) => onChange({ color: e.target.value })} /></label>
        <label className="color">Contorno <input type="color" value={style.outlineColor} onChange={(e) => onChange({ outlineColor: e.target.value })} /></label>
      </div>
      <label>Contorno <output>{style.outlineWidth.toFixed(0)}</output>
        <input type="range" min={0} max={20} step={1} value={style.outlineWidth} onChange={(e) => onChange({ outlineWidth: Number(e.target.value) })} />
      </label>
      <div className="style-row">
        <label className="check"><input type="checkbox" checked={style.box} onChange={(e) => onChange({ box: e.target.checked })} /> Caixa atrás do texto</label>
        {style.box && <label className="color"><input type="color" value={style.boxColor} onChange={(e) => onChange({ boxColor: e.target.value })} /></label>}
      </div>
      {style.box && (
        <label>Opacidade da caixa <output>{Math.round(style.boxOpacity * 100)}%</output>
          <input type="range" min={0} max={1} step={0.01} value={style.boxOpacity} onChange={(e) => onChange({ boxOpacity: Number(e.target.value) })} />
        </label>
      )}
      <label>Transição do slide <output />
        <select value={style.transition ?? 'fade'} onChange={(e) => onChange({ transition: e.target.value })}>
          {SLIDE_TRANSITIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </select>
      </label>
      <label>Duração <output>{style.fadeMs} ms</output>
        <input type="range" min={0} max={1500} step={50} value={style.fadeMs} onChange={(e) => onChange({ fadeMs: Number(e.target.value) })} />
      </label>
    </>
  )
}

function SongEditor({ song, onSave, onCancel }: { song: Song; onSave: (s: Song) => void; onCancel: () => void }) {
  const [draft, setDraft] = useState(song)
  const count = splitSlides(draft.text).length
  return (
    <div className="song-editor">
      <h2>{song.title ? 'Editar música' : 'Nova música'}</h2>
      <input className="name" placeholder="Título" value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} />
      <input className="name" placeholder="Artista / ministério (opcional)" value={draft.artist} onChange={(e) => setDraft({ ...draft, artist: e.target.value })} />
      <textarea rows={18} value={draft.text} onChange={(e) => setDraft({ ...draft, text: e.target.value })}
        placeholder={'[Verso 1]\nPrimeira linha do slide\nSegunda linha do slide\n\n[Refrão]\nUma linha em branco começa outro slide'} />
      <p className="meta">{count} slide(s). Linha em branco separa os slides; <code>[Refrão]</code> na primeira linha dá nome à seção (não aparece no telão). Para formatar um slide ou trecho, salve e use o ✎ do slide.</p>
      <div className="row">
        <button className="active" onClick={() => onSave(draft)}>Salvar</button>
        <button onClick={onCancel}>Cancelar</button>
      </div>
    </div>
  )
}

const SIZES = [['70%', 'Menor'], ['85%', 'Pouco menor'], ['115%', 'Pouco maior'], ['130%', 'Maior'], ['160%', 'Bem maior']]

/** Editor de um slide só: texto com barra de formatação, cena do slide e ajustes que valem só aqui. */
function SlideEditor({ index, slide, scenes, songSceneName, onSave, onCancel }: {
  index: number
  slide: Slide
  scenes: LyricScene[]
  songSceneName: string
  onSave: (s: Slide, project: boolean) => void
  onCancel: () => void
}) {
  const [draft, setDraft] = useState<Slide>(slide)
  const [color, setColor] = useState('#FFCC00')
  const [animType, setAnimType] = useState('balancar')
  const [animIntensity, setAnimIntensity] = useState(1)
  const [animSpeed, setAnimSpeed] = useState(0.6)
  const area = useRef<HTMLTextAreaElement>(null)
  const o = draft.overrides
  const setOverride = (k: string, v: string) => {
    const next = { ...o }
    if (v === '') delete next[k]; else next[k] = v
    setDraft({ ...draft, overrides: next })
  }

  // envolve o trecho selecionado com a marcação
  const wrap = (open: string, close: string) => {
    const el = area.current
    if (!el) return
    const { selectionStart: a, selectionEnd: b, value } = el
    if (a === b) { window.alert('Selecione o trecho do texto que você quer formatar.'); return }
    const text = value.slice(0, a) + open + value.slice(a, b) + close + value.slice(b)
    setDraft({ ...draft, text })
    requestAnimationFrame(() => { el.focus(); el.setSelectionRange(a + open.length, b + open.length) })
  }

  return (
    <div className="slide-editor">
      <h2>Slide {index + 1}{draft.label && ` · ${draft.label}`}</h2>

      <div className="format-bar">
        <button className="small" title="Negrito" onClick={() => wrap('<b>', '</b>')}><b>N</b></button>
        <button className="small" title="Itálico" onClick={() => wrap('<i>', '</i>')}><i>I</i></button>
        <button className="small" title="Sublinhado" onClick={() => wrap('<u>', '</u>')}><u>S</u></button>
        <span className="sep" />
        <input type="color" value={color} onChange={(e) => setColor(e.target.value)} title="Cor do trecho" />
        <button className="small" onClick={() => wrap(`<cor ${color.toUpperCase()}>`, '</cor>')}>Aplicar cor</button>
        <span className="sep" />
        <select value="" onChange={(e) => { if (e.target.value) wrap(`<tam ${e.target.value}>`, '</tam>') }}>
          <option value="">Tamanho do trecho…</option>
          {SIZES.map(([v, l]) => <option key={v} value={v}>{l} ({v})</option>)}
        </select>
        <span className="sep" />
        <select value={animType} onChange={(e) => setAnimType(e.target.value)} title="Animação do trecho">
          {WORD_ANIMS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </select>
        <label className="mini">força <input type="range" min={0.2} max={3} step={0.1} value={animIntensity} onChange={(e) => setAnimIntensity(Number(e.target.value))} /></label>
        <label className="mini">velocidade <input type="range" min={0.2} max={3} step={0.1} value={animSpeed} onChange={(e) => setAnimSpeed(Number(e.target.value))} /></label>
        <button className="small" onClick={() => wrap(`<anim ${animType} ${animIntensity} ${animSpeed}>`, '</anim>')}>Animar trecho</button>
        <span className="sep" />
        <button className="small" title="Tira a formatação do trecho selecionado (ou do slide todo)" onClick={() => {
          const el = area.current
          if (!el) return
          const { selectionStart: a, selectionEnd: b, value } = el
          const text = a === b ? stripTags(value) : value.slice(0, a) + stripTags(value.slice(a, b)) + value.slice(b)
          setDraft({ ...draft, text })
        }}>Limpar formatação</button>
      </div>

      <textarea ref={area} rows={6} value={draft.text} onChange={(e) => setDraft({ ...draft, text: e.target.value })} />

      <div className="slide-preview"><RichText text={draft.text} animate /></div>

      <div className="slide-options">
        <label>Seção <output />
          <input className="name" value={draft.label ?? ''} placeholder="ex.: Refrão" onChange={(e) => setDraft({ ...draft, label: e.target.value || null })} />
        </label>
        <label>Cena deste slide <output />
          <select value={draft.scene ?? ''} onChange={(e) => setDraft({ ...draft, scene: e.target.value || null })}>
            <option value="">Igual à música ({songSceneName})</option>
            {scenes.map((s) => <option key={s.id} value={s.name}>{s.name}</option>)}
          </select>
        </label>
        <h3>Ajustes só deste slide (vazio = igual à cena)</h3>
        <label>Transição <output />
          <select value={o.transicao ?? ''} onChange={(e) => setOverride('transicao', e.target.value)}>
            <option value="">herdar</option>
            {SLIDE_TRANSITIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </label>
        <label>Duração (ms) <output />
          <input className="name" type="number" min={0} max={5000} step={50} placeholder="herdar" value={o.duracao ?? ''} onChange={(e) => setOverride('duracao', e.target.value)} />
        </label>
        <label>Tamanho <output />
          <input className="name" type="number" min={12} max={300} placeholder="herdar" value={o.tamanho ?? ''} onChange={(e) => setOverride('tamanho', e.target.value)} />
        </label>
        <label>Cor do texto <output />
          <span className="row inline">
            <input type="checkbox" checked={!!o.cor} onChange={(e) => setOverride('cor', e.target.checked ? '#FFFFFF' : '')} />
            {o.cor && <input type="color" value={o.cor} onChange={(e) => setOverride('cor', e.target.value.toUpperCase())} />}
          </span>
        </label>
        <label>Posição <output />
          <select value={o.posicao ?? ''} onChange={(e) => setOverride('posicao', e.target.value)}>
            <option value="">herdar</option>
            <option value="cima">Em cima</option>
            <option value="meio">No meio</option>
            <option value="baixo">Embaixo</option>
          </select>
        </label>
        <label>Alinhamento <output />
          <select value={o.alinhamento ?? ''} onChange={(e) => setOverride('alinhamento', e.target.value)}>
            <option value="">herdar</option>
            <option value="esquerda">Esquerda</option>
            <option value="centro">Centro</option>
            <option value="direita">Direita</option>
          </select>
        </label>
        <label>Caixa atrás <output />
          <select value={o.caixa ?? ''} onChange={(e) => setOverride('caixa', e.target.value)}>
            <option value="">herdar</option>
            <option value="sim">Com caixa</option>
            <option value="nao">Sem caixa</option>
          </select>
        </label>
      </div>

      <div className="row">
        <button className="active" onClick={() => onSave(draft, false)}>Salvar</button>
        <button onClick={() => onSave(draft, true)}>Salvar e projetar</button>
        <button onClick={onCancel}>Voltar</button>
      </div>
    </div>
  )
}
