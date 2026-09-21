<p align="center">
  <img src="assets/beacon.png" alt="Beacon torch" width="240">
</p>

<h1 align="center">Beacon</h1>

**Find the fires. Light your own.**

A Dalamud plugin and companion server that let FFXIV roleplayers publish, find and travel to places
**anywhere in the overworld** — not just housing wards.

Venue plugins already solve the venue problem: a fixed address in a ward, open on a schedule. They do
not solve the other half of roleplay, which happens at a camp in Il Mheg, a shrine in Thavnair, a
crossroads in the Shroud — places with no plot number and no way to tell anyone they exist.

Beacon gives those places an address, a picture, a description, and a flame you light when you are
actually there.

---

## What it does

**Raise a beacon** anywhere you can stand. The plugin captures your exact position, zone, world and
nearest aetheryte, and you add a name, a description, a screenshot and some tags.

**Travel to one** from anywhere, across worlds and data centres. Beacon plans the route and drives
it: world visit or DC transfer via Lifestream, teleport to the nearest aetheryte, then walks the last
stretch with vnavmesh if you have it. An on-screen arrow points the rest of the way.

**Light the beacon** when you are there and open to roleplay. The atlas shows, live, who is out there
right now and how long they have said they will be. Mark a beacon as a public commons and anyone
standing there can light it, so a place stays alive whether or not its keeper is online.

Lighting is enforced **server-side**: you must be on the right world, in the right zone, and within
50 yalms. A flame that could be lit from anywhere would mean nothing.

---

## Repository layout

| Project | What it is |
|---|---|
| `src/Beacon.Shared` | DTOs, routes and limits shared by both ends. One source of truth for the wire contract. |
| `src/Beacon.Server` | ASP.NET Core 10 + EF Core + SQLite. The atlas, the image store, the realtime hub. |
| `src/Beacon.Plugin` | The Dalamud plugin. Builds to `Beacon.dll`. |

Targets .NET 10, Dalamud API level 15.

---

## Running the server

Double-click **`run-server.cmd`** in the repository root. Leave the window open while you test;
closing it stops the server.

Or from a terminal:

```bash
dotnet run --project src/Beacon.Server
```

It listens on `http://localhost:5215`, creates `src/Beacon.Server/var/` on first run, and applies
its migrations automatically. That directory holds `beacon.db` and every uploaded screenshot —
**back up that one directory and you have backed up the whole service.**

(It is called `var/`, not `data/`, because Windows paths are case-insensitive: a runtime `data/`
folder beside the source `Data/` folder would be the same directory.)

Configuration lives under the `Beacon` section of `appsettings.json`:

| Setting | Default | What it does |
|---|---|---|
| `DataDirectory` | `var` | Where the database and images live. |
| `RegistrationOpen` | `true` | Set false to freeze membership; existing keys keep working. |
| `MaxBeaconsPerAccount` | `50` | Quota per account. |
| `SweepInterval` | `00:00:30` | How often burnt-out flames are swept. |
| `ModeratorAccountIds` | `[]` | Account ids promoted to moderator on startup. |
| `RateLimitPerMinute` | `120` | Reads per account per minute. |
| `WriteRateLimitPerMinute` | `20` | Writes per account per minute. |

For anything public, read **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)** first. Two things there will
bite you otherwise: the default data directory sits inside the application folder, where a redeploy
destroys it, and behind a reverse proxy the per-IP rate limits collapse into one shared bucket until
you declare the proxy.

The public Aethercast instance is `https://plugins.aethercast.org:61249`. The distributed plugin is
compiled with that HTTPS endpoint as its default; `localhost` is used only by the local development
launcher.

### Changing the schema

Migrations are checked in and applied on startup. To add to them:

```bash
dotnet tool restore
dotnet dotnet-ef migrations add YourChange --project src/Beacon.Server --output-dir Data/Migrations
```

---

## Building the plugin

```bash
dotnet build src/Beacon.Plugin -c Release
```

Output lands in `src/Beacon.Plugin/bin/Release/`, including the generated `Beacon.json` manifest.

To install it straight into Dalamud's dev plugin folder:

```bash
dotnet build src/Beacon.Plugin -p:DeployToDevPlugins=true
```

That copies to `%AppData%\XIVLauncher\devPlugins\Beacon\`. It is opt-in because a build should not
write outside the repo unless you asked it to.

---

## Dependencies

| Plugin | Required? | Why |
|---|---|---|
| [Lifestream](https://github.com/NightmareXIV/Lifestream) | **Yes**, for travel | World visits, DC transfers, aetheryte teleports. Beacon does not reimplement any of it. |
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Optional | Walks the last stretch from the aetheryte to the beacon. Without it you arrive at the aetheryte and the overlay points the way. |

Everything else (browsing, lighting, screenshots) works with neither installed.

---

## Commands

| Command | What it does |
|---|---|
| `/beacon` | Open the atlas. |
| `/beacon here` | Raise a beacon where you stand. |
| `/beacon light` | Light the nearest beacon you may light. |
| `/beacon out` | Put out the beacon you lit. |
| `/beacon go <code>` | Open a beacon by its share code. |
| `/beacon settings` | Open the settings. |

---

## Accounts and keys

An account is a **person**, not a character — so rerolling an alt or transferring worlds never
orphans what you have published, and so roleplay profiles have somewhere to live later.

On first run the plugin mints a secret key and shows it once. That key is the only proof a beacon is
yours; it is stored hashed server-side and **cannot be reissued**. Copy it somewhere safe, and paste
it into the settings on any other PC to pick up the same account.

Characters are claimed automatically (one character belongs to one account, server-wide).

---

## Design notes

A few decisions worth knowing about before changing things:

- **Lighting is verified server-side.** The plugin checks proximity too, but only so the button can
  explain itself. The client is not a trust boundary.
- **Flames always expire**, and a sweeper enforces it. A beacon that can never go out becomes a lie
  the moment its owner logs off, and a stale atlas is worse than an empty one.
- **Timestamps are stored as integers.** SQLite has no date type, and EF's default ISO-with-offset
  string sorts correctly only while every row shares one offset.
- **Screenshots are re-encoded, never passed through.** That normalises the format, bounds the size,
  and strips metadata a screenshot can carry without its author intending it. Uploads are rejected on
  decoded pixel count, not file size, because that is what actually stops a decompression bomb.
- **Screenshots are stored as WebP**, with on-demand PNG transcoding for machines whose imaging stack
  cannot decode WebP. The plugin probes once and asks for whichever it can read.
- **The realtime hub drops events for slow clients** rather than buffering without limit. Losing a
  stale flame update for one bad connection is fine; stalling every other client is not.

---

## Deploying

```bash
publish.cmd     # Windows: server + plugin
./publish.sh    # Linux: server only
```

The server runs on Linux or Windows. The plugin must be built on Windows, since it targets the
Dalamud SDK. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

Production releases are deliberately gated. `tools/publish-release.ps1` performs a locked build,
checks that no database or uploaded images are tracked, exercises the API and protocol guard, restarts
the server against the same temporary database, and validates the final plugin ZIP. The exact release
order and rollback procedure are in [docs/RELEASING.md](docs/RELEASING.md).

To install Beacon, add `https://plugins.aethercast.org/` under Dalamud's custom plugin repositories,
then install **Beacon** from the plugin installer.

---

## Roadmap

Roleplay profiles are the intended next feature, and the architecture is already shaped for them —
see [docs/ROADMAP-profiles.md](docs/ROADMAP-profiles.md) for the specific seams and what remains.
