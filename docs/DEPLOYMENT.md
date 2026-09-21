# Deploying Beacon

Two things ship: the **server**, which you run somewhere your community can reach, and the **plugin**,
which they install. The plugin is useless without a server to point at, so do the server first.

**On Windows:**

```bash
publish.cmd
```

Produces `publish\server\` and `publish\plugin\`.

**On Linux:**

```bash
./publish.sh
```

Produces `publish/server-linux-x64/`. Pass a runtime identifier for anything else, such as
`./publish.sh linux-arm64`.

Neither output is committed.

The **server runs on Linux without changes** — it is ASP.NET Core on .NET 10, the native SQLite
library ships for Linux in the published output, and image processing is managed code. The **plugin**
must be built on Windows: it targets the Dalamud SDK, which resolves against an XIVLauncher
installation. Build the server wherever it will run, and the plugin on the machine you play on.

The published output is framework-dependent, so the target needs the **ASP.NET Core 10 runtime**
installed. Add `--self-contained true` to `publish.sh` if you would rather not manage that separately.

---

## Two things to get right before you expose it

### 1. Put the data somewhere a redeploy cannot reach

`Beacon:DataDirectory` defaults to `var`, **relative to the application folder**. That is right for
local development and wrong for a deployment: the application folder is the folder you replace when
you update, and replacing it destroys every account, beacon and profile on the server.

Set an absolute path outside the deployment:

```bash
Beacon__DataDirectory=/var/lib/beacon
```

The server refuses to start in Production when this path is relative. That failure is intentional: a
relative path appears safe until the *second* deploy replaces the application directory and destroys
real user data.

That one directory holds the SQLite database and every uploaded image. Back it up and you have backed
up the whole service.

### 2. Put it behind TLS

Every authenticated request carries the account's secret key in an `X-Beacon-Key` header. Over plain
HTTP that key is readable by anything between the player and you, and it is the only credential
guarding their beacons and profile.

Terminate TLS at a reverse proxy (Caddy, nginx, Traefik) and point the plugin at the `https://` URL.
The plugin switches to `wss://` for the realtime hub automatically when the base URL is HTTPS.

---

## Behind a reverse proxy, name it

```bash
Beacon__TrustedProxies__0=127.0.0.1
```

Without this, every request appears to originate from the proxy. The rate limiter partitions anonymous
callers by remote address, so the per-IP limits collapse into a single shared bucket: **five account
registrations per hour for the entire internet**, and one anonymous rate limit shared by everybody.

Forwarded headers are ignored entirely until you list the proxies, so an untrusted client can never
spoof its own address by sending `X-Forwarded-For` itself.

---

## Configuration

Everything lives under the `Beacon` section, settable in `appsettings.json` or as environment
variables using `Beacon__Name` (double underscore).

| Setting | Default | Notes |
|---|---|---|
| `DataDirectory` | `var` | **Set an absolute path.** See above. |
| `RegistrationOpen` | `true` | `false` freezes membership; existing keys keep working. |
| `MaxBeaconsPerAccount` | `50` | |
| `SweepInterval` | `00:00:30` | How often burnt-out flames are cleared. |
| `ImageQuality` | `80` | WebP quality for stored images. |
| `RateLimitPerMinute` | `120` | Reads per account. |
| `WriteRateLimitPerMinute` | `20` | Writes per account. |
| `RegistrationsPerHour` | `5` | New accounts per IP. Raise for a shared household or a convention. |
| `ModeratorAccountIds` | `[]` | Promoted on startup. See below. |
| `TrustedProxies` | `[]` | Reverse proxy addresses. See above. |

