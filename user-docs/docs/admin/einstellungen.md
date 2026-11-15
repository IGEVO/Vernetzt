---
title: "Administration: Einstellungen"
description: "Systemweite Grundeinstellungen in Vernetzt."
---

# Administration: Einstellungen

Hier pflegst du zentrale Angaben, die Vernetzt an mehreren Stellen verwendet.

## Bereich: Ansprechpartner (E-Mail-Footer)

Du hinterlegst:

- Namen des Ansprechpartners
- E-Mail-Adresse des Ansprechpartners

Diese Daten erscheinen z. B. im Footer systemseitig versendeter E-Mails.

## Bereich: Datenschutz

Für die Datenschutzerklärung von Vernetzt wählst du eine von zwei Varianten:

- **Externer Link**: Du hinterlegst die Adresse einer Seite, z. B. auf eurer Homepage.
- **Eigener Text**: Du schreibst die Erklärung direkt in Vernetzt. Sie erscheint ohne Anmeldung unter `/datenschutz` (z. B. `https://vernetzt.example.org/datenschutz`).

Beide Inhalte bleiben gespeichert, wenn du die Variante wechselst. Veröffentlicht wird nur die gewählte.

Verlinkt wird die Datenschutzerklärung:

- in der Fußzeile aller E-Mails von Vernetzt und im Hinweis bei Rundmails und weitergeleiteten Nachrichten
- bei der Registrierung, wo neue Nutzer bestätigen, dass sie die Erklärung zur Kenntnis genommen haben
- auf der Anmeldeseite und im Hilfe-Menü

Ohne Datenschutzerklärung (und ohne Ansprechpartner) lassen sich keine Einladungen versenden.

::: callout info "Öffentliches Portal"
Das öffentliche Portal hat eine eigene Datenschutzerklärung (Bereich **Öffentliches Portal**). Gilt für beide derselbe Text, wähle beim Portal **Externer Link** und trage die Adresse `/datenschutz` von Vernetzt ein – oder umgekehrt.
:::

## Bereich: Globale Kalender-Feeds (iCal)

In den Einstellungen kannst du als Systemadministration globale iCal-Feeds verwalten.

- **Alle Termine (global)**: enthält alle Termine aus dem gesamten System
- **Highlights (global)**: enthält nur als Highlight markierte Termine

Für beide Feed-Typen gilt:

- Der gespeicherte Link wird dauerhaft auf der Seite angezeigt
- Rotation ersetzt den alten Link durch einen neuen
- Widerruf deaktiviert den Feed sofort

Zusätzlich gilt:

- Der Zugriff auf den Feed erfolgt über den Link selbst (tokenbasiert)
- Teile Feed-Links nur gezielt mit Personen oder Systemen, die den Inhalt sehen dürfen

::: callout warning "Datenschutz und Link-Freigabe"
iCal-Feed-Links sind tokenbasiert. Personen mit dem Link können den jeweiligen Feed abrufen.
Gib Links nur kontrolliert weiter und rotiere sie bei Verdacht auf unbeabsichtigte Weitergabe.
:::

::: callout warning "Pflichtangaben"
Name, Kontakt-E-Mail und Datenschutz-URL sollten immer gepflegt sein, damit Onboarding und Kommunikation sauber funktionieren.
:::
