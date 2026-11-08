<p align="center">
  <img src="Igevo.Vernetzt.WebApp/wwwroot/images/logo-no-bg.svg" alt="Vernetzt-Logo" width="140">
</p>

<h1 align="center">Vernetzt</h1>

<p align="center"><strong>Vereine und Organisationen vernetzen – selbst verwaltet, datenschutzfreundlich, quelloffen.</strong></p>

[![CI](https://github.com/IGEVO/Vernetzt/actions/workflows/ci.yml/badge.svg)](https://github.com/IGEVO/Vernetzt/actions/workflows/ci.yml)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=IGEVO_Vernetzt&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=IGEVO_Vernetzt)
[![Lizenz: EUPL-1.2](https://img.shields.io/badge/Lizenz-EUPL--1.2-blue.svg)](LICENSE)
[![Dokumentation](https://img.shields.io/badge/Doku-igevo.github.io%2FVernetzt-informational)](https://igevo.github.io/Vernetzt/)

> **English:** Vernetzt is a self-hosted web application for connecting clubs and organisations, for example in a city or region. Each club or organisation maintains its own departments, contact persons and events, while a coordinating body keeps central oversight. It is licensed under the EUPL-1.2. The project targets German-speaking users, so documentation, issues and discussions are in German; code and code comments are in English.

**Vernetzt** ist eine selbst gehostete Webanwendung, um **Vereine und Organisationen** zu vernetzen, zum Beispiel in einer Stadt oder Region. Jeder Verein und jede Organisation pflegt die eigenen Daten selbst: Abteilungen, Gruppen, Ansprechpersonen und Termine. Eine koordinierende Stelle (z. B. ein Dachverband, eine Kommune oder ein Netzwerk) behält den Überblick. Datenschutz und einfache Bedienung stehen dabei im Vordergrund.

## Funktionen

- **Vereine und Organisationen** mit Abteilungen, Gruppen, Ansprechpersonen, Positionen und Sichtbarkeitsstufen
- **Selbstverwaltung** mit fein abgestuften Rechten pro Verein bzw. Organisation, Abteilung oder Gruppe
- **Anmeldung ohne Passwort**: Login-Link per E-Mail und Passkeys (WebAuthn)
- **Amtsadressen**: Funktionsadressen wie `vorstand@…` je Zuordnung, getrennt von der persönlichen Login-Adresse
- **E-Mail-Versand** an Vereine, Organisationen und Kontakte, ohne Empfängeradressen offenzulegen, plus E-Mail-Relay
- **Kalender** mit Terminen, Ferien und Feiertagen sowie iCal-Abos
- **Adressbuch-Abo** über CardDAV (nur lesend)
- **Öffentliches Portal** (Nuxt, SSR) mit freigegebenen Vereinen, Organisationen und Terminen, angebunden über eine schreibgeschützte API
- **DSGVO**: Datenexport, Kontolöschung, Audit-Log und automatische Bereinigung
- **Betrieb**: SQLite statt Datenbankserver, eingebaute Backups, Health-Endpunkte, OpenTelemetry mit fertigen Grafana-Dashboards

Die Bedienung beschreibt die **[Nutzerdokumentation](https://igevo.github.io/Vernetzt/)**.

## Architektur

| Teil | Technik |
|---|---|
| Backend | ASP.NET Core 10 (REST-API), EF Core mit SQLite, ASP.NET Core Identity, FluentValidation |
| Verwaltungsoberfläche | Vue 3, TypeScript, Vite, Vuetify |
| Öffentliches Portal | Nuxt (SSR) |
| Entwicklung | .NET Aspire, Dev Container |

```
Igevo.Vernetzt.AppHost/          Aspire-AppHost (startet alles für die Entwicklung)
Igevo.Vernetzt.Server/           ASP.NET-Core-Backend (API, liefert die Vue-App aus)
Igevo.Vernetzt.WebApp/           Vue-App (Verwaltung)
Igevo.Vernetzt.PublicWeb/        Nuxt-App (öffentliches Portal)
Igevo.Vernetzt.Core/             Domänenmodell, Interfaces, DTOs
Igevo.Vernetzt.Infrastructure/   EF Core, Repositories, Services, Validatoren
Igevo.Vernetzt.ServiceDefaults/  Gemeinsame Aspire-Defaults (Health, OpenTelemetry)
Igevo.Vernetzt.Tests/            MSTest-Unit- und Integrationstests
user-docs/                       Nutzerdokumentation (docmd)
observability/                   Grafana-Dashboards und Alert-Regeln
```

## Betrieb

Fertige Container-Images liegen in der GitHub Container Registry:

- `ghcr.io/igevo/vernetzt`: Server mit Verwaltungsoberfläche
- `ghcr.io/igevo/vernetzt-public`: öffentliches Portal (optional)

Tags: `<version>` (z. B. `0.4.0`), `<major>.<minor>`, `latest` (neueste stabile Version) und `edge` (aktueller Stand von `main`, nicht für den Produktivbetrieb). Jedes Release-Image hat eine signierte Build-Provenance (`gh attestation verify oci://ghcr.io/igevo/vernetzt:<version> --owner IGEVO`).

- **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**: Docker-Compose-Setups (mit und ohne Observability-Stack), Umgebungsvariablen, Volumes
- **[docs/HOSTING.md](docs/HOSTING.md)**: Checkliste vor dem Go-live: Reverse Proxy, Secrets, Data-Protection-Zertifikat, Container-Limits, Datensicherung

## Entwicklung

Es gibt zwei Wege:

**A: Dev Container.** Das Projekt in VS Code öffnen und **Reopen in Container** wählen. Danach den Task `aspire: run` starten.

**B: Lokal** (volles Debugging und Hot Reload). Voraussetzungen: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10), [Node.js 24](https://nodejs.org/), [Docker](https://www.docker.com/products/docker-desktop/) (Aspire startet Mailpit und Grafana als Container).

```bash
dotnet dev-certs https --trust
dotnet restore
dotnet watch run --project Igevo.Vernetzt.AppHost
```

Ein Aspire-Workload ist nicht nötig, Aspire kommt als NuGet-SDK.

| Dienst | Adresse |
|---|---|
| Aspire-Dashboard | http://localhost:15215 |
| Verwaltungsoberfläche (Vite) | http://localhost:5173 |
| Öffentliches Portal | http://localhost:5174 |
| Mailpit (Login-Links, System-Mails) | http://localhost:56080 |
| Grafana | http://localhost:56300 |
| Datenbank | `Igevo.Vernetzt.Server/App_Data/vernetzt.db` |

### Testkonten

In der Development-Umgebung wird eine leere Datenbank automatisch mit Beispieldaten befüllt. Die Anmeldung läuft per Login-Link über Mailpit.

| E-Mail | Berechtigung |
|---|---|
| `admin@test.de` | Systemadministrator |
| `manager@test.de` | Manager mit globalen Schreibrechten |
| `leser@test.de` | Globaler Datenleser ohne Schreibrechte |
| `verein.schreiben@test.de` | Schreibrecht für den TSV Musterstadt und dessen Abteilungen |
| `verein.lesen@test.de` | Nur Ansprechpartner beim TSV Musterstadt, ohne Schreibrecht |
| `abteilung.schreiben@test.de` | Schreibrecht ausschließlich für die Junioren des FC Beispielberg |
| `abteilung.lesen@test.de` | Nur Ansprechpartner der Junioren, ohne Schreibrecht |
| `gruppe.schreiben@test.de` | Schreibrecht ausschließlich für die Gruppe Bambini (TSV Musterstadt › Handball) |
| `mehrere.vereine@test.de` | Schreibrecht für zwei unterschiedliche Vereine |
| `ohne.zuordnung@test.de` | Angemeldeter Benutzer ohne Vereins- oder Abteilungszuordnung |

Für einen Neuaufbau bei gestopptem Server `vernetzt.db`, `vernetzt.db-wal` und `vernetzt.db-shm` in `Igevo.Vernetzt.Server/App_Data` löschen.

### Tests

```bash
dotnet test Igevo.Vernetzt.slnx                         # Backend
npm --prefix Igevo.Vernetzt.WebApp test                 # Vue-Unit-Tests
npm --prefix Igevo.Vernetzt.WebApp run test:e2e         # UI-Tests mit gemockter API
```

Technische Details zu Authentifizierung, Datenbank, Backups, E-Mail, Dateiablage, Health-Endpunkten und den Playwright-Suiten stehen in **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)**.

## Mitmachen

Beiträge sind willkommen. Bitte vorher **[CONTRIBUTING.md](CONTRIBUTING.md)** lesen. Es gilt der **[Verhaltenskodex](CODE_OF_CONDUCT.md)**.

Sicherheitslücken bitte **nicht** als öffentliches Issue melden, sondern wie in **[SECURITY.md](SECURITY.md)** beschrieben.

## Lizenz

Vernetzt steht unter der **[European Union Public Licence 1.2 (EUPL-1.2)](LICENSE)**. Die EUPL ist in allen Amtssprachen der EU gleichermaßen verbindlich. Die [deutsche Fassung](https://interoperable-europe.ec.europa.eu/sites/default/files/custom-page/attachment/2020-03/EUPL-1.2%20DE.txt) gilt also ebenso wie der englische Text in `LICENSE`.

Kurz gesagt: Vernetzt darf frei genutzt, verändert und weitergegeben werden. Wer eine veränderte Fassung weitergibt oder öffentlich als Dienst betreibt, muss deren Quellcode unter der EUPL (oder einer kompatiblen Lizenz) zugänglich machen.
