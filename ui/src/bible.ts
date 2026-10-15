import type { BibleBookInfo } from './bridge'
import { normalize } from './lyrics'

/** Abreviações em português (e algumas em inglês) por código de livro. O nome completo vem da versão. */
const ALIASES: Record<string, string[]> = {
  GEN: ['gn', 'gen', 'genesis'], EXO: ['ex', 'exo', 'exodo', 'exodus'], LEV: ['lv', 'lev', 'levitico'],
  NUM: ['nm', 'num', 'numeros'], DEU: ['dt', 'deut', 'deuteronomio'], JOS: ['js', 'jos', 'josue', 'joshua'],
  JDG: ['jz', 'jui', 'juizes', 'judges'], RUT: ['rt', 'rute', 'ruth'], '1SA': ['1sm', '1sam', '1samuel'],
  '2SA': ['2sm', '2sam', '2samuel'], '1KI': ['1rs', '1reis', '1kings'], '2KI': ['2rs', '2reis', '2kings'],
  '1CH': ['1cr', '1cronicas'], '2CH': ['2cr', '2cronicas'], EZR: ['ed', 'esd', 'esdras', 'ezra'],
  NEH: ['ne', 'nee', 'neemias'], EST: ['et', 'est', 'ester'], JOB: ['jó', 'jo', 'job'],
  PSA: ['sl', 'sal', 'salmo', 'salmos', 'ps', 'psalm'], PRO: ['pv', 'prov', 'proverbios'],
  ECC: ['ec', 'ecl', 'eclesiastes'], SNG: ['ct', 'cant', 'cantares', 'canticos'], ISA: ['is', 'isa', 'isaias'],
  JER: ['jr', 'jer', 'jeremias'], LAM: ['lm', 'lam', 'lamentacoes'], EZK: ['ez', 'eze', 'ezequiel'],
  DAN: ['dn', 'dan', 'daniel'], HOS: ['os', 'oseias'], JOL: ['jl', 'joel'], AMO: ['am', 'amos'],
  OBA: ['ob', 'obd', 'obadias'], JON: ['jn', 'jonas'], MIC: ['mq', 'miq', 'miqueias'], NAM: ['na', 'naum'],
  HAB: ['hc', 'hab', 'habacuque'], ZEP: ['sf', 'sof', 'sofonias'], HAG: ['ag', 'ageu'],
  ZEC: ['zc', 'zac', 'zacarias'], MAL: ['ml', 'mal', 'malaquias'],
  MAT: ['mt', 'mat', 'mateus'], MRK: ['mc', 'mar', 'marcos'], LUK: ['lc', 'luc', 'lucas'],
  JHN: ['jo', 'joao', 'john'], ACT: ['at', 'atos'], ROM: ['rm', 'rom', 'romanos'],
  '1CO': ['1co', '1cor', '1corintios'], '2CO': ['2co', '2cor', '2corintios'], GAL: ['gl', 'gal', 'galatas'],
  EPH: ['ef', 'efe', 'efesios'], PHP: ['fp', 'fil', 'filipenses'], COL: ['cl', 'col', 'colossenses'],
  '1TH': ['1ts', '1tes', '1tessalonicenses'], '2TH': ['2ts', '2tes', '2tessalonicenses'],
  '1TI': ['1tm', '1tim', '1timoteo'], '2TI': ['2tm', '2tim', '2timoteo'], TIT: ['tt', 'tito'],
  PHM: ['fm', 'flm', 'filemom'], HEB: ['hb', 'heb', 'hebreus'], JAS: ['tg', 'tia', 'tiago'],
  '1PE': ['1pe', '1ped', '1pedro'], '2PE': ['2pe', '2ped', '2pedro'], '1JN': ['1jo', '1joao'],
  '2JN': ['2jo', '2joao'], '3JN': ['3jo', '3joao'], JUD: ['jd', 'jud', 'judas'], REV: ['ap', 'apoc', 'apocalipse', 'rev'],
}

/** Ordem canônica (para separar Antigo e Novo Testamento). */
export const NT_START = 'MAT'

export type ParsedRef = { book: string; chapter: number; verse?: number; verseEnd?: number }

const key = (s: string) => normalize(s).replace(/[\s.º°ª]/g, '')

/** Acha o livro pelo nome ou abreviação ("jo", "1 co", "salmos", "apoc", "gên"). */
export function findBook(text: string, books: BibleBookInfo[]): BibleBookInfo | null {
  const q = key(text)
  if (!q) return null
  // 1) abreviação exata ("jo" = João; "jó" com acento = Jó)
  const accentJob = normalize(text).trim() === 'jo' && /ó/i.test(text)
  if (accentJob) return books.find((b) => b.id === 'JOB') ?? null
  for (const id of Object.keys(ALIASES)) {
    if (id === 'JOB' && q === 'jo') continue
    if (ALIASES[id].includes(q)) { const b = books.find((x) => x.id === id); if (b) return b }
  }
  for (const b of books) if (key(b.name) === q) return b
  // 2) começo do nome ou de uma abreviação
  for (const b of books) if (key(b.name).startsWith(q)) return b
  for (const id of Object.keys(ALIASES))
    if (ALIASES[id].some((a) => a.startsWith(q))) { const b = books.find((x) => x.id === id); if (b) return b }
  return null
}

/**
 * Lê uma referência digitada: "jo 3 16", "João 3:16-18", "1co 13", "sl 23.1", "gn1 1".
 * Devolve null quando não parece referência (aí vira pesquisa por palavra).
 */
export function parseRef(text: string, books: BibleBookInfo[]): ParsedRef | null {
  const m = /^\s*([1-3]?\s*[^\d\s:.,-][^\d:.,-]*?)\s*(\d+)?(?:\s*[\s:.,]\s*(\d+)(?:\s*-\s*(\d+))?)?\s*$/u.exec(text)
  if (!m) return null
  const book = findBook(m[1], books)
  if (!book) return null
  const chapter = Math.min(Math.max(1, Number(m[2] ?? 1)), book.chapters.length)
  const count = book.chapters[chapter - 1] ?? 1
  const verse = m[3] ? Math.min(Math.max(1, Number(m[3])), count) : undefined
  const verseEnd = m[4] && verse ? Math.min(Math.max(verse, Number(m[4])), count) : undefined
  return { book: book.id, chapter, verse, verseEnd }
}

/** Abreviação curta para os botões dos livros. */
export function shortName(b: BibleBookInfo) {
  const a = ALIASES[b.id]?.[0]
  if (a) return a.charAt(0).toUpperCase() + a.slice(1)
  return b.name.slice(0, 3)
}

export function refLabel(bookName: string, chapter: number, verse?: number, verseEnd?: number) {
  return `${bookName} ${chapter}${verse ? `:${verse}${verseEnd && verseEnd > verse ? `-${verseEnd}` : ''}` : ''}`
}
