# Feed Demo Seed Data Summary

## Seed data root
- Media folder: `Backend/Compose/seed-data/feed/media`
- Runtime state folder: `Backend/Compose/seed-data/feed/runtime`

## Media files imported from `assets/`
- `landscape.gif`
- `landscape.jpg`
- `landscape2.jpg`
- `landscapevideo.mp4`
- `portailvideo.mp4`
- `squaregif.gif`
- `squaregif2.gif`

## Feed demo user seed

### Summary
- Total feed demo users: **18**
- Users with media resources: **10**
- Feed demo resources created: **70**
- Default password for demo users: `12345678`
- Demo users are created without avatar resources.

### Login credentials
Use the existing login endpoint with either `EmailOrUsername = <username>` or `EmailOrUsername = <email>`, plus the shared password `12345678`. The login request shape is `LoginRequest(string EmailOrUsername, string Password)`. Reference: `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AuthController.cs:49-57`, `Backend/Microservices/User.Microservice/src/WebApi/Controllers/AuthController.cs:359`

| Username | Email | Password | Full name | Profile kind |
|---|---|---|---|---|
| `maya_canvas` | `maya.canvas+seed@meai.local` | `12345678` | Maya Canvas | `hub` |
| `leo_travelnotes` | `leo.travelnotes+seed@meai.local` | `12345678` | Leo Travel Notes | `hub` |
| `sora_frames` | `sora.frames+seed@meai.local` | `12345678` | Sora Frames | `media` |
| `iris_motion` | `iris.motion+seed@meai.local` | `12345678` | Iris Motion | `media` |
| `nora_bookclub` | `nora.bookclub+seed@meai.local` | `12345678` | Nora Book Club | `storyteller` |
| `quang_nomad` | `quang.nomad+seed@meai.local` | `12345678` | Quang Nomad | `storyteller` |
| `vera_grid` | `vera.grid+seed@meai.local` | `12345678` | Vera Grid | `designer` |
| `zane_looplab` | `zane.looplab+seed@meai.local` | `12345678` | Zane Loop Lab | `designer` |
| `linh_overflow_test` | `linh.overflow+seed@meai.local` | `12345678` | Linh với một cái tên hiển thị rất dài để test card trên mobile | `balanced` |
| `otto_smalltalk` | `otto.smalltalk+seed@meai.local` | `12345678` | Otto Smalltalk | `balanced` |
| `mina_unicode` | `mina.unicode+seed@meai.local` | `12345678` | Mina Unicode ミナ ユニコード | `balanced` |
| `kai_newline` | `kai.newline+seed@meai.local` | `12345678` | Kai Newline | `balanced` |
| `hana_numbers` | `hana.numbers+seed@meai.local` | `12345678` | Hana Numbers 123 | `balanced` |
| `bao_capsule` | `bao.capsule+seed@meai.local` | `12345678` | Bảo Capsule | `balanced` |
| `ria_quietmode` | `ria.quietmode+seed@meai.local` | `12345678` | Ria Quiet Mode | `quiet` |
| `tuan_minimal` | `tuan.minimal+seed@meai.local` | `12345678` | Tuấn Minimal | `quiet` |
| `yuki_firstday` | `yuki.firstday+seed@meai.local` | `12345678` | Yuki First Day | `newcomer` |
| `pax_reader` | `pax.reader+seed@meai.local` | `12345678` | Pax Reader | `observer` |

### Profile-kind distribution
- `hub`: 2
- `media`: 2
- `storyteller`: 2
- `designer`: 2
- `balanced`: 6
- `quiet`: 2
- `newcomer`: 1
- `observer`: 1

