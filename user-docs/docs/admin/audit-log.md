---
title: "Administration: Audit-Log"
description: "Änderungen und sicherheitsrelevante Ereignisse in Vernetzt nachvollziehen."
---

# Administration: Audit-Log

Das Audit-Log zeigt nachvollziehbar, was in **Vernetzt** passiert ist.

## Wofür nutzt du das Audit-Log?

- Änderungen prüfen
- Fehlerquellen eingrenzen
- Sicherheitsereignisse nachvollziehen
- Datenschutz-Anfragen unterstützen

## Filtermöglichkeiten

- Entitätstyp
- Aktion
- Seitenweise Navigation (Pagination)

## Typische Aktionen im Log

- erstellt
- geändert
- gelöscht
- Anmeldung / Abmeldung
- E-Mail-Versand
- Relay-Weiterleitung

## Detailansicht

Für jeden Eintrag kannst du eine Detailansicht öffnen, um mehr Kontext zu sehen.

## Aufbewahrung und Inhalt

- Einträge werden nach **90 Tagen** automatisch gelöscht (Einstellung `App:AuditLogRetentionDays`).
- Zu jedem Eintrag werden die handelnde Person (als ID), der Zeitpunkt und die IP-Adresse gespeichert.
- Wird ein Konto gelöscht, enthält der Eintrag nur noch die IDs von gelöschtem Konto und handelnder Person, die Zahl der entfernten Zuordnungen und den Löschgrund. Name und E-Mail-Adresse der gelöschten Person werden nicht ins Log übernommen. Ältere Einträge zu diesem Konto bleiben bis zum Ablauf der 90 Tage erhalten.

::: callout info "Datenschutz im Log"
Sensible technische Inhalte werden in Übersichten reduziert dargestellt, damit keine unnötigen Details offengelegt werden.
:::
