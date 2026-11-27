<p align="center">
  <img src="frontend/public/logo.svg" width="96" alt="Issuarr logo" />
</p>

<h1 align="center">Issuarr</h1>
<p align="center"><b>Never miss an issue.</b></p>

<p align="center">
  A self-hosted manager for <b>comics</b>, <b>magazines</b> and <b>eBooks</b>, built in the spirit of Sonarr and Radarr,<br />
  with a built-in reader, an OPDS catalog for e-readers, KOReader sync, and accounts for friends and family.
</p>

---

## Contents

- [What Issuarr does](#what-issuarr-does)
- [Install with Docker](#install-with-docker)
- [First-time setup](#first-time-setup)
- [Your library](#your-library)
- [Getting new issues automatically](#getting-new-issues-automatically)
- [Reading](#reading)
- [E-readers: OPDS and KOReader sync](#e-readers-opds-and-koreader-sync)
- [Sharing with friends and family](#sharing-with-friends-and-family)
- [Settings reference](#settings-reference)
- [Security and access](#security-and-access)
- [API](#api)
- [Development](#development)
- [Project layout and tech](#project-layout-and-tech)

---

## What Issuarr does

Tell Issuarr which comic series, magazines and authors you follow. It works out every issue or book that exists, shows you what you have and what's missing, and fills the gaps:

1. **Tracks** each title's issues from a metadata source (ComicVine, Metron, Wikidata, Open Library or Google Books), or from a magazine's publishing schedule.
2. **Searches** your indexers (Prowlarr, Newznab or Torznab) when something is released or missing, and picks the best release using your quality profile.
3. **Downloads** it with your download client (qBittorrent, Deluge, Transmission, SABnzbd or NZBGet).
4. **Imports** the finished download: renames it, files it into a tidy folder, writes ComicInfo.xml into comics, and tells Komga or Kavita to rescan.
5. **Lets you read** it in the browser, on a Kobo/KOReader device through OPDS, or in any OPDS app, with your place remembered everywhere.

It looks and works like the other *arr apps: the same layout and settings, a `config.xml`, an `X-Api-Key` REST API and a Docker image.

### Highlights

| | |
| --- | --- |
| 📚 **Comics, magazines and eBooks** | One library. Comics by series and issue number, magazines by date or number, books grouped by author. |
| 🔍 **Indexers** | Prowlarr (all its indexers in one), Newznab, Torznab. RSS sync, automatic and interactive search. |
| ⬇️ **Download clients** | qBittorrent, Deluge, Transmission, SABnzbd, NZBGet. Remote path mappings for Docker setups. |
| 🗂️ **Organising** | Renaming with your own naming formats, hardlinks for torrents, ComicInfo.xml, disk scans. |
| 🖼️ **Covers everywhere** | Per-issue covers (PDF page 1 included), custom title covers, list/detailed/grid views. |
| 📖 **Built-in reader** | CBZ, CBR, CB7, PDF and EPUB in the browser, with progress synced across devices. |
| 📱 **E-readers** | OPDS 1.2 catalog with comic page streaming, and a KOReader progress sync server. |
| 👥 **Friends and family** | Invite links, Reader and Admin roles, separate reading progress for each person. |
| 🔔 **Connections** | Komga and Kavita library rescans, Discord and webhook notifications, iCal calendar feed. |
| 🐳 **Runs anywhere** | Multi-arch Docker image (amd64, arm64) that runs happily on a NAS. |

---

## Install with Docker

Images are published to the GitHub Container Registry for `amd64` and `arm64` (most NAS boxes and the Raspberry Pi 4/5):

```
ghcr.io/hazarduz/issuarr:latest
```

### Docker Compose (or a Portainer stack)

```yaml
services:
  issuarr:
    image: ghcr.io/hazarduz/issuarr:latest
    container_name: issuarr
    environment:
      - TZ=Europe/London
      # "true" starts with a sample library to click around in.
      - ISSUARR_DEMO=false
    volumes:
      - ./config:/config
      # Use the same /data layout as your download client so hardlinks work.
      - /path/to/data:/data
    ports:
      - 8788:8788
    restart: unless-stopped
```

```bash
docker compose up -d
```

Then open `http://<your-server>:8788`.

**Volumes**

| Path in the container | What goes there |
| --- | --- |
| `/config` | Everything Issuarr saves: `config.xml` (port, API key, login), `issuarr.db` (library, history, users, reading progress), `MediaCover/` (custom and cached covers) and `keys/` (keeps logins valid across restarts). Back this folder up. |
| `/data` (or any path you like) | Your comics, magazines and books, plus the download client's download folder. Point Issuarr's root folders here. |

**Updating:** pull the new image and recreate the container. In Portainer, open the stack, pick **Pull and redeploy** (or update the stack with "re-pull image" on). Database changes are applied automatically on start.

**Environment variables**

| Variable | Default | Purpose |
| --- | --- | --- |
| `TZ` | `Etc/UTC` | Time zone for dates and the calendar. |
| `ISSUARR_DEMO` | `false` | `true` seeds a sample library into an empty database. |
| `ISSUARR_DATA` | `/config` in Docker | Where `config.xml` and the database live. `--data=/path` on the command line does the same. |

### Image tags

| Tag | What it is |
| --- | --- |
| `latest` | The newest build of `main` (every push to `main` publishes a new image) |
| `1.2.3`, `1.2` | Releases, created by pushing a `v1.2.3` git tag |
| `<branch>` | Manual builds of a branch (Actions → Docker → Run workflow) |
| `sha-abc1234` | A specific commit |

To build the image yourself, replace `image:` with `build: .` in the compose file. The Dockerfile builds the web UI and the .NET app, then keeps only the native libraries (SQLite, PDF rendering) for the target architecture.

---

## First-time setup

1. **Settings → Media Management → Root Folders:** add the folder(s) where your comics, magazines and books live. The folder picker lets you browse the server.
2. **Settings → Indexers:** add Prowlarr (one entry searches all its indexers) or individual Newznab/Torznab indexers. Use **Test** to check each one.
3. **Settings → Download Clients:** add your client. Use a category or label such as `issuarr`. If the client runs in a different container, set its **Remote Path** (under the advanced fields) to the download folder as the client sees it.
4. **Settings → General → Metadata:**
   - **Comics:** add a free [ComicVine API key](https://comicvine.gamespot.com/api/), or a Metron username and password.
   - **Magazines:** Wikidata works without a key. ComicVine also works if you've added a key.
   - **eBooks:** Open Library works without a key. Google Books works without one, and an optional key raises its limits.
5. **Settings → General → Security:** turn on **Forms** login and choose a username and password, especially before exposing Issuarr outside your network or inviting anyone.
6. Add titles with **Library → Add New**, or bring in what you already have with **Library → Library Import**.

The bell icon at the top shows health checks: a missing root folder, indexer or download client, an unreachable client, an import that needs attention, a missing ComicVine key, or login being off.

---

## Your library

### Adding titles

**Add New** searches the metadata source for the kind you pick:

- **Comics:** ComicVine or Metron series, with covers, publishers and full issue lists.
- **Magazines:** Wikidata (publisher, frequency, cover) or ComicVine. Issuarr creates the issues from the magazine's schedule (weekly, fortnightly, monthly, bi-monthly, quarterly or annually). Numbered magazines continue with `#n`, and you can set the date of the first issue.
- **eBooks:** follow an **author** (Open Library or Google Books list their books) or add a **single book**. Books are grouped under their author.

When adding, choose the root folder, the quality profile, which issues to monitor and whether to search for missing ones straight away. If a matching folder already exists, Issuarr uses it and picks up the files inside.

### Library Import

Point **Library Import** at an existing collection. Each folder becomes a title and each file becomes an issue or book. You can match each folder to the right metadata entry during the import or later. Filenames are understood in common styles, including:

- `Iron Tide 046 (2026).cbr`, `Iron Tide #46 (2026).cbr`, `Starfall.Rangers.037.2026.Digital.cbz`
- `Velocity - 2026-11.pdf`, `Field Notes Monthly - December 2026.pdf`, `The.Kitchen.Table.Sept.2026.PDF`
- `0259 - Retro Gamer.pdf`, `012. Saga (2014).cbz` (number first)
- eBook files are matched to the author's books by title.

### Library page

- **Posters**, **Overview** or **Table** view, with a poster size slider.
- Filter by **All / Comics / Magazines / eBooks**, sort by name, missing, next release or date added, and filter by monitored, unmonitored, missing issues, continuing, ended or not matched.
- **Update All** refreshes metadata; **Search Missing** searches for every missing monitored issue.

### Title page

- **Three views:** **List** (compact table), **Detailed** (cover thumbnail, file name, format, size and reading progress) and **Grid** (a wall of covers with status and progress badges; click a cover to read). Issuarr remembers your choice.
- Issues are grouped by year, each year with its own monitor toggle and "search missing" button. An author's books form one list.
- **Collection** shows the issues you have out of the released issues that are monitored or already on disk.
- **Toolbar:** Refresh, Search Monitored, Interactive Search, Preview Rename, Manage Files, History, Match/Rematch, Edit and Delete.
- **Each issue or book:** read it, match it to a different metadata entry (duplicates are merged), edit its name or date, delete it (with or without the file), search automatically or interactively, and monitor or unmonitor it.
- **Release dates:** click a date to type a new one or pick it from a calendar. Dates you set yourself are pinned, so metadata refreshes don't overwrite them; click the pin to unpin.
- **Hidden issues:** metadata entries you delete stay hidden on future refreshes. "Restore" at the bottom of the page brings them back.

### Matching

- **Match a title** (toolbar or the "Not matched" badge) to a ComicVine, Metron, Wikidata, Open Library or Google Books entry. Your files stay on their issues and the missing ones are filled in. **Unmatch** keeps your issues and files.
- **Match a single issue or book** when an import guessed wrong, for example to link a file to *Project Hail Mary*.

### Covers

- **Title covers:** **Edit cover** on the title page offers the metadata cover, issue covers, the first page of your own files, an upload, or an image address. You can reset to the automatic cover at any time.
- **Issue covers:** each issue shows its own cover: the metadata cover if there is one, otherwise the file's own cover. That's the first page of a comic archive, the EPUB's embedded cover, or page 1 of a PDF. Issuarr renders these once and saves them as small JPEGs in `/config/MediaCover/issues`, so grids, Home shelves and OPDS stay fast.

### Disk scan

**Refresh** (or the scheduled metadata refresh) also scans each title's folder. It picks up files you copied in yourself, links them to the right issue, and forgets files that were deleted outside Issuarr.

### Calendar

A month view of upcoming and recent releases, plus an **iCal feed** (`/feed/v1/calendar/issuarr.ics`) to subscribe to from your calendar app.

---

## Getting new issues automatically

### How releases are chosen

Issuarr reads each release name and checks it against your title before grabbing it. Releases are skipped when:

- the name doesn't mention the title, or can't be understood;
- the issue number or date isn't a known issue of that title;
- the issue or title isn't monitored;
- you already have it in an equal or better format, or the profile doesn't allow upgrades;
- it's already downloading or blocklisted;
- a torrent has fewer seeders than your minimum.

**Interactive Search** shows every release found, with the reason each would or wouldn't be grabbed, and lets you download any of them.

### Quality profiles

| Profile | Formats, best first | Upgrades |
| --- | --- | --- |
| **CBZ / CBR** | CBZ, CBR, CB7 | Yes: CBR and CB7 are upgraded to CBZ |
| **PDF Preferred** | PDF, EPUB, CBZ, CBR | Yes: anything else is upgraded to PDF (good for magazines) |
| **eBook** | EPUB, AZW3, MOBI, PDF | Yes: AZW3, MOBI and PDF are upgraded to EPUB |
| **Any** | CBZ, CBR, CB7, PDF, EPUB, AZW3, MOBI | No: the first file wins |

### Scheduled tasks

| Task | How often |
| --- | --- |
| Check download queue (import finished downloads) | Every minute |
| RSS sync (grab new releases) | Every 15 minutes (adjustable, minimum 10) |
| Refresh metadata and scan disks | Every 12 hours |

Searches also run when you add a title (if you ask), from the toolbar, per year, per issue, or for everything missing (**Wanted → Missing**). **Wanted → Cutoff Unmet** lists issues that could be upgraded to a better format.

### Importing finished downloads

When a download finishes, Issuarr:

1. Matches the file to its issue or book.
2. Renames it with your naming format and moves it into the title's folder, or **hardlinks** it for torrents so seeding continues without using extra space.
3. Writes **ComicInfo.xml** into CBZ files (series, number, release date, publisher).
4. Tells **Komga** or **Kavita** to rescan, and sends **Discord** or **webhook** notifications.

If a download fails, the release is blocklisted and, with **Redownload Failed** on, Issuarr searches for another. **Activity** shows the queue, history and blocklist.

---

## Reading

Click 📖 on any issue or book, a cover in the Grid view, or a card on the Home page.

| Format | Reader |
| --- | --- |
| **CBZ, CBR, CB7** (comics) | Page by page, streamed from the archive without unpacking it. |
| **PDF** (magazines) | Rendered in the browser with pdf.js. |
| **EPUB** (books) | Reflowable text with themes, text size and a table of contents. |
| **MOBI, AZW3** | Downloaded instead (send them to KOReader or your reading app). |

**Comics and PDFs:** fit to screen, fit width (scroll) or original size; single page or two-page spread; left-to-right or right-to-left (manga); a page slider; keyboard (←/→, Space, Page Up/Down, Home/End) or click/tap the sides.

**EPUB:** dark, light or sepia theme; text size; contents; two pages side by side on wide screens. Covers and full-page pictures are scaled to fill the page.

Your place is saved as you read and shows up as a progress badge on the title page and in **Continue Reading**.

### Home page

- **Continue Reading:** what you're partway through.
- **Up Next:** the next issue of anything you've finished one of.
- **Latest Comics**, **Latest Magazines**, **Latest Books:** newest additions of each kind, each with a **See all** link to the filtered library.

Each card's **⋯** menu can mark it read or unread, or jump to the title.

---

## E-readers: OPDS and KOReader sync

### OPDS catalog

Issuarr serves an **OPDS 1.2** catalog at `http://<your-server>:8788/opds` for KOReader (Kobo, Kindle, PocketBook, Android), Panels, Chunky, KyBook, Moon+ Reader, Thorium and others. It contains:

- **Continue Reading** and **Recently Added** shelves (for whoever is logged in);
- **Comics**, **Magazines**, **eBooks** and **Everything**;
- **Search**;
- downloads of every file, and **OPDS-PSE page streaming** so comic apps can page through CBZ/CBR files without downloading them.

Log in with your Issuarr username and password (HTTP Basic). For apps that can't log in, Settings → General shows an address that includes the API key; it gives owner access, so keep it private.

**Kobo:** Kobo's own reading software doesn't support OPDS. Install [KOReader](https://koreader.rocks), which runs alongside it, then in KOReader go to **Search → OPDS catalog → +** and add the address with your username and password. Use the NAS's network address, not `localhost`.

### KOReader progress sync

Issuarr is a KOReader **progress sync server** (the kosync protocol). Your place on the Kobo shows up in Issuarr's Continue Reading, other KOReader devices pick up where you left off, and comic and PDF pages read in Issuarr's web reader are offered back to KOReader. EPUB positions sync from KOReader to Issuarr only, because KOReader uses its own internal positions for reflowable books.

1. **Settings → General → KOReader Sync:** turn it on (this switches it on for everyone).
2. Choose a sync password:
   - **Owner:** set a sync username and password in Settings → General, or a sync password in **My Account**.
   - **Everyone else:** set a sync password in **My Account** and log in to KOReader with your Issuarr username.

   Use a password just for this, because KOReader sends it as an unsalted MD5 hash.
3. In KOReader, with a book open: **Tools → Progress sync → Custom sync server** → `http://<your-server>:8788/kosync`, then **Login**, and turn on **Auto sync**. Leave *Document matching method* on **Binary**, which still works after files are renamed.

---

## Sharing with friends and family

Issuarr has accounts, so other people can read your library with their own logins and their own progress.

1. Turn on **Forms** login in Settings → General → Security.
2. **Settings → Users → Invite:** add a note (who it's for), choose **Reader** or **Admin**, and how long the link lasts (1 to 30 days).
3. Send the link. It works once: your friend picks a username and password and lands on their own Home page.

| | Reader | Admin |
| --- | --- | --- |
| Browse the library, calendar and covers | ✅ | ✅ |
| Read in the browser and download files | ✅ | ✅ |
| OPDS and KOReader sync with their own login | ✅ | ✅ |
| Their own Continue Reading, Up Next, read badges and sync positions | ✅ | ✅ |
| Add, edit, match, delete or monitor titles, search, change settings, manage users | ❌ | ✅ |

Readers only see Home, Library and Calendar. The server enforces this too: anything that would change the library is refused for readers. They never see the API key or your folder paths.

**Managing people:** Settings → Users lists everyone with when they were last seen. You can change roles, reset passwords, remove people (and their reading progress), and cancel unused invites.

**Each person's account:** the avatar menu (top right) has **My Account** to change their password, copy the OPDS and KOReader addresses, set their KOReader sync password, and log out.

**Friends outside your home:** the invite link uses whatever address you're viewing Issuarr on. For people elsewhere, make Issuarr reachable from the internet, for example behind a reverse proxy with HTTPS (set **URL Base** if it lives under a sub-path), and send them that address.

---

## Settings reference

| Section | What's there |
| --- | --- |
| **Media Management** | Root folders; **Rename Issues**; naming formats for comic issues, magazine issues, title folders, eBook files and author folders; **Use Hardlinks**; **Write ComicInfo.xml**. |
| **Profiles** | The four quality profiles (see above). |
| **Indexers** | Indexer list (Prowlarr, Newznab, Torznab); **RSS Sync Interval**; **Minimum Seeders**. |
| **Download Clients** | Client list (qBittorrent, Deluge, Transmission, SABnzbd, NZBGet) with categories/labels and remote path mappings; **Redownload Failed**; **Remove Completed Torrents**. |
| **Connect** | Komga, Kavita, Discord and Webhook connections. |
| **General** | Metadata sources and keys for comics, magazines and eBooks; Security (authentication, username, password, API key); OPDS addresses; KOReader Sync; Host (instance name, bind address, port, URL base). |
| **Users** | Accounts, roles and invite links. |

### Naming tokens

Default formats:

| Kind | Default |
| --- | --- |
| Comic issue | `{Title} {Issue:000} ({Release Year})` |
| Magazine issue | `{Title} - {Release Date:yyyy-MM}` |
| Title folder | `{Title} ({Title Year})` |
| eBook file | `{Title} - {Issue} ({Release Year})` |
| Author folder | `{Title}` |

Available tokens: `{Title}`, `{Author}`, `{Title Year}`, `{Publisher}`, `{Issue}`, `{Issue:000}` (zero-padded), `{Issue Title}`, `{Release Date}`, `{Release Date:yyyy-MM}`, `{Release Year}`, `{Format}`. The settings page shows a live preview, and **Preview Rename** on a title shows what would change before you rename existing files.

---

## Security and access

- **Authentication:** **Forms** (login page, "remember me" for 30 days), **Basic** (browser pop-up), or **None**. **Authentication Required** can be *Enabled* or *Disabled for local addresses*. With the second option, anyone on your home network who isn't logged in is treated as the owner. People who log in are always themselves.
- **The owner** is the account set in Settings → General → Security (stored in `config.xml`). It's always an admin and can't be removed.
- **API key:** the master key for other apps (e.g. Prowlarr) and scripts, sent as the `X-Api-Key` header or `?apikey=`. It acts as the owner. Only admins can see it, and you can regenerate it in Settings → General.
- Passwords are stored as salted PBKDF2-SHA256 hashes. Login cookies are encrypted with keys kept in `/config/keys`.

---

## API

The REST API lives under `/api/v1`. Authenticate with the API key (`X-Api-Key` header or `?apikey=`) or a login cookie. Readers can only use the read-only endpoints (titles, issues, calendar, covers, reading) plus `/me`.

| Method | Endpoint | |
| --- | --- | --- |
| GET | `/title?includeIssues=true` | All titles with statistics |
| GET | `/title/{id}` · `/title/slug/{slug}` | One title with its issues |
| POST · PUT · DELETE | `/title` · `/title/{id}` | Add, edit, remove a title (`?deleteFiles=`) |
| GET | `/title/lookup?term=&kind=&source=` · `/title/lookup/book?term=` | Search series, magazines or authors / single books |
| POST · DELETE | `/title/{id}/match` | Match a title `{ source, externalId, useMetadataName }` / unmatch |
| GET | `/title/{id}/candidates` | Metadata entries an issue or book can be matched to |
| POST | `/title/{id}/restorehidden` | Restore hidden issues |
| GET | `/title/{id}/files` · `/title/{id}/rename` | Files on disk / rename preview |
| GET · POST · PUT · DELETE | `/title/{id}/covers` · `/title/{id}/cover` | Cover candidates / upload (multipart) / set from URL `{ url }` / reset |
| GET | `/mediacover/{titleId}` | A title's custom cover image |
| GET | `/issue?titleId=` | Issues for a title |
| PUT | `/issue/monitor` | `{ "issueIds": [1, 2], "monitored": false }` |
| PUT · DELETE | `/issue/{id}` | Edit `{ number, title, releaseDate }` / delete (`?deleteFile=`) |
| PUT | `/issue/{id}/releasedate` | Pin a release date `{ releaseDate }` (`null` unpins) |
| POST | `/issue/{id}/match` | Match an issue or book `{ externalId }` |
| DELETE | `/issuefile/{id}` | Delete a file |
| GET | `/calendar?start=&end=` | Releases in a date range |
| GET | `/wanted/missing` · `/wanted/cutoff` | Missing issues / issues below the cutoff format |
| GET · POST | `/release?titleId=&issueId=` | Interactive search / grab a release `{ guid, titleId, issueId }` |
| GET · POST | `/command` · `/command/{id}` | Run a job: `RefreshTitle`, `RefreshAll`, `TitleSearch`, `IssueSearch`, `MissingSearch`, `RssSync`, `ProcessDownloads`, `RenameTitle` |
| GET · DELETE | `/queue` · `/queue/{id}` | Download queue / remove (`?removeFromClient=&blocklist=`) |
| GET · DELETE | `/history` · `/blocklist` · `/blocklist/{id}` | History and blocklist |
| GET · POST · PUT · DELETE | `/indexer` · `/downloadclient` · `/notification` | Providers, plus `/schema` and `/test` |
| GET · POST · DELETE | `/rootfolder` · `/rootfolder/{id}` | Root folders |
| GET · POST | `/libraryimport?path=` | Scan a folder for titles / import them |
| GET | `/filesystem?path=` | Browse server folders |
| GET | `/parse?title=` | Show how a release name is parsed |
| GET | `/qualityprofile` | Quality profiles |
| GET · PUT | `/config` · `/config/host` | Settings |
| GET | `/config/naming/tokens` | Naming tokens |
| POST | `/config/host/apikey` | Regenerate the API key |
| GET | `/read/{issueId}` · `/read/{issueId}/page/{n}` · `/read/{issueId}/file` · `/read/{issueId}/cover` | Reader info, comic page images, the file (`?download=true`), the issue's cover |
| PUT · DELETE | `/read/{issueId}/progress` | Save `{ position, percent }` / clear your reading progress |
| GET | `/reading/home` | Your Home shelves |
| GET · PUT · DELETE | `/user` · `/user/{id}` | Users (admins): list, change `{ role, password }`, remove |
| GET · POST · DELETE | `/user/invite` · `/user/invite/{token}` | Invite links `{ role, note, days }` |
| GET · PUT | `/me` · `/me/password` · `/me/kosync` | Your own account, password `{ current, password }`, KOReader sync password |
| GET | `/health` · `/system/status` · `/system/task` | Health checks, system info, scheduled tasks |

Outside `/api/v1`:

| Endpoint | |
| --- | --- |
| `/opds` | OPDS catalog (HTTP Basic, a login cookie, or `?apikey=`) |
| `/kosync` | KOReader sync server (`users/auth`, `users/create`, `syncs/progress`, `healthcheck`) |
| `/feed/v1/calendar/issuarr.ics` | iCal feed |
| `/invite/{token}` | Invite sign-up page |
| `/login` · `/logout` | Forms login |
| `/ping` | Liveness check |

---

## Development

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node 22.

Run the backend. It uses a sample library and stores data in `./_data`:

```bash
cd src/Issuarr.Host
dotnet run
```

In another terminal, run the web UI with hot reload. It forwards API calls to the backend on port 8788:

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

Pushes to `main` and pull requests run the **Build** workflow (UI build plus tests). Pushes to `main` and `v*` tags run the **Docker** workflow, which publishes the multi-arch image to GHCR.

---

## Project layout and tech

```
frontend/                React + TypeScript + Vite web UI
  src/pages/             Home, Library, Title, Reader, Calendar, Activity, Wanted, Settings, System, Account…
  src/components/        Layout, dialogs, covers, toolbar…
  src/styles.css         Design tokens and styles (dark and light themes)
src/Issuarr.Core/        Domain logic
  Titles/ Issues/        Titles, issues, refresh and schedules
  Metadata/              ComicVine, Metron, Wikidata, Open Library, Google Books
  Indexers/ IndexerSearch/ Decisions/ Parser/    Searching and choosing releases
  DownloadClients/ Download/ MediaFiles/ Organizer/    Downloading, importing, renaming, covers
  Reading/ Users/        Reading progress, Home shelves, KOReader sync, accounts and invites
  Datastore/Migrations/  Database schema (FluentMigrator)
src/Issuarr.Host/        ASP.NET Core host: REST API, OPDS, KOReader sync, login, serves the UI
src/Issuarr.Core.Test/   NUnit tests
```

- **Backend:** C# / .NET 10, ASP.NET Core, SQLite with Dapper and FluentMigrator, SharpCompress (CBR/CB7), PDFtoImage/PDFium and SkiaSharp (server-side covers), NUnit.
- **Web UI:** React 19, TypeScript, Vite, React Router, Lucide icons, pdf.js and epub.js for the reader, plain CSS with design tokens.
- **Same building blocks as Sonarr and Radarr:** `config.xml`, an `X-Api-Key` REST API, SQLite, FluentMigrator, NUnit and a React UI.

See [ROADMAP.md](ROADMAP.md) for what's planned next.
