---
target: samples/Inkwell ClientApp
total_score: 26
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:C:\\Users\\adzam\\Documents\\Work\\dotnet-inertia-samples\\samples\\Inkwell\\src\\Inkwell\\ClientApp"
timestamp: 2026-11-16T22-27-27Z
slug: src-inkwell-clientapp
---
# Inkwell critique, round 1 (baseline after stage A)

Method: dual-agent (A: design review with 49 screenshots; B: `impeccable detect` + live overlay + playwright-cli mechanical checks at 390 and 1440).

## Design-specificity verdict
Mostly authored (~6/10). The reading surfaces are specific: Newsreader display, `prose-post` column, `· · ·` rule, warm paper OKLCH tokens, split login. The social layer around them is generic: a four-hue action cluster (blue comment, green repost, red like, amber bookmark) on every row, solid black Follow pills, pastel KPI chips and green/red delta pills. Together they drown the single terracotta accent.

## Heuristics
| # | Heuristic | Score |
|---|---|---|
| 1 | Visibility of system status | 3 |
| 2 | Match with real world | 3 |
| 3 | User control and freedom | 3 |
| 4 | Consistency and standards | 2 |
| 5 | Error prevention | 3 |
| 6 | Recognition over recall | 3 |
| 7 | Flexibility and efficiency | 3 |
| 8 | Aesthetic and minimalist | 2 |
| 9 | Error recovery | 2 |
| 10 | Help and documentation | 2 |
| | **Total** | **26/40** |

## Priority issues

### P1. Focus hidden under the mobile tab bar
- The fixed 57px bar covers focused controls: WCAG 2.4.11 fails on /settings, /users and /analytics, and 49 focus stops on /posts are partly hidden.
- `scroll-padding-bottom` is `auto`.
- On the post page, the sticky action pill stacks on top of the bar.
- **Fix:** scroll-padding top and bottom with the safe-area inset; hide the tab bar on the composer.

### P1. The action palette breaks the one-accent identity
- Inactive icons should be muted. Only the active state gets colour, using brand and foreground.
- Drop the comment blue.
- Neutralise the KPI chips, delta pills and Admin badge.

### P1. Notifications are anonymous
- Every notification reads "Someone liked/commented on your post".
- The kind is inferred by regex on the title text.
- **Fix:** send `kind`, actor and post title; render the actor avatar and name with the post title; group repeats; give the unread dot an accessible name.

### P2. Dead space and hierarchy
- The signed-out phone hero is about 680px tall.
- VirtualList leaves about 600px of void below short lists.
- The byline-to-body gap is about 100px.

### P2. Post-row consistency and labels
- The "…" slot shifts the action cluster on some rows.
- The comment count reads "1 comments".
- Toggles swap their labels ("Unlike") and hide the count.
- Excerpts show raw markdown.

### P2. Composer on phone
- The Public/Draft toggle is icon-only.
- Two fixed bars stack around the keyboard.
- Publish does not say "Save draft" in draft mode.

### P3. Small targets and other details
- Small targets: switches 32×18, show-password 32×18, chips 24px, header links 20px.
- Global `scrollbar-width: none` removes the scroll-position cue.
- The theme hook ignores OS changes.
- The analytics curve uses monotone interpolation over zero days.
- Login uses the framework's default validation copy.

## Personas
- **Casey (mobile):**
  - The hero hides the first essay.
  - No bookmark confirmation.
  - Follow sits behind the tab bar.
- **Sam (accessibility):**
  - Obscured focus.
  - 12 chips before content in tab order.
  - Label swapping on toggles.
  - Unlabelled mute icon.
  - Unread state shown only by a dot.
- **Jordan (first-timer):**
  - Latest, For you and Following each appear twice.
  - All chips show ~1,960 posts.
  - Register autofocuses Name on phone.
  - No first-run guidance.

## Detector
- side-tab on the blockquote: false positive (editorial rule).
- Geist flagged as an overused font: accepted, because the serif carries identity.
- Bounce easing on the like pop: accepted, since it is a small micro-interaction.
- Lines run 90–95 characters in feed excerpts.
- 3:1 muted text on paper.

## Questions
1. Should lists show counts at all, or keep counts for the essay page?
2. Is "Repost" right for essays?
3. Should the signed-in home be a reading queue instead of Latest?
