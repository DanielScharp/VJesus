// Ponte UI <-> motor C#. Dentro do app usa o WebView2; no navegador comum só registra no console.

export type Display = { index: number; name: string; width: number; height: number; primary: boolean }

export type ClipKind = 'video' | 'plasma' | 'testCard' | 'camera' | 'spout' | 'lyrics' | 'ndi' | 'presentation'
export type PresentationState = { layer: number; layerName: string; name: string; slide: number; count: number; slides: number; labels: string[]; current: string; images: string[] }
export type LiveSources = { cameras: string[]; spout: string[]; ndi?: string[]; ndiAvailable?: boolean }
export type GeneratorSettings = { type: string; speed: number; intensity: number; scale: number; detail: number; angle: number; colors: string[] }
export type ClipProps = { speed: number; fit: 'encaixar' | 'preencher' | 'esticar'; zoom: number; offsetX: number; offsetY: number; volume: number; end: 'repetir' | 'parar' | 'seguir'; next?: ClipNext | null; inPoint?: number; outPoint?: number; rotation?: number; width?: number; height?: number; radius?: number[]; effects: EffectInstance[] }
/** O que entra quando o vídeo acaba: outro clip (aba, layer, coluna), a próxima coluna ou limpar a layer. */
export type ClipNext = { action: 'clip' | 'proxima' | 'limpar'; deckId?: string | null; layer: number; column: number }
/** Posição, tamanho, giro e cantos (px: ↖ ↗ ↘ ↙) da layer inteira. */
export type LayerTransform = { x: number; y: number; scale: number; width: number; height: number; rotation: number; radius: number[] }
export type Clip = { kind: ClipKind; path: string | null; name: string; captureSize?: string | null; captureFps?: number | null; gen?: GeneratorSettings | null; props?: ClipProps | null; duration?: number | null }

export const BLEND_MODES = ['normal', 'add', 'multiply', 'screen', 'overlay', 'difference', 'lighten', 'darken', 'subtract', 'exclusion', 'hardLight', 'softLight', 'colorDodge', 'colorBurn', 'linearLight', 'divide', 'hue', 'saturation', 'color', 'luminosity'] as const
export type BlendMode = (typeof BLEND_MODES)[number]

export type EffectParam = { id: string; label: string; min: number; max: number; default: number; step: number }
export type EffectDef = { id: string; name: string; feedback: boolean; params: EffectParam[] }
export type EffectInstance = { effect: string; enabled: boolean; values: Record<string, number> }

export const TRANSITIONS = ['cut', 'crossfade', 'dip', 'wipe', 'push', 'zoom', 'blur'] as const
export type Transition = (typeof TRANSITIONS)[number]

export type Layer = {
  name: string
  opacity: number
  blend: BlendMode
  active: number // coluna tocando nesta aba, -1 = vazia (ou tocando de outra aba)
  playing: Clip | null // clip tocando (de qualquer aba)
  playingDeck: string | null // aba de onde ele veio
  clips: (Clip | null)[]
  effects: EffectInstance[]
  transition: Transition
  transitionSeconds: number
  transitionIn?: boolean
  transitionBetween?: boolean
  transitionOut?: boolean
  volume: number
  muted?: boolean
  hasAudio: boolean
  sendToStage: boolean
  transform?: LayerTransform
}

export type DeckClip = { kind: ClipKind; name: string; path: string | null }
export type CarouselSettings = { seconds: number; allLayers: boolean; layers: number[]; skipEmpty: boolean }
export type CarouselRun = { deckId: string; column: number; nextAt: number }
export type Deck = { id: string; name: string; carousel?: CarouselSettings | null; columns?: number; columnNames?: (string | null)[]; clips?: (DeckClip | null)[][] }
export type Composition = { columns: number; columnNames?: (string | null)[]; layers: Layer[]; masterEffects: EffectInstance[]; decks: Deck[]; currentDeck: number }

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
  position: 'top' | 'center' | 'bottom'; margin: number; lineSpacing: number; sphere?: number; fadeMs: number
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

export type BibleVersion = { id: string; name: string; shortName: string; language: string; languageName: string; books: number; verses: number; publicDomain: boolean; licenseUrl: string; copyright?: string; provider?: string | null }
export type BibleSettings = {
  mainVersion: string | null; compare: string[]; onScreen: string[]; sceneId: string | null
  reference: 'abaixo' | 'acima' | 'nenhuma'; showVersion: boolean; referenceScale: number; referenceColor: string; history: string[]
}
export type BiblePassage = { book: string; chapter: number; verse: number; verseEnd: number }
export type BibleBookInfo = { id: string; name: string; chapters: number[] }
export type BibleHit = { book: string; bookName: string; chapter: number; verse: number; text: string }
export type LayerPlayback = { position: number; duration: number; playing: boolean; inPoint?: number; outPoint?: number } | null

