# Roadmap

## Done

**Library**
- [x] Comics from ComicVine or Metron: search, covers, issue lists, scheduled refresh
- [x] Magazines from Wikidata or ComicVine, with issues generated from the publishing schedule (dated or numbered)
- [x] eBooks: authors and single books from Open Library or Google Books
- [x] Library Import of an existing collection, with number-first and date-based filename parsing
- [x] Disk scan: picks up files added outside Issuarr, forgets deleted ones
- [x] Matching titles and individual issues/books; editing, deleting and hiding issues
- [x] Editable, pinned release dates
- [x] Custom title covers and per-issue covers (including PDF page 1, rendered on the server)
- [x] Title page List / Detailed / Grid views; Library Posters / Overview / Table views

**Downloading**
- [x] Indexers: Newznab, Torznab, Prowlarr; RSS sync; automatic and interactive search
- [x] Decisions: title/issue matching, four quality profiles with upgrades, blocklist, minimum seeders
- [x] Download clients: qBittorrent, Deluge, Transmission, SABnzbd, NZBGet; queue; remote path mappings
- [x] Import: rename, move/hardlink, ComicInfo.xml, failed download handling and re-search
- [x] Connections: Komga, Kavita, Discord, Webhook
- [x] Calendar with iCal feed; health checks; scheduled tasks

**Reading**
- [x] Built-in reader for CBZ/CBR/CB7, PDF and EPUB with synced progress
- [x] Home page: Continue Reading, Up Next, Latest Comics / Magazines / Books
- [x] OPDS 1.2 catalog with page streaming
- [x] KOReader progress sync server

**Accounts**
- [x] Forms/Basic login, local-network bypass, API key
- [x] User accounts with invite links, Reader and Admin roles, per-user reading progress and KOReader sync

## Next
- [ ] Editable quality profiles (currently four built-in profiles)
- [ ] Tags to route titles to specific indexers / download clients
- [ ] Issue packs (one release containing many issues)
- [ ] Manual import of a folder of loose downloads
- [ ] Logs page and backups of `/config`
- [ ] Recycle bin for deleted files
- [ ] Book series (e.g. Stormlight Archive) grouping on author pages
- [ ] Per-user library restrictions (e.g. share only some titles with a reader)
- [ ] Audiobooks
