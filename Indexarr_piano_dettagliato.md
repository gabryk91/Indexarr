# Indexarr – Piano dettagliato progetto

## 1. Obiettivo

Indexarr è una web app self-hosted per monitorare, correggere e automatizzare la gestione degli indexer configurati in Prowlarr.

L’obiettivo è superare lo script Bash e creare un servizio con:

- wizard iniziale di configurazione;
- dashboard web;
- health check automatico;
- backup e restore;
- riparazione automatica controllata;
- aggiunta automatica di nuovi indexer compatibili;
- audit delle definizioni;
- log e report leggibili;
- modalità dry-run/apply configurabile da UI.

## 2. Filosofia per l’homelab

Per l’ambiente Mimir/Unraid:

- Docker nativo Unraid;
- template Unraid preferito;
- configurazione, database SQLite e cache su NVMe;
- backup sul QNAP;
- nessuna modifica distruttiva senza backup;
- rollback sempre disponibile;
- integrazione con Prowlarr esistente;
- nessun nuovo componente inutile.

Container previsto:

- Indexarr

Mount consigliati:

- `/config` → `/mnt/cache/appdata/Indexarr`
- `/backups` → QNAP `Backup/Indexarr`
- `/logs` → opzionale su NVMe o dentro `/config/logs`

## 3. Stack tecnico consigliato

### Backend

- ASP.NET Core 9
- Minimal API oppure Controllers
- HostedService per job periodici
- Quartz.NET per scheduler avanzato
- SQLite tramite Entity Framework Core
- Serilog per logging
- SignalR per log realtime
- Swagger/OpenAPI per debug API

### Frontend

- Vue 3
- Vuetify
- Pinia per stato globale
- Axios/fetch per API
- Chart.js oppure ApexCharts per grafici
- Dark mode

### Database

SQLite locale in:

```text
/config/Indexarr.db
```

### Container

Docker singolo:

```text
Indexarr
```

Porte consigliate:

```text
9697:8080
```

## 4. Architettura generale

```text
Indexarr
│
├── Backend ASP.NET Core
│   ├── Prowlarr API Client
│   ├── Health Engine
│   ├── Remediation Engine
│   ├── Backup Engine
│   ├── Restore Engine
│   ├── Indexer Discovery Engine
│   ├── Scheduler
│   ├── Notification Service
│   └── SignalR Hub
│
├── Frontend Vue
│   ├── Wizard
│   ├── Dashboard
│   ├── Indexers
│   ├── Rules
│   ├── Backups
│   ├── Reports
│   └── Logs
│
└── SQLite
    ├── Settings
    ├── IndexerHealth
    ├── Actions
    ├── Backups
    ├── Jobs
    └── Audit
```

## 5. Wizard iniziale

Alla prima apertura l’app mostra un wizard.

### Step 1 – Connessione Prowlarr

Campi:

- URL Prowlarr
- API Key
- oppure lettura automatica da path Docker, se montato

Test:

- HTTP raggiungibile
- API key valida
- versione Prowlarr
- numero indexer configurati

### Step 2 – Ambiente

Test:

- DNS dal container
- HTTPS verso Internet
- accesso a Prowlarr
- eventuale FlareSolverr
- path backup scrivibile
- path config scrivibile

### Step 3 – Policy iniziale

Scelte:

- modalità predefinita: Dry Run o Apply
- backup prima di ogni modifica: sempre attivo
- disabilita indexer morti dopo N fallimenti
- tenta cambio mirror automatico
- aggiungi nuovi indexer automaticamente
- lingue preferite
- torrent/usenet
- pubblico/privato
- categorie abilitate

### Step 4 – Primo scan

Azioni:

- backup indexer Prowlarr
- test configurazione indexer
- test ricerca reale
- import definizioni
- report iniziale

## 6. Dashboard

La dashboard mostra:

- stato Prowlarr
- numero indexer totali
- indexer OK
- indexer warning
- indexer failed
- health score medio
- ultimo controllo
- prossima esecuzione
- ultime azioni automatiche
- stato backup

Esempio:

```text
Prowlarr: Online
Indexer: 11 / 11 OK
Health: 98%
Ultimo controllo: 5 minuti fa
Azioni automatiche oggi: 0
Backup: OK
```

