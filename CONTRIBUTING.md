# Mitwirken an Vernetzt

Danke für dein Interesse! Fehlermeldungen, Ideen, Doku-Verbesserungen und Code sind gleichermaßen willkommen. Es gilt der [Verhaltenskodex](CODE_OF_CONDUCT.md).

## Bevor du loslegst

- **Fehler oder Idee?** Lege ein [Issue](https://github.com/IGEVO/Vernetzt/issues/new/choose) an. Für größere Änderungen bitte erst ein Issue eröffnen und den Ansatz abstimmen, bevor du viel Zeit investierst.
- **Sicherheitslücke?** Bitte *kein* öffentliches Issue, sondern wie in [SECURITY.md](SECURITY.md) beschrieben melden.

## Sprache

- **Englisch**: Code, Kommentare, Bezeichner, Commit-Messages, PR-Titel.
- **Deutsch**: Oberflächentexte, Nutzerdokumentation (`user-docs/`, in der Du-Form), README, Issues und Diskussionen.

## Arbeitsweise

Wir arbeiten nach **GitHub Flow**:

1. Fork bzw. Branch von `main` anlegen. Branch-Namen: `feat/…`, `fix/…`, `docs/…`, `chore/…`.
2. Änderungen in kleinen, nachvollziehbaren Schritten umsetzen, mit Tests.
3. Pull Request gegen `main` öffnen. Der PR-Titel folgt [Conventional Commits](https://www.conventionalcommits.org/de/), z. B. `feat: CSV-Export für Kontakte` oder `fix(auth): Login-Link nach Ablauf korrekt ablehnen`. Er wird beim Squash-Merge zur Commit-Message.
4. Die CI muss grün sein. Gemergt wird ausschließlich per **Squash-Merge**.

`main` ist geschützt: Direkte Pushes und Force-Pushes sind nicht möglich.

## Lokale Prüfungen vor dem PR

```bash
# Backend
dotnet build Igevo.Vernetzt.slnx
dotnet test Igevo.Vernetzt.slnx

# Verwaltungsoberfläche
cd Igevo.Vernetzt.WebApp
npm run type-check && npm run lint && npm run api:check && npm test

# Öffentliches Portal
cd Igevo.Vernetzt.PublicWeb
npm run type-check && npm test
```

Ändert sich die API, müssen die OpenAPI-Snapshots aktualisiert werden (`npm run api:snapshot` und `npm run api:generate` in der WebApp, siehe [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)). Die CI prüft, dass Snapshot und Server übereinstimmen.

Datenbankänderungen brauchen eine EF-Core-Migration:

```bash
dotnet ef migrations add <Name> --project Igevo.Vernetzt.Infrastructure --startup-project Igevo.Vernetzt.Server
```

Neue oder geänderte Funktionen bitte auch in der Nutzerdokumentation unter `user-docs/docs/` beschreiben.

## Abhängigkeiten und Lizenz

Vernetzt steht unter der [EUPL-1.2](LICENSE). Mit deinem Beitrag stimmst du zu, dass er unter dieser Lizenz veröffentlicht wird. Neue Abhängigkeiten müssen eine dazu kompatible Lizenz haben (z. B. MIT, BSD, Apache-2.0, MPL-2.0, LGPL, EUPL). Abhängigkeits-Updates erledigt Dependabot.

## Releases (für Maintainer)

Versionen folgen [SemVer](https://semver.org/lang/de/) und werden **bewusst** vergeben, nicht automatisch hochgezählt:

1. Unter *Actions → Release → Run workflow* auf `main` entweder `bump` (`patch`, `minor`, `major`) wählen oder eine explizite `version` angeben, etwa `0.5.0-rc.1` für eine Vorabversion.
2. Der Workflow lässt die komplette CI laufen. Danach baut er die Images für amd64 und arm64 und veröffentlicht sie mit Provenance unter `ghcr.io/igevo/vernetzt` und `ghcr.io/igevo/vernetzt-public`. Zum Schluss legt er den Tag `vX.Y.Z` samt GitHub-Release mit generierten Release Notes an.
3. Bei stabilen Versionen wird außerdem die Nutzerdokumentation auf GitHub Pages aktualisiert.

Die Release Notes werden nach PR-Labels gruppiert (siehe `.github/release.yml`). Labels deshalb beim Mergen setzen, z. B. `feature`, `bug`, `security` oder `breaking-change`.

Tags nie von Hand anlegen.
