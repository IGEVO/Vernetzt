# Sicherheitsrichtlinie

## Unterstützte Versionen

Sicherheitskorrekturen gibt es nur für die **jeweils neueste veröffentlichte Version** (siehe [Releases](https://github.com/IGEVO/Vernetzt/releases)). Bitte halte deine Installation aktuell. Container-Images mit dem Tag `latest` bzw. `<major>.<minor>` erhalten Korrekturen automatisch beim nächsten Update.

## Sicherheitslücke melden

Bitte melde Sicherheitslücken **nicht** über öffentliche Issues, Diskussionen oder Pull Requests.

Nutze stattdessen die vertrauliche Meldung über GitHub:
**[Sicherheitslücke melden](https://github.com/IGEVO/Vernetzt/security/advisories/new)** (Reiter *Security → Report a vulnerability*).

Hilfreich sind:

- betroffene Version bzw. Image-Tag,
- Beschreibung der Lücke und der möglichen Auswirkungen,
- Schritte zur Reproduktion oder ein Proof of Concept,
- gegebenenfalls ein Vorschlag zur Behebung.

## Was dann passiert

Vernetzt ist ein **ehrenamtlich betriebenes Freizeitprojekt**. Es gibt kein Sicherheitsteam, keine Bereitschaft und keine vertraglich zugesicherten Reaktionszeiten. Jede Meldung wird ernst genommen und nach bestem Bemühen bearbeitet:

- Wir bestätigen den Eingang, sobald es uns möglich ist, in der Regel innerhalb von etwa **zwei Wochen**.
- Kritische Lücken, die Daten von Nutzenden gefährden, haben Vorrang vor allen anderen Arbeiten am Projekt.
- Wir halten dich über das Advisory auf dem Laufenden, auch wenn es mal länger dauert.
- Nach der Behebung veröffentlichen wir ein Release und ein GitHub Security Advisory. Wenn du möchtest, nennen wir dich dort als Finder.

Bitte gib uns angemessen Zeit für eine Korrektur, bevor du Details veröffentlichst (Coordinated Disclosure). Als Richtwert gelten 90 Tage; bei Bedarf stimmen wir einen anderen Zeitpunkt gemeinsam ab.

Ein Bug-Bounty-Programm gibt es nicht.

## Gewährleistung

Vernetzt wird ohne Gewährleistung bereitgestellt (siehe Artikel 7 und 8 der [EUPL-1.2](LICENSE)). Für den sicheren Betrieb einer Installation (Updates einspielen, Zugänge absichern, Datensicherung) ist die jeweils betreibende Organisation verantwortlich.

## Hinweise für den Betrieb

Hinweise zum sicheren Betrieb (Reverse Proxy, Secrets, Data-Protection-Zertifikat, Container-Limits) stehen in [docs/HOSTING.md](docs/HOSTING.md).
