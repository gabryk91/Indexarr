# Indexarr

![Indexarr logo](src/Indexarr.Web/wwwroot/images/indexarr_logo.png)

`📡 Self-hosted dashboard to monitor, protect, and automate Prowlarr indexers.`

`🇮🇹 Italiano | 🇬🇧 English`

## `🇮🇹 Italiano`

### `✨ Perche' esiste`

Indexarr nasce per dare una vista chiara sugli indexer configurati in Prowlarr e per automatizzare le azioni piu' noiose: controlli di salute, backup, blocco, disattivazione e aggiunta guidata di nuovi indexer.

E' anche un progetto costruito in **vibe coding**: iterazione rapida, feedback continuo, funzionalita' concrete prima della perfezione teorica. L'obiettivo e' avere uno strumento utile davvero, da usare e migliorare mentre evolve.

### `🧭 Cosa fa`

- `📊 Dashboard` con stato indexer, filtri e storico audit
- `🩺 Health check` manuali e schedulati
- `🔒 Protezioni` con blocco o disattivazione degli indexer problematici
- `💾 Backup` automatici o manuali prima delle modifiche
- `➕ Auto-add` di indexer con filtri e regole predefinite
- `⚙️ Setup web` iniziale con test connessione a Prowlarr
- `🔐 Accesso protetto` via autenticazione cookie
- `🌍 UI bilingue` italiano e inglese

### `🧱 Stack`

- `.NET 9`
- `ASP.NET Core Razor Pages`
- `Entity Framework Core`
- `SQLite`
- `Docker`
- `Unraid-ready`

### `🚀 Avvio rapido`

#### `Docker`

```bash
docker build -t indexarr .

docker run -d \
  --name indexarr \
  -p 9697:8080 \
  -e TZ=Europe/Rome \
  -e Indexarr__Prowlarr__Url=http://prowlarr:9696 \
  -e Indexarr__Prowlarr__ApiKey=YOUR_API_KEY \
  -v /path/to/indexarr/config:/config \
  -v /path/to/indexarr/backups:/backups \
  -v /path/to/indexarr/logs:/logs \
  indexarr
```

Poi apri `http://localhost:9697`.

#### `Aggiornare Indexarr`

Per aggiornare un'installazione Docker:

```bash
docker pull gabryk83/indexarr:latest
docker stop indexarr
docker rm indexarr
```

Poi ricrea il container usando il comando `docker run` del paragrafo precedente. Le configurazioni e i dati restano disponibili nelle cartelle montate (`/config`, `/backups`, `/logs`). Su Unraid usa `Check for Updates` e poi `Update` nel container.

#### `Unraid`

E' disponibile un template pronto per Unraid in [unraid/Indexarr.xml](unraid/Indexarr.xml).

Usa questo URL come template:

```text
https://raw.githubusercontent.com/gabryk91/Indexarr/main/unraid/Indexarr.xml
```

Il template precompila:

- porta web
- path `/config`, `/backups`, `/logs`
- variabili `TZ`, `Indexarr__ConfigPath`, `Indexarr__BackupPath`, `Indexarr__LogsPath`
- variabili Prowlarr e scheduler

Se installi da immagine Docker senza template, assicurati comunque di mappare almeno:

- `/config`
- `/backups`
- opzionalmente `/logs`

#### `Sviluppo locale`

```bash
dotnet restore src/Indexarr.Web/Indexarr.Web.csproj
dotnet run --project src/Indexarr.Web/Indexarr.Web.csproj
```

Per default l'app punta a `http://127.0.0.1:9696` come istanza Prowlarr.

### `⚙️ Configurazione`

Variabili ambiente principali:

- `Indexarr__Prowlarr__Url`
- `Indexarr__Prowlarr__ApiKey`
- `Indexarr__Automation__Enabled`
- `Indexarr__Automation__IntervalMinutes`
- `Indexarr__ConfigPath`
- `Indexarr__BackupPath`
- `Indexarr__LogsPath`
- `TZ`

Endpoint utili:

