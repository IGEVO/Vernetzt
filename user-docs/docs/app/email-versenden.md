---
title: "E-Mail versenden"
description: "Rundmails in Vernetzt sicher an Gruppen senden."
---

# E-Mail versenden

In **Vernetzt** versendest du E-Mails in einem klaren 4-Schritt-Ablauf.

## Schritt 1: Schnellauswahl

Du kannst Empfänger vorfiltern nach:

- Vereinstyp
- Position
- Verein
- Standort

Diese Auswahl ist eine Vorauswahl für den nächsten Schritt. Filtergruppen werden mit UND kombiniert, innerhalb einer Gruppe gilt ODER.

### Filter „Nach Standort“

Der Standortfilter wählt Vereine, Abteilungen und Gruppen, bei denen einer der gewählten [Standorte](/admin/standorte) eingetragen ist. Standorte werden nicht vererbt: Ein Standort am Verein wählt nicht dessen Abteilungen und Gruppen aus.

Je Organisation werden dann ausgewählt:

- die Kontaktpersonen mit **Erhält Standort-Infos**
- **alle** Kontaktpersonen, wenn bei der Organisation niemand markiert ist

::: callout tip "Mit Positionsfilter"
Ist zusätzlich ein Positionsfilter aktiv, entscheidet die Position statt der Markierung. Beispiel: Standort *Sporthalle Nord* + Position *Hallenwart* wählt die Hallenwarte aller Organisationen, die die Halle nutzen, ob markiert oder nicht.
:::

In Schritt 2 erklärt ein Hinweis, welche Regel angewendet wurde. Du kannst dort wie gewohnt einzelne Personen ab- oder anwählen.

## Schritt 2: Empfänger bestätigen

- Prüfe die konkrete Empfängerliste.
- Wähle einzelne Kontakte ab oder zusätzlich an.
- Mindestens ein Empfänger ist erforderlich.

## Schritt 3: Versandart wählen

### Einfache Mail

Direkt in Vernetzt schreiben und senden:

- Betreff
- Nachricht (Rich-Text-Editor)

### Eigener E-Mail-Client

Vernetzt erzeugt eine einmalige Zieladresse. Du sendest dann aus Outlook, Apple Mail oder einem anderen Mail-Programm.

::: callout info "Verfügbarkeit"
Die Option **Eigener E-Mail-Client** ist nur sichtbar, wenn der Relay-Dienst aktiv ist.
:::

## Schritt 4: Senden

### Bei „Einfache Mail“

- Vernetzt sendet als Sammelversand.
- Du bestätigst den Versand vor dem Senden.

### Bei „Eigener E-Mail-Client“

- Vernetzt erstellt eine temporäre, einmalig nutzbare Adresse.
- Diese Adresse hat ein Ablaufdatum.
- Nach erfolgreicher Verarbeitung bekommst du eine Bestätigung.
- Die maximale Größe der E-Mail steht direkt bei der erzeugten Adresse (Standard 20 MB inklusive Anhängen). Anhänge werden beim Versand etwa ein Drittel größer, praktisch sind das rund 15 MB Anhänge.
- Ist die E-Mail zu groß, wird sie nicht weitergeleitet und du bekommst eine Benachrichtigung. Die Adresse bleibt gültig, du kannst eine kleinere E-Mail an dieselbe Adresse senden.

## Datenschutz beim E-Mail-Versand

::: callout success "Empfänger bleiben untereinander verborgen"
Vernetzt leitet Rundmails so weiter, dass Empfänger die Adressen anderer Empfänger nicht sehen.
:::

Zusätzlich gilt:

- Empfängerauflösung erfolgt serverseitig anhand deiner zulässigen Zuordnungen.
- Doppelte E-Mail-Adressen werden technisch bereinigt.
- Deine eigene Absenderadresse bleibt sichtbar, damit Antworten möglich sind.
