---
title: "Administration: Benutzerverwaltung"
description: "Benutzer in Vernetzt einladen, bearbeiten, berechtigen und löschen."
---

# Administration: Benutzerverwaltung

Diese Seite ist für Rollen mit Verwaltungsrechten gedacht (z. B. SystemAdmin, Manager).

## Funktionen

- Benutzer suchen
- Benutzer einladen
- Benutzerdaten bearbeiten
- Rollen verwalten
- Passkeys zurücksetzen (nur SystemAdmin)
- Benutzer löschen

## Benutzer einladen

Beim Einladen kannst du optional direkt festlegen:

- Zuordnung zu Verein, Abteilung oder Gruppe
- Position
- Schreibrechte für die Zuordnung
- (rollenabhängig) zusätzliche Berechtigungen

::: callout warning "Vor Einladung prüfen"
Prüfe zuerst, ob die Person bereits in Vernetzt existiert, um doppelte Konten zu vermeiden.
:::

::: callout warning "Persönliche Adresse verwenden"
Lade Personen immer mit ihrer **persönlichen** E-Mail-Adresse ein – nicht mit einer Adresse wie vorstand@verein.de, die bei einem Amtswechsel weitergegeben wird. Eine solche Amtsadresse kannst du bei einer Zuordnung im Feld **Amtsadresse für diese Zuordnung** angeben. Mehr dazu unter [Persönliche Adresse und Amtsadresse](/app/amtsadressen).
:::

Welche Einladungen noch offen sind, siehst du unter [Ausstehende Bestätigungen](/admin/ausstehende-bestaetigungen). Dort kannst du sie auch widerrufen.

## Rollen verwalten

Je nach eigener Berechtigung kannst du Rollen vergeben oder entziehen, z. B.:

- SystemAdmin
- Manager
- DataReader

## Sichtbarkeit im Überblick

In der Tabelle siehst du pro Benutzer auch die Sichtbarkeitsstufen von E-Mail und Telefon.

## Benutzer bearbeiten

Über den Bearbeiten-Dialog kannst du:

- Stammdaten und Sichtbarkeit ändern

## Passkeys zurücksetzen

Hat jemand sein Gerät verloren oder kommt mit seinem Passkey nicht mehr weiter, kann eine Person mit der Rolle **SystemAdmin** die Passkeys zurücksetzen:

1. Klicke in der Zeile der Person auf das Schlüssel-Symbol **Passkeys zurücksetzen**.
2. Bestätige mit **Zurücksetzen**.

Dabei werden alle Passkeys der Person entfernt und alle ihre Sitzungen beendet. Sie meldet sich danach per E-Mail-Anmeldelink an und kann einen neuen Passkey einrichten. Der Vorgang wird im Audit-Log protokolliert.

::: callout info "Eigene Passkeys"
Deine eigenen Passkeys verwaltest du unter **Mein Konto**. Gibt es nur eine Person mit SystemAdmin-Rolle, kann niemand ihre Passkeys über die Oberfläche zurücksetzen – vergib die Rolle daher am besten an mindestens zwei Personen.
:::