## 7. Pagina Indexer

Tabella indexer:

- Nome
- ID Prowlarr
- Enabled
- Protocollo
- Implementazione
- Pubblico/privato
- Lingua
- Categorie
- Health score
- Ultimo successo
- Ultimo errore
- Latenza media
- Auto repair abilitato
- Auto disable abilitato

Azioni per singolo indexer:

- test configurazione
- test ricerca reale
- prova mirror
- cambia mirror
- disabilita
- abilita
- ripristina da backup
- escludi da automazioni

## 8. Health Engine

Il motore di health esegue due tipi di test.

### Test 1 – Configurazione

Usa endpoint Prowlarr:

```text
POST /api/v1/indexer/test
```

Serve per verificare se la configurazione è valida.

### Test 2 – Ricerca reale

Esegue una query configurabile, ad esempio:

```text
ubuntu
```

Serve per capire se l’indexer risponde davvero.

Metriche raccolte:

- risultato
- errore
- latenza
- codice HTTP se disponibile
- classe errore
- timestamp
- conteggio fallimenti consecutivi

Classi errore:

- DNS
- SSL
- Timeout
- HTTP 403
- HTTP 404
- HTTP 5xx
- Cloudflare
- Auth
- Captcha
- Rate limit
- Unknown

## 9. Health Score

Ogni indexer riceve un punteggio 0-100.

Esempio formula iniziale:

- base 100
- -40 se ultimo test fallito
- -20 per DNS error
- -20 per SSL error
- -15 per timeout
- -10 se latenza > 5s
- -5 per ogni fallimento consecutivo
- +10 se stabile da 7 giorni

Stati:

```text
90-100  Healthy
70-89   Good
40-69   Warning
1-39    Critical
0       Dead
```

## 10. Remediation Engine

Il motore di remediation interviene solo dopo backup.

Pipeline:

```text
Indexer FAIL
│
├── Classifica errore
│
├── Se DNS/404/Timeout:
│     ├── cerca mirror alternativi
│     ├── testa mirror
│     ├── seleziona migliore
│     └── aggiorna baseUrl
│
├── Se Cloudflare:
│     ├── verifica FlareSolverr
│     └── suggerisce/abilita proxy se configurabile
│
├── Se Auth/Captcha:
│     └── non modifica automaticamente
│
└── Se fallisce ancora:
      ├── rollback
      └── opzionalmente disabilita dopo soglia
```

Regole configurabili:

- soglia fallimenti prima di intervenire
- soglia fallimenti prima di disabilitare
- dry-run/apply globale
- dry-run/apply per singolo indexer
- esclusione indexer specifici
- rollback automatico se test post-modifica fallisce

## 11. Backup e Restore

Prima di ogni modifica viene creato un backup.

Struttura:

```text
/backups/
└── backup-20260705-153000/
    ├── manifest.json
    ├── prowlarr-indexers.json
    ├── Indexarr-settings.json
    ├── state.json
    └── report-before.json
```

Manifest:

```json
{
  "version": "0.1.0",
  "created": "2026-07-05T15:30:00Z",
  "prowlarrVersion": "2.4.0.5397",
  "indexerCount": 11,
  "reason": "before-auto-repair"
}
```

Restore:

- restore completo indexer
- restore singolo indexer
- preview differenze prima del restore
- log operazione
- test dopo restore

## 12. Aggiunta automatica nuovi indexer

Funzione configurabile da UI.

Filtri:

- protocollo:
  - torrent
  - usenet
  - entrambi
- privacy:
  - public
  - private
  - entrambi
- lingua:
  - Italian
  - English
  - Japanese
  - multi
- categorie:
  - Movies
  - TV
  - Anime
  - Music
  - Books
  - Games
  - Software
- tipo implementazione:
  - Cardigann
  - Torznab
  - Newznab
  - custom
- richiede login:
  - sì/no
- richiede API key:
  - sì/no
- richiede cookie:
  - sì/no
- richiede captcha:
  - sì/no

Regola prudente:

L’app aggiunge automaticamente solo indexer che:

- non sono già configurati;
- sono compatibili con i filtri;
- non richiedono credenziali;
- non richiedono cookie/API key;
- hanno baseUrl testabile;
- passano test configurazione e ricerca reale.

Se falliscono:

- rollback immediato;
- segnati nello stato come “tentato e fallito”;
- non riprovati per N giorni.

## 13. Audit definizioni

L’app monitora le definizioni Prowlarr.

Rileva:

- nuovi indexer disponibili;
- indexer rimossi;
- definizioni aggiornate;
- mirror aggiunti;
- mirror rimossi;
- cambi categoria/lingua;
- cambi privacy.

Report esempio:

```text
Audit 2026-07-05

Nuovi indexer:
+ ExampleTorrent

Mirror aggiunti:
+ 1337x.st

Mirror rimossi:
- oldmirror.example

Definizioni aggiornate:
- EZTV
- The Pirate Bay
```

## 14. Scheduler

Job configurabili:

```text
Ogni 6 ore:
- Health Check

Ogni notte:
- Audit definitions
- Backup stato

Ogni domenica:
- Cleanup backup vecchi
- Report settimanale

Dopo ogni modifica:
- Backup
- Test
- Report
```

## 15. Notifiche

Canali previsti:

- UI
- log
- webhook generico
- Telegram
- notifiche Unraid tramite script opzionale
- email in futuro

Eventi notificabili:

- Prowlarr offline
- indexer critico
- mirror cambiato
- indexer disabilitato
- nuovo indexer aggiunto
- backup creato
- restore completato
- errore rollback

## 16. Sicurezza

- API key cifrata nel database o protetta tramite permessi file
- nessuna esposizione pubblica consigliata
- reverse proxy opzionale solo con autenticazione
- backup prima di ogni modifica
- audit trail immutabile
- modalità dry-run predefinita alla prima installazione
- nessuna cancellazione automatica degli indexer

## 17. API interne

Esempi endpoint:

```text
GET  /api/status
GET  /api/indexers
POST /api/indexers/{id}/test
POST /api/indexers/{id}/repair
POST /api/indexers/{id}/disable
GET  /api/backups
POST /api/backups
POST /api/backups/{id}/restore
GET  /api/audit
POST /api/discovery/run
GET  /api/settings
PUT  /api/settings
GET  /api/logs/stream
```

## 18. Database iniziale

Tabelle:

```text
Settings
ProwlarrConnections
Indexers
IndexerHealthChecks
IndexerActions
Backups
AuditSnapshots
AuditChanges
DiscoveryCandidates
Jobs
Notifications
```

## 19. MVP

La prima versione utile dovrebbe includere:

- wizard Prowlarr
- dashboard
- lista indexer
- test indexer
- storico health
- backup/restore
- dry-run/apply
- scheduler
- log realtime

## 20. Versioni

### v0.1 MVP tecnico

- backend ASP.NET Core
- frontend minimale
- connessione Prowlarr
- SQLite
- test indexer
- report

### v0.2 Backup/Restore

- backup indexer
- restore completo
- restore singolo
- manifest

### v0.3 Health Score

- storico
- grafici
- classificazione errori
- soglie

### v0.4 Remediation

- cambio mirror
- test mirror
- rollback

### v0.5 Discovery

- lettura definizioni
- filtri
- candidati nuovi indexer
- aggiunta automatica prudente

### v1.0 Stable

- UI completa
- scheduler stabile
- notifiche
- backup cleanup
- template Unraid
- documentazione

## 21. Template Unraid

Variabili:

```text
PUID=99
PGID=100
TZ=Europe/Rome
PROWLARR_URL=http://192.168.1.36:9696
```

Mount:

```text
/config -> /mnt/cache/appdata/Indexarr
/backups -> /mnt/remotes/192.168.1.101_Public/Backup/Indexarr
```

Porta:

```text
9697:8080
```

## 22. Nome progetto

Nomi possibili:

- Indexarr
- IndexerHub
- Prowlarr Guardian
- Arr Indexer Manager

Nome consigliato:

```text
Indexarr
```

Perché in futuro potrebbe supportare anche Jackett o NZBHydra2, non solo Prowlarr.

## 23. Decisione consigliata

Procedere con Indexarr come progetto separato, non come script.

Prima milestone:

- creare struttura progetto;
- implementare backend Prowlarr client;
- implementare wizard;
- salvare configurazione SQLite;
- mostrare lista indexer;
- fare test indexer;
- creare primo backup.
