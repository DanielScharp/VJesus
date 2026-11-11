// Grava a animação de letra como MP4 (H.264, e o som da música se pedido) usando o codificador do navegador.
// Mais rápido que tempo real e sempre igual à prévia: cada quadro é desenhado no tempo exato.
import { Muxer, StreamTarget } from 'mp4-muxer'
import { drawFrame, ensureFonts, KH, KW, projectDuration, type KProject } from './kinetic'

export const FPS = 30

async function pickVideoConfig(): Promise<VideoEncoderConfig | null> {
  if (typeof VideoEncoder === 'undefined') return null
  for (const codec of ['avc1.640028', 'avc1.4d0028', 'avc1.42e028']) {
    for (const hw of ['prefer-hardware', 'no-preference'] as const) {
      const cfg: VideoEncoderConfig = { codec, width: KW, height: KH, bitrate: 8_000_000, framerate: FPS, avc: { format: 'avc' }, hardwareAcceleration: hw }
      try { if ((await VideoEncoder.isConfigSupported(cfg)).supported) return cfg } catch { /* tenta o próximo */ }
    }
  }
  return null
}

async function pickAudioConfig(): Promise<{ cfg: AudioEncoderConfig; codec: 'aac' | 'opus' } | null> {
  if (typeof AudioEncoder === 'undefined') return null
  for (const [codec, name] of [['mp4a.40.2', 'aac'], ['opus', 'opus']] as const) {
    const cfg: AudioEncoderConfig = { codec, sampleRate: 48000, numberOfChannels: 2, bitrate: 192_000 }
    try { if ((await AudioEncoder.isConfigSupported(cfg)).supported) return { cfg, codec: name } } catch { /* próximo */ }
  }
  return null
}

/** Decodifica a música em 48 kHz (para gravar junto). */
export async function decodeAudio(file: Blob): Promise<AudioBuffer> {
  const ctx = new OfflineAudioContext(2, 48000, 48000)
  return ctx.decodeAudioData(await file.arrayBuffer())
}

export async function canExport() { return (await pickVideoConfig()) !== null }

/**
 * Gera o vídeo. onData recebe os pedaços do arquivo e a posição de cada um (o fim do arquivo volta para
 * acertar o cabeçalho). Retorna false se foi cancelado.
 */
export async function exportMp4(p: KProject, opts: {
  audio?: AudioBuffer | null
  onData: (data: Uint8Array, position: number) => void
  onProgress: (fraction: number) => void
  cancelled: () => boolean
}): Promise<boolean> {
  const vcfg = await pickVideoConfig()
  if (!vcfg) throw new Error('Este computador não tem codificador de vídeo H.264 disponível no navegador do VJesus.')
  const acfg = opts.audio ? await pickAudioConfig() : null
  await ensureFonts(p)

  const target = new StreamTarget({ onData: (data, position) => opts.onData(data, position), chunked: true, chunkSize: 1 << 20 })
  const muxer = new Muxer({
    target,
    video: { codec: 'avc', width: KW, height: KH, frameRate: FPS },
    audio: acfg ? { codec: acfg.codec, numberOfChannels: 2, sampleRate: 48000 } : undefined,
    fastStart: false,
    firstTimestampBehavior: 'offset',
  })

  let failure: unknown = null
  const venc = new VideoEncoder({ output: (chunk, meta) => muxer.addVideoChunk(chunk, meta), error: (e) => { failure = e } })
  venc.configure(vcfg)
  const aenc = acfg ? new AudioEncoder({ output: (chunk, meta) => muxer.addAudioChunk(chunk, meta), error: (e) => { failure = e } }) : null
  aenc?.configure(acfg!.cfg)

  const canvas = document.createElement('canvas')
  canvas.width = KW
  canvas.height = KH
  const ctx = canvas.getContext('2d', { alpha: false })!
  const total = projectDuration(p)
  const frames = Math.ceil(total * FPS)
  const audio = opts.audio
  let audioPos = 0 // em amostras
  const audioEnd = audio ? Math.min(audio.length, Math.round(total * 48000)) : 0

  const pushAudioUntil = (sample: number) => {
    if (!audio || !aenc) return
    const left = audio.getChannelData(0), right = audio.numberOfChannels > 1 ? audio.getChannelData(1) : left
    while (audioPos < Math.min(sample, audioEnd)) {
      const n = Math.min(4800, audioEnd - audioPos)
      const buf = new Float32Array(n * 2)
      buf.set(left.subarray(audioPos, audioPos + n), 0)
      buf.set(right.subarray(audioPos, audioPos + n), n)
      const data = new AudioData({ format: 'f32-planar', sampleRate: 48000, numberOfFrames: n, numberOfChannels: 2, timestamp: Math.round((audioPos / 48000) * 1e6), data: buf })
      aenc.encode(data)
      data.close()
      audioPos += n
    }
  }

  for (let i = 0; i < frames; i++) {
    if (failure) throw failure
    if (opts.cancelled()) { try { venc.close(); aenc?.close() } catch { /* já fechado */ } return false }
    drawFrame(ctx, p, i / FPS)
    const frame = new VideoFrame(canvas, { timestamp: Math.round((i * 1e6) / FPS), duration: Math.round(1e6 / FPS) })
    venc.encode(frame, { keyFrame: i % (FPS * 2) === 0 })
    frame.close()
    pushAudioUntil(Math.round(((i + 1) / FPS) * 48000))
    // não deixa a fila do codificador crescer (memória) e deixa a tela respirar
    while (venc.encodeQueueSize > 6) await new Promise((r) => setTimeout(r, 2))
    if (i % 15 === 0) { opts.onProgress(i / frames); await new Promise((r) => setTimeout(r, 0)) }
  }
  pushAudioUntil(audioEnd)
  await venc.flush()
  if (aenc) await aenc.flush()
  if (failure) throw failure
  muxer.finalize()
  opts.onProgress(1)
  return true
}

/** Bytes → base64 (em blocos, para não estourar a pilha). */
export function toBase64(data: Uint8Array) {
  let s = ''
  for (let i = 0; i < data.length; i += 0x8000) s += String.fromCharCode(...data.subarray(i, i + 0x8000))
  return btoa(s)
}