- `GET /healthz`
- `GET /readyz`
- `GET /api/meta`
- `GET /api/automation-status`

### `🗂️ Persistenza`

Indexarr usa cartelle dedicate per mantenere dati e cronologia:

- `/config` per configurazione e database SQLite
- `/backups` per i dump degli indexer
- `/logs` per i log applicativi

### `🧪 Stato del progetto`

Il progetto e' attivo e pragmatico: alcune scelte privilegiano velocita' di iterazione, usabilita' e deploy semplice. Se cerchi un tool estremamente rifinito o enterprise-first, questo non e' il punto. Se invece vuoi un progetto utile, self-hosted e in evoluzione rapida, sei nel posto giusto.

## `🇬🇧 English`

### `✨ Why it exists`

Indexarr is built to give you a clear view of the indexers configured in Prowlarr and to automate the most repetitive tasks: health checks, backups, blocking, disabling, and guided onboarding of new indexers.

It is also a **vibe coding** project: fast iteration, continuous feedback, and practical features before theoretical perfection. The goal is to build something genuinely useful, then keep improving it while it is being used.

### `🧭 What it does`

- `📊 Dashboard` with indexer status, filters, and audit history
- `🩺 Health checks` both manual and scheduled
- `🔒 Safeguards` to block or disable problematic indexers
- `💾 Backups` automatic or manual before changes
- `➕ Auto-add` for indexers with predefined filters and rules
- `⚙️ Web setup` with built-in Prowlarr connection test
- `🔐 Protected access` through cookie authentication
- `🌍 Bilingual UI` in Italian and English

### `🧱 Stack`

- `.NET 9`
- `ASP.NET Core Razor Pages`
- `Entity Framework Core`
- `SQLite`
- `Docker`
- `Unraid-ready`

### `🚀 Quick start`

#### `Docker`

```bash
docker build -t indexarr .

docker run -d \
  --name indexarr \
  -p 9697:8080 \
  -e TZ=Europe/Rome \
  -e Indexarr__Prowlarr__Url=http://prowlarr:9696 \
  -e Indexarr__Prowlarr__ApiKey=YOUR_API_KEY \
  -v /path/to/indexarr/config:/config \
  -v /path/to/indexarr/backups:/backups \
  -v /path/to/indexarr/logs:/logs \
  indexarr
```

Then open `http://localhost:9697`.

#### `Updating Indexarr`

To update a Docker installation:

```bash
docker pull gabryk83/indexarr:latest
docker stop indexarr
docker rm indexarr
```

Then recreate the container using the `docker run` command from the previous section. Configuration and data remain available in the mounted folders (`/config`, `/backups`, `/logs`). On Unraid, use `Check for Updates` and then `Update` for the container.

#### `Local development`

```bash
dotnet restore src/Indexarr.Web/Indexarr.Web.csproj
dotnet run --project src/Indexarr.Web/Indexarr.Web.csproj
```

By default, the app points to `http://127.0.0.1:9696` as the Prowlarr instance.

### `⚙️ Configuration`

Main environment variables:

- `Indexarr__Prowlarr__Url`
- `Indexarr__Prowlarr__ApiKey`
- `Indexarr__Automation__Enabled`
- `Indexarr__Automation__IntervalMinutes`
- `Indexarr__ConfigPath`
- `Indexarr__BackupPath`
- `Indexarr__LogsPath`
- `TZ`

Useful endpoints:

- `GET /healthz`
- `GET /readyz`
- `GET /api/meta`
- `GET /api/automation-status`

### `🗂️ Persistence`

Indexarr uses dedicated folders to keep data and history:

- `/config` for configuration and the SQLite database
- `/backups` for indexer dumps
- `/logs` for application logs

### `🧪 Project status`

This project is active and pragmatic: some decisions intentionally favor iteration speed, usability, and simple deployment. If you are looking for an extremely polished or enterprise-first tool, this is probably not it. If you want something useful, self-hosted, and evolving quickly, this is the right direction.

## `📜 License`

This project is released under the MIT license. See [LICENSE](LICENSE).
