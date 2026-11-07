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

Du hinterlegst die URL zur Datenschutzerklärung.

Diese URL wird z. B. im Onboarding genutzt, damit neue Nutzer die Datenschutzhinweise bestätigen können.

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
