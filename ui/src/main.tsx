import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { StageApp } from './StageApp.tsx'
import './App.css'

// a mesma UI serve a janela de controle e a tela de retorno (index.html#stage)
const isStage = location.hash === '#stage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {isStage ? <StageApp /> : <App />}
  </StrictMode>,
)
