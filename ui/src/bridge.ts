// Ponte UI <-> motor C#. Dentro do app usa o WebView2; no navegador comum só registra no console.

export type Display = { index: number; name: string; width: number; height: number; primary: boolean }

export type ClipKind = 'video' | 'plasma' | 'testCard' | 'camera' | 'spout' | 'lyrics'
export type LiveSources = { cameras: string[]; spout: string[] }
export type Clip = { kind: ClipKind; path: string | null; name: string }

export const BLEND_MODES = ['normal', 'add', 'multiply', 'screen', 'overlay', 'difference', 'lighten', 'darken'] as const
export type BlendMode = (typeof BLEND_MODES)[number]

export type EffectParam = { id: string; label: string; min: number; max: number; default: number; step: number }
export type EffectDef = { id: string; name: string; feedback: boolean; params: EffectParam[] }
export type EffectInstance = { effect: string; enabled: boolean; values: Record<string, number> }

export const TRANSITIONS = ['cut', 'crossfade', 'dip', 'wipe'] as const
export type Transition = (typeof TRANSITIONS)[number]

export type Layer = {
  name: string
  opacity: number
  blend: BlendMode
  active: number // coluna tocando, -1 = vazia
  clips: (Clip | null)[]
  effects: EffectInstance[]
  transition: Transition
  transitionSeconds: number
  volume: number
  hasAudio: boolean
  sendToStage: boolean
}

export type Composition = { columns: number; layers: Layer[]; masterEffects: EffectInstance[] }

export type Pt = { x: number; y: number }
export type Slice = {
  id: string
  name: string
  enabled: boolean
  opacity: number
  input: { x: number; y: number; w: number; h: number }
  cols: number
  rows: number
  points: Pt[]
  softEdge: { left: number; right: number; top: number; bottom: number; gamma: number }
  /** null = composição inteira; id de grupo; ou "layer:N" (uma layer só) */
  source?: string | null
  /** criado pelo mapa de painéis de LED */
  ledPart?: string | null
}
export type Mask = { id: string; name: string; enabled: boolean; points: Pt[] }
export type OutputScreen = { id: string; name: string; display: number; fullscreen: boolean; slices: Slice[]; masks: Mask[]; led?: LedWall | null }
export type LayerGroup = { id: string; name: string; layers: number[] }
export type LedPart = { id: string; name: string; x: number; y: number; w: number; h: number; mapX: number; mapY: number; rotation: number; source: string | null }
export type LedWall = { layout: 'separado' | 'unido'; source: string | null; parts: LedPart[] }
export type MappingSetup = { screens: OutputScreen[]; groups?: LayerGroup[] }

export type ControlInfo = {
  oscPort: number | null
  remoteUrl: string | null
  midiDevices: string[]
  midiBindings: { address: string; label: string; device: string }[]
  learning: string | null
  error: string | null
}

export type StreamingInfo = { spout: string | null; ndi: string | null; ndiAvailable: boolean }

export type Song = { id: string; title: string; artist: string; text: string; sceneId?: string | null; updatedAt?: string }
export type LyricScene = { id: string; name: string; style: LyricStyle }
export type LyricStyle = {
  fontFamily: string; fontSize: number; bold: boolean; italic: boolean; uppercase: boolean; align: 'left' | 'center' | 'right'
  color: string; outlineColor: string; outlineWidth: number; shadow: boolean
  box: boolean; boxColor: string; boxOpacity: number
  position: 'top' | 'center' | 'bottom'; margin: number; lineSpacing: number; fadeMs: number
  transition: string
}
export type LyricsState = { songId: string | null; slide: number; text: string | null; blank: boolean }

export type StageElement = { visible: boolean; x: number; y: number; w: number; h: number; fontSize: number; color: string; align: string }
export type StageConfig = {
  enabled: boolean; display: number; fullscreen: boolean; background: string
  clockSeconds: boolean; nextLabel: string; showSongTitle: boolean; programFull: boolean
  elements: Record<string, StageElement>
}
export type StageTimer = { mode: 'stopwatch' | 'countdown'; durationSeconds: number; running: boolean; startedAt: number; accumulated: number }
export type StageState = {
  config: StageConfig
  live: { current: string | null; next: string | null; title: string | null; label: string | null; blank: boolean }
  timer: StageTimer
  alert: string | null
  message: string | null
  program: { active: boolean; mirror: boolean; layers: string[] }
}

