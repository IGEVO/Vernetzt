---
title: "Datenverarbeitung und Sichtbarkeit"
description: "Welche Daten Vernetzt verarbeitet und wer welche Daten sehen kann."
---

# Datenverarbeitung und Sichtbarkeit

Diese Seite erklärt transparent, welche Daten in **Vernetzt** verarbeitet werden und wer darauf Zugriff hat.

## Grundprinzipien in Vernetzt

- Datensparsamkeit: Es werden nur notwendige Daten verarbeitet.
- Rollen- und Sichtbarkeitssteuerung: Nicht alle sehen alles.
- Nachvollziehbarkeit: Wichtige Änderungen werden protokolliert.
- Trennung von Login und Kommunikation: Die persönliche Login-Adresse und Amtsadressen sind getrennt.

## Persönliche Adresse vs. Amtsadresse

- Die persönliche E-Mail-Adresse ist die **einzige Login-Adresse**.
- Pro Zuordnung (z. B. Verein, Abteilung oder Gruppe) kann optional eine **Amtsadresse** gesetzt werden, z. B. vorstand@verein.de.
- Amtsadressen dienen der Kommunikation, nicht dem Login. So bekommt bei einem Amtswechsel niemand über die weitergegebene Adresse Zugriff auf ein persönliches Konto.
- Nicht bestätigte Amtsadressen werden nicht für den Versand genutzt.

## Sichtbarkeitsstufen

- **Administration & Verwaltung**
- **Alle registrierten Benutzer**
- **Öffentlich (auch Gäste)**

Diese Stufen gelten für zentrale Kontaktdaten wie E-Mail und Telefonnummer.

## Datenmatrix (vereinfacht)

| Datentyp | Zweck in Vernetzt | Speicherung | Sichtbarkeit / Zugriff |
|---|---|---|---|
| Persönliche E-Mail (Benutzer) | Anmeldung, direkte Kommunikation | Datenbank | laut persönlicher Sichtbarkeit |
| Telefon (Benutzer) | Kontaktaufnahme | Datenbank | laut persönlicher Sichtbarkeit |
| Amtsadresse je Zuordnung | Rollenbezogene Kommunikation | Datenbank | laut Zuordnungs-Sichtbarkeit; unbestätigt eingeschränkt |
| Vereins-/Abteilungs-Stammdaten (inkl. Webseite und Social-Media-Links) | öffentliche Stamminformation | Datenbank | öffentlich einsehbar |
| Termine inkl. Bilder und Dokumente | öffentlicher Veranstaltungskalender | Datenbank + Dateispeicher | öffentlich einsehbar (Portal, Kalender-Abos) |
| Rollen/Berechtigungen | Zugriffskontrolle | Datenbank | Verwaltungsrollen, systemseitige Prüfung |
| Audit-Log-Einträge | Nachvollziehbarkeit und Sicherheit | Datenbank | primär SystemAdmin |
| Avatare | visuelle Zuordnung | Dateispeicher + Datenbank-Referenz | je nach Entität sichtbar |
| Adressbuch-Abo (CardDAV) | Kontakte auf eigenen Geräten | nur Hash des App-Passworts, Zeitpunkt der letzten Nutzung | Abonnent sieht dieselben Kontaktdaten wie in der App; die Daten liegen danach zusätzlich auf seinen Geräten bzw. in deren Cloud-Synchronisierung |

## E-Mail-Versand ohne Offenlegung der Empfänger

Beim Rundmail-Versand über Vernetzt gilt:

- Empfängeradressen werden untereinander nicht offengelegt.
- Empfängerauflösung erfolgt serverseitig über zulässige Zuordnungen.
- Doppelte Zieladressen werden zusammengeführt.

::: callout success "Datenschutzvorteil"
Du kannst in Vernetzt Gruppen anschreiben, ohne dass Empfänger die Adressen anderer Empfänger sehen.
:::

## DSGVO-Funktionen für dich

Unter **Mein Konto** stehen dir bereit:

- Datenauskunft (Export als JSON)
- Kontolöschung

Beides ist als Self-Service direkt in Vernetzt erreichbar.
