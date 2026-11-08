# Betrieb und Deployment

> Vor dem Go-live unbedingt zusätzlich [HOSTING.md](HOSTING.md) lesen: Reverse Proxy, Secrets, Data-Protection-Zertifikat, Container-Limits und Datensicherung.

## Docker-Images

Veröffentlichte Images liegen in der GitHub Container Registry:

| Image | Inhalt |
|---|---|
| `ghcr.io/igevo/vernetzt` | Server mit Verwaltungsoberfläche und Hilfe |
| `ghcr.io/igevo/vernetzt-public` | Öffentliches Portal (optional) |

Tags: `<version>` (z. B. `0.4.0`), `<major>.<minor>`, `latest` (neueste stabile Version) und `edge` (aktueller Stand von `main`, nicht für den Produktivbetrieb). Für den Betrieb immer eine konkrete Version eintragen und Updates bewusst einspielen.

Lokal bauen:

```bash
docker build -t ghcr.io/igevo/vernetzt:dev .
```

---

## Variante A: Einfacher Produktivbetrieb

Nur der App-Container (die Datenbank ist eine SQLite-Datei in einem Volume), ohne Observability-Stack. E-Mails gehen über einen externen SMTP-Server.

`.env`-Datei anlegen:
```env
BASE_URL=https://your-domain.com
# PUBLIC_WEB_URL=https://portal.your-domain.com
SYSTEM_ADMIN_EMAIL=admin@your-domain.com
SMTP_HOST=smtp.your-provider.com
SMTP_PORT=587
SMTP_USERNAME=your-smtp-user
SMTP_PASSWORD=your-smtp-password
SMTP_FROM_ADDRESS=noreply@your-domain.com
HEALTH_TOKEN=your-secure-random-token
DATAPROTECTION_CERT_PASSWORD=password-of-dataprotection.pfx
# Passkey (WebAuthn): eTLD+1 von BASE_URL (nur Domain, ohne Schema und Pfad)
PASSKEY_RP_ID=your-domain.com
# Reverse Proxy: Adresse bzw. Netz des Proxys aus Sicht des App-Containers (siehe docs/HOSTING.md)
REVERSE_PROXY_ENABLED=true
REVERSE_PROXY_NETWORK=172.18.0.0/16
# E-Mail-Relay (optional, mit EMAIL_RELAY_ENABLED=true aktivieren)
EMAIL_RELAY_ENABLED=false
EMAIL_RELAY_BASE_ADDRESS=relay@your-domain.com
# Absender der weitergeleiteten Mails. Muss eine Adresse sein, die das SMTP-Konto senden darf.
# Leer: EMAIL_RELAY_BASE_ADDRESS (falls gültige Adresse), sonst SMTP_FROM_ADDRESS
EMAIL_RELAY_FORWARDING_FROM_ADDRESS=
EMAIL_RELAY_ADDRESSING_MODE=PlusAddressing
EMAIL_RELAY_MONITOR_MODE=Imap
EMAIL_RELAY_IMAP_HOST=imap.your-provider.com
EMAIL_RELAY_IMAP_PORT=993
EMAIL_RELAY_IMAP_USERNAME=your-imap-user
EMAIL_RELAY_IMAP_PASSWORD=your-imap-password
```