export type AttackType = 'flash' | 'strobe' | 'zoom' | 'shake' | 'invert' | 'ring' | 'sweep'
export type AttackPreset = {
  id: string; name: string; type: AttackType; color: string
  attackMs: number; holdMs: number; releaseMs: number; intensity: number; amount: number; count: number
  key: string | null; holdWhilePressed: boolean
}

export type BibleVersion = { id: string; name: string; shortName: string; language: string; languageName: string; books: number; verses: number; publicDomain: boolean; licenseUrl: string }
export type BibleSettings = {
  mainVersion: string | null; compare: string[]; onScreen: string[]; sceneId: string | null
  reference: 'abaixo' | 'acima' | 'nenhuma'; showVersion: boolean; referenceScale: number; referenceColor: string; history: string[]
}
export type BiblePassage = { book: string; chapter: number; verse: number; verseEnd: number }
export type BibleBookInfo = { id: string; name: string; chapters: number[] }
export type BibleHit = { book: string; bookName: string; chapter: number; verse: number; text: string }
export type LayerPlayback = { position: number; duration: number; playing: boolean } | null

export type EngineMessage =
  | { type: 'displays'; list: Display[] }
  | { type: 'stats'; fps: number }
  | { type: 'mapping'; mapping: MappingSetup }
  | { type: 'preview'; data: string }
  | { type: 'stagePreview'; data: string }
  | ({ type: 'composition' } & Composition)
  | { type: 'thumb'; path: string; data: string }
  | { type: 'playback'; layers: LayerPlayback[]; beat: number; bpm: number }
  | { type: 'tempo'; bpm: number; masterVolume: number; audioDevice: string | null; audioError: string | null }
  | { type: 'error'; message: string }
  | { type: 'notice'; message: string }
  | { type: 'project'; name: string | null; path: string | null }
  | { type: 'presets'; list: string[] }
  | ({ type: 'control' } & ControlInfo)
  | ({ type: 'streaming' } & StreamingInfo)
  | ({ type: 'liveSources' } & LiveSources)
  | { type: 'master'; brightness: number; speed: number; testCard: boolean }
  | { type: 'effects'; list: EffectDef[] }
  | { type: 'lyricsLibrary'; songs: Song[]; style: LyricStyle; scenes: LyricScene[] }
  | ({ type: 'lyricsState' } & LyricsState)
  | { type: 'fonts'; list: string[] }
  | ({ type: 'stage' } & StageState)
  | { type: 'attacks'; list: AttackPreset[] }
  | { type: 'bibleState'; installed: BibleVersion[]; downloading: string[]; settings: BibleSettings; live: BiblePassage | null }
  | { type: 'bibleCatalog'; list: BibleVersion[]; error?: string }
  | { type: 'bibleBooks'; version: string | null; books: BibleBookInfo[] }
  | { type: 'bibleChapter'; book: string; chapter: number; texts: Record<string, string[] | null> }
  | { type: 'bibleSearch'; query: string; total: number; hits: BibleHit[] }

type Cell = { layer: number; column: number }

