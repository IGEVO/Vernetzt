# Vernetzt: Grafana-Dashboards und Alerts (OTel + LGTM)

Dashboards und Alert-Regeln für den Betrieb von Vernetzt mit `grafana/otel-lgtm`. Die Daten kommen per OpenTelemetry (OTLP) aus der App:

- **Logs** über `ILogger` nach Loki. Strukturierte Felder wie `scope_name`, `detected_level`, `trace_id` und die Platzhalter der Log-Templates landen als Structured Metadata.
- **Metriken** nach Prometheus:
  - ASP.NET Core, HttpClient und .NET-Runtime
  - Rate Limiting
  - Fachmetriken aus `Igevo.Vernetzt.Infrastructure/Observability/VernetztMetrics.cs`
- **Traces** nach Tempo. Aus einer Log-Zeile führt der `trace_id`-Link direkt zum Trace.

Der Export ist nur aktiv, wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist (siehe `Igevo.Vernetzt.ServiceDefaults/Extensions.cs`). Ohne diese Variable gehen Logs nur auf die Konsole (`docker logs`). Mit `Observability__AdditionalOtlpEndpoint` (OTLP/gRPC) geht zusätzlich eine Kopie an einen zweiten Empfänger.

## Inhalt

| Datei | Zweck |
|-------|-------|
| `dashboards/vernetzt-overview.json` | **Vernetzt - Betrieb**: Anfragen, 5xx-Quote, Latenz, Rate Limiting, Alter des letzten Backups, Version, .NET-Laufzeit (Speicher, CPU, ThreadPool, GC, Exceptions), Fehler-Logs |
| `dashboards/vernetzt-auth-security.json` | **Vernetzt - Anmeldung & Sicherheit**: Login-Links (ausgestellt/abgelehnt nach Grund), Passkey-Fehler, Rate Limiting der anonymen Auth-Endpunkte, sicherheitsrelevante Warnungen |
| `dashboards/vernetzt-logs.json` | **Vernetzt - Logs**: Volumen nach Level, häufigste Fehler/Warnungen gruppiert nach Log-Template, Exceptions nach Typ, Log-Ansicht mit Filtern für Service, Level, Komponente und Text. Nutzt nur Loki |
| `dashboards/vernetzt-email-relay-ops.json` | **Vernetzt - E-Mail, Relay & Jobs**: SMTP-Versand und -Fehler, Retries/Circuit Breaker, E-Mail-Relay, Backups, Cleanup |
| `provisioning/dashboards/vernetzt.yaml` | Dashboard-Provider: lädt die JSON-Dateien beim Start in den Ordner „Vernetzt“ |
| `provisioning/alerting/vernetzt-alerts.yaml` | Alert-Regeln (siehe unten) |

## Einrichtung

**Produktion:** Das Compose-Beispiel „Setup B“ im [Haupt-README](../../README.md) bindet die Dateien per Volume ein. Kopiert den Ordner `observability/` neben die `docker-compose.yml`. Grafana lädt Dashboards und Alerts beim Start selbst, ein manueller Import ist nicht nötig.

**Aspire:** Der AppHost startet `otel-lgtm` als Container `lgtm` mit den Dashboards. Grafana läuft auf http://localhost:56300, die Daten bleiben im Volume `vernetzt-lgtm-dev-data`. Der Server schickt seine Telemetrie weiter an das Aspire-Dashboard und zusätzlich an LGTM (`Observability__AdditionalOtlpEndpoint`). Der Service heißt dort `server`, wie die Aspire-Ressource. Die Alert-Regeln filtern auf `vernetzt` und werden deshalb unter Aspire nicht geladen. Abschalten: `Observability:Lgtm:Enabled=false` in den AppHost-Einstellungen.

