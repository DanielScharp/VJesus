import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { send } from './bridge'
import { pauseShortcuts } from './shortcuts'
import {
  autoStyle, BG_FX, BUILTIN_FONTS, drawFrame, EFFECTS, ensureFonts, newId, PALETTES, projectDuration, sceneAt, scenesFromLyrics,
  sceneStart, TRANSITIONS_K, type KProject, type KScene,
} from './kinetic'
import { decodeAudio, exportMp4, toBase64 } from './kineticExport'

const SAMPLE: KProject = {
  version: 1, name: 'Nova animação', lead: 0, includeAudio: true,
  scenes: scenesFromLyrics('Escreva a primeira frase\nCole a letra inteira\nem "Colar letra"'),
}

const fmt = (s: number) => `${Math.floor(s / 60)}:${(s % 60).toFixed(1).padStart(4, '0')}`

/**
 * Editor de animação de letra (tipografia cinética): cada frase é uma cena com efeito, fonte e cores.
 * Marca o tempo batendo na barra de espaço com a música tocando, e gera um vídeo que entra no grid.
 */
export function KineticEditor({ layer, column, initial, fonts, onClose }: {
  layer: number; column: number; initial?: KProject | null; fonts: string[]; onClose: () => void
}) {
  const [p, setP] = useState<KProject>(() => initial ?? SAMPLE)
  const [sel, setSel] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [time, setTime] = useState(0)
  const [paste, setPaste] = useState<string | null>(null)
  const [audioFile, setAudioFile] = useState<File | null>(null)
  const [audioUrl, setAudioUrl] = useState<string | null>(null)
  const [tap, setTap] = useState<number | null>(null) // marcando o tempo: próxima cena a marcar
  const [exporting, setExporting] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const audio = useRef<HTMLAudioElement>(null)
  const clock = useRef({ t: 0, startedAt: 0, startT: 0 })
  const cancel = useRef(false)
  const pRef = useRef(p)
  pRef.current = p
  const tapRef = useRef<number | null>(null)
  tapRef.current = tap

  const total = projectDuration(p)
  const scene = p.scenes[sel] ?? p.scenes[0]
  const fontList = useMemo(() => {
    const builtin = BUILTIN_FONTS.map((f) => f.family)
    return [...builtin, ...['Impact', 'Arial Black', 'Bahnschrift', 'Segoe UI Black'], ...fonts.filter((f) => !builtin.includes(f))]
  }, [fonts])

  // tempo atual: o da música (se carregada) ou um relógio
  const now = () => {
    const a = audio.current
    if (playing && a && audioUrl) return a.currentTime
    if (playing) return clock.current.startT + (performance.now() - clock.current.startedAt) / 1000
    return clock.current.t
  }

  useEffect(() => { ensureFonts(p).then(() => paint(clock.current.t)) }, [p.scenes.map((s) => s.font).join()]) // eslint-disable-line

  function paint(t: number) {
    const ctx = canvas.current?.getContext('2d')
    if (!ctx) return
    let proj = pRef.current
    const k = tapRef.current
    if (k !== null) {
      // marcando o tempo: a frase esperada fica na tela até a próxima batida (a introdução, até a primeira)
      proj = { ...proj, scenes: proj.scenes.map((s) => ({ ...s })) }
      if (k === 0) proj.lead = t + 0.05
      else if (k - 1 < proj.scenes.length) {
        const sc = proj.scenes[k - 1]
        sc.duration = Math.max(sc.duration, t - sceneStart(proj, k - 1) + 0.6)
      }
    }
    drawFrame(ctx, proj, t)
  }

  // desenha a prévia (sempre que muda algo; tocando, a cada quadro)
  useEffect(() => {
    if (!playing) { paint(clock.current.t); return }
    let raf = 0
    const loop = () => {
      const t = now()
      clock.current.t = t
      paint(t)
      setTime(t)
      if (t >= projectDuration(pRef.current) && tap === null) { stop(); return }
      raf = requestAnimationFrame(loop)
    }
    raf = requestAnimationFrame(loop)
    return () => cancelAnimationFrame(raf)
  }, [playing, p, audioUrl, tap]) // eslint-disable-line

  function play(from = clock.current.t) {
    if (from >= total - 0.05) from = 0
    clock.current = { t: from, startT: from, startedAt: performance.now() }
    if (audio.current && audioUrl) { audio.current.currentTime = from; void audio.current.play() }
    setPlaying(true)
  }
  function stop() {
    audio.current?.pause()
    clock.current.t = now()
    setPlaying(false)
    setTap(null)
  }
  function seek(t: number) {
    clock.current = { t, startT: t, startedAt: performance.now() }
    if (audio.current && audioUrl) audio.current.currentTime = t
    setTime(t)
    if (!playing) paint(t)
  }

  const update = (patch: Partial<KProject>) => setP((x) => ({ ...x, ...patch }))
  const setScene = (i: number, patch: Partial<KScene>) => setP((x) => ({ ...x, scenes: x.scenes.map((s, k) => (k === i ? { ...s, ...patch } : s)) }))
  const goScene = (i: number) => { setSel(i); seek(sceneStart(p, i) + Math.min(1.2, p.scenes[i].duration * 0.6)) }

  // ---------------- marcar o tempo (barra de espaço a cada frase)
  function startTap() {
    setTap(0)
    seek(0)
    play(0)
  }
  useEffect(() => {
    if (tap === null) return
    pauseShortcuts(true) // a barra de espaço é só para marcar o tempo
    ;(document.activeElement as HTMLElement | null)?.blur?.()
    const onKey = (e: KeyboardEvent) => {
      if (e.code === 'Escape') { stop(); return }
      if (e.code !== 'Space' && e.code !== 'Enter') return
      e.preventDefault()
      e.stopImmediatePropagation()
      if (e.repeat) return
      const t = now()
      setP((x) => {
        const scenes = x.scenes.map((s) => ({ ...s }))
        let lead = x.lead
        if (tap === 0) lead = Math.round(t * 100) / 100
        else {
          const start = lead + scenes.slice(0, tap - 1).reduce((s, sc) => s + sc.duration, 0)
          scenes[tap - 1].duration = Math.max(0.3, Math.round((t - start) * 100) / 100)
        }
        return { ...x, lead, scenes }
      })
      if (tap >= p.scenes.length) { stop(); return }
      setSel(tap)
      setTap(tap + 1)
    }
    window.addEventListener('keydown', onKey, true)
    return () => { window.removeEventListener('keydown', onKey, true); pauseShortcuts(false) }
  }, [tap, p.scenes.length]) // eslint-disable-line

  // ---------------- cenas
  function addScene() {
    const s = autoStyle('NOVA FRASE', p.scenes.length)
    const scenes = [...p.scenes]
    scenes.splice(sel + 1, 0, s)
    update({ scenes })
    setSel(sel + 1)
  }
  function removeScene(i: number) {
    if (p.scenes.length <= 1) return
    update({ scenes: p.scenes.filter((_, k) => k !== i) })
    setSel(Math.max(0, Math.min(sel, p.scenes.length - 2)))
  }
  function moveScene(i: number, d: number) {
    const j = i + d
    if (j < 0 || j >= p.scenes.length) return
    const scenes = [...p.scenes]
    ;[scenes[i], scenes[j]] = [scenes[j], scenes[i]]
    update({ scenes })
    setSel(j)
  }
  function applyPaste(replace: boolean) {
    if (!paste?.trim()) { setPaste(null); return }
    const added = scenesFromLyrics(paste, replace ? 0 : p.scenes.length)
    update({ scenes: replace ? added : [...p.scenes, ...added] })
    setSel(replace ? 0 : p.scenes.length)
    setPaste(null)
  }
  function restyleAll() {
    update({ scenes: p.scenes.map((s, i) => ({ ...autoStyle(s.text, i), id: s.id, duration: s.duration, upper: s.upper })) })
  }

  // ---------------- música
  function loadAudio(f: File | undefined) {
    if (!f) return
    if (audioUrl) URL.revokeObjectURL(audioUrl)
    setAudioFile(f)
    setAudioUrl(URL.createObjectURL(f))
    update({ audioName: f.name })
  }

  // ---------------- gerar o vídeo
  async function generate() {
    stop()
    setError(null); setDone(null)
    cancel.current = false
    setExporting(0)
    const id = newId() + Date.now().toString(36)
    const project = pRef.current
    send({ type: 'kineticBegin', id, layer, column, name: project.name || 'Animação', project })
    let ok = false
    try {
      const buf = audioFile && project.includeAudio ? await decodeAudio(audioFile) : null
      ok = await exportMp4(project, {
        audio: buf,
        onData: (data, pos) => send({ type: 'kineticChunk', id, pos, data: toBase64(data) }),
        onProgress: (f) => setExporting(f),
        cancelled: () => cancel.current,
      })
      if (ok) setDone('Vídeo gerado e colocado no grid.')
    } catch (e) {
      setError(`Não consegui gerar o vídeo: ${e instanceof Error ? e.message : String(e)}`)
    } finally {
      send({ type: 'kineticEnd', id, ok })
      setExporting(null)
    }
  }

  const cur = sceneAt(p, time)
  const tapNext = tap !== null ? (tap === 0 ? p.scenes[0] : p.scenes[tap]) : null

  return createPortal(
    <div className="modal-back kin-back">
      <div className="modal kin" onMouseDown={(e) => e.stopPropagation()}>
        <div className="kin-head">
          <h3>Animação de letra</h3>
          <input className="kin-name" value={p.name} onChange={(e) => update({ name: e.target.value })} title="Nome do clip no grid" />
          <button className="small" onClick={() => setPaste('')} title="Cola a letra: cada linha vira uma frase animada">📋 Colar letra…</button>
          <button className="small" onClick={restyleAll} title="Escolhe de novo efeito, fonte e cores de todas as frases (mantém textos e tempos)">🎲 Estilo automático</button>
          <span className="kin-spacer" />
          <button onClick={onClose} disabled={exporting !== null}>Fechar</button>
        </div>

        <div className="kin-body">
          <div className="kin-list">
            <div className="kin-list-head">
              <b>Frases ({p.scenes.length})</b>
              <button className="small" onClick={addScene}>+ Frase</button>
            </div>
            <div className="kin-scenes">
              {p.scenes.map((s, i) => (
                <div key={s.id} className={`kin-scene ${i === sel ? 'sel' : ''} ${cur.index === i && playing ? 'live' : ''}`} onClick={() => goScene(i)}>
                  <span className="kin-swatch" style={{ background: s.bg, color: s.color, borderColor: s.accent }}>Aa</span>
                  <span className="kin-scene-text">{s.text.replace(/\n/g, ' / ') || '(vazia)'}</span>
                  <span className="kin-scene-meta">{EFFECTS.find((e) => e.id === s.effect)?.label.split(' (')[0]} · {s.duration.toFixed(1)} s</span>
                </div>
              ))}
            </div>
          </div>

          <div className="kin-stage">
            <div className="kin-canvas-wrap">
              <canvas ref={canvas} width={960} height={540} className="kin-canvas" onClick={() => (playing ? stop() : play())} />
              {tapNext && (
                <div className="kin-tap">
                  <b>Marcando o tempo:</b> aperte <kbd>Espaço</kbd> quando começar
                  <div className="kin-tap-next">“{tapNext.text.replace(/\n/g, ' ')}”</div>
                  {tap !== null && tap >= p.scenes.length && <div>(fim da última frase)</div>}
                  <span className="meta">Esc para parar</span>
                </div>
              )}
            </div>
            <div className="kin-transport">
              <button className="small" onClick={() => (playing ? stop() : play())}>{playing ? '❚❚ Pausar' : '▶ Tocar'}</button>
              <button className="small" onClick={() => { stop(); seek(0) }}>⏮</button>
              <span className="kin-time">{fmt(time)} / {fmt(total)}</span>
              <div className="kin-timeline" onMouseDown={(e) => {
                const r = e.currentTarget.getBoundingClientRect()
                const t = ((e.clientX - r.left) / r.width) * total
                seek(Math.max(0, Math.min(total, t)))
                const i = sceneAt(p, t).index
                if (i >= 0) setSel(i)
              }}>
                {p.lead > 0 && <div className="kin-block lead" style={{ width: `${(p.lead / total) * 100}%` }} title="Introdução (sem texto)" />}
                {p.scenes.map((s, i) => (
                  <div key={s.id} className={`kin-block ${i === sel ? 'sel' : ''}`} style={{ width: `${(Math.max(0.2, s.duration) / total) * 100}%`, background: s.bg, color: s.color }} title={s.text}>
                    {s.text.split('\n')[0]}
                  </div>
                ))}
                <div className="kin-head-line" style={{ left: `${(time / Math.max(0.01, total)) * 100}%` }} />
              </div>
            </div>
            <div className="kin-music">
              <label className="small-btn">
                🎵 {audioFile ? audioFile.name : p.audioName ? `Carregar de novo: ${p.audioName}` : 'Carregar a música…'}
                <input type="file" accept="audio/*,video/*" hidden onChange={(e) => loadAudio(e.target.files?.[0])} />
              </label>
              <button className="small primary" onClick={startTap} disabled={playing && tap === null}
                title="Toca do começo: aperte Espaço no instante em que cada frase começa (a primeira batida marca o fim da introdução)">
                ⏱ Marcar o tempo
              </button>
              <label className="row" title="O som da música vai junto no vídeo (o clip toca com áudio)">
                <input type="checkbox" checked={p.includeAudio !== false} disabled={!audioFile} onChange={(e) => update({ includeAudio: e.target.checked })} /> Gravar o som da música no vídeo
              </label>
              <label className="kin-lead">Introdução <input type="number" min={0} step={0.1} value={p.lead} onChange={(e) => update({ lead: Math.max(0, Number(e.target.value) || 0) })} /> s</label>
              {audioUrl && <audio ref={audio} src={audioUrl} preload="auto" />}
            </div>
            {!audioFile && <p className="meta">Sem música, cada frase usa a duração escrita ao lado. Com a música carregada, use "Marcar o tempo" para acertar cada frase no ritmo.</p>}
          </div>

          {scene && (
            <div className="kin-props">
              <div className="kin-props-head">
                <b>Frase {sel + 1}</b>
                <button className="small" title="Subir" onClick={() => moveScene(sel, -1)}>↑</button>
                <button className="small" title="Descer" onClick={() => moveScene(sel, 1)}>↓</button>
                <button className="small" title="Duplicar" onClick={() => { const scenes = [...p.scenes]; scenes.splice(sel + 1, 0, { ...scene, id: newId() }); update({ scenes }); setSel(sel + 1) }}>⧉</button>
                <button className="small danger" title="Tirar esta frase" disabled={p.scenes.length <= 1} onClick={() => removeScene(sel)}>✕</button>
              </div>
              <label className="kin-field">Texto <span className="meta">(Enter = outra linha)</span>
                <textarea rows={3} value={scene.text} onChange={(e) => setScene(sel, { text: e.target.value })} />
              </label>
              <label className="kin-field">Efeito
                <select value={scene.effect} onChange={(e) => setScene(sel, { effect: e.target.value as KScene['effect'] })}>
                  {EFFECTS.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                </select>
                <span className="meta">{EFFECTS.find((x) => x.id === scene.effect)?.hint}</span>
              </label>
              {scene.effect === 'caos' && (
                <label className="kin-field">Palavra espalhada <input value={scene.word ?? ''} placeholder="(a última da frase)" onChange={(e) => setScene(sel, { word: e.target.value })} /></label>
              )}
              <label className="kin-field">Fonte
                <select value={scene.font} onChange={(e) => setScene(sel, { font: e.target.value })}>
                  {fontList.map((f) => <option key={f} value={f} style={{ fontFamily: f }}>{BUILTIN_FONTS.find((b) => b.family === f)?.label ?? f}</option>)}
                </select>
              </label>
              <div className="kin-grid2">
                <label className="kin-field">Tamanho <input type="range" min={0.4} max={1.6} step={0.05} value={scene.size} onChange={(e) => setScene(sel, { size: Number(e.target.value) })} /></label>
                <label className="kin-field">Duração (s) <input type="number" min={0.3} step={0.1} value={scene.duration} onChange={(e) => setScene(sel, { duration: Math.max(0.3, Number(e.target.value) || 0.3) })} /></label>
              </div>
              <label className="row"><input type="checkbox" checked={scene.upper} onChange={(e) => setScene(sel, { upper: e.target.checked })} /> TUDO MAIÚSCULO</label>
              <div className="kin-colors">
                <label>Fundo <input type="color" value={scene.bg} onChange={(e) => setScene(sel, { bg: e.target.value })} /></label>
                <label>Texto <input type="color" value={scene.color} onChange={(e) => setScene(sel, { color: e.target.value })} /></label>
                <label>Destaque <input type="color" value={scene.accent} onChange={(e) => setScene(sel, { accent: e.target.value })} /></label>
              </div>
              <div className="kin-palettes">
                {PALETTES.map(([bg, color, accent], i) => (
                  <button key={i} className="kin-pal" style={{ background: bg, color, borderColor: accent }} onClick={() => setScene(sel, { bg, color, accent })} title="Usar estas cores">Aa</button>
                ))}
              </div>
              <label className="kin-field">Fundo animado
                <select value={scene.bgFx} onChange={(e) => setScene(sel, { bgFx: e.target.value as KScene['bgFx'] })}>
                  {BG_FX.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                </select>
              </label>
              <label className="kin-field">Entrada (depois da frase anterior)
                <select value={scene.transition} onChange={(e) => setScene(sel, { transition: e.target.value as KScene['transition'] })}>
                  {TRANSITIONS_K.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                </select>
              </label>
              <button className="small" onClick={() => update({ scenes: p.scenes.map((s) => ({ ...s, font: scene.font, upper: scene.upper })) })}>Usar esta fonte em todas as frases</button>
            </div>
          )}
        </div>

        <div className="kin-foot">
          {error && <span className="warn-text">{error}</span>}
          {done && <span className="ok-text">✔ {done}</span>}
          {exporting !== null ? (
            <>
              <div className="kin-progress"><div style={{ width: `${exporting * 100}%` }} /></div>
              <span>Gerando o vídeo… {Math.round(exporting * 100)}%</span>
              <button className="small" onClick={() => { cancel.current = true }}>Cancelar</button>
            </>
          ) : (
            <>
              <span className="meta">O vídeo (1920×1080, 30 qps, {fmt(total)}) fica salvo na pasta do VJesus e entra nesta célula do grid. Para mudar depois: botão direito no clip → Editar animação.</span>
              <button className="primary" onClick={generate} disabled={!p.scenes.length}>🎬 Gerar vídeo e pôr no grid</button>
            </>
          )}
        </div>

        {paste !== null && (
          <div className="modal-back" onMouseDown={() => setPaste(null)}>
            <div className="modal kin-paste" onMouseDown={(e) => e.stopPropagation()}>
              <h3>Colar a letra</h3>
              <p className="meta">Cada linha vira uma frase animada, com efeito e cores escolhidos automaticamente (dá para trocar depois). Use “ / ” para quebrar uma frase em duas linhas na mesma tela.</p>
              <textarea autoFocus rows={14} value={paste} onChange={(e) => setPaste(e.target.value)} placeholder={'Primeira frase\nSegunda frase\nTerceira / com duas linhas'} />
              <div className="modal-actions">
                <button onClick={() => setPaste(null)}>Cancelar</button>
                <button onClick={() => applyPaste(false)}>Acrescentar no fim</button>
                <button className="primary" onClick={() => applyPaste(true)}>Trocar todas as frases</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}