export type UiMessage =
  | { type: 'hello' }
  | { type: 'setMaster'; brightness?: number; speed?: number; testCard?: boolean }
  | { type: 'setMapping'; mapping: MappingSetup }
  | { type: 'projectNew' | 'projectOpen' | 'projectSave' | 'projectSaveAs' }
  | { type: 'presetSave' | 'presetLoad' | 'presetDelete'; name: string }
  | { type: 'learnStart' | 'midiUnbind'; address: string }
  | { type: 'learnCancel' | 'midiClear' }
  | { type: 'setStreaming'; spout: string | null; ndi: string | null }
  | ({ type: 'browseClip' } & Cell)
  | ({ type: 'dropFiles' } & Cell)
  | ({ type: 'setGenerator'; kind: 'plasma' | 'testCard' | 'lyrics' } & Cell)
  | { type: 'lyricsSave'; song: Song }
  | { type: 'lyricsDelete'; id: string }
  | { type: 'lyricsImport' | 'lyricsClear' }
  | { type: 'lyricsShow'; songId: string; slide: number }
  | { type: 'lyricsText'; text: string }
  | { type: 'lyricsBlank'; blank?: boolean }
  | { type: 'lyricsStep'; direction: 1 | -1 }
  | { type: 'lyricsStyle'; style: LyricStyle; sceneId?: string | null }
  | { type: 'lyricsSceneSave'; scene: LyricScene }
  | { type: 'lyricsSceneDelete'; id: string }
  | ({ type: 'lyricsToGrid' } & Cell)
  | { type: 'stageConfig'; config: StageConfig }
  | { type: 'stageTimer'; action?: 'start' | 'pause' | 'toggle' | 'reset'; mode?: 'stopwatch' | 'countdown'; durationSeconds?: number }
  | { type: 'stageAlert' | 'stageMessage'; text: string | null }
  | { type: 'stageHello' }
  | { type: 'stageMirror'; on?: boolean }
  | { type: 'attackFire' | 'attackRelease'; id: string }
  | { type: 'attackStopAll' | 'attacksReset' }
  | { type: 'attacksSave'; attacks: AttackPreset[] }
  | ({ type: 'setLiveClip'; kind: 'camera' | 'spout'; name: string } & Cell)
  | { type: 'listLiveSources' }
  | ({ type: 'moveClip'; fromLayer: number; fromColumn: number } & Cell)
  | ({ type: 'removeClip' } & Cell)
  | ({ type: 'trigger' } & Cell)
  | { type: 'triggerColumn'; column: number }
  | { type: 'clearLayer'; layer: number }
  | { type: 'clearAll' }
  | { type: 'layerParams'; layer: number; opacity?: number; blend?: BlendMode; transition?: Transition; transitionSeconds?: number; volume?: number; sendToStage?: boolean }
  | { type: 'tap' | 'beatResync' }
  | { type: 'setBpm'; bpm: number }
  | { type: 'setMasterVolume'; volume: number }
  | { type: 'transport'; layer: number; action: 'play' | 'pause' }
  | { type: 'seek'; layer: number; position: number }
  | { type: 'effectAdd'; target: number; effect: string }
  | { type: 'effectRemove'; target: number; index: number }
  | { type: 'effectMove'; target: number; index: number; direction: -1 | 1 }
  | { type: 'effectToggle'; target: number; index: number; enabled: boolean }
  | { type: 'effectParam'; target: number; index: number; param: string; value: number }
  | { type: 'bibleHello' | 'bibleCatalog' }
  | { type: 'bibleDownload' | 'bibleDelete'; id: string }
  | { type: 'bibleSettings'; settings: BibleSettings }
  | { type: 'bibleImport'; name: string; shortName: string }
  | { type: 'bibleBooks'; version: string }
  | { type: 'bibleChapter'; book: string; chapter: number; versions: string[] }
  | { type: 'bibleSearch'; version: string; query: string }
  | { type: 'bibleShow'; book: string; chapter: number; verse: number; verseEnd?: number }

type WebView = {
  postMessage(msg: unknown): void
  postMessageWithAdditionalObjects(msg: unknown, objects: ArrayLike<unknown>): void
  addEventListener(type: 'message', fn: (e: { data: unknown }) => void): void
  removeEventListener(type: 'message', fn: (e: { data: unknown }) => void): void
}

const webview = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview

export const inApp = webview !== undefined

export function send(msg: UiMessage) {
  if (webview) webview.postMessage(msg)
  else console.debug('[bridge] fora do app:', msg)
}

/** Envia arquivos arrastados do Explorer; o C# recebe o caminho completo de cada um. */
export function sendFiles(msg: UiMessage, files: FileList) {
  if (webview) webview.postMessageWithAdditionalObjects(msg, files)
  else console.debug('[bridge] fora do app:', msg, files)
}

export function onMessage(fn: (msg: EngineMessage) => void): () => void {
  if (!webview) return () => {}
  const handler = (e: { data: unknown }) => fn(e.data as EngineMessage)
  webview.addEventListener('message', handler)
  return () => webview.removeEventListener('message', handler)
}
