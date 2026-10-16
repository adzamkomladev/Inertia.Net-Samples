# `/me/posts` timeline performance

The signed-in chronological timelines (`/me/posts`, `/me/archives`, `/bookmarks` and the public
profile timeline in `Users/Show`) page posts plus reposts by their *effective* time: a repost
shows at the moment it was reposted, everything else at its `CreatedAt`. The opaque cursor
`(At, Id)` keeps new posts and reposts from breaking pagination.

## What was slow

`Chronological` ranked a single query by
`COALESCE((SELECT r."CreatedAt" FROM "Reposts" r WHERE r."UserId" = u AND r."PostId" = p."Id"), p."CreatedAt")`.
That expression cannot be served by any index — PostgreSQL has to compute it for every visible
post before picking the page:

```
Limit  (cost=2532238.41..2532424.18 rows=11 ...)
  ->  Sort  ... Sort Key: (COALESCE((SubPlan 2), p."CreatedAt")) DESC, p."Id" DESC
        ->  Seq Scan on "Posts" p  (actual time=20.698..56.460 rows=113.00 loops=1)
              Filter: (("ArchivedAt" IS NULL) AND (("AuthorId" = ...) OR (ANY ("Id" = (hashed SubPlan 4).col1))))
              Rows Removed by Filter: 199,925
```

200,000 scanned posts and ~4,600 buffer hits to produce eleven rows for a user with 40 posts and
60 reposts. The same plan hit `/users/{id}` (profile) and any of the signed-in timeline modes.

## The fix

`SocialService` now pages two **index-backed** streams and merges them in memory:

- authored posts of the timeline user, excluding ones they reposted themselves
  (`IX_Posts_AuthorId_CreatedAt`, anti-join against the unique `IX_Reposts_UserId_PostId`),
- reposts of visible posts (`IX_Reposts_UserId_CreatedAt_Id`, joined against `Posts` by key).

Each stream takes its own top page (`size + 1`, the cursor predicate applies to its own time and
id columns), and the global top page can only contain rows from those two pages, so merging
them in-process is complete for the page — sorting the merged ≤ 2 × (size + 1) rows is trivial.
Because the streams are disjoint, no rows are duplicated across pages. Modes whose cursor is the
creation timestamp (`bookmarks`, `archives`) keep the original single query, now wrapped in
`CreatedTimelineAsync`.

## Measured (PostgreSQL 18, 20,000 users / 200,000 posts seeded locally)

| Request | Before | After |
| --- | --- | --- |
| `GET /me/posts` (median) | 210.6 ms | 24.6 ms |
| `GET /users/{id}` profile | 348.5 ms | 22.2 ms |
| `/me/archives`, `/bookmarks`, `GET /posts` | 14.9 / 21.0 / 16.9 ms | 13.2 / 19.0 / 16.1 ms (unchanged scope) |

The timeline statements after the change are plan-limited:
each branch runs in under 0.1 ms and lands on its index (`Nested Loop Anti Join` on
`IX_Posts_AuthorId_CreatedAt` for authored posts; indexed repost range for reposts). The
remaining request time is the existing per-page enrichment (summaries, topics, viewer state)
plus Inertia rendering.

Cursor semantics and responses are unchanged; the existing suite (including
`Cursor_order_survives_new_posts_and_rejects_invalid_values`) passes on both PostgreSQL samples.
