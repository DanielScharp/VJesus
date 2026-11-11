import { useEffect } from 'react'
import { onMessage, send } from './bridge'
import { injectFontFaces } from './fontFaces'
import { drawFrame, ensureFonts, slideScene, type KProject, type KScene, type KStyle } from './kinetic'

type SceneMsg = { type: 'kineticScene'; seq: number; text: string | null; index: number; style: KStyle | null; transparent: boolean; light: boolean; cut?: boolean }
type Live = { scene: KScene; start: number; leaveAt: number | null }

const OUT = 0.35
const FADE = 0.3
const now = () => performance.now() / 1000

/**
 * Página escondida (index.html#kinetic) que desenha a animação de letra ao vivo. Cada slide que chega
 * entra animado; o anterior sai com a animação de saída. Os quadros vão para o motor por um buffer
 * compartilhado: escreve, avisa ("kineticFrame") e espera o motor responder ("kineticAck").
 */
export function KineticLiveApp() {
  useEffect(() => {
    const canvas = document.createElement('canvas')
    canvas.width = 1920
    canvas.height = 1080
    let ctx = canvas.getContext('2d', { willReadFrequently: true })!
    let shared: Uint8Array | null = null
    let waiting = false
    let prev: Live | null = null
    let cur: Live | null = null
    let transparent = false
    let idle = true
    let lastDraw = 0

    const wv = (window as unknown as { chrome?: { webview?: { addEventListener(t: string, fn: (e: { getBuffer(): ArrayBuffer }) => void): void } } }).chrome?.webview
    wv?.addEventListener('sharedbufferreceived', (e) => { shared = new Uint8Array(e.getBuffer()); waiting = false })

    function setSize(light: boolean) {
      const w = light ? 1280 : 1920, h = light ? 720 : 1080
      if (canvas.width !== w) { canvas.width = w; canvas.height = h; ctx = canvas.getContext('2d', { willReadFrequently: true })! }
    }

    function onScene(m: SceneMsg) {
      const t = now()
      transparent = m.transparent
      setSize(m.light)
      if (m.cut) { prev = null; cur = null; idle = false; return }
      // quem estava no ar começa a sair; quem estava saindo some de vez
      if (cur) { cur.leaveAt = t; prev = cur } else if (prev && prev.leaveAt !== null && t - prev.leaveAt > OUT + FADE) prev = null
      cur = null
      if (m.text && m.text.trim()) {
        const scene = slideScene(m.text, Math.max(0, m.index), m.style)
        scene.duration = 1e6
        // entra quando a saída do anterior termina
        cur = { scene, start: prev?.leaveAt != null ? prev.leaveAt + OUT : t, leaveAt: null }
        void ensureFonts({ version: 1, name: '', lead: 0, scenes: [scene] })
      }
      idle = false
    }

    /** O quadro do instante t: o slide saindo e o que entra, como uma animação de duas cenas. */
    function render(t: number): boolean {
      const scenes: KScene[] = []
      let base = t
      if (prev && prev.leaveAt !== null) {
        const d = prev.leaveAt - prev.start + OUT
        if (t < prev.start + d || cur) { scenes.push({ ...prev.scene, duration: Math.max(0.2, d) }); base = prev.start }
      }
      if (cur) {
        scenes.push(cur.scene)
        if (scenes.length === 1) base = cur.start
      }
      ctx.setTransform(1, 0, 0, 1, 0, 0)
      if (!scenes.length) { ctx.clearRect(0, 0, canvas.width, canvas.height); return false }
      const p: KProject = { version: 1, name: '', lead: 0, scenes }
      drawFrame(ctx, p, Math.max(0, t - base), transparent)
      // aparece/some suavemente com o fundo (primeiro slide e fim da música)
      let alpha = 1
      if (cur && !prev) alpha = Math.min(1, (t - cur.start) / FADE + 0.001)
      if (!cur && prev && prev.leaveAt !== null) alpha = 1 - Math.min(1, Math.max(0, (t - prev.leaveAt) / OUT))
      if (alpha < 1) {
        ctx.setTransform(1, 0, 0, 1, 0, 0)
        ctx.globalCompositeOperation = 'destination-in'
        ctx.fillStyle = `rgba(0,0,0,${Math.max(0, alpha)})`
        ctx.fillRect(0, 0, canvas.width, canvas.height)
        ctx.globalCompositeOperation = 'source-over'
      }
      if (!cur && prev && prev.leaveAt !== null && t > prev.leaveAt + OUT + FADE) { prev = null; return false }
      return true
    }

    const timer = window.setInterval(() => {
      if (!shared || waiting || idle) return
      const t = now()
      if (t - lastDraw < 1 / 30 - 0.004) return
      lastDraw = t
      const active = render(t)
      const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data
      if (data.length > shared.length) return
      shared.set(data)
      waiting = true
      send({ type: 'kineticFrame', w: canvas.width, h: canvas.height } as never)
      if (!active) idle = true // mandou o quadro vazio: descansa até o próximo slide
    }, 8)

    const off = onMessage((m) => {
      const msg = m as unknown as { type: string }
      if (msg.type === 'kineticAck') waiting = false
      else if (msg.type === 'kineticScene') onScene(msg as SceneMsg)
      else if (msg.type === 'fonts') injectFontFaces((msg as unknown as { faces: Parameters<typeof injectFontFaces>[0] }).faces)
    })
    send({ type: 'kineticHello' } as never)
    return () => { off(); window.clearInterval(timer) }
  }, [])
  return null
}
