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

Early days. The repository currently contains the **web UI** (`frontend/`), running on mock data. The backend (metadata, indexers, download clients, import) comes next.

## UI

A modern, dark-first interface in the *arr family style:

- **Library** – poster grid (with a size slider), overview list and table views; filter comics/magazines; sort by title, missing, next issue
- **Title detail** – blurred cover backdrop, metadata cards, collection progress, issues grouped by year with per-issue monitoring and search
- **Calendar** – month view of releases, colour coded by status
- **Activity** – live download queue, history, blocklist
- **Wanted** – missing issues and cutoff unmet
- **Settings** – media management (naming tokens, ComicInfo.xml, hardlinks), profiles, indexers, download clients, connections, general
- **System** – stats and health checks
- Global search (press <kbd>/</kbd>), light/dark theme toggle, responsive down to phone width

### Run it

```bash
cd frontend
npm install
npm run dev
```

Then open http://localhost:8788.

## Tech

- React 19 + TypeScript + Vite
- React Router
- Lucide icons
- Plain CSS with design tokens (`frontend/src/styles.css`) – no UI framework
