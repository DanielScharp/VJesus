import { useEffect, useState } from 'react'
import {
  inApp, onMessage, send,
  type Composition, type Display, type EffectDef, type EffectInstance, type Layer, type LayerPlayback, type MappingSetup, type ControlInfo, type StreamingInfo, type LiveSources, type Song, type LyricStyle, type LyricsState, type LyricScene, type StageState, type StageConfig, type AttackPreset,
} from './bridge'
import { ClipGrid } from './ClipGrid'
import { EffectsPanel } from './EffectsPanel'
import { ActiveEffectsBar, EffectChips } from './EffectChips'
import { LayerSettings, TempoBar } from './Tempo'
import { LyricsPage } from './LyricsPage'
import { ProjectMenu } from './ProjectMenu'
import { injectFontFaces } from './fontFaces'
import { BiblePage } from './BiblePage'
import { StagePage } from './StagePage'
import { MonitorsBar } from './MonitorsBar'
import { AttacksBar } from './AttacksBar'
import { MappingEditor } from './MappingEditor'
import { ControlPanel, StreamingPanel } from './ControlPanel'
import './App.css'

type Tab = 'clips' | 'mapping' | 'lyrics' | 'bible' | 'stage'

function App() {
  const [tab, setTab] = useState<Tab>('clips')
  const [displays, setDisplays] = useState<Display[]>([])
  const [fps, setFps] = useState(0)
  const [comp, setComp] = useState<Composition | null>(null)
  const [thumbs, setThumbs] = useState<Record<string, string>>({})
  const [playback, setPlayback] = useState<LayerPlayback[]>([])
  const [brightness, setBrightness] = useState(1)
  const [speed, setSpeed] = useState(1)
  const [testCard, setTestCard] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [library, setLibrary] = useState<EffectDef[]>([])
  const [target, setTarget] = useState(-1) // layer selecionada para efeitos (-1 = master)
  const [mapping, setMappingState] = useState<MappingSetup | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [project, setProject] = useState<string | null>(null)
  const [presets, setPresets] = useState<string[]>([])
  const [notice, setNotice] = useState<string | null>(null)
  const [control, setControl] = useState<ControlInfo | null>(null)
  const [learn, setLearn] = useState(false)
  const [streaming, setStreaming] = useState<StreamingInfo | null>(null)
  const [live, setLive] = useState<LiveSources | null>(null)
  const [tempo, setTempo] = useState({ bpm: 120, beat: 0 })
  const [songs, setSongs] = useState<Song[]>([])
  const [lyricStyle, setLyricStyle] = useState<LyricStyle | null>(null)
  const [lyricsState, setLyricsState] = useState<LyricsState>({ songId: null, slide: -1, text: null, blank: false })
  const [fonts, setFonts] = useState<string[]>([])
  const [scenes, setScenes] = useState<LyricScene[]>([])
  const [stage, setStage] = useState<StageState | null>(null)
  const [stagePreview, setStagePreview] = useState<string | null>(null)
  const [attacks, setAttacks] = useState<AttackPreset[]>([])
  const [masterVolume, setMasterVolume] = useState(1)
  const [audioInfo, setAudioInfo] = useState<{ device: string | null; error: string | null }>({ device: null, error: null })

  useEffect(() => {
    // impede o WebView de abrir um arquivo solto fora da grade
    const block = (e: DragEvent) => e.preventDefault()
    window.addEventListener('dragover', block)
    window.addEventListener('drop', block)

    const off = onMessage((msg) => {
      switch (msg.type) {
        case 'displays': setDisplays(msg.list); break
        case 'stats': setFps(msg.fps); break
        case 'composition': setComp({ columns: msg.columns, layers: msg.layers, masterEffects: msg.masterEffects, decks: msg.decks, currentDeck: msg.currentDeck }); break
        case 'effects': setLibrary(msg.list); break
        case 'thumb': setThumbs((t) => ({ ...t, [msg.path]: msg.data })); break
        case 'playback': setPlayback(msg.layers); setTempo({ bpm: msg.bpm, beat: msg.beat }); break
        case 'tempo': setMasterVolume(msg.masterVolume); setAudioInfo({ device: msg.audioDevice, error: msg.audioError }); break
        case 'mapping': setMappingState(msg.mapping); break
        case 'preview': setPreview(msg.data); break
        case 'error': setError(msg.message); break
        case 'notice': setNotice(msg.message); setTimeout(() => setNotice(null), 2500); break
        case 'project': setProject(msg.name); break
        case 'presets': setPresets(msg.list); break
        case 'control': setControl(msg); break
        case 'streaming': setStreaming(msg); break
        case 'liveSources': setLive(msg); break
        case 'lyricsLibrary': setSongs(msg.songs); setLyricStyle(msg.style); setScenes(msg.scenes ?? []); break
        case 'lyricsState': setLyricsState(msg); break
        case 'fonts': setFonts(msg.list); injectFontFaces(msg.faces); break
        case 'stage': setStage(msg); break
        case 'stagePreview': setStagePreview(msg.data); break
        case 'attacks': setAttacks(msg.list); break
        case 'master': setBrightness(msg.brightness); setSpeed(msg.speed); setTestCard(msg.testCard); break
      }
    })
    send({ type: 'hello' })
    return () => {
      off()
      window.removeEventListener('dragover', block)
      window.removeEventListener('drop', block)
    }
  }, [])

  useEffect(() => send({ type: 'setMaster', brightness, speed, testCard }), [brightness, speed, testCard])

  // opacidade/blend: atualiza a tela na hora e avisa o motor
  const changeLayer = (layer: number, patch: Partial<Pick<Layer, 'opacity' | 'blend' | 'transition' | 'transitionSeconds' | 'volume' | 'sendToStage'>>) => {
    setComp((c) => c && { ...c, layers: c.layers.map((l, i) => (i === layer ? { ...l, ...patch } : l)) })
    send({ type: 'layerParams', layer, ...patch })
  }

  // efeitos: atualização otimista da cadeia do alvo selecionado
  const setChain = (chain: EffectInstance[]) =>
    setComp((c) => c && (target < 0
      ? { ...c, masterEffects: chain }
      : { ...c, layers: c.layers.map((l, i) => (i === target ? { ...l, effects: chain } : l)) }))

  // liga/desliga um efeito de qualquer layer (ou do master) direto da grade
  const toggleEffect = (t: number, index: number, enabled: boolean) => {
    setComp((c) => {
      if (!c) return c
      const flip = (chain: EffectInstance[]) => chain.map((e, i) => (i === index ? { ...e, enabled } : e))
      return t < 0 ? { ...c, masterEffects: flip(c.masterEffects) }
        : { ...c, layers: c.layers.map((l, i) => (i === t ? { ...l, effects: flip(l.effects) } : l)) }
    })
    send({ type: 'effectToggle', target: t, index, enabled })
  }

  // mapeamento: a UI é dona do estado enquanto edita e manda tudo para o motor a cada mudança
  const setMapping = (m: MappingSetup) => {
    setMappingState(m)
    send({ type: 'setMapping', mapping: m })
  }

  return (
    <div className="app">
      <header className="topbar">
        <h1>VJApp</h1>
        <ProjectMenu project={project} />
        <nav className="row tabs">
          <button className={tab === 'clips' ? 'active' : ''} onClick={() => setTab('clips')}>Clips e efeitos</button>
          <button className={tab === 'mapping' ? 'active' : ''} onClick={() => setTab('mapping')}>Mapeamento</button>
          <button className={tab === 'lyrics' ? 'active' : ''} onClick={() => setTab('lyrics')}>Letras</button>
          <button className={tab === 'bible' ? 'active' : ''} onClick={() => setTab('bible')}>Bíblia</button>
          <button className={tab === 'stage' ? 'active' : ''} onClick={() => setTab('stage')}>Retorno</button>
        </nav>
        <TempoBar bpm={tempo.bpm} beat={tempo.beat} />
        <span className="stats" title="Quadros por segundo da composição 1920×1080">
          {inApp ? <><b>{fps.toFixed(2)}</b> fps · 1920×1080</> : 'fora do app (só UI)'}
        </span>
      </header>

      {error && <div className="error" onClick={() => setError(null)}>{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      <AttacksBar attacks={attacks} />

      <MonitorsBar preview={preview} stagePreview={stagePreview} stage={stage} lyrics={lyricsState} comp={comp} displays={displays} />

      {tab === 'stage' && stage && (
        <StagePage state={stage} displays={displays} preview={stagePreview} onConfig={(config: StageConfig) => {
          setStage({ ...stage, config })
          send({ type: 'stageConfig', config })
        }} />
      )}

      {tab === 'lyrics' && (
        <LyricsPage songs={songs} style={lyricStyle} scenes={scenes} onStyleLocal={setLyricStyle} onScenesLocal={setScenes} state={lyricsState} fonts={fonts} preview={preview} comp={comp} />
      )}

      {tab === 'bible' && <BiblePage scenes={scenes} comp={comp} />}

      {tab === 'mapping' && (mapping
        ? <MappingEditor mapping={mapping} onChange={setMapping} displays={displays} preview={preview} comp={comp}
            testCard={testCard} onTestCard={setTestCard} presets={presets} />
        : <div className="meta">Conectando ao motor…</div>)}

      {tab === 'clips' && (
        <>
          {comp && <ActiveEffectsBar comp={comp} library={library} onToggle={toggleEffect} onShow={setTarget} />}
          {comp ? (
            <ClipGrid comp={comp} thumbs={thumbs} playback={playback} onLayerChange={changeLayer}
              selected={target} onSelect={setTarget} live={live}
              library={library} onToggleEffect={toggleEffect} />
          ) : (
            <div className="meta">Conectando ao motor…</div>
          )}
          <p className="hint">
            Clique num clip para tocar · clique numa célula vazia ou arraste vídeos do Explorer para adicionar ·
            arraste clips entre células para mover · botão direito para trocar ou remover · número da coluna dispara a coluna inteira
          </p>

          <div className="panels">
            {comp && (
              <div className="layer-panel">
              {target >= 0 && (
                <section>
                  <h2>Layer: <span className="fx-target">{comp.layers[target].name}</span></h2>
                  <LayerSettings layer={comp.layers[target]} index={target} onChange={(p) => changeLayer(target, p)} />
                </section>
              )}
              <EffectsPanel library={library} target={target}
                targetName={target < 0 ? 'Master' : comp.layers[target].name}
                chain={target < 0 ? comp.masterEffects : comp.layers[target].effects}
                onChange={setChain} />
              </div>
            )}

            <div className="side">
              <section>
                <div className="effects-head">
                  <h2>Master</h2>
                  <button className={`small ${target < 0 ? 'active' : ''}`} onClick={() => setTarget(-1)}>Efeitos do master</button>
                </div>
                {comp && <EffectChips chain={comp.masterEffects} target={-1} library={library} onToggle={toggleEffect} />}
                {preview && <img className="preview" src={preview} alt="Preview da composição" />}
                <label data-address="/composition/video/brightness">
                  Brilho <output>{brightness.toFixed(2)}</output>
                  <input type="range" min={0} max={1.5} step={0.01} value={brightness}
                    onChange={(e) => setBrightness(Number(e.target.value))} />
                </label>
                <label data-address="/composition/speed">
                  Velocidade <output>{speed.toFixed(2)}</output>
                  <input type="range" min={0} max={4} step={0.01} value={speed}
                    onChange={(e) => setSpeed(Number(e.target.value))} />
                </label>
                <label data-address="/composition/audio/volume">
                  Volume <output>{Math.round(masterVolume * 100)}%</output>
                  <input type="range" min={0} max={1.5} step={0.01} value={masterVolume}
                    onChange={(e) => { const v = Number(e.target.value); setMasterVolume(v); send({ type: 'setMasterVolume', volume: v }) }} />
                </label>
                {audioInfo.error ? <p className="meta warn">{audioInfo.error}</p>
                  : audioInfo.device && <p className="meta">Áudio dos clips sai em: {audioInfo.device}</p>}
                <label className="check" data-address="/composition/testcard">
                  <input type="checkbox" checked={testCard} onChange={(e) => setTestCard(e.target.checked)} />
                  Test card nas saídas (para alinhar projetores)
                </label>
                <p className="meta">Telas de saída, monitores e tela cheia ficam na aba Mapeamento.</p>
              </section>
              <ControlPanel info={control} learn={learn} onLearn={setLearn} />
              {streaming && <StreamingPanel info={streaming} />}
            </div>
          </div>
        </>
      )}
    </div>
  )
}

export default App
