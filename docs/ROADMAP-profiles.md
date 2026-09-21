# Roadmap: roleplay profiles

Beacon was built with profiles in mind. This describes what is already in place for them, and what
is left to build — so that adding profiles is an additive change rather than a refactor.

## Why the account model exists

The single most important decision already made is that **ownership is rooted at the account, not the
character**.

```
Account  (a person, holds the secret key)
 ├── Character: Shaala Xiv @ Balmung
 ├── Character: an alt @ Mateus
 ├── Beacon: The Bramblewood Camp
 └── Profile: ...              <- the addition
```

Roleplayers reroll, transfer worlds, and run several characters at once. Had beacons been keyed to a
character, every one of those events would orphan somebody's published work, and profiles would have
inherited the same problem. They are keyed to an account instead, and profiles hang off the same root.

## Seams already in place

| Seam | Where | Why it matters for profiles |
|---|---|---|
| Account-rooted ownership | `AccountEntity` has `Characters` and `Beacons` collections | A `Profiles` collection is a sibling, not a restructure. |
| Character claiming | `POST /api/accounts/me/characters`, unique on `(Name, WorldId)` | A profile belongs to a character that is already proven to belong to an account. |
| `BeaconDto.OwnerAccountId` | Shared contracts | The atlas can link an owner straight to their profile with no new join. |
| Protocol version header | `ApiRoutes.ProtocolHeader` / `ProtocolVersion` | An older plugin gets "please update" rather than a confusing deserialisation failure. |
| Image pipeline | `ImageService`, sharded storage, WebP + PNG fallback | Portraits are the same problem as screenshots; reuse it rather than writing a second one. |
| Realtime hub | `BeaconHub`, `BeaconEvent` | Profile changes can ride the same socket by adding an event kind. |
| Moderation | `ReportEntity`, moderator flag, soft delete | Profiles need reporting on day one, and the machinery already exists. |

## What is not yet built

Nothing profile-specific ships today. There are deliberately **no unused DTOs or dead tables** in the
repo for it — speculative code drifts out of date faster than it saves work. What exists is the shape
that makes the addition cheap.

### Suggested shape when you build it

**Schema** (a single additive migration):

```
ProfileEntity
  Id, AccountId, CharacterName, WorldId
  DisplayName, Age, Race, Occupation
  Appearance, Personality, Background   (long text)
  Hooks                                 (short "what you could approach me about" lines)
  PortraitImageId  -> reuses BeaconImageEntity's storage
  Visibility       -> reuse BeaconVisibility
  CreatedAt, UpdatedAt, IsDeleted
```

Index on `(AccountId)` and unique on `(CharacterName, WorldId)`, mirroring `CharacterEntity`.

**Endpoints**, following the existing patterns exactly:

```
GET    /api/profiles?search=&world=&tag=     browse
GET    /api/profiles/{id}                    one profile
GET    /api/profiles/character/{name}/{world} look up whoever you just met
POST   /api/profiles                         create
PATCH  /api/profiles/{id}                    edit
DELETE /api/profiles/{id}
POST   /api/profiles/{id}/portrait           reuse ImageService
POST   /api/profiles/{id}/report
```

**Plugin**: a `ProfileService` mirroring `AtlasService`, a `ProfileWindow` reusing `Theme` and
`Ornament`, and a context-menu entry to view the profile of a targeted player.

### Two things worth deciding early

1. **Does a profile belong to a character or to an account?** The schema above says character (so an
   alt can have their own), owned by an account (so you manage them all with one key). That is almost
   certainly right, but it is the decision everything else follows from.

2. **Should profiles be linkable from beacons?** A beacon's `OwnerAccountId` already makes this
   possible with no schema change — "whose camp is this?" leading to "who are they?" is the obvious
   join, and probably the feature that makes both halves worth having.

## Bumping the protocol

If a change to the shared contracts is not backwards compatible, increment
`ApiRoutes.ProtocolVersion`. The server compares it on `/health` and the plugin refuses to connect
with a clear message rather than failing strangely. Adding new endpoints and new optional fields does
**not** need a bump — only removing or changing the meaning of something existing does.