Als `docker-compose.yml` speichern:
```yaml
# Einfacher Produktivbetrieb: nur die App (SQLite-Datenbank im Volume), ohne Observability-Stack.
# Für den E-Mail-Versand ist ein externer SMTP-Server nötig.

services:
  app:
    image: ghcr.io/igevo/vernetzt:0.4.0   # konkrete Release-Version eintragen
    container_name: vernetzt-app
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"   # nur über den Reverse Proxy auf diesem Host erreichbar
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      # UID/GID, unter der die App läuft (kein root, siehe docs/HOSTING.md)
      - PUID=${PUID:-1654}
      - PGID=${PGID:-1654}
      - App__SystemAdminEmail=${SYSTEM_ADMIN_EMAIL}
      - App__BaseUrl=${BASE_URL}
      # Optional: URL des öffentlichen Portals, aktiviert die Links „Öffentliche Seite“ in der Verwaltung
      - App__PublicWebUrl=${PUBLIC_WEB_URL:-}
      - Email__Smtp__Host=${SMTP_HOST}
      - Email__Smtp__Port=${SMTP_PORT}
      - Email__Smtp__UseSsl=true
      - Email__Smtp__Username=${SMTP_USERNAME}
      - Email__Smtp__Password=${SMTP_PASSWORD}
      - Email__From__Address=${SMTP_FROM_ADDRESS}
      - Email__From__Name=Vernetzt
      - Health__Token=${HEALTH_TOKEN}
      # Datenbank-Backups (optional, Standardwerte): täglich 03:00 Uhr Europe/Berlin nach /app/backups
      # - Backup__Time=03:00
      # - Backup__KeepDaily=14
      # - Backup__KeepWeekly=8
      # Verschlüsselt die Data-Protection-Schlüssel (im Produktivbetrieb Pflicht, siehe docs/HOSTING.md)
      - DataProtection__CertificatePath=/run/secrets/dataprotection.pfx
      - DataProtection__CertificatePassword=${DATAPROTECTION_CERT_PASSWORD}
      # Passkey (WebAuthn)
      - Auth__Passkeys__RelyingPartyId=${PASSKEY_RP_ID}
      - Auth__Passkeys__RelyingPartyName=Vernetzt
      - Auth__Passkeys__AllowedOrigins__0=${BASE_URL}
      - Auth__Passkeys__ChallengeTimeoutSeconds=300
      # Reverse Proxy: muss der echten Absenderadresse des Proxys vertrauen, siehe docs/HOSTING.md
      - ReverseProxy__Enabled=${REVERSE_PROXY_ENABLED:-true}
      - ReverseProxy__KnownProxies__0=127.0.0.1
      - ReverseProxy__KnownNetworks__0=${REVERSE_PROXY_NETWORK}
      - ReverseProxy__ForwardLimit=1
      # E-Mail-Relay (optional, mit EMAIL_RELAY_ENABLED=true aktivieren)
      - EmailRelay__Enabled=${EMAIL_RELAY_ENABLED:-false}
      - EmailRelay__BaseAddress=${EMAIL_RELAY_BASE_ADDRESS}
      - EmailRelay__ForwardingFromAddress=${EMAIL_RELAY_FORWARDING_FROM_ADDRESS:-}
      - EmailRelay__AddressingMode=${EMAIL_RELAY_ADDRESSING_MODE:-PlusAddressing}
      - EmailRelay__MonitorMode=${EMAIL_RELAY_MONITOR_MODE:-Imap}
      - EmailRelay__Imap__Host=${EMAIL_RELAY_IMAP_HOST}
      - EmailRelay__Imap__Port=${EMAIL_RELAY_IMAP_PORT:-993}
      - EmailRelay__Imap__UseSsl=true
      - EmailRelay__Imap__Username=${EMAIL_RELAY_IMAP_USERNAME}
      - EmailRelay__Imap__Password=${EMAIL_RELAY_IMAP_PASSWORD}
    volumes:
      - vernetzt-data:/app/data          # SQLite-Datenbank, nur lokale Platte (kein NFS/SMB)
      - vernetzt-uploads:/app/uploads    # hochgeladene Dateien
      - /srv/backup/vernetzt:/app/backups  # Datenbank-Backups, idealerweise andere Platte oder NAS
      - ./secrets/dataprotection.pfx:/run/secrets/dataprotection.pfx:ro   # muss für PUID/PGID lesbar sein
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

volumes:
  vernetzt-data:
    name: vernetzt-data
  vernetzt-uploads:
    name: vernetzt-uploads
```

Starten:

```bash
docker compose up -d
```

---

## Variante B: Mit Grafana-LGTM-Stack (Observability)

