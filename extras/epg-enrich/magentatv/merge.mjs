// Nur aktualisieren statt ersetzen: Hat der neue Abruf für einen Sender an einem Tag
// keine Sendungen (MagentaTV drosselt gelegentlich), bleiben die bisherigen erhalten.
// Aufruf: node merge.mjs <alt.xml> <neu.xml> <ausgabe.xml>
import fs from 'node:fs'
const [altPfad, neuPfad, ausPfad] = process.argv.slice(2)
const re = /<programme\b[^>]*\bstart="(\d{8})\d*[^"]*"[^>]*\bchannel="([^"]+)"[\s\S]*?<\/programme>/g
const re2 = /<programme\b[^>]*\bchannel="([^"]+)"[^>]*\bstart="(\d{8})\d*[^"]*"[\s\S]*?<\/programme>/g
function sendungen(xml) {
  const m = new Map()
  for (const r of [re, re2]) {
    r.lastIndex = 0
    for (const x of xml.matchAll(r)) {
      const [tag, kanal] = r === re ? [x[1], x[2]] : [x[2], x[1]]
      const k = kanal + '|' + tag
      if (!m.has(k)) m.set(k, [])
      m.get(k).push(x[0])
    }
    if (m.size) break
  }
  return m
}
const neu = fs.readFileSync(neuPfad, 'utf8')
const alt = fs.existsSync(altPfad) ? fs.readFileSync(altPfad, 'utf8') : ''
const mNeu = sendungen(neu), mAlt = sendungen(alt)
const heute = new Date().toISOString().slice(0, 10).replace(/-/g, '')
const kanaeleNeu = new Set([...mNeu.keys()].map(k => k.split('|')[0]))
const dazu = []
let tage = 0
for (const [k, liste] of mAlt) {
  const [kanal, tag] = k.split('|')
  if (tag < heute || mNeu.has(k) || !kanaeleNeu.has(kanal) && !neu.includes(`id="${kanal}"`)) continue
  dazu.push(...liste); tage++
}
const ende = neu.lastIndexOf('</tv>')
fs.writeFileSync(ausPfad, dazu.length ? neu.slice(0, ende) + dazu.join('\n') + '\n' + neu.slice(ende) : neu)
console.log(`${tage} Sender-Tage aus dem letzten Stand übernommen (${dazu.length} Sendungen)`)