### Seeded demo users
| Username | Full name | Profile kind | Has media |
|---|---|---:|---:|
| `maya_canvas` | Maya Canvas | `hub` | yes |
| `leo_travelnotes` | Leo Travel Notes | `hub` | yes |
| `sora_frames` | Sora Frames | `media` | yes |
| `iris_motion` | Iris Motion | `media` | yes |
| `nora_bookclub` | Nora Book Club | `storyteller` | yes |
| `quang_nomad` | Quang Nomad | `storyteller` | yes |
| `vera_grid` | Vera Grid | `designer` | yes |
| `zane_looplab` | Zane Loop Lab | `designer` | yes |
| `linh_overflow_test` | Linh với một cái tên hiển thị rất dài để test card trên mobile | `balanced` | no |
| `otto_smalltalk` | Otto Smalltalk | `balanced` | no |
| `mina_unicode` | Mina Unicode ミナ ユニコード | `balanced` | no |
| `kai_newline` | Kai Newline | `balanced` | no |
| `hana_numbers` | Hana Numbers 123 | `balanced` | no |
| `bao_capsule` | Bảo Capsule | `balanced` | yes |
| `ria_quietmode` | Ria Quiet Mode | `quiet` | no |
| `tuan_minimal` | Tuấn Minimal | `quiet` | yes |
| `yuki_firstday` | Yuki First Day | `newcomer` | no |
| `pax_reader` | Pax Reader | `observer` | no |

### Users that own media resources
- `maya_canvas`
- `leo_travelnotes`
- `sora_frames`
- `iris_motion`
- `nora_bookclub`
- `quang_nomad`
- `vera_grid`
- `zane_looplab`
- `bao_capsule`
- `tuan_minimal`

### Resource allocation rule
Each media-enabled demo user receives resource records for all 7 imported media files, producing **70 feed demo resources** in total.

## Feed demo follow graph
- Total follow edges: **65**
- No self-follow edges are included.

### Outgoing follows by user
- `maya_canvas` → `leo_travelnotes`, `sora_frames`, `iris_motion`, `nora_bookclub`, `linh_overflow_test`, `kai_newline`, `yuki_firstday`
- `leo_travelnotes` → `maya_canvas`, `quang_nomad`, `vera_grid`, `zane_looplab`, `mina_unicode`, `bao_capsule`
- `sora_frames` → `maya_canvas`, `leo_travelnotes`, `iris_motion`, `vera_grid`, `zane_looplab`
- `iris_motion` → `maya_canvas`, `sora_frames`, `leo_travelnotes`, `quang_nomad`, `bao_capsule`
- `nora_bookclub` → `maya_canvas`, `quang_nomad`, `otto_smalltalk`, `pax_reader`
- `quang_nomad` → `maya_canvas`, `leo_travelnotes`, `nora_bookclub`, `vera_grid`
- `vera_grid` → `maya_canvas`, `sora_frames`, `zane_looplab`, `iris_motion`, `linh_overflow_test`
- `zane_looplab` → `vera_grid`, `sora_frames`, `maya_canvas`, `kai_newline`
- `linh_overflow_test` → `maya_canvas`, `leo_travelnotes`, `mina_unicode`, `kai_newline`
- `otto_smalltalk` → `nora_bookclub`, `maya_canvas`
- `mina_unicode` → `maya_canvas`, `leo_travelnotes`, `bao_capsule`
- `kai_newline` → `maya_canvas`, `linh_overflow_test`
- `hana_numbers` → `maya_canvas`, `leo_travelnotes`, `bao_capsule`
- `bao_capsule` → `maya_canvas`, `hana_numbers`, `mina_unicode`
- `ria_quietmode` → `maya_canvas`, `otto_smalltalk`
- `tuan_minimal` → `maya_canvas`
- `yuki_firstday` → `maya_canvas`, `leo_travelnotes`, `kai_newline`
- `pax_reader` → `nora_bookclub`, `maya_canvas`

## Feed demo posts
- Total posts: **22**
- Total comments: **28**
- Total post likes: **72**
- Total hashtags: **38**
- Total post-hashtag links: **51**

