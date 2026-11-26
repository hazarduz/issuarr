<p align="center">
  <img src="frontend/public/logo.svg" width="96" alt="Issuarr logo" />
</p>

<h1 align="center">Issuarr</h1>
<p align="center"><b>Never miss an issue.</b></p>

Issuarr is a self-hosted manager for magazines and comics, built in the spirit of Sonarr and Radarr. Tell it which titles you follow and it keeps track of every issue: what's out, what you have and what's missing. It searches your indexers through Prowlarr, sends releases to your download client (qBittorrent, Deluge, Transmission, SABnzbd and more), then renames and files each issue into a clean `Title/Issue` library ready for Komga, Kavita or any OPDS reader.

Magazines and comics have always been an afterthought in the *arr world. Issuarr treats them as first-class: one app, one library, every issue accounted for.

## Features

- 📚 Track magazines and comic series side by side
- 🔔 Automatically grab new issues as they're released
- 🧩 Find and fill missing back issues
- 🔍 Prowlarr / Torznab / Newznab support
- ⬇️ Works with qBittorrent, Deluge (labels and all), Transmission and SABnzbd
- 🗂️ Consistent renaming and folder structure
- 📖 Plays nicely with Komga, Kavita and OPDS readers
- 🐳 Docker-first, runs happily on a NAS

## Status

Early days. What works today:

- ✅ Web UI (library, title pages, calendar, activity, wanted, settings, system)
- ✅ Backend with SQLite database, REST API (`/api/v1`) and API-key auth
- ✅ `config.xml` + app-data folder, same as Sonarr/Radarr
- ✅ Release-name parser for comics and magazines (`#46`, `037`, `November 2026`, `2026-W48`, `Q3 2026`…)
- ✅ Root folders, monitoring, health checks
- ✅ Docker image

Not yet: metadata lookup (ComicVine/Metron), indexers/Prowlarr, download clients, importing files. See [ROADMAP.md](ROADMAP.md).

## Quick start (Docker)

```yaml
services:
  issuarr:
    build: .
    environment:
      - ISSUARR_DEMO=true   # start with a sample library to explore
    volumes:
      - ./config:/config
      - /path/to/data:/data
    ports:
      - 8788:8788
```

```bash
docker compose up -d
```

Open http://localhost:8788. Settings and the database live in `/config` (`config.xml`, `issuarr.db`).

## Development

You need [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node 22.

Run the backend (with a sample library, data stored in `./_data`):

```bash
cd src/Issuarr.Host
dotnet run
```

In another terminal, run the UI with hot reload. It forwards API calls to the backend:

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:5173. If the backend isn't running, the UI falls back to built-in sample data and shows a **Demo data** badge.

Run the tests:

```bash
dotnet test src/Issuarr.slnx
```

## Project layout

```
frontend/              React + TypeScript + Vite web UI
src/Issuarr.Core/      Domain, database (SQLite + Dapper + FluentMigrator), parser, services
src/Issuarr.Host/      ASP.NET Core host: REST API, auth, serves the UI
src/Issuarr.Core.Test/ NUnit tests
```

Same building blocks as Sonarr and Radarr: C#/.NET, SQLite, Dapper, FluentMigrator, NUnit, a React UI, `config.xml`, and an `X-Api-Key` REST API.

## API

All endpoints need the API key (from `config.xml`, or **Settings → General**) as an `X-Api-Key` header or `?apikey=` parameter.

| Method | Endpoint | |
| --- | --- | --- |
| GET | `/api/v1/title?includeIssues=true` | All titles with statistics |
| GET | `/api/v1/title/{id}` · `/api/v1/title/slug/{slug}` | One title with issues |
| POST / PUT / DELETE | `/api/v1/title` | Add, edit, remove titles |
| GET | `/api/v1/issue?titleId=` | Issues for a title |
| PUT | `/api/v1/issue/monitor` | `{ "issueIds": [1,2], "monitored": false }` |
| GET | `/api/v1/calendar?start=&end=` | Releases in a date range |
| GET | `/api/v1/wanted/missing` | Missing issues |
| GET / POST / DELETE | `/api/v1/rootfolder` | Root folders |
| GET | `/api/v1/parse?title=` | Show how a release name is parsed |
| GET | `/api/v1/health` · `/api/v1/system/status` | Health checks, system info |

## Tech

- **Backend:** C# / .NET 10, ASP.NET Core, SQLite, Dapper, FluentMigrator, NUnit
- **UI:** React 19, TypeScript, Vite, React Router, Lucide icons, plain CSS with design tokens (`frontend/src/styles.css`)