Set the environment explicitly. `ASPNETCORE_ENVIRONMENT=Development` enables sensitive data logging
and relaxes the registration limit to 500/hour; neither belongs on a public server.

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5215
```

Bind to loopback when a proxy sits in front. There is no reason for the app itself to listen publicly.

---

## Running it on Linux

The checked-in Portainer deployment is `deploy/portainer-stack.yml`. The Aethercast deployment uses
immutable versioned images, publishes HTTPS on port `61249`, stores live state under
`/opt/portainer/beacon/data`, stores 30 days of verified daily backups under
`/opt/portainer/beacon/backups`, and reloads the existing `plugins.aethercast.org` certificate daily.
The application container itself is never published directly.

The systemd example below is an alternative for hosts that do not use Portainer.

```bash
sudo useradd --system --home /var/lib/beacon --create-home beacon
sudo mkdir -p /opt/beacon && sudo cp -r publish/server-linux-x64/* /opt/beacon/
sudo chown -R beacon:beacon /opt/beacon /var/lib/beacon
```

`/etc/systemd/system/beacon.service`:

```ini
[Unit]
Description=Beacon
After=network.target

[Service]
Type=simple
User=beacon
WorkingDirectory=/opt/beacon
ExecStart=/opt/beacon/Beacon.Server
Restart=always
RestartSec=5

Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5215
Environment=Beacon__DataDirectory=/var/lib/beacon
Environment=Beacon__TrustedProxies__0=127.0.0.1

# The service needs nothing outside its own two directories.
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/beacon

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now beacon
journalctl -u beacon -f
```

Note that `/var/lib/beacon` sits outside `/opt/beacon`, so replacing the application directory on an
update cannot touch the data. That separation is the whole point of the warning above.

A Caddyfile is the shortest route to TLS:

```
beacon.example.com {
    reverse_proxy 127.0.0.1:5215
}
```

Caddy forwards WebSocket upgrades without extra configuration, which the realtime hub needs. With
nginx, remember `proxy_set_header Upgrade $http_upgrade;` and `proxy_set_header Connection "upgrade";`
or the Chronicle and atlas will load but never update live.

---

## First run

1. Start the server. It creates the data directory and applies its migrations on startup.
2. Install the plugin, open **Settings**, point `Server URL` at your address, and create an account.
3. Copy your account id from the settings window.
4. Add it to `ModeratorAccountIds` and restart. That account can now resolve reports and remove other
   people's beacons and profiles.

There is no admin UI, and that is deliberate: on a self-hosted instance, who moderates is a decision
that belongs in a config file you control rather than in a screen somebody could reach.

---

## Distributing the plugin

`publish\plugin\` contains `Beacon.dll`, its manifest, and `Beacon\latest.zip` — the zip a Dalamud
third-party repository serves.

Two ways to get it to people:

- **A repository JSON** they add under Dalamud's experimental settings. This is what makes updates
  automatic, and it is worth the setup if more than a handful of people will use it.
- **Hand them the folder** to drop into `%AppData%\XIVLauncher\devPlugins\Beacon\`. Fine for testing
  with a few friends, tedious beyond that.

Before distributing a fork, change the plugin's default `ServerUrl` to that fork's public HTTPS
endpoint. The Aethercast release is already pinned to `https://plugins.aethercast.org:61249` in
`src/Beacon.Plugin/Services/Configuration.cs`.

---

## Updating

```bash
publish.cmd
```

For the Aethercast deployment, follow `docs/RELEASING.md`: build immutable versioned images from the
release tag, back up first, then advance the Portainer image tags. Migrations apply on startup, so
schema changes need no separate command.

**Back up the data directory first.** Migrations move forward only; there is no down path.

If you have changed the database schema, remember that `dotnet ef migrations add` builds the project
*before* writing the migration file, so the binary it produced does not contain it. Build again
afterwards, or the server refuses to start with "the model has pending changes".

---

## What is not here

Worth knowing before you run this for a community rather than a handful of friends:

- **No automated backups.** Copy the data directory on a schedule; it is a single SQLite file plus
  images, so a nightly `cp` or `rsync` is genuinely sufficient.
- **No moderation queue UI.** Reports land in the `Reports` table. Reading them today means querying
  the database.
- **SQLite means one writer at a time.** Entirely fine for a community of hundreds; it is not the
  right shape for thousands of concurrent writers, and moving to Postgres would be a provider change
  plus a data migration rather than a rewrite.
- **Account keys cannot be recovered.** They are stored hashed. Somebody who loses their key loses
  their beacons and profile, and there is no reset you can perform for them. Tell people to save it.
