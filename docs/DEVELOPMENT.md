# Entwicklerreferenz

> Technische Referenz für Mitwirkende. Einstieg und Setup stehen im [README](../README.md), der Ablauf für Beiträge in [CONTRIBUTING.md](../CONTRIBUTING.md), der Betrieb in [DEPLOYMENT.md](DEPLOYMENT.md) und [HOSTING.md](HOSTING.md).

## 📚 Nutzerdokumentation (docmd)

Die Nutzerdokumentation liegt in `user-docs/`. Sie wird mit der Web-App unter `/help` ausgeliefert und bei jedem stabilen Release zusätzlich auf [GitHub Pages](https://igevo.github.io/Vernetzt/) veröffentlicht.

### Wo bearbeiten?

- Seiten: `user-docs/docs/`
- Navigation: `user-docs/docmd.config.js`
- Build- und Kopierskript: `scripts/build-help-docs.sh`
- Ziel zur Laufzeit (generiert): `Igevo.Vernetzt.WebApp/wwwroot/help/`

Die Basis-URL steuert die Umgebungsvariable `DOCS_BASE` (Standard `/help/`, auf GitHub Pages `/Vernetzt/`).

### Lokal arbeiten

1. Doku bauen und in die Web-App kopieren:
   ```bash
   ./scripts/build-help-docs.sh
   ```
2. Optional beim Schreiben den Dev-Server nutzen:
   ```bash
   cd user-docs
   npm ci
   npm run dev
   ```

### Schreibregeln

- Auf **Deutsch** und in der **Du-Form** schreiben.
- Zielgruppe sind **Menschen ohne technischen Hintergrund**.
- Die Anwendung heißt immer **„Vernetzt“**.
- Inhalte nach Seiten bzw. Funktionen gliedern, passend zur Navigation der App.
- Die Seiten zu Anmeldung, PWA (inkl. Passkey-Hinweis) sowie Datenverarbeitung und Datenschutz aktuell halten.
- Für wichtige Hinweise und Warnungen docmd-Komponenten nutzen (z. B. Callouts).

> Generierte Dateien werden bewusst nicht versioniert (`user-docs/site/`, `Igevo.Vernetzt.WebApp/wwwroot/help/`).

## 🔐 Authentifizierung

Die Authentifizierung baut auf **ASP.NET Core Identity (.NET 10)** auf, ohne eigenen Token- oder Krypto-Code:

- **Login-Link per E-Mail** (ohne Passwort): Tokens erzeugt und prüft der Data-Protection-Token-Provider von Identity (`LoginLink`, Standard 15 Minuten, `Auth:LoginLink:ExpirationMinutes`). Jeder Link funktioniert **nur einmal** (der Hash wird in `ConsumedLoginTokens` vermerkt).
- **Passkeys (WebAuthn)** über die eingebaute Passkey-Unterstützung von Identity (`SignInManager.MakePasskey*/PerformPasskey*`, Tabelle `AspNetUserPasskeys`). Relying Party und erlaubte Origins: `Auth:Passkeys`.
- **Session**: HttpOnly-Identity-Cookie `vernetzt.session` mit `SameSite=Strict` (gleitend, `Auth:Session:ExpirationDays`). Im Browser liegen keine Tokens.
- **Widerruf**: Der Security Stamp wird alle `Auth:Session:ValidationIntervalMinutes` Minuten neu geprüft (Standard 5). „Auf allen Geräten abmelden“ rotiert den Stamp; gelöschte Benutzer verlieren ihre Session bei der nächsten Prüfung. Rollenänderungen greifen im selben Intervall.
- **Konten**: Jeder Domänen-`User` hat ein 1:1-Identity-Konto (`AspNetUsers`, gleiche Id). Es wird in `VernetztDbContext.SaveChanges` automatisch angelegt bzw. aktualisiert; die Rollen bleiben in `User.Claims`.
- **Onboarding**: Das Annehmen einer Einladung meldet **nicht** an. Stattdessen kommt ein neuer Login-Link per E-Mail (Ziel `/willkommen`, dort wird die Passkey-Einrichtung angeboten).
- **Passkey-Pflicht für privilegierte Rollen** (`Auth:PasskeyPolicy:RequiredForRoles`, Standard `["SystemAdmin", "Manager"]`, `[]` schaltet sie ab):
  - Eine per Login-Link gestartete Session ist eingeschränkt (`PasskeyPolicyMiddleware`, Fehlercode `PASSKEY_REQUIRED`). Erlaubt sind dann nur die Endpunkte zur Passkey-Einrichtung bzw. -Anmeldung und `GET /api/users/me`.
  - Der erste Passkey darf aus einer solchen Session registriert werden. Sobald einer existiert, braucht es für weitere eine Passkey-Session.
  - Bei Sessions, die eingeschränkt sein könnten, werden die Rollen bei jeder Anfrage aus der Datenbank gelesen. So deckt die Regel alle fachlichen Berechtigungsprüfungen ab.
  - Ein SystemAdmin kann die Passkeys anderer Benutzer zurücksetzen (`DELETE /api/users/{id}/passkeys`). Das beendet auch deren Sessions und wird im Audit-Log festgehalten.
- **Data-Protection-Schlüssel** liegen in der Datenbank (`DataProtectionKeys`), damit Sessions und Links Neustarts und neue Deployments überstehen. Im Produktivbetrieb werden sie mit einem Zertifikat verschlüsselt (`DataProtection:CertificatePath`, Pflicht), siehe [HOSTING.md](HOSTING.md#data-protection-zertifikat). Der Anwendungsname (`Igevo.Vernetzt`) darf sich nie ändern, sonst werden alle Sessions, Login-Links und Kalender-Abo-URLs ungültig.

### Login-Adresse und Amtsadresse (Kontext-E-Mail)

Vernetzt trennt Login-Adressen strikt von Kommunikationsadressen:

- `User.Email` ist die einzige Login-Adresse (Login-Link bzw. Identity-Konto).
- Jede `ContactAssignment` kann optional eine `ContextEmail` haben.
- Kontext-E-Mails werden erst nach der Bestätigung (`ContextEmailVerified`) für den Versand genutzt.
- Mit einer Kontext-E-Mail kann man sich nie anmelden.
- Die Sichtbarkeit in API und UI steuert `ContextEmailVisibility` (`VisibilityLevel`).
- Unbestätigte Kontext-E-Mails sehen nur Inhaber, Manager und SystemAdmin, und sie werden nie für den Versand genutzt.

Bestätigung und Hinweis-Mail:

- Die Bestätigung je Zuordnung läuft über `ContactAssignmentEmailVerificationToken`.
- Bestätigungslink: `GET /api/contact-assignment-emails/verify?token=...` (HTML-Statusseite).
- Versucht jemand, sich mit einer bestätigten Kontext-E-Mail anzumelden, kann eine allgemeine Hinweis-Mail verschickt werden. Sie verrät die echte Login-Adresse nicht und ist über `LoginEmailHintRateLimit` begrenzt.

Konfiguration (`appsettings.json`):

```json
"Auth": {
  "ContextEmailLoginHint": {
    "Enabled": true
  },
  "ContextEmailLoginHintCooldownMinutes": 60
}
```

Standard: höchstens ein Hinweis pro E-Mail-Adresse und Stunde.

### APIs für Amtsadressen

- `GET /api/users/recipient-candidates`: Empfängerliste auf Basis der Zuordnungen, inkl. Badge-Feldern (`EmailBadgeType`, `EmailBadgeLabel`, `EmailBadgeReason`)
- `GET /api/users/me/contact-assignments`: eigene Zuordnungen inkl. Amtsadresse, Bestätigungsstatus und Sichtbarkeit
- `PUT /api/users/me/contact-assignments/{assignmentId}/email`: Amtsadresse und Sichtbarkeit für eine eigene Zuordnung setzen
- `POST /api/users/me/contact-assignments/{assignmentId}/email/resend-verification`: Bestätigungs-Mail erneut senden
- `GET /api/contact-assignment-emails/verify?token=...`: Amtsadresse bestätigen (HTML-Statusseite)

## 🗄️ Datenbank

Die Anwendung nutzt **SQLite** über **Entity Framework Core** (`Microsoft.EntityFrameworkCore.Sqlite`). Vernetzt läuft als einzelne Instanz, eine Datenbankdatei genügt also, und ein Datenbankserver ist nicht nötig:

- Migrationen laufen automatisch beim Start.
- Soft Delete: Daten werden nie endgültig gelöscht.
- WAL-Modus: Lesende blockieren nie, es schreibt immer nur einer gleichzeitig.
- Alle Zeitstempel werden in UTC gespeichert (`VernetztDbContext.ConfigureConventions`).
- Entwicklung: `Igevo.Vernetzt.Server/App_Data/vernetzt.db` (für eine frische Datenbank die Datei löschen).
- Produktion: `/app/data/vernetzt.db`. Das Verzeichnis **muss auf einer lokalen Platte liegen** (kein NFS/SMB, dort ist das Datei-Locking von SQLite unzuverlässig).

Connection String (`ConnectionStrings:DefaultConnection`): `Data Source=/app/data/vernetzt.db`. Relative Pfade gelten relativ zum Content Root.

Die Tests nutzen dieselbe Engine:

- Unit-Tests: eine In-Memory-SQLite-Datenbank je Test (`Igevo.Vernetzt.Tests/TestInfrastructure/SqliteTestDatabase.cs`).
- `WebApplicationFactory`-Tests und die Playwright-Integrationssuite: eine temporäre Datenbankdatei je Anwendungsstart (`Database:UseTemporaryDatabase=true`, gesetzt in der Umgebung `Test`).

Migration anlegen:

```bash
dotnet ef migrations add <Name> --project Igevo.Vernetzt.Infrastructure --startup-project Igevo.Vernetzt.Server
```

### Backups

Vernetzt sichert seine Datenbank selbst mit `VACUUM INTO` (eine konsistente Kopie, während die App weiterläuft):

- **Geplant**: täglich um `Backup:Time` (Standard `03:00`, Zeitzone `Backup:TimeZone`, Standard `Europe/Berlin`).
- **Manuell**: *Administration → Systemeinstellungen → Datensicherung → Jetzt sichern* (SystemAdmin), z. B. vor einem Update. API: `GET/POST /api/backups`.
- **Vor Migrationen**: beim Start, bevor ausstehende Migrationen eine bestehende Datenbank ändern (`Backup:BeforeMigration`).
- **Aufbewahrung**:
  - Von den geplanten Backups bleibt das jeweils neueste pro Tag für `Backup:KeepDaily` Tage (Standard 14) und das neueste pro ISO-Woche für `Backup:KeepWeekly` Wochen (Standard 8).
  - Manuelle Backups und Backups vor Migrationen bleiben alle `Backup:KeepDaily` Tage erhalten.
  - Das neueste Backup wird nie gelöscht.
- **Dateien**: `vernetzt-<yyyyMMdd-HHmmss>-<scheduled|manual|pre-migration>.db` in `Backup:Path` (Produktion `/app/backups`, Entwicklung `App_Data/backups`). Sie werden zuerst als `*.tmp` geschrieben und erst nach Abschluss umbenannt.
- **Status**: im Administrationsbereich und in `GET /api/health/details` (Check `backups`). Er steht auf „degraded“, wenn das letzte Backup älter als 48 Stunden ist oder der letzte Versuch fehlschlug.

Alle Einstellungen sind optional:

```jsonc
"Backup": {
  "Enabled": true,            // tägliches geplantes Backup (manuelle Backups und Backups vor Migrationen funktionieren immer)
  "Path": "/app/backups",
  "Time": "03:00",
  "TimeZone": "Europe/Berlin",
  "KeepDaily": 14,
  "KeepWeekly": 8,
  "BeforeMigration": true
}
```

Die Backups enthalten nur die Datenbank. Uploads sind normale Dateien und werden mit dem Backup-Werkzeug des Hosts gesichert (siehe [HOSTING.md](HOSTING.md#datensicherung)).

Wiederherstellen:

1. Container stoppen.
2. `vernetzt.db` im Daten-Volume durch eine Backup-Datei ersetzen und `vernetzt.db-wal` sowie `vernetzt.db-shm` löschen.
3. Container starten.

## 📧 E-Mail (Entwicklung)

In der Entwicklung laufen **zwei Mailpit-Instanzen**, die der Aspire-AppHost automatisch startet. Sie sind getrennt, weil das E-Mail-Relay jede per POP3 abgeholte Nachricht löscht und Login-Links nicht in diesem Postfach landen dürfen.

- **mailpit-system**: System-Mails (Login-Links, Benachrichtigungen), SMTP auf Port 56025, Web-UI unter http://localhost:56080
- **mailpit-relay**: Postfach des E-Mail-Relays, SMTP auf Port 56026, POP3 auf Port 56110, Web-UI unter http://localhost:58025

### Feste lokale Ports (Aspire-AppHost)

Aspire nutzt feste Host-Ports, damit externe Werkzeuge (z. B. DBeaver oder `swaks`) nicht neu eingestellt werden müssen:

- Mailpit (System) SMTP: `localhost:56025`
- Mailpit (System) Web: `http://localhost:56080`
- Mailpit (Relay) SMTP: `localhost:56026`
- Mailpit (Relay) POP3: `localhost:56110`
- Mailpit (Relay) Web: `http://localhost:58025`
- Grafana (LGTM): `http://localhost:56300`, OTLP/gRPC: `localhost:56317`

Ein manuelles `docker compose up` ist nicht nötig: Aspire verwaltet beide Mailpit-Instanzen und den LGTM-Container. Der Server schickt seine Telemetrie an das Aspire-Dashboard und eine Kopie an LGTM (`Observability__AdditionalOtlpEndpoint`). In LGTM heißt der Dienst `server` (Ressourcenname in Aspire). Die Alert-Regeln werden nur im Produktiv-Setup eingerichtet.

## 📁 Dateiablage und Avatare

Hochgeladene Dateien (z. B. Avatare) speichert die Anwendung lokal. Ein späterer Umzug in einen Cloud-Speicher ist vorgesehen.

### Konfiguration

Ablagepfad in `appsettings.json`:

```json
{
  "Storage": {
    "RootPath": "/app/uploads"
  }
}
```

- **Entwicklung**: Dateien landen in `App_Data/storage/` (wird automatisch angelegt).
- **Produktion**: ein persistentes Volume nach `/app/uploads` mounten.

### Avatare

Benutzer, Vereine und Abteilungen können Avatare haben:

| Objekt | Verhalten |
|--------|----------|
| Benutzer | eigener Avatar |
| Verein | eigener Avatar |
| Abteilung | eigener Avatar; ohne eigenen wird der des Vereins verwendet |

**Unterstützte Formate**: JPEG, PNG, WebP, GIF, BMP, HEIC/HEIF (max. 10 MB)

**Erzeugte Größen**:

- `thumb` (64×64), quadratisches Vorschaubild
- `small` (128×128)
- `medium` (256×256)
- `original` (max. 512×512)

Alle Größen werden für kleine Dateien in WebP umgewandelt.

### Avatar-Endpunkte

Alle Avatar-Endpunkte erfordern eine Anmeldung:

| Methode | Endpunkt | Beschreibung |
|--------|----------|-------------|
| POST | `/api/avatars/users/me` | eigenen Avatar hochladen |
| DELETE | `/api/avatars/users/me` | eigenen Avatar löschen |
| GET | `/api/avatars/users/{id}` | Avatar-URLs eines Benutzers |
| POST | `/api/avatars/clubs/{id}` | Vereinsavatar hochladen |
| DELETE | `/api/avatars/clubs/{id}` | Vereinsavatar löschen |
| GET | `/api/avatars/clubs/{id}` | Avatar-URLs eines Vereins |
| POST | `/api/avatars/departments/{id}` | Abteilungsavatar hochladen |
| DELETE | `/api/avatars/departments/{id}` | Abteilungsavatar löschen |
| GET | `/api/avatars/departments/{id}` | Avatar-URLs einer Abteilung (mit Rückfall auf den Verein) |

## 🩺 Health-Endpunkte

### Einfacher Health-Check (für Docker/Kubernetes)

| Endpunkt | Beschreibung |
|----------|-------------|
| `GET /health` | einfacher Health-Check inkl. Datenbankverbindung (ohne Anmeldung) |
| `GET /alive` | Liveness-Probe (ohne Anmeldung) |
| `GET /health/ready` | Readiness-Probe (ohne Anmeldung) |

Die Antwort ist ein einfaches JSON:

```json
{ "status": "healthy" }
```

### Ausführlicher Health-Check (geschützt)

`GET /api/health/details` mit dem Header `X-Health-Token: DEIN_TOKEN`

Erfordert den `Health:Token` aus der Konfiguration. Die Antwort enthält:

- **Version**: Assembly-Version, Git-Hash
- **Laufzeit**: .NET-Version, Betriebssystem, Architektur
- **Prozess**: Laufzeit seit Start, Speicherverbrauch, Anzahl Threads
- **Checks**: Datenbankverbindung, Erreichbarkeit der Dateiablage, ausstehende Migrationen, Backups

Beispiel:

```json
{
  "status": "healthy",
  "timestamp": "2026-02-28T21:30:00Z",
  "environment": "Production",
  "version": {
    "assemblyVersion": "1.0.0.0",
    "informationalVersion": "1.0.0+abc123",
    "gitHash": "abc123"
  },
  "process": {
    "uptime": "02:30:15",
    "workingSetMb": 128.5,
    "threadCount": 24
  },
  "checks": [
    { "name": "database", "status": "healthy", "durationMs": 12 },
    { "name": "storage", "status": "healthy", "durationMs": 5 }
  ]
}
```

## 🧪 Tests

Alle Tests ausführen:

```bash
dotnet test Igevo.Vernetzt.slnx
```

Nur das Testprojekt:

```bash
dotnet test Igevo.Vernetzt.Tests
```

### UI-Tests (Playwright, `Igevo.Vernetzt.WebApp`)

Es gibt zwei Suiten:

| Suite | Befehl | Läuft gegen | Hinweise |
|---|---|---|---|
| UI-Tests mit Mock | `npm run test:e2e` | Vite-Dev-Server, API je Test gemockt | Schnell, kein Backend nötig. Feature-Specs in Chromium; `smoke`- und `responsive`-Specs zusätzlich in Firefox, WebKit und mobilen Viewports. Läuft in der CI. |
| Integrationsablauf | `npm run test:integration` | Echter Server (`ASPNETCORE_ENVIRONMENT=Test`, frische Datenbank) und Vite auf Port 5174 | Playwright startet beides automatisch. Braucht ein laufendes **Mailpit** (Aspire-AppHost oder `docker run -p 56025:1025 -p 56080:8025 axllent/mailpit`). Entwicklungsdaten bleiben unberührt. |

- `npm run test:e2e:ui` öffnet den UI-Modus von Playwright zum Schreiben und Debuggen von Tests.
- Mock-Tests: `tests/e2e/support/mockApi.ts` ersetzt die API, z. B. `api.get('/api/clubs/:id', data.club())`, `fail()`/`raw()` für Fehler und `api.lastBody(...)`, um Anfragen zu prüfen. Nicht gemockte API-Aufrufe lassen den Test fehlschlagen. Testdaten-Factories liegen in `tests/e2e/support/data.ts`.
- Integrationsablauf (`tests/integration`): Admin-Login per E-Mail-Link, Passkey-Registrierung (virtueller Authenticator in Chromium), Einstellungen, Anlegen eines Vereins, Einladung einer Ansprechperson sowie Onboarding und Login der eingeladenen Person. Die E-Mails werden über die API von Mailpit gelesen.
  - Ports und Mailpit lassen sich mit `IT_API_PORT`, `IT_WEB_PORT`, `MAILPIT_URL` und `MAILPIT_SMTP_PORT` überschreiben.
  - `IT_REUSE=1` nutzt bereits laufende Server weiter, praktisch zum Debuggen.
- Bekannte Einschränkung: Der EF-In-Memory-Provider verliert die JSON-gemappten Passkey-Daten von ASP.NET Identity. Passkey-**Anmeldung** und Passkey-Liste sind in der Integrationssuite deshalb nicht testbar; die Mock-Tests decken sie ab.
  - Die Suite verschiebt die Passkey-Pflicht deshalb auf eine Rolle, die niemand hat (`Auth__PasskeyPolicy__RequiredForRoles__0=DataReader`).
  - Außerdem hebt sie die Rate Limits für die Anmeldung an (`RateLimiting__PublicAuth__PermitLimit`, Standard 10 pro Minute und IP).

## 🌍 Sprache

- **Code**: Englisch (Code, Kommentare, Bezeichner, Commit-Messages)
- **Oberfläche und Doku**: Deutsch (alle Texte für Nutzende, README, Nutzerdokumentation)

## 🛡️ Sicherheit und Datenschutz

- Anmeldung ohne Passwort: einmal nutzbarer Login-Link per E-Mail (Token-Provider von ASP.NET Core Identity)
- Passkeys (WebAuthn, ASP.NET Core Identity)
- HttpOnly-Cookie-Session mit Widerruf über den Security Stamp („auf allen Geräten abmelden“)
- Audit-Log für alle Datenänderungen
- Rollenbasierte Zugriffskontrolle (RBAC)
- Soft Delete (Daten wiederherstellbar)
- DSGVO-Endpunkte:
  - `GET /api/gdpr/export`: Datenexport
  - `GET /api/gdpr/export/download`: Datenexport als JSON-Datei
  - `POST /api/gdpr/delete-account`: Kontolöschung (direkt, ohne Anonymisierung)
- Tägliche automatische Bereinigung (abgelaufene Tokens, alte Audit-Log-Einträge)
- Sichtbarkeitsstufen für Kontaktdaten
- Empfängerauflösung auf Basis der Zuordnungen mit abschließender Deduplizierung nach normalisierter E-Mail-Adresse (jede Adresse nur einmal im BCC)
- `RecipientAssignmentIds` werden serverseitig auf Berechtigungen geprüft (`403` bei unzulässigen IDs)
- Audit-Log für Setzen, Ändern und Löschen von Amtsadressen, für Bestätigung, erneuten Versand, Versandauflösung und Login-Hinweis
- Eingabevalidierung mit FluentValidation
