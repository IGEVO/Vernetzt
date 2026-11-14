# Vernetzt – öffentliches Portal

Öffentliches Vereinsregister und Veranstaltungskalender für Vernetzt. Das Portal liegt als eigenständige Nuxt-Anwendung im Vernetzt-Monorepo, bleibt separat containerisierbar und enthält keine fachliche Datenbank.

## Entwicklung

Voraussetzungen: Node.js 24 und eine laufende Vernetzt-API.

```bash
cp .env.example .env
npm ci
npm run dev
```

Alternativ startet `dotnet run --project Igevo.Vernetzt.AppHost` das Portal zusammen mit API, interner WebApp und Entwicklungsdiensten über Aspire. Das Public-Portal ist dann unter `http://localhost:5174` erreichbar.

`NUXT_API_BASE_URL` zeigt auf den ASP.NET-Core-Server. Der Browser spricht ausschließlich mit dem gleichnamigen Proxy unter `/api/public/v1`.

## Prüfungen

```bash
npm run type-check
npm test
npm run build
npm run test:e2e
```

Die API-Typen stammen aus `openapi/public-openapi.json` und werden mit `npm run api:generate` aktualisiert.

## Betrieb

Das Image läuft auf Port 3000. Erforderlich sind mindestens `NUXT_API_BASE_URL`, `NUXT_API_ACCESS_KEY` und `NUXT_PUBLIC_SITE_URL`.

### Zugriff auf die API

Die öffentliche API (`/api/public/*`) ist nur für dieses Portal da. Der Server beantwortet sie nur, wenn der Header `X-Vernetzt-Api-Key` den Schlüssel aus `PublicApi:AccessKey` enthält, sonst mit 404. Das Portal sendet ihn bei jeder serverseitigen Anfrage (`server/utils/api-headers.ts`); der Browser bekommt ihn nie zu sehen.

1. Einen Schlüssel erzeugen, z. B. `openssl rand -hex 32` (mindestens 32 Zeichen).
2. In der API als `PublicApi__AccessKey` und im Portal als `NUXT_API_ACCESS_KEY` setzen.

Fehlt der Schlüssel in der API oder ist er zu kurz, bleibt die öffentliche API für alle gesperrt und das Portal zeigt keine Daten. Die API schreibt dann beim Start eine Warnung ins Log. In der Entwicklung erzeugt Aspire bei jedem Start einen Schlüssel für beide Seiten.

### Inhalte

Titel und Untertitel der Startseite, Logo, Impressum, Datenschutz (jeweils eigener Text oder externer Link) und der Anbieter-Link werden in der Vernetzt-App unter **Systemeinstellungen → Öffentliches Portal** gepflegt (nur SystemAdmins). Ohne Eintrag zeigt das Portal Standardtexte bzw. den Hinweis „noch nicht hinterlegt“.

Caching: Die API hält `/api/public/v1/portal` 60 Sekunden im Output-Cache und leert ihn beim Speichern. Das Portal speichert die Daten zusätzlich 60 Sekunden serverseitig (`server/utils/portal.ts`, Stale-While-Revalidate). Änderungen sind also nach spätestens einer Minute sichtbar. Das Logo wird über eine versionierte URL ausgeliefert und darf dauerhaft gecacht werden.

### Proxy und Rate-Limit

Das Portal leitet unter `/api/public/v1/*` ausschließlich die bekannten, lesenden Routen der öffentlichen API weiter (Allowlist in `server/utils/public-api-path.ts`, nur `GET`/`HEAD`). Cookies, `Authorization` und vom Client gesendete `X-Forwarded-*`-Header werden nicht weitergegeben.

Die API begrenzt Anfragen pro Client-IP. Damit nicht alle Besucher über die IP des Portals in einem gemeinsamen Kontingent landen:

1. `NUXT_TRUSTED_PROXY_HOPS` auf die Anzahl der Reverse Proxies vor dem Portal setzen (meist `1`, ohne Proxy `0`). Das Portal ermittelt daraus die echte Client-IP und sendet sie als `X-Forwarded-For` an die API.
2. In der API die Adresse des Portal-Containers unter `ReverseProxy:KnownProxies` bzw. `ReverseProxy:KnownNetworks` eintragen und `ReverseProxy:Enabled` aktivieren.

### Schutz von E-Mail-Adressen und Telefonnummern

E-Mail-Adressen und Telefonnummern stehen weder im HTML noch im Nuxt-Payload, im JSON-LD oder in den Listen- und Detailantworten der API. Dort gibt es nur `hasEmail` bzw. `hasPhoneNumber`. Erst ein Klick auf „E-Mail anzeigen“ (Komponente `ContactReveal`) lädt sie über einen eigenen Endpoint:

- `/api/public/v1/clubs/{id}/contact-details` und `/api/public/v1/departments/{id}/contact-details` für die allgemeine Adresse
- `/api/public/v1/contacts/{id}/contact-details` für Ansprechpersonen; `{id}` ist `PublicContactDto.id`

Diese Endpoints werden nicht gecacht und haben ein eigenes, strengeres Limit (`RateLimiting:PublicContactReveal:PermitLimit`, Standard 10 pro Minute und Client-IP). Wichtig: Ohne die Proxy-Konfiguration aus dem vorigen Abschnitt teilen sich alle Besucher dieses Kontingent.

Die Sitemap wird eine Stunde zwischengespeichert.

### Farbschema

Hell/Dunkel folgt standardmäßig der Geräteeinstellung. Die manuelle Wahl im Header wird nur im `localStorage` des Browsers gespeichert.
