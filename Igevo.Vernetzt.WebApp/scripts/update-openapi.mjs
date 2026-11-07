import { mkdir, writeFile } from 'node:fs/promises'

const source = process.argv[2] ?? process.env.OPENAPI_URL ?? 'http://localhost:5142/swagger/v1/swagger.json'
const response = await fetch(source)
if (!response.ok) throw new Error(`OpenAPI download failed: ${response.status}`)
const document = await response.json()
await mkdir(new URL('../openapi/', import.meta.url), { recursive: true })
await writeFile(new URL('../openapi/openapi.json', import.meta.url), `${JSON.stringify(document, null, 2)}\n`)