**Lokal ohne Aspire (mit Alerts):** `docker compose --profile observability up -d` startet `otel-lgtm` mit denselben Dateien. Grafana läuft dann auf http://localhost:3000 (lokal ohne Login). Die App startet ihr mit:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 OTEL_SERVICE_NAME=vernetzt dotnet run --project Igevo.Vernetzt.Server
```

**Anderes Grafana:**
- Dashboards: über `Dashboards → New → Import` hochladen und die Variablen `Metriken`/`Logs` auf eure Prometheus- bzw. Loki-Quelle setzen.
- Alert-Regeln: Sie erwarten eine Prometheus-Datenquelle mit der UID `prometheus`. Sonst in `vernetzt-alerts.yaml` `datasourceUid` anpassen.

### Benachrichtigungen

Die Alert-Regeln haben die Labels `severity=critical|warning` und `service=vernetzt`. Benachrichtigt wird erst, wenn ein Kontaktpunkt existiert:

1. In Grafana unter `Alerting → Contact points` einen Kontaktpunkt anlegen, z. B. E-Mail (braucht die `GF_SMTP_*`-Einstellungen), Webhook oder Matrix/Slack.
2. Unter `Alerting → Notification policies` die Default-Policy auf diesen Kontaktpunkt setzen, oder eine Policy für `severity=critical` anlegen.

Zugangsdaten für Kontaktpunkte gehören nicht ins Repository. Wer sie provisionieren will, legt eine eigene Datei neben `vernetzt-alerts.yaml` und mountet sie mit.

## Alert-Regeln

| Regel | Bedingung | Schwere |
|-------|-----------|---------|
| Hohe Serverfehlerquote | > 5 % 5xx und mindestens 5 Fehler in 5 min, 5 min lang | critical |
| API langsam | p95 > 2 s, 10 min lang | warning |
| Datenbank-Backup veraltet | neuestes Backup älter als 26 h (nur bei aktivierten geplanten Backups) | critical |
| Datenbank-Backup fehlgeschlagen | ein fehlgeschlagener Lauf in der letzten Stunde | critical |
| E-Mail-Versand schlägt fehl | mindestens 3 endgültig fehlgeschlagene Mails in 15 min | critical |
| SMTP-Circuit-Breaker offen | Breaker in den letzten 15 min geöffnet | warning |
| Keine Telemetrie | 10 min keine Metriken von `service_name="vernetzt"` | critical |

Die Regeln filtern auf `service_name="vernetzt"`, also muss `OTEL_SERVICE_NAME=vernetzt` gesetzt sein.

## Fachmetriken (Meter `Igevo.Vernetzt`)

| Prometheus-Name | Labels | Bedeutung |
|-----------------|--------|-----------|
| `vernetzt_auth_login_link_issued_total` | – | ausgestellte Login-Links |
| `vernetzt_auth_login_link_rejected_total` | `reason`: `unknown_email`, `budget_exhausted`, `invalid_or_expired`, `already_used` | abgelehnte Anforderungen/Einlösungen |
| `vernetzt_auth_passkey_failures_total` | `operation`: `registration`, `signin` | fehlgeschlagene Passkey-Vorgänge |
| `vernetzt_email_sends_total` | `outcome`: `success`, `failure` | SMTP-Versand nach allen Retries |
| `vernetzt_email_circuit_breaker_opened_total` | – | SMTP-Circuit-Breaker geöffnet |
| `vernetzt_backup_runs_total` | `trigger`: `scheduled`, `manual`, `premigration`; `outcome` | Backup-Läufe |
| `vernetzt_backup_last_success_timestamp_seconds` | – | Unix-Zeit des neuesten Backups (nur bei aktivierten geplanten Backups) |
| `vernetzt_backup_last_failure_timestamp_seconds` | – | Unix-Zeit des letzten fehlgeschlagenen Backups seit dem Start |
| `vernetzt_relay_emails_total` | `outcome`: `forwarded`, `forward_failed`, `discarded_token_expired`, `discarded_no_token`, `rejected_too_large`, `error` | eingehende Relay-Mails |

Die Labels enthalten nur feste Werte, nie Nutzerdaten. Neue Fachmetriken gehören in `VernetztMetrics` und brauchen einen Test mit `MetricsRecorder` (siehe `Igevo.Vernetzt.Tests`).

## Weitere verwendete Metriken und Labels

- HTTP: `http_server_request_duration_seconds_*` mit `http_route` und `http_response_status_code`. `/health` ist ausgenommen, damit der Docker-Healthcheck die Zahlen nicht verfälscht.
- Rate Limiting: `aspnetcore_rate_limiting_requests_total` mit `aspnetcore_rate_limiting_policy` und `aspnetcore_rate_limiting_result`. Alles außer `acquired` wurde mit 429 abgewiesen.
- .NET: `dotnet_process_memory_working_set_bytes`, `dotnet_gc_*`, `dotnet_thread_pool_*`, `dotnet_exceptions_total` (`error_type`).
- Resource-Labels (von `otel-lgtm` übernommen): `service_name`, `service_version`, `deployment_environment`.

## Wenn ein Panel leer bleibt

- Viele Panels zeigen nur Ereignisse, z. B. 5xx-Fehler, Relay-Mails oder Passkey-Fehler. Leer heißt dann: Das Ereignis ist im Zeitraum nicht vorgekommen.
- In `Explore` prüfen, ob `target_info{service_name="vernetzt"}` existiert. Fehlt es, kommen keine Daten an: `OTEL_EXPORTER_OTLP_ENDPOINT` und Netzwerk prüfen.
- Ein anderes Prometheus ohne `promote_resource_attributes` liefert `service_name` eventuell nur als `job`. Dann `service_name` in den Queries durch `job` ersetzen.
