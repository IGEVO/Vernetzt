# Hosting-Hinweise (Produktion)

Diese Seite ergänzt die Compose-Beispiele im [README](../README.md#-production-deployment) um die Punkte, die für einen sicheren Betrieb wichtig sind. Sie stammen aus dem Security-Audit (November 2026). Alle Werte werden als Umgebungsvariablen gesetzt (z. B. im Portainer-Stack), nicht in `appsettings.Production.json`.

## Checkliste vor dem Go-live

- [ ] Reverse Proxy terminiert TLS, der App-Port ist **nicht** öffentlich erreichbar
- [ ] `ReverseProxy__Enabled=true` und `ReverseProxy__KnownProxies`/`KnownNetworks` passen zur **tatsächlichen** Absenderadresse des Proxys (siehe unten, prüfen!)
- [ ] Data-Protection-Zertifikat erzeugt, gemountet und **separat gesichert** (Pflicht, sonst startet die App in Production nicht)
- [ ] `Health__Token` gesetzt (zufällig, mindestens 32 Zeichen) – oder bewusst leer gelassen
- [ ] Alle Secrets (SMTP-, IMAP-Passwort, Health-Token, Zertifikatspasswort) nur als Umgebungsvariablen/Secrets
- [ ] Drei Mounts eingerichtet: `/app/data` (Datenbank, **lokale Disk**), `/app/uploads`, `/app/backups` (möglichst andere Disk/NAS)
- [ ] Host-Backup sichert `/app/uploads` und `/app/backups` nach extern, zeitlich **nach** dem täglichen DB-Backup (siehe [Datensicherung](#datensicherung))
- [ ] Wiederherstellung einmal ausprobiert
- [ ] Container-Limits für Speicher, CPU und Prozesse gesetzt
- [ ] `PUID`/`PGID` passen zu den Rechten der gemounteten Dateien (Volumes, Zertifikat)
- [ ] Relay-Postfach (IONOS) nur für Vernetzt genutzt, Größenlimits bekannt
- [ ] Monitoring: Telemetrie kommt im LGTM-Stack an, Grafana nicht öffentlich und ohne anonymen Zugriff, Kontaktpunkt für Alerts eingerichtet (siehe [Monitoring](#monitoring-logs-metriken-alerts))

## Reverse Proxy und Client-IP

Die anonymen Auth-Endpunkte (Login-Link anfordern/einlösen, Passkey-Anmeldung, `/api/auth/status`) sind pro **Client-IP** rate-limitiert (z. B. 10 Anfragen/Minute). Die App sieht die echte Client-IP nur, wenn sie dem Proxy vertraut: `X-Forwarded-For` wird ausschließlich ausgewertet, wenn die Verbindung von einer Adresse aus `ReverseProxy:KnownProxies` bzw. `ReverseProxy:KnownNetworks` kommt.

**Wenn das nicht passt, teilen sich alle Nutzer ein einziges Rate-Limit.** Dann kann ein einzelner anonymer Angreifer mit 10 Anfragen pro Minute die Anmeldung für alle blockieren.

Typische Fallstricke:

- Der Proxy läuft auf dem Host oder in einem anderen Container. Die App sieht dann **nicht** `127.0.0.1`, sondern die Gateway-Adresse des Docker-Netzes (z. B. `172.18.0.1`) bzw. die Container-IP des Proxys.
- `ReverseProxy__Enabled` ist nicht gesetzt. Dann werden Forwarded-Header gar nicht ausgewertet.

Empfohlene Konfiguration (Beispiel: Proxy und App im selben Docker-Netz `proxy`):

```yaml
services:
  app:
    networks: [proxy, default]
    # kein "ports:" – nur der Proxy spricht mit der App
    environment:
      - ReverseProxy__Enabled=true
      - ReverseProxy__KnownNetworks__0=172.18.0.0/16   # Subnetz des Netzes "proxy"
      - ReverseProxy__ForwardLimit=1
```

Das Subnetz zeigt `docker network inspect proxy` (Feld `Subnet`). Das Netz sollte nur Proxy und App enthalten, weil jeder Container darin `X-Forwarded-For` setzen darf. Wer die Adresse noch enger fassen will, gibt dem Proxy eine feste IP und trägt sie unter `KnownProxies` ein.

Läuft der Proxy auf dem Host, den App-Port nur an localhost binden (`"127.0.0.1:8080:8080"`) und als `KnownNetworks` das Subnetz des Bridge-Netzes der App eintragen.

**Prüfen nach dem Deployment:** Zweimal von unterschiedlichen Anschlüssen (z. B. Mobilfunk und Festnetz) einen Login-Link anfordern und im Audit-Log (Admin → Audit-Log, Einträge `AuthLogin`) bzw. in den Logs nachsehen, ob die **echten, unterschiedlichen** Client-IPs erscheinen. Steht dort überall dieselbe private Adresse, stimmt die Proxy-Konfiguration nicht.

## Health-Endpunkt

`GET /api/health/details` liefert Versions- und Systeminformationen und erfordert den Header `X-Health-Token`.

- Token per `Health__Token` setzen, z. B. erzeugt mit `openssl rand -hex 32`.
- Ohne Token (leer), mit dem früheren Platzhalter `CHANGE-THIS-…` oder mit weniger als 32 Zeichen ist der Endpunkt **deaktiviert** (immer 401).
- Der einfache Check `GET /health` bleibt ohne Token verfügbar (für Docker-Healthchecks).
- Wenn möglich `/api/health/details` am Proxy nur aus dem internen Netz bzw. für das Monitoring freigeben.

## Monitoring (Logs, Metriken, Alerts)

Vernetzt schickt Logs, Metriken und Traces per OpenTelemetry an den LGTM-Stack (`grafana/otel-lgtm`, Compose „Setup B“ im README). Ohne `OTEL_EXPORTER_OTLP_ENDPOINT` gehen Logs nur auf die Konsole (`docker logs vernetzt-app`).

Nötige Umgebungsvariablen der App:

```yaml
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://lgtm:4317
      - OTEL_EXPORTER_OTLP_PROTOCOL=grpc
      - OTEL_SERVICE_NAME=vernetzt          # Dashboards und Alerts filtern darauf
      - OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production
```

Für den LGTM-Container gilt:

- **`GF_AUTH_ANONYMOUS_ENABLED=false` setzen.** `otel-lgtm` erlaubt sonst jedem ohne Login Admin-Zugriff auf Grafana.
- Port 3000 nur an `127.0.0.1` binden und Grafana über den Reverse Proxy (mit TLS) oder einen SSH-Tunnel erreichen. Die OTLP-Ports 4317/4318 nicht veröffentlichen, die App erreicht sie im Compose-Netz.
- `/data` als Volume mounten. Darin liegen Grafana-Einstellungen, Logs, Metriken und Traces. Ohne Volume ist nach einem Neuerstellen des Containers alles weg.
- Image-Version festlegen (z. B. `grafana/otel-lgtm:0.40.0`) und bewusst aktualisieren.

Dashboards und Alert-Regeln werden aus `observability/grafana/` provisioniert (Ordner „Vernetzt“ in Grafana). Damit Alerts jemanden erreichen, unter *Alerting → Contact points* einen Kontaktpunkt anlegen und der Default-Policy zuweisen. Details und alle Metriken: [observability/grafana/README.md](../observability/grafana/README.md).

**Prüfen nach dem Deployment:**

1. In Grafana *Explore* → Prometheus: `target_info{service_name="vernetzt"}` liefert einen Eintrag mit der erwarteten `service_version`.
2. *Explore* → Loki: `{service_name="vernetzt"}` zeigt aktuelle Logs.
3. Im Dashboard **Vernetzt - Betrieb** zeigt „Letztes Backup vor“ einen Wert, sobald das erste geplante Backup gelaufen ist.
4. *Alerting → Alert rules*: alle Regeln im Ordner „Vernetzt“ sind „Normal“, keine steht auf „Error“.

Der All-in-one-Container ist für eine einzelne Instanz gedacht. Das Image setzt keine eigenen Aufbewahrungsfristen. Es gelten die Standards: Prometheus 15 Tage, Tempo 14 Tage, Loki unbegrenzt. Den Platz auf dem `/data`-Volume deshalb im Blick behalten.

## CardDAV-Adressbuch

Jeder Benutzer kann unter **Mein Konto → Adressbuch** ein persönliches App-Passwort erzeugen und die Kontakte per CardDAV (nur lesend) abonnieren. Die Endpunkte liegen unter `/carddav/` und `/.well-known/carddav` (Weiterleitung für die automatische Erkennung, RFC 6764). Damit das hinter dem Reverse Proxy funktioniert:

- Die WebDAV-Methoden **`PROPFIND`, `REPORT` und `OPTIONS`** müssen durchgelassen werden. Viele WAFs erlauben standardmäßig nur `GET|POST|HEAD` (z. B. BunkerWeb `ALLOWED_METHODS`, OWASP CRS `tx.allowed_methods`). Für `/carddav/` und `/.well-known/carddav` die Liste um `OPTIONS|PROPFIND|REPORT` erweitern.
- Der Header **`Authorization`** (HTTP Basic) muss unverändert an die App gehen.
- Request-Bodies mit `Content-Type: application/xml` bzw. `text/xml` dürfen nicht blockiert werden.
- Rate-Limit: 120 Anfragen/Minute pro Client-IP (`RateLimiting__CardDav__PermitLimit`). Nach 10 fehlgeschlagenen Anmeldungen innerhalb von 5 Minuten antwortet die App für diese IP mit 429. Beides setzt eine korrekte Client-IP voraus (siehe oben).

Sicherheitsmodell: Das App-Passwort wirkt nur auf `/carddav` und erlaubt ausschließlich Lesen; die `/api`-Endpunkte werten Basic-Credentials nicht aus. Das Adressbuch zeigt die Kontaktdaten mit den Rechten des Benutzers – bewusst auch für SystemAdmin/Manager ohne Passkey-Sitzung. Ein App-Passwort erzeugen kann ein Admin/Manager aber nur aus einer Passkey-Sitzung. „Auf allen Geräten abmelden“ widerruft das App-Passwort.

Prüfen nach dem Deployment: `curl -i -X PROPFIND https://<host>/carddav/` muss `401` mit `WWW-Authenticate: Basic …` liefern, nicht `403` oder `405` vom Proxy.

## Container-Limits

Bild-Uploads (Avatare, Event-Medien) werden im App-Prozess mit ImageMagick verarbeitet. Der Code begrenzt das selbst:

- Avatare und Event-Medien: nur JPEG/PNG/WebP/HEIC, erkannt am Dateiinhalt. PDFs und andere Dokumente werden nicht angenommen.
- Maximal 40 Megapixel, geprüft vor dem Dekodieren.
- ImageMagick-Ressourcenlimits (u. a. 512 MB Speicher, 1 GB Platte, 60 s).

Zusätzlich sollte der Container selbst begrenzt sein, damit ein Ausreißer nicht den Host trifft:

```yaml
    mem_limit: 2g
    cpus: 2
    pids_limit: 256
    tmpfs:
      - /tmp:size=1g
```

`restart: unless-stopped` sorgt dafür, dass der Container nach einem OOM-Kill neu startet.

### Kein Ghostscript

Das Image enthält bewusst kein Ghostscript. Ohne Ghostscript kann ImageMagick kein PDF oder PostScript verarbeiten, auch nicht über Umwege. Wer später PDF-Uploads wieder einführen will, sollte die Vorschau in einem separaten, stark begrenzten Prozess oder Container rendern.

## E-Mail-Relay (IMAP, IONOS)

Das Relay pollt das konfigurierte Postfach und lädt die Nachrichten in den Speicher.

- **Eigenes Postfach** nur für das Relay verwenden. Alle Nachrichten im Posteingang werden verarbeitet und nach der Verarbeitung gelöscht.
- Pro Abfrage werden höchstens `EmailRelay__MaxMessagesPerPoll` (Standard 25) Nachrichten geladen.
- Nachrichten über `EmailRelay__MaxMessageSizeBytes` (Standard 20 MB) werden nicht heruntergeladen. Ist die Zieladresse ein aktiver Token, erhält der **Besitzer des Tokens** (nicht der Absender der Mail, dessen Adresse gefälscht sein kann) eine Fehlermail. Der Token bleibt gültig.
- Die UI zeigt das Limit bei der erzeugten Adresse an. Das Limit des SMTP-Servers vor dem Postfach kann niedriger sein und gilt zusätzlich.
- Nachrichten, die länger als `EmailRelay__MailboxRetentionHours` (Standard 24, Minimum 1) im Postfach liegen, werden automatisch gelöscht. Das betrifft auch zu große, nicht verarbeitbare und nicht zuordenbare Nachrichten. Die Aufbewahrung dient der Fehlersuche und sollte wegen personenbezogener Daten kurz bleiben.
- Die Postfach-Quota bei IONOS begrenzt zusätzlich, wie viel sich ansammeln kann.

```yaml
      - EmailRelay__Enabled=true
      - EmailRelay__MonitorMode=Imap
      - EmailRelay__Imap__Host=imap.ionos.de
      - EmailRelay__Imap__Port=993
      - EmailRelay__Imap__UseSsl=true
      - EmailRelay__Imap__Username=${EMAIL_RELAY_IMAP_USERNAME}
      - EmailRelay__Imap__Password=${EMAIL_RELAY_IMAP_PASSWORD}
```

## Login-Links

Pro Konto werden höchstens `Auth__LoginLinkMaxPerUserPerHour` (Standard 5) Login-Link-Mails pro Stunde verschickt. Weitere Anfragen werden still verworfen; die Antwort an den Client bleibt gleich, damit sich Konten nicht ausprobieren lassen. Der Zähler liegt im Speicher und wird bei einem Neustart zurückgesetzt. Er gilt pro Instanz, was bei einer einzelnen Instanz ausreicht.

## Data-Protection-Zertifikat

Mit den Data-Protection-Keys signiert die App Session-Cookies und Login-Links. Sie liegen in der Tabelle `DataProtectionKeys`. In Production werden sie mit einem Zertifikat verschlüsselt, das **nicht** in der Datenbank liegt. Ein DB-Dump oder Backup allein reicht dann nicht, um Sessions zu fälschen.

Ohne `DataProtection__CertificatePath` startet die App in Production nicht. In Development und Test bleibt das Zertifikat optional.

Zertifikat einmalig erzeugen (gültig 10 Jahre, nur zum Verschlüsseln der Keys, kein TLS-Zertifikat):

```bash
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
  -subj "/CN=Vernetzt DataProtection" -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dataprotection.pfx \
  -passout pass:"$(openssl rand -hex 24 | tee dataprotection.pfx.password)"
rm dp.key dp.crt
```

Im Portainer-Stack einbinden:

```yaml
    environment:
      - DataProtection__CertificatePath=/run/secrets/dataprotection.pfx
      - DataProtection__CertificatePassword=${DATAPROTECTION_CERT_PASSWORD}
    volumes:
      - /srv/vernetzt/secrets/dataprotection.pfx:/run/secrets/dataprotection.pfx:ro
```

Wichtig:

- **`dataprotection.pfx` und das Passwort getrennt von den DB-Backups sichern**, z. B. im Passwortmanager. Wenn das Zertifikat verloren geht, sind nach einem Neustart alle Sessions und offenen Login-Links ungültig. Alle müssen sich dann neu anmelden. Daten gehen dabei keine verloren.
- Die Datei auf dem Host nur für den App-User lesbar machen: `chown 1654:1654 dataprotection.pfx && chmod 400 dataprotection.pfx` (bzw. die eigene `PUID`/`PGID`, siehe unten). Gehört sie root mit `chmod 600`, kann die App sie nicht lesen und startet nicht.
- **Zertifikatswechsel:** Das neue Zertifikat konfigurieren und das alte zusätzlich zum Entschlüsseln bereitstellen (`UnprotectKeysWithAnyCertificate`, erfordert eine kleine Code-Erweiterung), bis alle alten Keys abgelaufen sind (90 Tage).

## Benutzer und Dateirechte (PUID/PGID)

Die App läuft im Container nicht als root. Der Entrypoint (`docker/entrypoint.sh`) startet als root, übergibt die Datenverzeichnisse (`/app/App_Data`, `/app/data`, `/app/uploads`, `/app/backups` sowie `Storage__RootPath` und `Backup__Path`, falls abweichend) an den App-User und startet die App dann ohne Root-Rechte und ohne Capabilities.

Standard ist UID/GID `1654` (User `app` des .NET-Base-Images). Anpassen per Umgebungsvariablen:

```yaml
    environment:
      - PUID=1000
      - PGID=1000
```

- Bei jedem Start bekommt jede Datei und jedes Verzeichnis mit anderem Besitzer `PUID`/`PGID`, also auch einzeln als root hineinkopierte Dateien (z. B. eine zurückgespielte Datenbank). Es werden nur Metadaten gelesen, Neustarts bleiben auch mit vielen Uploads schnell.
- `PUID=0` wird abgelehnt.
- Read-only gemountete Dateien (z. B. `dataprotection.pfx`) passt der Entrypoint **nicht** an. Sie müssen für `PUID`/`PGID` lesbar sein.

Alternativ lässt sich der User direkt per `user: "1000:1000"` (bzw. `docker run --user`) setzen. Dann läuft auch der Entrypoint nicht als root und ändert keine Rechte; die gemounteten Verzeichnisse müssen vorher schon für diesen User beschreibbar sein.

## Datensicherung

Vernetzt nutzt eine SQLite-Datenbank (`/app/data/vernetzt.db`). Es gibt keinen Datenbank-Container und keine DB-Zugangsdaten.

### Was Vernetzt selbst macht

- Täglich um `Backup__Time` (Standard `03:00`, Zeitzone `Backup__TimeZone`, Standard `Europe/Berlin`) eine konsistente Kopie per `VACUUM INTO` nach `/app/backups`, im laufenden Betrieb
- Vor Datenbank-Migrationen beim Start eine Sicherung (`Backup__BeforeMigration`)
- Auf Knopfdruck unter *Administration → Systemeinstellungen → Datensicherung*, z. B. vor einem Update
- Aufbewahrung: von den täglichen Sicherungen die letzten `Backup__KeepDaily` Tage (14) und je Kalenderwoche die letzte für `Backup__KeepWeekly` Wochen (8). Manuelle Sicherungen und Sicherungen vor Updates bleiben alle `Backup__KeepDaily` Tage erhalten.
- Status im Admin-Bereich und unter `/api/health/details` (Check `backups`)

Alle Werte sind optional. Den täglichen Lauf schaltet `Backup__Enabled=false` ab (z. B. wenn ein externes Tool sichert); manuelle Sicherungen und die Sicherung vor Migrationen funktionieren weiterhin.

### Was der Host machen muss

Die Sicherungen liegen zunächst auf demselben Server. Gegen Plattenausfall oder ein gelöschtes Volume helfen sie nur, wenn sie zusätzlich nach extern kopiert werden:

| Mount | Inhalt | Host-Backup |
|---|---|---|
| `/app/data` | Datenbank inkl. `-wal`/`-shm` | **nicht** kopieren, solange die App läuft – stattdessen `/app/backups` sichern |
| `/app/uploads` | Avatare, Termin-Medien | ja, inkrementell (restic, borg, rsync) |
| `/app/backups` | DB-Sicherungen von Vernetzt | ja |

- `/app/data` muss auf einer **lokalen Disk** liegen (kein NFS/SMB – das File-Locking von SQLite ist dort unzuverlässig). `/app/uploads` und `/app/backups` dürfen auf einem NAS liegen.
- Das Host-Backup zeitlich **nach** der DB-Sicherung laufen lassen (z. B. 04:00). Dann sind alle Dateien, auf die der DB-Stand verweist, mitgesichert; später hochgeladene Dateien sind im Backup nur überzählig.
- Das Data-Protection-Zertifikat gehört nicht in dieses Backup (siehe [oben](#data-protection-zertifikat)).

Beispiel mit restic (Cron auf dem Host):

```bash
0 4 * * * restic -r sftp:backup@nas:/vernetzt backup /srv/vernetzt/uploads /srv/backup/vernetzt --quiet
```

### Wiederherstellen

1. Container stoppen: `docker compose stop app`
2. Im Daten-Volume `vernetzt.db` durch die gewünschte Sicherung ersetzen und `vernetzt.db-wal` sowie `vernetzt.db-shm` löschen:
   ```bash
   docker run --rm -v vernetzt-data:/data -v /srv/backup/vernetzt:/backups alpine sh -c \
     'cp /backups/vernetzt-20261105-030000-scheduled.db /data/vernetzt.db && rm -f /data/vernetzt.db-wal /data/vernetzt.db-shm'
   ```
3. Bei Bedarf `/app/uploads` aus dem Host-Backup zurückspielen
4. Container starten: `docker compose start app` (der Entrypoint korrigiert die Dateirechte)

Ist die Sicherung älter als die laufende Version, spielt die App fehlende Migrationen beim Start automatisch ein.
