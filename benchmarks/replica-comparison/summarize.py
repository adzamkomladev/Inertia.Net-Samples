#!/usr/bin/env python3
"""Summarize the CarParker/CarParkerReplicas and TechLandier/TechLandierDistributed runs."""
from __future__ import annotations

import glob
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
RESULTS = ROOT / "results"
VARIANTS = ["CarParker", "CarParkerReplicas", "TechLandier", "TechLandierDistributed"]

PHASES = {
    "CarParker": ["public-rps100", "public-vus8", "auth-rps40", "auth-vus8", "public-ssr0-rps60"],
    "CarParkerReplicas": ["public-rps100", "public-vus8", "auth-rps40", "auth-vus8", "public-ssr0-rps60", "proxy-public-vus8"],
    "TechLandier": ["public-rps100", "public-vus8", "auth-rps40", "auth-vus8", "public-ssr0-rps60"],
    "TechLandierDistributed": ["public-rps100", "public-vus8", "auth-rps40", "auth-vus8", "public-ssr0-rps30"],
}


def load(path: Path) -> dict:
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def latest_result(variant: str, name: str) -> dict | None:
    files = sorted(glob.glob(str(RESULTS / variant / f"{name}-*" / "result.json")))
    if not files:
        return None
    data = load(Path(files[-1]))
    data["_path"] = files[-1]
    return data


def val(data: dict | None, path: str, default=None):
    current = data
    for part in path.split("."):
        if current is None:
            return default
        if isinstance(current, dict):
            current = current.get(part)
        else:
            current = getattr(current, part, None)
    return default if current is None else current


def f(value, digits: int = 1) -> str:
    if value is None:
        return "-"
    try:
        return f"{float(value):.{digits}f}"
    except (TypeError, ValueError):
        return str(value)


def parse_mib(value: str | None) -> float:
    if not value:
        return 0.0
    text = value.split("/")[0].strip()
    match = re.match(r"([0-9.]+)\s*([KMGT]?i?B)", text, re.I)
    if not match:
        return 0.0
    number = float(match.group(1))
    unit = match.group(2).lower()
    if unit in ("gib", "gb"):
        number *= 1024
    elif unit in ("kib", "kb"):
        number /= 1024
    elif unit in ("mib", "mb"):
        pass
    return number


def docker_memory(data: dict | None) -> float:
    entries = val(data, "docker.after", []) or []
    seen = {}
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        name = entry.get("Name")
        if name:
            seen[name] = parse_mib(entry.get("MemUsage"))
    return sum(seen.values())


def host_memory(data: dict | None) -> float:
    total = 0.0
    for role in ("app", "ssr", "aux"):
        total += float(val(data, f"process.{role}.workingSetMbAvg", 0) or 0)
    return total