App plus kompletter Observability-Stack über [`grafana/otel-lgtm`](https://github.com/grafana/docker-otel-lgtm): ein All-in-one-Container mit Grafana, Loki (Logs), Tempo (Traces) und Prometheus/Mimir (Metriken).

**Funktionsweise:** Der App-Container schickt Traces, Metriken und Logs per OTLP/gRPC direkt an den LGTM-Container. `ServiceDefaults` schaltet den OTLP-Exporter automatisch ein, sobald `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist, genau wie Aspire in der Entwicklung.

`.env`-Datei anlegen:
```env
BASE_URL=https://your-domain.com
# PUBLIC_WEB_URL=https://portal.your-domain.com
SYSTEM_ADMIN_EMAIL=admin@your-domain.com
SMTP_HOST=smtp.your-provider.com
SMTP_PORT=587
SMTP_USERNAME=your-smtp-user
SMTP_PASSWORD=your-smtp-password
SMTP_FROM_ADDRESS=noreply@your-domain.com
HEALTH_TOKEN=your-secure-random-token
DATAPROTECTION_CERT_PASSWORD=password-of-dataprotection.pfx
GRAFANA_PASSWORD=your-secure-grafana-password
# Passkey (WebAuthn): eTLD+1 von BASE_URL (nur Domain, ohne Schema und Pfad)
PASSKEY_RP_ID=your-domain.com
# Reverse Proxy: Adresse bzw. Netz des Proxys aus Sicht des App-Containers (siehe docs/HOSTING.md)
REVERSE_PROXY_ENABLED=true
REVERSE_PROXY_NETWORK=172.18.0.0/16
# E-Mail-Relay (optional, mit EMAIL_RELAY_ENABLED=true aktivieren)
EMAIL_RELAY_ENABLED=false
EMAIL_RELAY_BASE_ADDRESS=relay@your-domain.com
# Absender der weitergeleiteten Mails. Muss eine Adresse sein, die das SMTP-Konto senden darf.
# Leer: EMAIL_RELAY_BASE_ADDRESS (falls gültige Adresse), sonst SMTP_FROM_ADDRESS
EMAIL_RELAY_FORWARDING_FROM_ADDRESS=
EMAIL_RELAY_ADDRESSING_MODE=PlusAddressing
EMAIL_RELAY_MONITOR_MODE=Imap
EMAIL_RELAY_IMAP_HOST=imap.your-provider.com
EMAIL_RELAY_IMAP_PORT=993
EMAIL_RELAY_IMAP_USERNAME=your-imap-user
EMAIL_RELAY_IMAP_PASSWORD=your-imap-password
```

Als `docker-compose.yml` speichern:
```yaml
# Produktivbetrieb mit Grafana-LGTM-Stack (Observability).
# Enthält: App (SQLite-Datenbank im Volume), Grafana (UI), Loki (Logs),
#          Tempo (Traces), Prometheus/Mimir (Metriken).
#
# Grafana-UI: http://localhost:3000 nur auf dem Host (admin / ${GRAFANA_PASSWORD});
#             über den Reverse Proxy oder einen SSH-Tunnel freigeben, nie direkt.
# Dashboards und Alert-Regeln kommen aus ./observability (den Ordner aus dem
# Repository neben diese Datei kopieren).

services:
  app:
    image: ghcr.io/igevo/vernetzt:0.4.0   # konkrete Release-Version eintragen
    container_name: vernetzt-app
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"   # nur über den Reverse Proxy auf diesem Host erreichbar
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      # UID/GID, unter der die App läuft (kein root, siehe docs/HOSTING.md)
      - PUID=${PUID:-1654}
      - PGID=${PGID:-1654}
      - App__SystemAdminEmail=${SYSTEM_ADMIN_EMAIL}
      - App__BaseUrl=${BASE_URL}
      # Optional: URL des öffentlichen Portals, aktiviert die Links „Öffentliche Seite“ in der Verwaltung
      - App__PublicWebUrl=${PUBLIC_WEB_URL:-}
      - Email__Smtp__Host=${SMTP_HOST}
      - Email__Smtp__Port=${SMTP_PORT}
      - Email__Smtp__UseSsl=true
      - Email__Smtp__Username=${SMTP_USERNAME}
      - Email__Smtp__Password=${SMTP_PASSWORD}
      - Email__From__Address=${SMTP_FROM_ADDRESS}
      - Email__From__Name=Vernetzt
      - Health__Token=${HEALTH_TOKEN}
      # Datenbank-Backups (optional, Standardwerte): täglich 03:00 Uhr Europe/Berlin nach /app/backups
      # - Backup__Time=03:00
      # - Backup__KeepDaily=14
      # - Backup__KeepWeekly=8
      # Verschlüsselt die Data-Protection-Schlüssel (im Produktivbetrieb Pflicht, siehe docs/HOSTING.md)
      - DataProtection__CertificatePath=/run/secrets/dataprotection.pfx
      - DataProtection__CertificatePassword=${DATAPROTECTION_CERT_PASSWORD}
      # Passkey (WebAuthn)
      - Auth__Passkeys__RelyingPartyId=${PASSKEY_RP_ID}
      - Auth__Passkeys__RelyingPartyName=Vernetzt
      - Auth__Passkeys__AllowedOrigins__0=${BASE_URL}
      - Auth__Passkeys__ChallengeTimeoutSeconds=300
      # Reverse Proxy: muss der echten Absenderadresse des Proxys vertrauen, siehe docs/HOSTING.md
      - ReverseProxy__Enabled=${REVERSE_PROXY_ENABLED:-true}
      - ReverseProxy__KnownProxies__0=127.0.0.1
      - ReverseProxy__KnownNetworks__0=${REVERSE_PROXY_NETWORK}
      - ReverseProxy__ForwardLimit=1
      # E-Mail-Relay (optional, mit EMAIL_RELAY_ENABLED=true aktivieren)
      - EmailRelay__Enabled=${EMAIL_RELAY_ENABLED:-false}
      - EmailRelay__BaseAddress=${EMAIL_RELAY_BASE_ADDRESS}
      - EmailRelay__ForwardingFromAddress=${EMAIL_RELAY_FORWARDING_FROM_ADDRESS:-}
      - EmailRelay__AddressingMode=${EMAIL_RELAY_ADDRESSING_MODE:-PlusAddressing}
      - EmailRelay__MonitorMode=${EMAIL_RELAY_MONITOR_MODE:-Imap}
      - EmailRelay__Imap__Host=${EMAIL_RELAY_IMAP_HOST}
      - EmailRelay__Imap__Port=${EMAIL_RELAY_IMAP_PORT:-993}
      - EmailRelay__Imap__UseSsl=true
      - EmailRelay__Imap__Username=${EMAIL_RELAY_IMAP_USERNAME}
      - EmailRelay__Imap__Password=${EMAIL_RELAY_IMAP_PASSWORD}
      # OpenTelemetry → Grafana LGTM (Traces, Metriken, Logs per OTLP/gRPC)
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://lgtm:4317
      - OTEL_EXPORTER_OTLP_PROTOCOL=grpc
      - OTEL_SERVICE_NAME=vernetzt   # Dashboards und Alerts filtern auf diesen Namen
      - OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production
    volumes:
      - vernetzt-data:/app/data          # SQLite-Datenbank, nur lokale Platte (kein NFS/SMB)
      - vernetzt-uploads:/app/uploads    # hochgeladene Dateien
      - /srv/backup/vernetzt:/app/backups  # Datenbank-Backups, idealerweise andere Platte oder NAS
      - ./secrets/dataprotection.pfx:/run/secrets/dataprotection.pfx:ro   # muss für PUID/PGID lesbar sein
    depends_on:
      lgtm:
        condition: service_started
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

  lgtm:
    image: grafana/otel-lgtm:0.40.0   # Version festlegen, bewusst aktualisieren
    container_name: vernetzt-lgtm
    restart: unless-stopped
    ports:
      - "127.0.0.1:3000:3000"   # Grafana-UI, nur auf dem Host
      # OTLP (4317/4318) wird nicht veröffentlicht: Die App erreicht es im Compose-Netz als lgtm:4317
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:?set GRAFANA_PASSWORD}
      - GF_USERS_ALLOW_SIGN_UP=false
      # otel-lgtm erlaubt standardmäßig anonymen ADMIN-Zugriff, im Produktivbetrieb unbedingt abschalten
      - GF_AUTH_ANONYMOUS_ENABLED=false
    volumes:
      - vernetzt-lgtm:/data   # Daten von Grafana, Loki, Tempo und Prometheus
      - ./observability/grafana/dashboards:/var/lib/vernetzt-dashboards:ro
      - ./observability/grafana/provisioning/dashboards/vernetzt.yaml:/otel-lgtm/grafana/conf/provisioning/dashboards/vernetzt.yaml:ro
      - ./observability/grafana/provisioning/alerting:/otel-lgtm/grafana/conf/provisioning/alerting:ro