export type ServiceKind = 'song' | 'bible' | 'text' | 'clip' | 'column' | 'notice' | 'timer' | 'clear'
export type ServiceItem = {
  id: string; kind: ServiceKind; title: string; note?: string | null
  songId?: string | null; book?: string | null; chapter?: number; verse?: number; verseEnd?: number
  text?: string | null; deckId?: string | null; layer?: number; column?: number; seconds?: number
}
export type ServicePlanInfo = { id: string; name: string; date: string | null; count: number }
export type ServiceState = { planId: string; name: string; date: string | null; plans: ServicePlanInfo[]; items: ServiceItem[]; current: string | null; verse: number }
export type NoticeItem = { id: string; text: string }
export type OverlaySettings = {
  notices: NoticeItem[]; noticeSeconds: number; noticeBackground: boolean; noticeStyle: LyricStyle
  timerLabel: string; timerEndText: string; timerBackground: boolean; timerStyle: LyricStyle
  screens?: string[] | null
}
export type OverlayState = { settings: OverlaySettings; mode: 'off' | 'timer' | 'notices' | 'notice'; noticeId: string | null; targets?: { id: string; name: string }[] }
export type CheckItem = { level: 'erro' | 'aviso' | 'info' | 'ok'; area: string; title: string; detail?: string | null; fix?: 'relink' | 'searchFolder' | null; fixLabel?: string | null; path?: string | null }
export type CheckState = { running: boolean; when?: string; errors?: number; warnings?: number; items: CheckItem[] }
export type LibraryItem = { path: string; name: string; kind: 'video' | 'image' | 'presentation' | 'presentationFile'; folder: string; size: number; modified: string; tags: string[]; used: boolean }

export type PanicState = {
  on: boolean
  settings: { image: string | null; color: string; fade: number; size: number }
  imageName: string | null
  imageMissing: boolean
  thumb: string | null
}

export type DecodeMode = 'auto' | 'gpu' | 'cpu'
export type PerfResult = { smooth: number; usage: number; count?: number; total?: number }
export type PerformanceState = {
  decode: DecodeMode
  testing: boolean
  layers: { layer: string; clip: string; codec: string; size: string; gpu: boolean }[]
  test: null | { error: string } | { files: string[]; cpu: PerfResult; gpu: PerfResult; suggestion: DecodeMode; why: string }
}