### Seeded posts
| Slug | Author | Media type | Resource count | Covered scenario |
|---|---|---|---:|---|
| `maya-short-hello` | `maya_canvas` | `none` | 0 | very short text card |
| `maya-long-overflow` | `maya_canvas` | `none` | 0 | long overflow text, many hashtags |
| `maya-multi-image` | `maya_canvas` | `image` | 4 | multi-image carousel with gif |
| `maya-media-only` | `maya_canvas` | `image` | 1 | media-only post |
| `leo-travel-story` | `leo_travelnotes` | `image` | 1 | long caption with single landscape image |
| `leo-video-only` | `leo_travelnotes` | `video` | 1 | video-only post |
| `leo-mixed-media` | `leo_travelnotes` | `mixed` | 2 | image + video mixed media |
| `sora-grid-stack` | `sora_frames` | `image` | 3 | square/gif-heavy grid |
| `iris-portrait-video` | `iris_motion` | `video` | 1 | portrait/mobile reel-like video |
| `nora-bookclub-text` | `nora_bookclub` | `none` | 0 | multiline review/story text |
| `quang-nomad-url` | `quang_nomad` | `none` | 0 | long URL wrapping |
| `vera-design-system` | `vera_grid` | `image` | 2 | hashtag wrap stress + image media |
| `zane-looplab-minimal` | `zane_looplab` | `none` | 0 | minimal text |
| `linh-overflow-nospace` | `linh_overflow_test` | `none` | 0 | no-space overflow token |
| `mina-unicode-cjk` | `mina_unicode` | `none` | 0 | Vietnamese + CJK + symbols |
| `kai-newline-poem` | `kai_newline` | `none` | 0 | multiline poem-like content |
| `hana-numbers-grid` | `hana_numbers` | `none` | 0 | number-heavy spacing test |
| `bao-capsule-combo` | `bao_capsule` | `mixed` | 3 | image + gif + video combo |
| `ria-quiet-single` | `ria_quietmode` | `none` | 0 | almost-empty profile |
| `tuan-minimal-emptycaption` | `tuan_minimal` | `image` | 1 | whitespace caption normalized to null |
| `yuki-firstday-intro` | `yuki_firstday` | `none` | 0 | first-post / populated-state transition |
| `pax-reader-bookmark` | `pax_reader` | `none` | 0 | low-activity reader profile |

## Covered frontend/backend cases
- Very short text
- Very long text with overflow stress
- Long token without spaces
- Whitespace-only caption normalization
- Multiline text / line breaks
- Unicode / CJK / special symbols
- Long URL wrapping
- Many hashtags / tag wrapping
- No-media text posts
- Media-only posts
- Single-image posts
- Multi-image gallery posts
- GIF-in-gallery posts
- Video-only posts
- Mixed image + video posts
- Quiet profile / almost-empty profile
- New user first-post case
- Ordering and pagination through varied timestamps

## Example comment coverage
Representative seeded comment themes include:
- line-clamp and ellipsis QA
- mobile overflow checks
- masonry height stability
- placeholder/skeleton loading
- hashtag wrap validation
- multilingual line-height checks
- empty-state to populated-state transition

## Runtime state files produced by seeding
- `Backend/Compose/seed-data/feed/runtime/users.state.json`
- `Backend/Compose/seed-data/feed/runtime/feed.state.json`

## Production verification snapshot
After resetting production compose volumes and starting the production stack, the verified database counts were:
- `userdb.users`: **20**
- `userdb.resources`: **84**
- `feeddb.follows`: **65**
- `feeddb.posts`: **22**
- `feeddb.comments`: **28**
- `feeddb.post_likes`: **72**
- `feeddb.hashtags`: **38**
- `feeddb.post_hashtags`: **51**

### Why user/resource totals are larger than feed demo totals
Production also seeds existing non-feed data in User service:
- admin user
- default user
- sample user data/resources

So:
- feed demo seed contributes **18 users** and **70 resources**
- full production verification shows **20 users** and **84 resources** overall

## Public media route used by feed demo resources
- `GET /api/User/seed-media/{*fileName}`

This route serves imported media files directly from the shared feed seed data root.
