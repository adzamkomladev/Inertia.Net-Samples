#!/usr/bin/env python3
"""Summarize the page probe and k6 runs produced by benchmarks/page-probe.ps1 and run.ps1.

Usage:
  python benchmarks/analyze-results.py page [--app inkwell] [--before 1.5.1] [--after 1.5.1-opt]
  python benchmarks/analyze-results.py load [--app inkwell] [--before 1.5.1] [--after 1.5.1-opt]
  python benchmarks/analyze-results.py props [--app inkwell]

The page probe is a single sequential client per page, so its rps field is not a load-test result.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent
RESULTS = ROOT / "results"
APPS = ("inkwell", "carparker", "techlandier")


def read_json(path: str | Path) -> dict:
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def latest_page_probe(app: str, version: str) -> dict | None:
    files = sorted(glob.glob(str(RESULTS / f"{app}-{version}-page-probe-*.json")))
    candidates = []
    for path in files:
        try:
            data = read_json(path)
        except Exception:
            continue
        if data.get("kind") != "page-probe":
            continue
        # Full probes have all pages; ignore the short smoke runs used during harness development.
        if data.get("requestsPerMode", 0) >= 20 and len(data.get("pages", [])) >= (20 if app == "inkwell" else 30):
            candidates.append((data.get("measuredAt", ""), path, data))
    if not candidates:
        return None
    _, path, data = max(candidates, key=lambda item: item[0])
    data["_path"] = path
    return data


def page_rows(data: dict) -> dict[tuple[str, str], dict]:
    rows = {}
    for page in data.get("pages", []):
        for mode in page.get("modes", []):
            rows[(page["name"], mode["mode"])] = {**mode, "page": page}
    return rows


def fmt(value, digits: int = 2) -> str:
    if value is None:
        return "-"
    if isinstance(value, float):
        return f"{value:.{digits}f}"
    return str(value)


def report_page(app: str, before: str, after: str) -> None:
    b = latest_page_probe(app, before)
    a = latest_page_probe(app, after)
    print(f"\n## {app}: {before} -> {after}")
    if not b or not a:
        print(f"missing page probe ({'before ' + str(bool(b))}, {'after ' + str(bool(a))})")
        return
    print(f"before: {b['_path']} ({len(b['pages'])} pages)")
    print(f"after:  {a['_path']} ({len(a['pages'])} pages)")
    br, ar = page_rows(b), page_rows(a)
    print("\n| Page | Mode | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after | ops before | ops after |")
    print("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for key in sorted(set(br) & set(ar)):
        x, y = br[key], ar[key]
        print(
            f"| {key[0]} | {key[1]} | {fmt(x['p50Ms'])} | {fmt(y['p50Ms'])} | "
            f"{fmt(x['p95Ms'])} | {fmt(y['p95Ms'])} | {fmt(x['p99Ms'])} | {fmt(y['p99Ms'])} | "
            f"{fmt(x['dbOpsPerRequest'])} | {fmt(y['dbOpsPerRequest'])} |"
        )
    only_after = sorted(set(ar) - set(br))
    if only_after:
        print("\nNew modes only measured after the change: " + ", ".join(f"{k[0]}/{k[1]}" for k in only_after))


def load_group(app: str, version: str, label_prefix: str, auth: bool | None = None) -> list[dict]:
    files = sorted(glob.glob(str(RESULTS / f"{app}-{version}-{label_prefix}-*.json")))
    runs = []
    for path in files:
        try:
            data = read_json(path)
        except Exception:
            continue
        if data.get("kind") != "load" and "load" not in data:
            continue
        if auth is not None and bool(data.get("authenticated", False)) != auth:
            continue
        runs.append(data)
    return runs


def median(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def load_summary(runs: list[dict]) -> dict:
    if not runs:
        return {}
    return {
        "rounds": len(runs),
        "rps": median([r["load"]["rps"] for r in runs]),
        "p50": median([r["load"]["latencyMs"]["p50"] for r in runs]),
        "p95": median([r["load"]["latencyMs"]["p95"] for r in runs]),
        "p99": median([r["load"]["latencyMs"]["p99"] for r in runs]),
        "appRss": median([r["process"]["app"]["workingSetMb"]["avg"] for r in runs if r.get("process", {}).get("app")]),
        "appCpu": median([r["process"]["app"]["cpuPctAvg"] for r in runs if r.get("process", {}).get("app")]),
        "ssrRss": median([r["process"]["ssr"]["workingSetMb"]["avg"] for r in runs if r.get("process", {}).get("ssr")]),
        "ssrCpu": median([r["process"]["ssr"]["cpuPctAvg"] for r in runs if r.get("process", {}).get("ssr")]),
        "alloc": median([r.get("runtime", {}).get("allocRateMbPerSecAvg") for r in runs]),
        "gc": median([r.get("runtime", {}).get("timeInGcPctAvg") for r in runs]),
        "ssrCache": median([r.get("runtime", {}).get("ssrCacheHitPct") for r in runs]),
    }


def report_load(app: str, before: str, after: str) -> None:
    print(f"\n## {app}: load")
    for label_prefix, auth in (("opt-rps", False), ("opt-vus", False), ("opt-auth-rps", True), ("opt-auth-vus", True), ("opt-ssr0", False)):
        b_runs = load_group(app, before, label_prefix, auth)
        a_runs = load_group(app, after, label_prefix, auth)
        b = load_summary(b_runs)
        a = load_summary(a_runs)
        if not b and not a:
            continue
        print(f"\n### {label_prefix} ({'auth' if auth else 'anonymous'})")
        print("| Version | Rounds | RPS | p50 | p95 | p99 | App RSS | App CPU % | SSR RSS | SSR CPU % | Alloc MB/s | GC % | SSR cache % |")
        print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
        for version, s in ((before, b), (after, a)):
            if not s:
                continue
            print(
                f"| {version} | {s['rounds']} | {fmt(s['rps'])} | {fmt(s['p50'])} | {fmt(s['p95'])} | {fmt(s['p99'])} | "
                f"{fmt(s['appRss'], 1)} | {fmt(s['appCpu'], 1)} | {fmt(s['ssrRss'], 1)} | {fmt(s['ssrCpu'], 2)} | "
                f"{fmt(s['alloc'], 1)} | {fmt(s['gc'], 2)} | {fmt(s['ssrCache'], 1)} |"
            )
        # Per-route medians for the low-load run, where the mix is comparable.
        if label_prefix == "opt-rps" and b_runs and a_runs:
            print("\nPer-route medians at 150 req/s:\n")
            print("| Route | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after |")
            print("| --- | ---: | ---: | ---: | ---: | ---: | ---: |")
            routes = sorted(set(b_runs[0]["load"].get("routes", {})) | set(a_runs[0]["load"].get("routes", {})))
            for route in routes:
                vals = []
                for runs in (b_runs, a_runs):
                    points = [r["load"]["routes"].get(route) for r in runs]
                    points = [p for p in points if p]
                    vals.append(tuple(median([p[k] for p in points]) for k in ("p50", "p95", "p99")))
                print(
                    f"| {route} | {fmt(vals[0][0])} | {fmt(vals[1][0])} | "
                    f"{fmt(vals[0][1])} | {fmt(vals[1][1])} | "
                    f"{fmt(vals[0][2])} | {fmt(vals[1][2])} |"
                )


def report_props(app: str) -> None:
    data = latest_page_probe(app, "1.5.1-opt")
    if not data:
        print(f"no after page probe for {app}")
        return
    print(f"\n## {app}: page props")
    print("| Page | Component | Shared props | Deferred groups | Props |")
    print("| --- | --- | --- | --- | --- |")
    for page in data["pages"]:
        if page["status"] != 200:
            continue
        shared = ", ".join(page.get("sharedProps") or [])
        deferred = page.get("deferredProps") or {}
        deferred_text = "; ".join(f"{group}: {', '.join(props)}" for group, props in deferred.items())
        props = ", ".join(p["name"] for p in page.get("props", []))
        print(f"| {page['name']} | {page.get('component') or '-'} | {shared or '-'} | {deferred_text or '-'} | {props or '-'} |")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("page", "load", "props", "all"))
    parser.add_argument("--app", choices=APPS)
    parser.add_argument("--before", default="1.5.1")
    parser.add_argument("--after", default="1.5.1-opt")
    args = parser.parse_args()
    apps = (args.app,) if args.app else APPS
    for app in apps:
        if args.command in ("page", "all"):
            report_page(app, args.before, args.after)
        if args.command in ("load", "all"):
            report_load(app, args.before, args.after)
        if args.command in ("props", "all"):
            report_props(app)


if __name__ == "__main__":
    main()
