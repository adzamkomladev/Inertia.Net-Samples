# Route & prop contract

Frontend types live in `ClientApp/types/index.d.ts`. JSON is camelCase. All non-GET requests need the antiforgery
header (`X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie); Inertia's router adds it automatically, `fetch` callers must add it.

| Method | Path | Auth | Result |
|---|---|---|---|
| GET | /posts?q=&page= | public | `Posts/Index` { posts: ScrollPage<PostSummary> (Scroll prop, wrapper `data`), filters: { q: string } } |
| GET | /posts/create | verified | `Posts/Create` {} |
| POST | /posts | verified | body { title, body, coverAssetId? } → redirect /posts/{slug} + toast. Precognition enabled |
| GET | /posts/{slug} | public | `Posts/Show` { post: PostDetail, comments: CommentItem[] (Defer, group `comments`) } ; 404 → `Error` {status:404} |
| GET | /posts/{slug}/edit | owner | `Posts/Edit` { post: PostDetail } |
| PUT | /posts/{id} | owner | body { title, body, coverAssetId? } → redirect /posts/{slug} + toast. Precognition enabled |
| DELETE | /posts/{id} | owner | → redirect /posts + toast |
| POST | /posts/{id}/view | public | view beacon, sent by Show.vue after mount, JSON body `{ referrer?: string|null }` (document.referrer; optional, max 2048 chars, host kept, dropped if same host) → 204; 404 if post missing; "views" rate limit (60/min); ignores bots, the author, prefetch |
| POST | /posts/{id}/comments | verified | body { body } → Back() |
| DELETE | /comments/{id} | comment owner or post author | → Back() |
| POST | /posts/{id}/like | verified | toggles → Back() |
| POST | /uploads/presign | signed in | JSON body { contentType, size, fileName } → JSON PresignResponse; 422 ValidationProblem on bad type/size |
| POST | /uploads/{id}/confirm | owner | → JSON UploadedAsset; 422 if the object is missing/mismatched |
| GET | /analytics?days=7\|30\|90 | signed in | `Analytics/Index` { days, kpis: Kpis (Defer group `kpis`), series: SeriesPoint[] (Defer group `charts`), topPosts: TopPost[] (Defer group `charts`) } |
| GET | /analytics/posts/{id}?days= | owner | `Analytics/Post` { days, post: {id,title,slug}, kpis (Defer), series (Defer), referrers: Referrer[] (Defer) } |
| GET | /settings/preferences | signed in | `Settings/Preferences` { preferences: Preferences, profile: { name, avatarAssetId } } (avatar URL is in auth.user.avatarUrl) |
| PUT | /settings/preferences | signed in | Preferences body → Back() + toast |
| PUT | /settings/profile | signed in | { name, avatarAssetId } → Back() + toast |
| GET/POST | /login, /register | guest | `Auth/Login` { returnUrl }, `Auth/Register` ; register has Precognition |
| POST | /logout | signed in | → /login |
| GET | /email/verify | signed in | `Auth/VerifyEmail` ; POST /email/verification-notification resends |

Post cards prefetch `/posts/{slug}` on hover (30s cache).

Shared props: `appName`, `auth.user` (AuthUser | null). Flash: `flash.toast` (Toast).

## Shared components (cross-agent)
- `ClientApp/components/ImageUpload.vue` — props `modelValue: number | null` (asset id), `previewUrl?: string | null`, `label?: string`;
  emits `update:modelValue` (number | null) and `uploaded` (UploadedAsset). Uploads via `useUpload()` in `ClientApp/composables/useUpload.ts`.

## Backend services (C#)
- `JobQueue.EnqueueAsync(db, IJob payload, runAt?, ct)` — saves pending changes + job in one SaveChanges. Payloads: SendEmail, NotifyComment(commentId), NotifyLike(postId, likerId), DeleteAssets(keys).
- `ViewTracker.Track(HttpContext, postId, authorId, string? referrer = null)` (`referrer` replaces the Referer header source) — non-blocking, call from the `POST /posts/{id}/view` beacon (not GET /posts/{slug}: prefetched visits skip it).
- `Storage.Url(key)`, `PresignPut`, `HeadAsync`, `DeleteAsync`, `UploadFileAsync`, `ListKeysAsync`.
- `Mailer.SendAsync(to, subject, html, text, ct)`; email bodies in `Emails.*`.
- `HttpExtensions.UserId()/UserIdOrNull()/Forbidden()`, `Toast(type, message)` flash record.