export type UpdateState = {
  current: string; status: 'idle' | 'checking' | 'none' | 'available' | 'downloading' | 'ready' | 'error'
  latest: string | null; page: string | null; notes: string | null; error: string | null; progress: number; canInstall: boolean
}
export type PreviewRequest =
  | { kind: 'song'; songId: string; slide: number }
  | { kind: 'bible'; book: string; chapter: number; verse: number; verseEnd: number }
  | { kind: 'text' | 'notice'; text: string }
  | { kind: 'timer'; seconds: number }

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
  | { type: 'fonts'; list: string[]; imported?: string[]; faces?: { family: string; url: string; bold: boolean; italic: boolean }[] }
  | { type: 'fontsBusy'; family: string | null }
  | { type: 'shortcuts'; shortcuts: Record<string, string[]> | null }
  | { type: 'lyricsWebResults'; query: string; results: { id: string; title: string; artist: string; album: string; duration: number; text: string }[]; error?: string }
  | ({ type: 'stage' } & StageState)
  | { type: 'attacks'; list: AttackPreset[] }
  | { type: 'bibleState'; installed: BibleVersion[]; downloading: string[]; settings: BibleSettings; live: BiblePassage | null; youVersionKey: boolean }
  | { type: 'bibleYvVersions'; language: string; list: BibleVersion[]; error?: string }
  | { type: 'bibleCatalog'; list: BibleVersion[]; error?: string }
  | { type: 'bibleBooks'; version: string | null; books: BibleBookInfo[] }
  | { type: 'bibleChapter'; book: string; chapter: number; texts: Record<string, string[] | null> }
  | { type: 'bibleSearch'; query: string; total: number; hits: BibleHit[] }
  | ({ type: 'overlay' } & OverlayState)
  | ({ type: 'service' } & ServiceState)
  | ({ type: 'update' } & UpdateState)
  | { type: 'slidePreview'; id: string; data: string | null; error?: string }
  | { type: 'carousels'; running: CarouselRun[] }
  | { type: 'presentations'; list: PresentationState[] }
  | { type: 'outputPreview'; screenId: string | null; data: string }
  | { type: 'ledTest'; mode: 'off' | 'regua' | 'partes' }
  | ({ type: 'performance' } & PerformanceState)
  | ({ type: 'panic' } & PanicState)
  | ({ type: 'check' } & CheckState)
  | { type: 'library'; scanning: boolean; folders: string[]; items: LibraryItem[] | null }
  | { type: 'libraryPreview'; path: string; frames: string[] }
  | { type: 'libraryTags'; path: string; tags: string[] }

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
  | ({ type: 'setGenerator'; kind: 'plasma' | 'testCard' | 'lyrics'; gen?: GeneratorSettings } & Cell)
  | { type: 'lyricsSave'; song: Song }
  | { type: 'lyricsWebSearch'; query: string }
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
  | ({ type: 'setLiveClip'; kind: 'camera' | 'spout' | 'ndi'; name: string } & Cell)
  | { type: 'listLiveSources' }
  | ({ type: 'moveClip'; fromLayer: number; fromColumn: number } & Cell)
  | ({ type: 'removeClip' } & Cell)
  | ({ type: 'trigger' } & Cell)
  | { type: 'triggerColumn'; column: number }
  | { type: 'clearLayer'; layer: number }
  | { type: 'clearAll' }
  | { type: 'columnAdd'; after?: number }
  | { type: 'layerAdd'; layer?: number }
  | { type: 'setCapture'; layer: number; column: number; size: string | null; fps: number | null }
  | { type: 'setGeneratorParams'; layer: number; column: number; gen: GeneratorSettings }
  | { type: 'setClipProps'; layer: number; column: number; props: ClipProps & { name: string } }
  | { type: 'layerRemove' | 'layerDuplicate'; layer: number }
  | { type: 'layerMove'; layer: number; direction: -1 | 1 }
  | { type: 'columnRemove'; column: number }
  | { type: 'columnRename'; column: number; name: string }
  | { type: 'deckAdd'; name?: string }
  | { type: 'deckSelect' | 'deckRemove'; index: number }
  | { type: 'deckRename'; index: number; name: string }
  | { type: 'deckMove'; index: number; direction: -1 | 1 }
  | { type: 'layerParams'; layer: number; name?: string; opacity?: number; blend?: BlendMode; transition?: Transition; transitionSeconds?: number; transitionIn?: boolean; transitionBetween?: boolean; transitionOut?: boolean; volume?: number; muted?: boolean; sendToStage?: boolean; transform?: LayerTransform }
  | { type: 'tap' | 'beatResync' }
  | { type: 'setBpm'; bpm: number }
  | { type: 'setMasterVolume'; volume: number }
  | { type: 'transport'; layer: number; action: 'play' | 'pause' | 'toggle' | 'stop' | 'restart' }
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
  | { type: 'fontsImport' | 'fontsList' }
  | { type: 'shortcutsSave'; shortcuts: Record<string, string[]> }
  | { type: 'fontsGoogle' | 'fontsDelete'; family: string }
  | { type: 'bibleYvKey'; key: string; language?: string }
  | { type: 'bibleYvVersions'; language: string }
  | { type: 'bibleYvAdd'; id: string }
  | { type: 'bibleBooks'; version: string }
  | { type: 'bibleChapter'; book: string; chapter: number; versions: string[] }
  | { type: 'bibleSearch'; version: string; query: string }
  | { type: 'bibleShow'; book: string; chapter: number; verse: number; verseEnd?: number }
  | { type: 'overlaySave'; settings: OverlaySettings }
  | { type: 'overlayTimer' | 'overlayNotices'; on: boolean }
  | { type: 'overlayNotice'; id: string }
  | { type: 'serviceSave'; plan: { name: string; date: string | null; items: ServiceItem[] } }
  | { type: 'servicePlanNew'; name: string; date: string | null; copy?: boolean }
  | { type: 'servicePlanSelect' | 'servicePlanDelete'; id: string }
  | { type: 'serviceGo'; id: string }
  | { type: 'serviceStep'; direction: 1 | -1 }
  | { type: 'serviceReset' }
  | { type: 'carouselSet'; deckId: string; settings: CarouselSettings }
  | { type: 'outputPreview'; screenId: string | null }
  | ({ type: 'presentationBrowse' } & Cell)
  | { type: 'presentationStep'; layer: number; direction: 1 | -1 }
  | { type: 'presentationGo'; layer: number; slide: number }
  | { type: 'ledTest'; mode: 'off' | 'regua' | 'partes' }
  | { type: 'carouselRun'; deckId: string; on: boolean }
  | { type: 'carouselStep'; deckId: string; direction: 1 | -1 }
  | { type: 'updateCheck' | 'updateDownload' | 'updateInstall' }
  | { type: 'performanceGet' | 'performanceTest' }
  | { type: 'panicToggle'; on?: boolean }
  | { type: 'panicBrowse' }
  | { type: 'checkRun' | 'checkSearchFolder' }
  | { type: 'checkRelink'; path: string }
  | { type: 'libraryGet' | 'libraryAddFolder' }
  | { type: 'libraryRemoveFolder' | 'libraryPreview' | 'libraryReveal'; path: string }
  | { type: 'libraryTags'; path: string; tags: string[] }
  | { type: 'libraryThumbs'; paths: string[] }
  | { type: 'libraryPlace'; layer: number; column: number; paths: string[] }
  | { type: 'panicSet'; settings: PanicState['settings'] }
  | { type: 'performanceSet'; decode: DecodeMode }
  | ({ type: 'previewRender'; id: string } & PreviewRequest)

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