volumes:
  vernetzt-data:
    name: vernetzt-data
  vernetzt-uploads:
    name: vernetzt-uploads
  vernetzt-lgtm:
    name: vernetzt-lgtm
```

Starten:

```bash
docker compose up -d
```

> **Hinweis:** `grafana/otel-lgtm` ist ein All-in-one-Container für kleinere Installationen, vergleichbar mit dem Aspire-Dashboard in der Entwicklung. Für größere Installationen die Komponenten (Loki, Tempo, Prometheus, Grafana) einzeln betreiben.

### Dashboards und Alerts

Mit den Mounts oben richtet Grafana beim Start alles ein (Ordner **Vernetzt**):

- **Vernetzt - Betrieb**: Anfragerate, 5xx-Anteil, Latenz, Rate Limiting, Alter des letzten Backups, Version, .NET-Laufzeit, Fehler-Logs
- **Vernetzt - Anmeldung & Sicherheit**: Login-Links (ausgestellt bzw. abgelehnt nach Grund), Passkey-Fehler, Rate Limiting bei der Anmeldung, Sicherheitswarnungen
- **Vernetzt - E-Mail, Relay & Jobs**: SMTP-Versand und -Fehler, Wiederholungen und Circuit Breaker, E-Mail-Relay, Backups, Bereinigungsjobs
- **Vernetzt - Logs**: Log-Menge nach Level, häufigste Fehler und Warnungen (gruppiert nach Message-Template), Exceptions nach Typ, durchsuchbare Log-Ansicht
- **Alert-Regeln**: 5xx-Anteil, p95-Latenz, Backup veraltet oder fehlgeschlagen, E-Mail-Versand schlägt fehl, SMTP-Circuit-Breaker offen, keine Telemetrie

Alerts benachrichtigen erst, wenn ein Contact Point eingerichtet ist (Grafana → Alerting → Contact points, z. B. E-Mail oder Webhook). Details, Metriknamen und die Nutzung der Dateien mit einem anderen Grafana: [observability/grafana/README.md](../observability/grafana/README.md).

---

## Persistente Volumes

| Mount | Inhalt | Anforderung | Sicherung |
|-------|----------|--------------|--------|
| `/app/data` (`vernetzt-data`) | SQLite-Datenbank (`vernetzt.db` und WAL-Dateien) | **lokale Platte** (kein NFS/SMB) | **nicht** im laufenden Betrieb kopieren, stattdessen die Dateien in `/app/backups` sichern |
| `/app/uploads` (`vernetzt-uploads`) | Hochgeladene Dateien (Avatare, Termin-Medien) | darf ein NAS sein | Backup des Hosts (z. B. restic/borg) |
| `/app/backups` | Von Vernetzt erstellte Datenbank-Backups | idealerweise andere Platte oder NAS | Backup des Hosts an einen externen Ort |
| `vernetzt-lgtm` | Grafana-Einstellungen, Logs, Metriken, Traces | – | optional (nur Variante B) |

**Wichtig:** Vernetzt erstellt nur die Datenbank-Backups. `/app/uploads` und `/app/backups` mit dem Backup-Werkzeug des Hosts an einen externen Ort kopieren, zeitlich nach dem täglichen Datenbank-Backup (siehe [HOSTING.md](HOSTING.md#datensicherung)).

