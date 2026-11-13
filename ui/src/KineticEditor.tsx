import { ColorInput } from './ColorInput'
import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { Song } from './bridge'
import { splitSlides } from './lyrics'
import {
  BG_FX, BUILTIN_FONTS, drawFrame, EFFECTS, ensureFonts, PALETTES, plainSlideText, slideScene, styleOf, TRANSITIONS_K,
  type KStyle, type SongKinetic,
} from './kinetic'

/** Quanto tempo a prévia mostra cada slide antes de repetir (entrada, um tempo parado e a saída). */
const PREVIEW_SECONDS = 4.5

/**
 * Animação de letra de uma música: cada slide ganha um efeito, fonte e cores. Ao vivo, quando o
 * slide passa, o próximo entra com a sua animação na layer do clip Letras.
 */
export function KineticEditor({ song, fonts, onSave, onClose }: {
  song: Song; fonts: string[]; onSave: (k: SongKinetic) => void; onClose: () => void
}) {
  const slides = useMemo(() => splitSlides(song.text), [song.text])
  const initial = song.kinetic
  const [enabled, setEnabled] = useState(initial?.enabled ?? true)
  const [transparent, setTransparent] = useState(initial?.transparent ?? false)
  const [light, setLight] = useState(initial?.light ?? false)
  const [styles, setStyles] = useState<KStyle[]>(() =>
    slides.map((sl, i) => initial?.styles?.[i] ?? styleOf(slideScene(sl.text, i, null))))
  const [sel, setSel] = useState(0)
  const [replay, setReplay] = useState(0)
  const canvas = useRef<HTMLCanvasElement>(null)

  const style = styles[sel]
  const setStyle = (patch: Partial<KStyle>) => { setStyles(styles.map((s, i) => (i === sel ? { ...s, ...patch } : s))); setReplay((r) => r + 1) }
  const fontList = useMemo(() => {
    const builtin = BUILTIN_FONTS.map((f) => f.family)
    return [...builtin, 'Impact', 'Arial Black', 'Bahnschrift', 'Segoe UI Black', ...fonts.filter((f) => !builtin.includes(f))]
  }, [fonts])

  // prévia do slide escolhido: entra, fica e sai, e repete
  useEffect(() => {
    if (!slides[sel] || !style) return
    const scene = { ...slideScene(slides[sel].text, sel, style), duration: PREVIEW_SECONDS - 0.6 }
    const project = { version: 1 as const, name: '', lead: 0, scenes: [scene] }
    let raf = 0
    let start = performance.now()
    let ready = false
    ensureFonts(project).then(() => { ready = true; start = performance.now() })
    const loop = () => {
      const ctx = canvas.current?.getContext('2d')
      if (ctx && ready) {
        const t = ((performance.now() - start) / 1000) % PREVIEW_SECONDS
        if (t > scene.duration) { ctx.setTransform(1, 0, 0, 1, 0, 0); if (transparent) ctx.clearRect(0, 0, 960, 540); else { ctx.fillStyle = scene.bg; ctx.fillRect(0, 0, 960, 540) } }
        else drawFrame(ctx, project, t, transparent)
      }
      raf = requestAnimationFrame(loop)
    }
    raf = requestAnimationFrame(loop)
    return () => cancelAnimationFrame(raf)
  }, [sel, style, slides, transparent, replay])

  const autoAll = () => { setStyles(slides.map((sl, i) => styleOf(slideScene(sl.text, i, null)))); setReplay((r) => r + 1) }
  const allLike = (keys: (keyof KStyle)[]) => setStyles(styles.map((s) => ({ ...s, ...Object.fromEntries(keys.map((k) => [k, style[k]])) })))
  const save = () => { onSave({ enabled, transparent, light, styles }); onClose() }

  return createPortal(
    <div className="modal-back kin-back">
      <div className="modal kin" onMouseDown={(e) => e.stopPropagation()}>
        <div className="kin-head">
          <h3>Animação de letra · {song.title}</h3>
          <label className="row kin-check" title="Desligada, a música volta a aparecer com a letra normal (cena e estilo da aba Letras)">
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Ligada nesta música
          </label>
          <label className="row kin-check" title="Sem a cor de fundo: o texto animado fica por cima do vídeo que estiver tocando embaixo">
            <input type="checkbox" checked={transparent} onChange={(e) => setTransparent(e.target.checked)} /> Fundo transparente (texto sobre o vídeo)
          </label>
          <label className="row kin-check" title="Desenha em 1280×720: bem mais leve para computadores fracos">
            <input type="checkbox" checked={light} onChange={(e) => setLight(e.target.checked)} /> Modo leve
          </label>
          <span className="kin-spacer" />
          <button className="small" onClick={autoAll} title="Escolhe de novo efeito, fonte e cores de todos os slides">🎲 Estilo automático</button>
          <button onClick={onClose}>Cancelar</button>
          <button className="primary" onClick={save}>Salvar</button>
        </div>

        {slides.length === 0 ? <p className="meta">Esta música ainda não tem slides. Escreva a letra primeiro.</p> : (
          <div className="kin-body">
            <div className="kin-list">
              <div className="kin-list-head"><b>Slides ({slides.length})</b></div>
              <div className="kin-scenes">
                {slides.map((sl, i) => (
                  <div key={i} className={`kin-scene ${i === sel ? 'sel' : ''}`} onClick={() => setSel(i)}>
                    <span className="kin-swatch" style={{ background: transparent ? '#333' : styles[i].bg, color: styles[i].color, borderColor: styles[i].accent }}>{i + 1}</span>
                    <span className="kin-scene-text">{plainSlideText(sl.text).replace(/\n/g, ' / ')}</span>
                    <span className="kin-scene-meta">{EFFECTS.find((e) => e.id === styles[i].effect)?.label}</span>
                  </div>
                ))}
              </div>
            </div>

            <div className="kin-stage">
              <div className={`kin-canvas-wrap ${transparent ? 'checker' : ''}`}>
                <canvas ref={canvas} width={960} height={540} className="kin-canvas" onClick={() => setReplay((r) => r + 1)} title="Clique para ver de novo" />
              </div>
              <div className="kin-transport">
                <button className="small" onClick={() => setSel(Math.max(0, sel - 1))} disabled={sel === 0}>◀ Slide anterior</button>
                <button className="small" onClick={() => setReplay((r) => r + 1)}>↻ Ver de novo</button>
                <button className="small" onClick={() => setSel(Math.min(slides.length - 1, sel + 1))} disabled={sel >= slides.length - 1}>Próximo slide ▶</button>
              </div>
              <p className="meta">
                Ao vivo, cada slide entra com a sua animação quando você passa para ele, e o anterior sai. O texto aparece na
                layer do clip Letras. Para mudar o texto, edite a letra da música; aqui ficam só o efeito e as cores de cada slide.
              </p>
            </div>

            {style && (
              <div className="kin-props">
                <div className="kin-props-head"><b>Slide {sel + 1}</b></div>
                <label className="kin-field">Efeito
                  <select value={style.effect} onChange={(e) => setStyle({ effect: e.target.value as KStyle['effect'] })}>
                    {EFFECTS.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                  </select>
                  <span className="meta">{EFFECTS.find((x) => x.id === style.effect)?.hint}</span>
                </label>
                {style.effect === 'caos' && (
                  <label className="kin-field">Palavra espalhada <input value={style.word ?? ''} placeholder="(a última do slide)" onChange={(e) => setStyle({ word: e.target.value })} /></label>
                )}
                <label className="kin-field">Fonte
                  <select value={style.font} onChange={(e) => setStyle({ font: e.target.value })}>
                    {fontList.map((f) => <option key={f} value={f} style={{ fontFamily: f }}>{BUILTIN_FONTS.find((b) => b.family === f)?.label ?? f}</option>)}
                  </select>
                </label>
                <label className="kin-field">Tamanho <input type="range" min={0.4} max={1.6} step={0.05} value={style.size} onChange={(e) => setStyle({ size: Number(e.target.value) })} /></label>
                <label className="row"><input type="checkbox" checked={style.upper} onChange={(e) => setStyle({ upper: e.target.checked })} /> TUDO MAIÚSCULO</label>
                <div className="kin-colors">
                  {!transparent && <label>Fundo <ColorInput value={style.bg} onChange={(e) => setStyle({ bg: e.target.value })} /></label>}
                  <label>Texto <ColorInput value={style.color} onChange={(e) => setStyle({ color: e.target.value })} /></label>
                  <label>Destaque <ColorInput value={style.accent} onChange={(e) => setStyle({ accent: e.target.value })} /></label>
                </div>
                <div className="kin-palettes">
                  {PALETTES.map(([bg, color, accent], i) => (
                    <button key={i} className="kin-pal" style={{ background: bg, color, borderColor: accent }} onClick={() => setStyle({ bg, color, accent })} title="Usar estas cores">Aa</button>
                  ))}
                </div>
                <label className="kin-field">Fundo animado
                  <select value={style.bgFx} onChange={(e) => setStyle({ bgFx: e.target.value as KStyle['bgFx'] })}>
                    {BG_FX.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                  </select>
                </label>
                {!transparent && (
                  <label className="kin-field">Entrada (depois do slide anterior)
                    <select value={style.transition} onChange={(e) => setStyle({ transition: e.target.value as KStyle['transition'] })}>
                      {TRANSITIONS_K.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}
                    </select>
                  </label>
                )}
                <div className="kin-all">
                  <span className="meta">Usar deste slide em todos:</span>
                  <button className="small" onClick={() => allLike(['font', 'upper', 'size'])}>Fonte</button>
                  <button className="small" onClick={() => allLike(['bg', 'color', 'accent'])}>Cores</button>
                  <button className="small" onClick={() => allLike(['effect', 'bgFx', 'transition', 'word'])}>Efeito</button>
                  <button className="small" onClick={() => setStyles(styles.map(() => ({ ...style })))}>Tudo</button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}
