import { useEffect, useMemo, useRef, useState } from 'react'
import { onMessage, send, type LibraryItem } from './bridge'

export const MEDIA_MIME = 'application/x-vjesus-media'

const KIND_LABEL: Record<LibraryItem['kind'], string> = {
  video: 'Vídeo', image: 'Imagem', presentation: 'Apresentação', presentationFile: 'PowerPoint/PDF',
}
const SUGGESTED = ['louvor', 'ceia', 'batismo', 'pregação', 'abertura', 'fundo', 'contagem', 'kids', 'natal', 'páscoa']

/**
 * Biblioteca de mídia: tudo das pastas escolhidas e da grade, com busca, filtro por tag e tipo,
 * prévia animada ao passar o mouse e arrastar para uma célula da grade.
 */
export function MediaLibrary({ thumbs, compact, onPick }: { thumbs: Record<string, string | null>; compact?: boolean; onPick?: (path: string) => void }) {
  const [items, setItems] = useState<LibraryItem[] | null>(null)
  const [folders, setFolders] = useState<string[]>([])
  const [scanning, setScanning] = useState(false)
  const [q, setQ] = useState('')
  const [tag, setTag] = useState<string | null>(null)
  const [kind, setKind] = useState<'all' | 'video' | 'image' | 'pres'>('all')
  const [previews, setPreviews] = useState<Record<string, string[]>>({})
  const [editing, setEditing] = useState<string | null>(null)
  const asked = useRef(new Set<string>())
  // aberta ou fechada fica lembrado (fechada por padrão, para não ocupar a tela)
  const [openState, setOpen] = useState(() => { try { return localStorage.getItem('biblioteca.aberta') === '1' } catch { return false } })
  const open = compact || openState
  const toggle = () => setOpen((o) => { try { localStorage.setItem('biblioteca.aberta', o ? '0' : '1') } catch { /* sem armazenamento */ } return !o })

  useEffect(() => {
    const off = onMessage((m) => {
      if (m.type === 'library') { setScanning(m.scanning); setFolders(m.folders); if (m.items) setItems(m.items) }
      if (m.type === 'libraryPreview') setPreviews((p) => ({ ...p, [m.path]: m.frames }))
      if (m.type === 'libraryTags') setItems((list) => list?.map((i) => (i.path === m.path ? { ...i, tags: m.tags } : i)) ?? null)
    })
    return off
  }, [])
  // só lê as pastas quando a biblioteca é aberta
  useEffect(() => { if (open && items === null) send({ type: 'libraryGet' }) }, [open])

  const allTags = useMemo(() => {
    const count = new Map<string, number>()
    for (const i of items ?? []) for (const t of i.tags) count.set(t, (count.get(t) ?? 0) + 1)
    return [...count.entries()].sort((a, b) => b[1] - a[1]).map(([t]) => t)
  }, [items])

  const shown = useMemo(() => {
    const words = q.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').split(/\s+/).filter(Boolean)
    const norm = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    return (items ?? []).filter((i) =>
      (kind === 'all' || (kind === 'video' && i.kind === 'video') || (kind === 'image' && i.kind === 'image') || (kind === 'pres' && i.kind.startsWith('presentation')))
      && (!tag || i.tags.includes(tag))
      && words.every((w) => norm(`${i.name} ${i.folder} ${i.tags.join(' ')}`).includes(w)))
  }, [items, q, tag, kind])

  const visible = shown.slice(0, 200)
  // miniaturas só do que aparece na tela
  useEffect(() => {
    if (!open) return
    const need = visible.filter((i) => i.kind !== 'presentationFile' && !(i.path in thumbs) && !asked.current.has(i.path)).map((i) => i.path)
    if (need.length === 0) return
    need.forEach((p) => asked.current.add(p))
    for (let k = 0; k < need.length; k += 40) send({ type: 'libraryThumbs', paths: need.slice(k, k + 40) })
  }, [visible, thumbs, open])

  return (
    <section className={`media-lib ${open ? 'open' : 'closed'} ${compact ? 'compact' : ''}`}>
      <div className="media-head">
        {!compact && <button className="media-toggle" onClick={toggle} aria-expanded={open} title={open ? 'Recolher a biblioteca' : 'Abrir a biblioteca'}>
          <span className="caret">{open ? '▾' : '▸'}</span> <h2>Biblioteca de mídia</h2>
          {items && <span className="meta">{items.length} itens</span>}
        </button>}
        {open && <>
        <input className="media-search" placeholder="Buscar por nome, pasta ou tag…" value={q} onChange={(e) => setQ(e.target.value)} />
        <select value={kind} onChange={(e) => setKind(e.target.value as typeof kind)}>
          <option value="all">Tudo</option><option value="video">Vídeos</option><option value="image">Imagens</option><option value="pres">Apresentações</option>
        </select>
        <button onClick={() => send({ type: 'libraryAddFolder' })}>+ Pasta…</button>
        <button onClick={() => send({ type: 'libraryGet' })} disabled={scanning}>{scanning ? 'Lendo…' : 'Atualizar'}</button>
        </>}
      </div>
      {open && <>
      <div className="media-folders">
        {folders.length === 0 && <span className="meta">Adicione as pastas onde ficam os vídeos da igreja. O que já está na grade aparece aqui sozinho.</span>}
        {folders.map((f) => (
          <span key={f} className="chip" title={f}>📁 {f.split('\\').pop()}
            <button className="chip-x" title="Tirar da biblioteca (não apaga os arquivos)" onClick={() => send({ type: 'libraryRemoveFolder', path: f })}>×</button>
          </span>
        ))}
      </div>
      {allTags.length > 0 && (
        <div className="media-tags">
          <button className={`small ${tag === null ? 'active' : ''}`} onClick={() => setTag(null)}>Todas as tags</button>
          {allTags.map((t) => <button key={t} className={`small ${tag === t ? 'active' : ''}`} onClick={() => setTag(tag === t ? null : t)}>#{t}</button>)}
        </div>
      )}
      <div className="media-grid">
        {items === null && <div className="meta">Lendo a biblioteca…</div>}
        {items !== null && shown.length === 0 && <div className="meta">Nada encontrado.</div>}
        {visible.map((i) => (
          <MediaCard key={i.path} item={i} thumb={thumbs[i.path] ?? null} frames={previews[i.path]}
            editing={editing === i.path} onEdit={(on) => setEditing(on ? i.path : null)} suggestions={[...new Set([...allTags, ...SUGGESTED])]} onPick={onPick} />
        ))}
      </div>
      {shown.length > visible.length && <div className="meta">Mostrando 200 de {shown.length}. Use a busca para achar o resto.</div>}
      <p className="hint">{onPick ? 'Clique para pôr no fim do roteiro ou arraste para a lista · passe o mouse para ver o vídeo' : 'Arraste da biblioteca para uma célula da grade · passe o mouse para ver o vídeo · # para marcar com tags'}</p>
      </>}
    </section>
  )
}

function MediaCard({ item, thumb, frames, editing, onEdit, suggestions, onPick }: {
  item: LibraryItem; thumb: string | null; frames?: string[]; editing: boolean; onEdit: (on: boolean) => void; suggestions: string[]; onPick?: (path: string) => void
}) {
  const [hover, setHover] = useState(false)
  const [frame, setFrame] = useState(0)
  const [text, setText] = useState('')
  useEffect(() => {
    if (!hover || item.kind !== 'video') return
    if (!frames) send({ type: 'libraryPreview', path: item.path })
    const t = setInterval(() => setFrame((f) => f + 1), 450)
    return () => { clearInterval(t); setFrame(0) }
  }, [hover])
  const img = hover && frames && frames.length > 0 ? frames[frame % frames.length] : thumb
  const setTags = (tags: string[]) => send({ type: 'libraryTags', path: item.path, tags })
  const add = (t: string) => { const v = t.trim().toLowerCase().replace(/^#/, ''); if (v && !item.tags.includes(v)) setTags([...item.tags, v]); setText('') }

  return (
    <div className={`media-card ${item.used ? 'used' : ''}`} draggable
      onDragStart={(e) => { e.dataTransfer.setData(MEDIA_MIME, JSON.stringify([item.path])); e.dataTransfer.effectAllowed = 'copy' }}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      onClick={() => onPick?.(item.path)}
      title={`${item.path}\nArraste para uma célula da grade`}>
      <div className="media-thumb">
        {img ? <img src={img} alt="" draggable={false} /> : <div className="media-noimg">{KIND_LABEL[item.kind]}</div>}
        <span className="media-kind">{KIND_LABEL[item.kind]}</span>
        {item.used && <span className="media-used" title="Já está na grade">na grade</span>}
      </div>
      <div className="media-name">{item.name}</div>
      <div className="media-meta">
        {item.tags.map((t) => (
          <span key={t} className="tag">#{t}{editing && <button className="chip-x" onClick={() => setTags(item.tags.filter((x) => x !== t))}>×</button>}</span>
        ))}
        <button className="small tag-btn" title="Marcar com tags" onClick={(e) => { e.stopPropagation(); onEdit(!editing) }}>#</button>
        <button className="small tag-btn" title="Mostrar no Explorer" onClick={(e) => { e.stopPropagation(); send({ type: 'libraryReveal', path: item.path }) }}>📂</button>
      </div>
      {editing && (
        <div className="media-tag-edit" onMouseDown={(e) => e.stopPropagation()} onClick={(e) => e.stopPropagation()}>
          <input autoFocus placeholder="nova tag + Enter" value={text} onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') add(text); if (e.key === 'Escape') onEdit(false) }} />
          <div className="media-sugg">
            {suggestions.filter((s) => !item.tags.includes(s)).slice(0, 10).map((s) => <button key={s} className="small" onClick={() => add(s)}>#{s}</button>)}
          </div>
        </div>
      )}
    </div>
  )
}
