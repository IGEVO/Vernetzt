# Vernetzt Vue WebApp

Vue 3 SPA mit TypeScript, Vite und Vuetify. Der ASP.NET-Core-Server liefert den
Produktions-Build unter derselben Origin aus; lokal proxyt Vite die API-Aufrufe.

## Entwicklung

```bash
npm ci
npm run dev
```

`VERNETZT_API_URL` überschreibt das API-Ziel, falls es nicht unter
`https://localhost:5001` erreichbar ist. Aspire setzt den Wert automatisch.

## Qualitätsprüfungen

```bash
npm run type-check
npm run lint
npm test
npm run test:e2e
npm run build-only
```

Der versionierte API-Vertrag liegt unter `openapi/openapi.json`. Nach einer
Backend-Vertragsänderung bei laufendem Development-Server aktualisieren:

```bash
npm run api:snapshot -- http://localhost:5142/swagger/v1/swagger.json
npm run api:generate
```

`npm run api:check` stellt sicher, dass die generierten TypeScript-Typen zum
Snapshot passen; CI vergleicht zusätzlich den Snapshot mit dem Backend.

Der Service Worker cached ausschließlich versionierte statische Assets. API,
Avatare, Downloads und Hilfe bleiben netzwerkgebunden.
