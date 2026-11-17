---
target: Inkwell writer and staff surfaces
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:C:\\Users\\adzam\\Documents\\Work\\dotnet-inertia-samples\\samples\\Inkwell\\src\\Inkwell\\ClientApp\\pages"
timestamp: 2026-11-16T23-43-53Z
slug: src-inkwell-clientapp-pages
---
# Inkwell critique round 2: writer and staff surfaces

Method: dual-agent (A: design review · B: detector + browser evidence)

Score 25/40 (Acceptable). Detector: 0 static findings; overlay found bounce-easing (--animate-pop) and a straggler in the bio hint.

Priority issues and outcome:
- [P1] Composer/edit had no h1 and a ~1.8:1 title placeholder: fixed (sr-only h1, placeholder /70).
- [P1] Analytics framed tiny numbers as crashes: fixed (baselines, absolute change under 10, bar chart filled to today, zero rows folded).
- [P1] Team flow contradicted its copy: fixed (promote existing verified writer; new account secondary).
- [P2] Touch targets under 44px on phone: fixed for buttons, inputs, tabs, menu items, switches, filters, logo, analytics links.
- [P2] Truncation on phone (time zone trigger, "You" chip): fixed.
Left: bell panel stays a full-height sheet; 8-item account menu not yet grouped.
