import { useEffect, useMemo, useRef, useState } from 'react'
import { onMessage, send, type LibraryFolder, type LibraryItem } from './bridge'

export const MEDIA_MIME = 'application/x-vjesus-media'
/** Arrastar uma pasta da biblioteca (para dentro de outra). */
const FOLDER_MIME = 'application/x-vjesus-libfolder'

const KIND_LABEL: Record<LibraryItem['kind'], string> = {
  video: 'Vídeo', image: 'Imagem', presentation: 'Apresentação', presentationFile: 'PowerPoint/PDF',
}
const SUGGESTED = ['louvor', 'ceia', 'batismo', 'pregação', 'abertura', 'fundo', 'contagem', 'kids', 'natal', 'páscoa']

const norm = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

/**
 * Biblioteca de mídia: tudo das pastas escolhidas e da grade, com busca, filtro por tag e tipo,
 * prévia animada ao passar o mouse e arrastar para uma célula da grade. Pastas e subpastas da
 * biblioteca organizam os arquivos (sem mexer neles no disco). Ctrl/Shift + clique seleciona vários.
 */
export function MediaLibrary({ thumbs, compact, onPick }: { thumbs: Record<string, string | null>; compact?: boolean; onPick?: (path: string) => void }) {
  const [items, setItems] = useState<LibraryItem[] | null>(null)
  const [folders, setFolders] = useState<string[]>([])
  const [collections, setCollections] = useState<LibraryFolder[]>([])
  const [placement, setPlacement] = useState<Record<string, string>>({})
  const [scanning, setScanning] = useState(false)
  const [q, setQ] = useState('')
  const [tag, setTag] = useState<string | null>(null)
  const [kind, setKind] = useState<'all' | 'video' | 'image' | 'pres'>('all')
  const [previews, setPreviews] = useState<Record<string, string[]>>({})
  const [editing, setEditing] = useState<string | null>(null)
  const [cur, setCur] = useState<string | null>(null) // pasta aberta (null = início)
  const [selected, setSelected] = useState<string[]>([])
  const [anchor, setAnchor] = useState<string | null>(null)
  const [renaming, setRenaming] = useState<string | null>(null)
  const [dropOn, setDropOn] = useState<string | null>(null)
  const asked = useRef(new Set<string>())
  // aberta ou fechada fica lembrado (fechada por padrão, para não ocupar a tela)
  const [openState, setOpen] = useState(() => { try { return localStorage.getItem('biblioteca.aberta') === '1' } catch { return false } })
  const open = compact || openState
  const toggle = () => setOpen((o) => { try { localStorage.setItem('biblioteca.aberta', o ? '0' : '1') } catch { /* sem armazenamento */ } return !o })

  useEffect(() => {
    const off = onMessage((m) => {
      if (m.type === 'library') {
        setScanning(m.scanning); setFolders(m.folders)
        if (m.items) setItems(m.items)
        if (m.collections) setCollections(m.collections)
        if (m.placement) setPlacement(m.placement)
      }
      if (m.type === 'libraryCollections') {
        setCollections(m.collections); setPlacement(m.placement)
        if (m.created) { setCur(m.created); setRenaming(m.created) }
      }
      if (m.type === 'libraryPreview') setPreviews((p) => ({ ...p, [m.path]: m.frames }))
      if (m.type === 'libraryTags') setItems((list) => list?.map((i) => (i.path === m.path ? { ...i, tags: m.tags } : i)) ?? null)
    })
    return off
  }, [])
  // só lê as pastas quando a biblioteca é aberta
  useEffect(() => { if (open && items === null) send({ type: 'libraryGet' }) }, [open])

  const byId = useMemo(() => new Map(collections.map((c) => [c.id, c])), [collections])
  // pasta de cada arquivo (ignora pasta que não existe mais)
  const folderOf = (path: string) => { const id = placement[path]; return id && byId.has(id) ? id : null }
  const childrenOf = (id: string | null) => collections.filter((c) => (c.parent ?? null) === id && (id === null || byId.has(id))).sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
  const pathTo = (id: string | null) => { const out: LibraryFolder[] = []; for (let f = id ? byId.get(id) : undefined; f; f = f.parent ? byId.get(f.parent) : undefined) out.unshift(f); return out }
  const countIn = useMemo(() => {
    const n = new Map<string, number>()
    for (const i of items ?? []) { const f = folderOf(i.path); if (f) n.set(f, (n.get(f) ?? 0) + 1) }
    return n
  }, [items, placement, collections])
  if (cur && !byId.has(cur)) setCur(null)

  const allTags = useMemo(() => {
    const count = new Map<string, number>()
    for (const i of items ?? []) for (const t of i.tags) count.set(t, (count.get(t) ?? 0) + 1)
    return [...count.entries()].sort((a, b) => b[1] - a[1]).map(([t]) => t)
  }, [items])

  const searching = q.trim().length > 0 || tag !== null
  const shown = useMemo(() => {
    const words = norm(q).split(/\s+/).filter(Boolean)
    return (items ?? []).filter((i) =>
      (kind === 'all' || (kind === 'video' && i.kind === 'video') || (kind === 'image' && i.kind === 'image') || (kind === 'pres' && i.kind.startsWith('presentation')))
      && (!tag || i.tags.includes(tag))
      // buscando: procura em todas as pastas; senão, só o que está na pasta aberta
      && (searching ? words.every((w) => norm(`${i.name} ${i.folder} ${i.tags.join(' ')} ${pathTo(folderOf(i.path)).map((f) => f.name).join(' ')}`).includes(w)) : folderOf(i.path) === cur))
  }, [items, q, tag, kind, cur, placement, collections])

  const visible = shown.slice(0, 200)
  // miniaturas só do que aparece na tela
  useEffect(() => {
    if (!open) return
    const need = visible.filter((i) => i.kind !== 'presentationFile' && !(i.path in thumbs) && !asked.current.has(i.path)).map((i) => i.path)
    if (need.length === 0) return
    need.forEach((p) => asked.current.add(p))
    for (let k = 0; k < need.length; k += 40) send({ type: 'libraryThumbs', paths: need.slice(k, k + 40) })
  }, [visible, thumbs, open])

  // seleção: clique = só ele; Ctrl = soma/tira; Shift = do último até este
  const select = (path: string, e: React.MouseEvent) => {
    if (e.shiftKey && anchor) {
      const a = visible.findIndex((i) => i.path === anchor), b = visible.findIndex((i) => i.path === path)
      if (a >= 0 && b >= 0) { const [x, y] = a < b ? [a, b] : [b, a]; setSelected((s) => [...new Set([...s, ...visible.slice(x, y + 1).map((i) => i.path)])]); return }
    }
    if (e.ctrlKey || e.metaKey) setSelected((s) => (s.includes(path) ? s.filter((p) => p !== path) : [...s, path]))
    else setSelected((s) => (s.length === 1 && s[0] === path ? [] : [path]))
    setAnchor(path)
  }
  const dragPaths = (path: string) => {
    if (!selected.includes(path)) return [path]
    // na ordem em que aparecem (vão para a grade nessa ordem)
    return visible.filter((i) => selected.includes(i.path)).map((i) => i.path).concat(selected.filter((p) => !visible.some((i) => i.path === p)))
  }
  const moveTo = (paths: string[], folder: string | null) => { send({ type: 'libraryMove', paths, folder }); setSelected([]) }
  const newFolder = () => send({ type: 'libraryFolderNew', name: 'Nova pasta', parent: cur })

  // soltar arquivos ou uma pasta em cima de uma pasta (ou do "Início")
  const dropProps = (id: string | null) => ({
    onDragOver: (e: React.DragEvent) => {
      if (e.dataTransfer.types.includes(MEDIA_MIME) || e.dataTransfer.types.includes(FOLDER_MIME)) { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; setDropOn(id ?? '__root') }
    },
    onDragLeave: () => setDropOn((d) => (d === (id ?? '__root') ? null : d)),
    onDrop: (e: React.DragEvent) => {
      setDropOn(null)
      const f = e.dataTransfer.getData(FOLDER_MIME)
      if (f) { e.preventDefault(); if (f !== id) send({ type: 'libraryFolderMove', id: f, parent: id }); return }
      const m = e.dataTransfer.getData(MEDIA_MIME)
      if (m) { e.preventDefault(); moveTo(JSON.parse(m), id) }
    },
  })

  const crumbs = pathTo(cur)
  const subfolders = searching ? [] : childrenOf(cur)

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
        <button onClick={() => send({ type: 'libraryAddFolder' })} title="Escolhe uma pasta do computador para a biblioteca ler">+ Pasta do computador…</button>
        <button onClick={() => send({ type: 'libraryGet' })} disabled={scanning}>{scanning ? 'Lendo…' : 'Atualizar'}</button>
        </>}
      </div>
      {open && <>
      <div className="media-folders">
        {folders.length === 0 && <span className="meta">Adicione as pastas onde ficam os vídeos da igreja. O que já está na grade aparece aqui sozinho.</span>}
        {folders.map((f) => (
          <span key={f} className="chip" title={`Pasta do computador lida pela biblioteca: ${f}`}>💻 {f.split('\\').pop()}
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
      <div className="media-body">
        {!compact && (
          <nav className="media-tree">
            <div className={`media-tree-row root ${cur === null ? 'sel' : ''} ${dropOn === '__root' ? 'drop' : ''}`} onClick={() => { setCur(null); setQ(''); setTag(null) }} {...dropProps(null)}>
              📚 Início <span className="meta">(sem pasta)</span>
            </div>
            <FolderTree parent={null} depth={0} childrenOf={childrenOf} cur={cur} setCur={(id) => { setCur(id); setQ(''); setTag(null) }} countIn={countIn} dropOn={dropOn} dropProps={dropProps} />
            <button className="small media-newfolder" onClick={newFolder} title={cur ? `Nova pasta dentro de "${byId.get(cur)?.name}"` : 'Nova pasta'}>+ Nova pasta</button>
            <p className="meta media-tree-hint">Arraste vídeos e imagens para uma pasta. As pastas só organizam a biblioteca; os arquivos continuam no mesmo lugar do computador.</p>
          </nav>
        )}
        <div className="media-main">
          <div className="media-crumbs">
            {searching ? <span className="meta">Resultado da busca em todas as pastas</span> : <>
              <button className="link" onClick={() => setCur(null)} {...dropProps(null)}>Início</button>
              {crumbs.map((f) => (
                <span key={f.id}> › {renaming === f.id
                  ? <input autoFocus className="media-rename" defaultValue={f.name} onFocus={(e) => e.target.select()}
                      onBlur={(e) => { send({ type: 'libraryFolderRename', id: f.id, name: e.target.value }); setRenaming(null) }}
                      onKeyDown={(e) => { if (e.key === 'Enter') { send({ type: 'libraryFolderRename', id: f.id, name: (e.target as HTMLInputElement).value }); setRenaming(null) } if (e.key === 'Escape') setRenaming(null) }} />
                  : <button className="link" onClick={() => setCur(f.id)} {...dropProps(f.id)}>{f.name}</button>}</span>
              ))}
              {cur && <>
                <button className="small" title="Renomear esta pasta" onClick={() => setRenaming(cur)}>✎ Renomear</button>
                <button className="small" title="Apagar esta pasta da biblioteca: o que estava nela sobe um nível (os arquivos não são apagados)"
                  onClick={() => { if (window.confirm(`Apagar a pasta "${byId.get(cur)?.name}" da biblioteca? O que está nela sobe um nível; nenhum arquivo é apagado do computador.`)) { send({ type: 'libraryFolderDelete', id: cur }); setCur(byId.get(cur)?.parent ?? null) } }}>🗑 Apagar pasta</button>
              </>}
              {compact && <button className="small" onClick={newFolder}>+ Pasta</button>}
            </>}
          </div>
          {selected.length > 0 && (
            <div className="media-selbar">
              <b>{selected.length} selecionado(s)</b>
              <span className="meta">arraste para a grade (entram em sequência) ou para uma pasta</span>
              <select value="" onChange={(e) => { if (e.target.value) moveTo(selected, e.target.value === '__root' ? null : e.target.value) }}>
                <option value="">Mover para…</option>
                <option value="__root">Início (sem pasta)</option>
                {collections.map((c) => <option key={c.id} value={c.id}>{pathTo(c.id).map((f) => f.name).join(' › ')}</option>)}
              </select>
              <button className="small" onClick={() => setSelected([])}>Limpar seleção</button>
            </div>
          )}
          <div className="media-grid">
            {subfolders.map((f) => (
              <div key={f.id} className={`media-folder-card ${dropOn === f.id ? 'drop' : ''}`} draggable
                onDragStart={(e) => { e.dataTransfer.setData(FOLDER_MIME, f.id); e.dataTransfer.effectAllowed = 'move' }}
                onClick={() => setCur(f.id)} {...dropProps(f.id)} title="Abrir · arraste arquivos para cá · arraste a pasta para dentro de outra">
                <span className="media-folder-icon">📁</span>
                <span className="media-name">{f.name}</span>
                <span className="meta">{countIn.get(f.id) ?? 0} itens{childrenOf(f.id).length ? ` · ${childrenOf(f.id).length} pastas` : ''}</span>
              </div>
            ))}
            {items === null && <div className="meta">Lendo a biblioteca…</div>}
            {items !== null && shown.length === 0 && subfolders.length === 0 && <div className="meta">{searching ? 'Nada encontrado.' : cur ? 'Pasta vazia. Arraste vídeos e imagens para cá.' : 'Nada solto aqui.'}</div>}
            {visible.map((i) => (
              <MediaCard key={i.path} item={i} thumb={thumbs[i.path] ?? null} frames={previews[i.path]} selected={selected.includes(i.path)}
                where={searching ? pathTo(folderOf(i.path)).map((f) => f.name).join(' › ') : ''}
                onSelect={(e) => select(i.path, e)} dragPaths={() => dragPaths(i.path)}
                editing={editing === i.path} onEdit={(on) => setEditing(on ? i.path : null)} suggestions={[...new Set([...allTags, ...SUGGESTED])]} onPick={onPick} />
            ))}
          </div>
          {shown.length > visible.length && <div className="meta">Mostrando 200 de {shown.length}. Use a busca para achar o resto.</div>}
        </div>
      </div>
      <p className="hint">{onPick ? 'Clique para pôr no fim do roteiro ou arraste para a lista · passe o mouse para ver o vídeo' : 'Arraste para uma célula da grade (vários de uma vez entram em sequência e criam as colunas que faltarem) · Ctrl/Shift + clique seleciona vários · # marca com tags'}</p>
      </>}
    </section>
  )
}

function FolderTree({ parent, depth, childrenOf, cur, setCur, countIn, dropOn, dropProps }: {
  parent: string | null; depth: number; childrenOf: (id: string | null) => LibraryFolder[]; cur: string | null; setCur: (id: string) => void
  countIn: Map<string, number>; dropOn: string | null; dropProps: (id: string | null) => object
}) {
  return <>
    {childrenOf(parent).map((f) => (
      <div key={f.id}>
        <div className={`media-tree-row ${cur === f.id ? 'sel' : ''} ${dropOn === f.id ? 'drop' : ''}`} style={{ paddingLeft: 8 + depth * 14 }}
          draggable onDragStart={(e) => { e.dataTransfer.setData(FOLDER_MIME, f.id); e.dataTransfer.effectAllowed = 'move' }}
          onClick={() => setCur(f.id)} {...dropProps(f.id)}>
          📁 {f.name} <span className="meta">{countIn.get(f.id) ?? ''}</span>
        </div>
        <FolderTree parent={f.id} depth={depth + 1} childrenOf={childrenOf} cur={cur} setCur={setCur} countIn={countIn} dropOn={dropOn} dropProps={dropProps} />
      </div>
    ))}
  </>
}

function MediaCard({ item, thumb, frames, editing, onEdit, suggestions, onPick, selected, onSelect, dragPaths, where }: {
  item: LibraryItem; thumb: string | null; frames?: string[]; editing: boolean; onEdit: (on: boolean) => void; suggestions: string[]; onPick?: (path: string) => void
  selected: boolean; onSelect: (e: React.MouseEvent) => void; dragPaths: () => string[]; where: string
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
    <div className={`media-card ${item.used ? 'used' : ''} ${selected ? 'selected' : ''}`} draggable
      onDragStart={(e) => { e.dataTransfer.setData(MEDIA_MIME, JSON.stringify(dragPaths())); e.dataTransfer.effectAllowed = 'copyMove' }}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      onClick={(e) => { if (onPick && !e.ctrlKey && !e.shiftKey) onPick(item.path); else onSelect(e) }}
      title={`${item.path}\nArraste para uma célula da grade ou para uma pasta · Ctrl/Shift + clique seleciona vários`}>
      <div className="media-thumb">
        {img ? <img src={img} alt="" draggable={false} /> : <div className="media-noimg">{KIND_LABEL[item.kind]}</div>}
        <span className="media-kind">{KIND_LABEL[item.kind]}</span>
        {item.used && <span className="media-used" title="Já está na grade">na grade</span>}
        {selected && <span className="media-check">✓</span>}
      </div>
      <div className="media-name">{item.name}</div>
      {where && <div className="media-where">📁 {where}</div>}
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
