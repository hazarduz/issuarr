<p align="center">
  <img src="frontend/public/logo.svg" width="96" alt="Issuarr logo" />
</p>

<h1 align="center">Issuarr</h1>
<p align="center"><b>Never miss an issue.</b></p>

Issuarr is a self-hosted manager for magazines, comics and eBooks, built in the spirit of Sonarr and Radarr. Tell it which titles you follow and it keeps track of every issue: what's out, what you have and what's missing. It searches your indexers through Prowlarr, sends releases to your download client (qBittorrent, Deluge, Transmission, SABnzbd and more), then renames and files each issue into a clean `Title/Issue` library ready for Komga, Kavita or any OPDS reader.

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

## What it does

- **Comics:** search ComicVine or Metron for series, with covers and full issue lists
- **Magazines:** look them up on Wikidata (publisher, frequency, cover) or ComicVine, and Issuarr works out the issues from how often they come out
- **eBooks:** follow authors or add single books from Open Library or Google Books; EPUB, AZW3, MOBI and PDF
- **Disk scan:** Refresh picks up files you put in a title's folder yourself and forgets ones you deleted; adding a title reuses a matching folder that's already there
- **Library import:** scan an existing collection; each folder becomes a title, each file an issue
- **Manual matching:** link imported titles to the right ComicVine/Metron series (during import or later from the title page); your files stay on their issues and the missing ones are filled in
- **Per-item match, edit and delete:** point a single issue or book at the right metadata entry (duplicates are merged), fix its name/date, or delete it (with or without the file); deleted metadata entries stay hidden until you restore them
- **Indexers:** Newznab, Torznab, or Prowlarr (search all its indexers at once)
- **Download clients:** qBittorrent, Deluge, Transmission, SABnzbd, NZBGet, with remote path mappings for Docker setups
- **Automatic downloads:** RSS sync every 15 minutes, plus search on add, per title, per issue or for everything missing
- **Interactive search:** see every release with the reason it would or wouldn't be grabbed, and download any of them
- **Quality profiles:** prefer CBZ or PDF, upgrade until the cutoff, blocklist failed releases and search again
- **Import:** finished downloads are renamed with your naming format and moved, or hardlinked for torrents; ComicInfo.xml is written into CBZ files
- **Built-in reader:** read comics (CBZ/CBR/CB7), magazines (PDF) and eBooks (EPUB) in the browser, with reading progress synced across devices; MOBI/AZW3 can be downloaded
- **Connections:** rescan Komga or Kavita on import; Discord and webhook notifications
- **Calendar:** month view and an iCal feed for your calendar app
- **Login:** Forms or Basic, optionally skipped on your home network; API key for other apps

### First-time setup

1. **Settings → Media Management:** add a root folder (where your comics/magazines live).
2. **Settings → Indexers:** add Prowlarr, or individual Newznab/Torznab indexers.
3. **Settings → Download Clients:** add your client. Use a category/label like `issuarr`.
4. **Settings → General:** add a free [ComicVine API key](https://comicvine.gamespot.com/api/) (or Metron login) to search for comics, choose your magazine and eBook sources (they work without keys), and turn on login.
5. **Add New** or **Library Import**.

## Quick start (Docker)

Images are published to GitHub Container Registry for `amd64` and `arm64` (most NAS boxes and Raspberry Pi):

```
ghcr.io/hazarduz/issuarr:latest
```

```yaml
services:
  issuarr:
    image: ghcr.io/hazarduz/issuarr:latest
    container_name: issuarr
    environment:
      - TZ=Etc/UTC
      - ISSUARR_DEMO=true   # start with a sample library to explore
    volumes:
      - ./config:/config
      - /path/to/data:/data
    ports:
      - 8788:8788
    restart: unless-stopped
```

```bash
docker compose up -d
```

Open http://localhost:8788. Settings and the database live in `/config` (`config.xml`, `issuarr.db`).

### Image tags

| Tag | What it is |
| --- | --- |
| `latest` | Latest build from `main` (or newest release) |
| `1.2.3`, `1.2` | Releases, created by pushing a `v1.2.3` git tag |
| `<branch>` | Manual builds of a branch (Actions → Docker → Run workflow) |
| `sha-abc1234` | A specific commit |

To build the image yourself instead, replace `image:` with `build: .` in the compose file.

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
| GET / POST | `/api/v1/libraryimport?path=` | Scan a folder for titles / import them |
| GET | `/api/v1/filesystem?path=` | Browse server folders (used by the folder picker) |
| GET | `/api/v1/parse?title=` | Show how a release name is parsed |
| GET / POST / PUT / DELETE | `/api/v1/indexer` · `/downloadclient` · `/notification` | Providers (+ `/schema`, `/test`) |
| GET | `/api/v1/title/lookup?term=&kind=&source=` | Search series (`comic`), magazines (`magazine`) or authors (`book`) |
| GET | `/api/v1/title/lookup/book?term=` | Search individual books |
| GET | `/api/v1/title/{id}/candidates` | Metadata entries an issue/book can be matched to |
| PUT / DELETE | `/api/v1/issue/{id}` | Edit an issue/book `{ number, title, releaseDate }` / delete (`?deleteFile=`) |
| POST | `/api/v1/issue/{id}/match` · `/api/v1/title/{id}/restorehidden` | Match an issue/book `{ externalId }` / restore hidden ones |
| POST / DELETE | `/api/v1/title/{id}/match` | Match a title to a series `{ source, externalId, useMetadataName }` / unmatch |
| GET / POST | `/api/v1/release?titleId=&issueId=` | Interactive search / grab a release |
| POST / GET | `/api/v1/command` | Run jobs: `RefreshTitle`, `RefreshAll`, `TitleSearch`, `IssueSearch`, `MissingSearch`, `RssSync`, `ProcessDownloads`, `RenameTitle` |
| GET / DELETE | `/api/v1/queue` | Download queue / remove (`?removeFromClient=&blocklist=`) |
| GET | `/api/v1/history` · `/api/v1/blocklist` | History, blocklist |
| GET / PUT | `/api/v1/config` · `/api/v1/config/host` | Settings |
| GET | `/api/v1/wanted/cutoff` | Issues that could be upgraded |
| GET | `/api/v1/read/{issueId}` · `/page/{n}` · `/file` | Reader: info, comic page images, the raw file (`?download=true`) |
| PUT / DELETE | `/api/v1/read/{issueId}/progress` | Save `{ position, percent }` / clear reading progress |
| GET | `/feed/v1/calendar/issuarr.ics` | iCal feed |
| GET | `/api/v1/health` · `/api/v1/system/status` · `/api/v1/system/task` | Health checks, system info, scheduled tasks |

## Tech

- **Backend:** C# / .NET 10, ASP.NET Core, SQLite, Dapper, FluentMigrator, NUnit
- **UI:** React 19, TypeScript, Vite, React Router, Lucide icons, plain CSS with design tokens (`frontend/src/styles.css`)