def row(variant: str, phase: str) -> dict | None:
    data = latest_result(variant, phase)
    if not data:
        return None
    load_ = data.get("load", {})
    return {
        "variant": variant,
        "phase": phase,
        "auth": bool(data.get("authenticated")),
        "rps": load_.get("rps"),
        "p50": val(load_, "latencyMs.p50"),
        "p95": val(load_, "latencyMs.p95"),
        "p99": val(load_, "latencyMs.p99"),
        "requests": load_.get("requests"),
        "dropped": load_.get("droppedIterations"),
        "failed": load_.get("failed"),
        "app_rss": val(data, "process.app.workingSetMbAvg"),
        "app_cpu": val(data, "process.app.cpuPctAvg"),
        "ssr_rss": val(data, "process.ssr.workingSetMbAvg"),
        "ssr_cpu": val(data, "process.ssr.cpuPctAvg"),
        "aux_rss": val(data, "process.aux.workingSetMbAvg"),
        "aux_cpu": val(data, "process.aux.cpuPctAvg"),
        "alloc": val(data, "runtime.aggregate.allocRateMbPerSec"),
        "heap": val(data, "runtime.aggregate.heapMb"),
        "committed": val(data, "runtime.aggregate.committedMb"),
        "gc_pause": val(data, "runtime.aggregate.gcPausePct"),
        "gen0": val(data, "runtime.aggregate.gen0PerSec"),
        "gen1": val(data, "runtime.aggregate.gen1PerSec"),
        "gen2": val(data, "runtime.aggregate.gen2PerSec"),
        "ssr_cache": val(data, "runtime.aggregate.ssrCacheHitPct"),
        "page_p95": val(data, "runtime.aggregate.pageP95Ms"),
        "mongo_ops_req": val(data, "dbPerRequest.mongoOpsPerRequest"),
        "mongo_queries_req": val(data, "dbPerRequest.mongoQueriesPerRequest"),
        "pg_xacts_req": val(data, "dbPerRequest.pgXactsPerRequest"),
        "pg_tuples_fetched_req": val(data, "dbPerRequest.pgTuplesFetchedPerRequest"),
        "pg_tuples_returned_req": val(data, "dbPerRequest.pgTuplesReturnedPerRequest"),
        "pgdog_reads": val(data, "db.pgdog.totalReadsDelta"),
        "pgdog_writes": val(data, "db.pgdog.totalWritesDelta"),
        "redis_commands": val(data, "cache.redis.commandDelta"),
        "redis_hits": val(data, "cache.redis.keyspaceHitsDelta"),
        "redis_misses": val(data, "cache.redis.keyspaceMissesDelta"),
        "redis_hit": val(data, "cache.redis.hitRatePct"),
        "redis_mem": val(data, "cache.redis.usedMemoryMb"),
        "redis_keys": val(data, "cache.redis.dbSize"),
        "cache_commands": val(data, "cache.cacheRedis.commandDelta"),
        "cache_hits": val(data, "cache.cacheRedis.keyspaceHitsDelta"),
        "cache_misses": val(data, "cache.cacheRedis.keyspaceMissesDelta"),
        "cache_hit": val(data, "cache.cacheRedis.hitRatePct"),
        "cache_mem": val(data, "cache.cacheRedis.usedMemoryMb"),
        "cache_keys": val(data, "cache.cacheRedis.dbSize"),
        "pg_primary_hit": next((p.get("blockCacheHitPct") for p in val(data, "db.postgres", []) or [] if p.get("label") == "primary"), None),
        "mongo_cache_hit": val(data, "db.mongo.cacheHitPct"),
        "docker_mem": docker_memory(data),
        "host_mem": host_memory(data),
        "routes": load_.get("routes", {}),
        "path": data.get("_path"),
    }


