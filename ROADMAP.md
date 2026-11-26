# Roadmap

Rough order. Each step should leave the app usable.

## 1. Metadata
- [ ] Look up titles and issue lists from ComicVine (comics) and Metron
- [ ] Real covers (cached in `/config/MediaCover`)
- [ ] Wire **Add New** search to the metadata provider
- [ ] Scheduled refresh to pick up newly announced issues
- [ ] Magazines: frequency-based issue schedule where no metadata source exists

## 2. Indexers
- [ ] Newznab and Torznab indexers
- [ ] Prowlarr app sync (Prowlarr pushes indexers to Issuarr)
- [ ] RSS sync every 15 minutes
- [ ] Automatic + interactive search for an issue

## 3. Decisions
- [ ] Match releases to titles/issues with the release parser
- [ ] Quality profiles (CBZ > CBR > PDF, True PDF > scan) with cutoff
- [ ] Blocklist for failed releases

## 4. Download clients
- [ ] qBittorrent, Deluge (labels), Transmission, SABnzbd, NZBGet
- [ ] Queue tracking (Activity → Queue)
- [ ] Failed download handling

## 5. Import
- [ ] Completed download handling: rename + move/hardlink into the library
- [ ] Naming formats from Settings → Media Management
- [ ] Write ComicInfo.xml into CBZ files
- [x] Library import for existing collections
- [ ] Disk scan to pick up files added outside Issuarr

## 6. Connect & polish
- [ ] Komga / Kavita library refresh on import
- [ ] Discord, webhook, Apprise notifications
- [ ] Forms login (needed before the UI can be safely exposed)
- [ ] Backups of `/config`
- [ ] Logs page, scheduled tasks page
- [ ] Publish multi-arch Docker image (amd64 + arm64 for NAS boxes)
