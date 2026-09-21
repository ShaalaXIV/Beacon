# Releasing Beacon safely

Beacon has two independently versioned deliverables: the plugin ZIP and the server container. A
source push changes neither production system. Production only changes when a versioned Git tag,
GitHub release asset, immutable Docker image tag, Portainer stack update, and shared repository entry
are deliberately advanced together.

## Non-negotiable release order

1. Bump the four-part plugin version in `Beacon.Plugin.csproj`. Bump `ApiRoutes.ProtocolVersion`
   whenever a wire contract becomes incompatible.
2. Run `tools/publish-release.ps1`. It performs a locked restore, builds both products, rejects local
   or insecure server URLs, exercises registration/authentication/protocol enforcement, restarts the
   server against the same database, and validates the Dalamud ZIP.
3. Back up `/opt/portainer/beacon/data` and verify the backup checksum and SQLite integrity.
4. Commit, tag `vX.Y.Z`, and publish `publish/Beacon-X.Y.Z.zip` as the GitHub release asset.
5. Build immutable `beacon-server:X.Y.Z` and `beacon-backup:X.Y.Z` images from that exact tag.
6. In Portainer, update only the versioned image tags. Do not use `latest` and do not enable automatic
   Git redeployment. Confirm all three containers become healthy and existing account data remains.
7. Update `/opt/portainer/plugins-repo/www/repo.json` only after the versioned GitHub asset works.
8. Verify the public feed, icon, release download, HTTPS health endpoint, WebSocket upgrade, and a
   clean plugin installation.

If any check fails, leave the existing GitHub asset, Portainer image tags, and public repository entry
unchanged. Rollback is the reverse: restore the previous immutable image tags and repository entry;
restore data only when a migration made the previous server unable to read the current database.