def print_load_table(rows: list[dict], title: str) -> None:
    print(f"\n### {title}\n")
    print("| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for r in rows:
        print(
            f"| {r['variant']} | {f(r['rps'])} | {f(r['p50'])} | {f(r['p95'])} | {f(r['p99'])} | "
            f"{r['requests'] or '-'} | {r['dropped'] if r['dropped'] is not None else '-'} | {f((r['failed'] or 0)*100, 2)}% | "
            f"{f(r['app_rss'])} | {f(r['app_cpu'], 2)} | {f(r['ssr_rss'])} | {f(r['ssr_cpu'], 2)} | "
            f"{f(r['alloc'])} | {f(r['heap'])} | {f(r['gc_pause'], 2)} | {f(r['ssr_cache'])} |"
        )


def print_db_table(rows: list[dict], title: str) -> None:
    print(f"\n### {title}\n")
    print("| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for r in rows:
        req = r["requests"] or 1
        redis_cmd = (r["redis_commands"] or 0) / req if r["redis_commands"] is not None else None
        cache_cmd = (r["cache_commands"] or 0) / req if r["cache_commands"] is not None else None
        print(
            f"| {r['variant']} | {f(r['mongo_ops_req'], 2)} | {f(r['mongo_queries_req'], 2)} | {f(r['pg_xacts_req'], 3)} | "
            f"{f(r['pg_tuples_fetched_req'], 2)} | {f(r['pgdog_reads'], 0)} | {f(r['pgdog_writes'], 0)} | "
            f"{f(redis_cmd, 2)} | {f(r['redis_hit'])} | {f(cache_cmd, 2)} | {f(r['cache_hit'])} | "
            f"{f(r['pg_primary_hit'])} | {f(r['mongo_cache_hit'])} |"
        )


def print_memory_table(rows: list[dict], title: str) -> None:
    print(f"\n### {title}\n")
    print("| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: |")
    for r in rows:
        total = (r["host_mem"] or 0) + (r["docker_mem"] or 0)
        print(
            f"| {r['variant']} | {f(r['host_mem'])} | {f(r['docker_mem'])} | {f(total)} | "
            f"{f(r['redis_mem'], 2)} | {f(r['cache_mem'], 2)} | {r['cache_keys'] if r['cache_keys'] is not None else '-'} |"
        )


def print_routes(variant: str, phase: str) -> None:
    data = latest_result(variant, phase)
    if not data:
        return
    print(f"\n#### {variant} / {phase}\n")
    print("| Route | Count | p50 ms | p95 ms | p99 ms |")
    print("| --- | ---: | ---: | ---: | ---: |")
    for name, route in (data.get("load", {}).get("routes") or {}).items():
        print(f"| {name} | {route.get('count')} | {f(route.get('p50'))} | {f(route.get('p95'))} | {f(route.get('p99'))} |")


def print_profiles() -> None:
    print("\n## Memory profiles (gcdump)\n")
    for variant in VARIANTS:
        files = sorted(glob.glob(str(RESULTS / variant / "profile-loaded-*" / "profile.json")))
        if not files:
            continue
        data = load(Path(files[-1]))
        processes = data.get("processes", [])
        print(f"\n### {variant}\n")
        print("| PID | Role | Managed heap MB | Objects | Working set MB | GCDump MB |")
        print("| ---: | --- | ---: | ---: | ---: | ---: |")
        total_heap = 0.0
        total_objects = 0
        total_ws = 0.0
        for process in processes:
            heap_mb = (process.get("heapBytes") or 0) / (1024 * 1024)
            total_heap += heap_mb
            total_objects += process.get("heapObjects") or 0
            total_ws += process.get("workingSetMb") or 0
            print(
                f"| {process.get('pid')} | {process.get('role')} | {f(heap_mb, 2)} | {process.get('heapObjects')} | "
                f"{f(process.get('workingSetMb'))} | {f((process.get('gcdumpBytes') or 0)/(1024*1024))} |"
            )
        print(f"| **total** | | **{f(total_heap, 2)}** | **{total_objects}** | **{f(total_ws)}** | |")
        first = next((p for p in processes if p.get("role") == "app"), None)
        if first:
            print("\nTop retained types (first app process):\n")
            print("| Type | Bytes | Count | Assembly |")
            print("| --- | ---: | ---: | --- |")
            for item in (first.get("topTypes") or [])[:8]:
                print(f"| `{item['type']}` | {item['bytes']:,} | {item['count']:,} | {item['assembly']} |")


def main() -> None:
    by_phase = {phase: [] for phases in PHASES.values() for phase in phases}
    for variant in VARIANTS:
        for phase in PHASES[variant]:
            r = row(variant, phase)
            if r:
                by_phase.setdefault(phase, []).append(r)

    print("# Replica comparison summary\n")
    for phase, title in (
        ("public-rps100", "Public load — 100 req/s, 30 s"),
        ("public-vus8", "Public load — 8 VUs, 30 s"),
        ("auth-rps40", "Signed-in load — 40 req/s, 30 s"),
        ("auth-vus8", "Signed-in load — 8 VUs, 30 s"),
        ("public-ssr0-rps60", "Public load — SSR cache off, 60 req/s, 30 s"),
        ("public-ssr0-rps30", "Public load — SSR cache off, 30 req/s, 30 s"),
        ("proxy-public-vus8", "Proxy control — public 8 VUs through HAProxy"),
    ):
        rows = by_phase.get(phase, [])
        if rows:
            print_load_table(rows, title)
            print_db_table(rows, title)
            print_memory_table(rows, title)

    print("\n## Per-route latency\n")
    for variant in VARIANTS:
        for phase in ("public-rps100", "public-vus8", "auth-rps40", "auth-vus8"):
            if phase in PHASES[variant]:
                print_routes(variant, phase)

    print_profiles()


if __name__ == "__main__":
    main()
