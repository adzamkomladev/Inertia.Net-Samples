# Replica comparison summary


### Public load — 100 req/s, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 99.9 | 9.1 | 15.7 | 26.3 | 3007 | 0 | 0.00% | 147.1 | 7.24 | 79.4 | 0.03 | 16.0 | 18.2 | 1.03 | 100.0 |
| CarParkerReplicas | 99.8 | 13.2 | 27.9 | 45.1 | 3006 | 0 | 0.00% | 446.2 | 9.59 | 225.2 | 0.10 | 19.8 | 45.1 | 0.51 | 100.0 |
| TechLandier | 100.0 | 6.2 | 11.2 | 22.5 | 3008 | 0 | 0.00% | 182.7 | 5.46 | 117.4 | 0.06 | 7.2 | 17.5 | 0.28 | 100.0 |
| TechLandierDistributed | 99.9 | 10.5 | 36.8 | 97.9 | 3009 | 0 | 0.00% | 564.1 | 6.87 | 247.8 | 0.02 | 10.7 | 90.3 | 0.17 | 100.0 |

### Public load — 100 req/s, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 7.13 | 7.13 | - | - | - | - | - | - | - | - | - | 100.0 |
| CarParkerReplicas | 25.99 | 25.99 | - | - | - | - | 3.32 | 98.3 | - | - | - | 100.0 |
| TechLandier | - | - | 6.487 | 44.81 | - | - | - | - | - | - | 99.8 | - |
| TechLandierDistributed | - | - | 32.944 | 149.34 | 0 | 5209 | 0.19 | 50.0 | 4.62 | 99.7 | 99.9 | - |

### Public load — 100 req/s, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 226.5 | 241.4 | 467.9 | - | - | - |
| CarParkerReplicas | 671.5 | 341.5 | 1012.9 | 2.14 | - | - |
| TechLandier | 300.2 | 132.9 | 433.1 | - | - | - |
| TechLandierDistributed | 1036.8 | 330.2 | 1367.1 | 1.80 | 2.11 | 24 |

### Public load — 8 VUs, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 280.0 | 26.6 | 43.4 | 53.4 | 8426 | 0 | 0.00% | 149.2 | 27.67 | 79.5 | 0.04 | 49.6 | 18.6 | 8.86 | 100.0 |
| CarParkerReplicas | 223.3 | 33.3 | 54.9 | 69.7 | 6735 | 0 | 0.00% | 446.5 | 27.12 | 225.5 | 0.12 | 42.7 | 46.3 | 2.23 | 100.0 |
| TechLandier | 428.8 | 16.1 | 28.7 | 37.5 | 12903 | 0 | 0.00% | 188.1 | 30.41 | 117.5 | 0.07 | 30.2 | 19.2 | 3.69 | 100.0 |
| TechLandierDistributed | 227.3 | 27.8 | 51.7 | 81.1 | 6888 | 0 | 0.00% | 581.9 | 21.42 | 248.8 | 0.33 | 24.2 | 91.2 | 0.80 | 99.9 |

### Public load — 8 VUs, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 3.79 | 3.79 | - | - | - | - | - | - | - | - | - | 100.0 |
| CarParkerReplicas | 12.85 | 12.85 | - | - | - | - | 2.19 | 99.0 | - | - | - | 100.0 |
| TechLandier | - | - | 3.496 | 18.65 | - | - | - | - | - | - | 99.8 | - |
| TechLandierDistributed | - | - | 14.700 | 65.93 | 0 | 7306 | 0.09 | 50.0 | 4.03 | 99.9 | 99.9 | - |

### Public load — 8 VUs, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 228.7 | 288.2 | 516.9 | - | - | - |
| CarParkerReplicas | 672.0 | 341.6 | 1013.6 | 2.14 | - | - |
| TechLandier | 305.6 | 146.5 | 452.1 | - | - | - |
| TechLandierDistributed | 1055.7 | 341.1 | 1396.8 | 1.80 | 2.19 | 24 |

### Signed-in load — 40 req/s, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 40.0 | 16.3 | 30.6 | 48.8 | 1215 | 0 | 0.00% | 150.3 | 4.18 | 79.5 | 0.03 | 12.4 | 18.3 | 0.81 | 100.0 |
| CarParkerReplicas | 39.7 | 18.8 | 37.5 | 59.8 | 1216 | 0 | 0.00% | 456.9 | 5.77 | 225.1 | 0.07 | 13.7 | 45.1 | 0.30 | 100.0 |
| TechLandier | 40.0 | 11.2 | 23.7 | 43.7 | 1216 | 0 | 0.00% | 187.3 | 2.72 | 93.4 | 0.05 | 7.7 | 21.8 | 0.35 | 100.0 |
| TechLandierDistributed | 39.9 | 13.7 | 30.1 | 2056.1 | 1216 | 0 | 0.00% | 581.4 | 3.34 | 274.6 | 0.03 | 8.0 | 74.8 | 0.11 | 100.0 |

### Signed-in load — 40 req/s, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 30.95 | 30.95 | - | - | - | - | - | - | - | - | - | 100.0 |
| CarParkerReplicas | 75.90 | 75.90 | - | - | - | - | 13.44 | 99.1 | - | - | - | 100.0 |
| TechLandier | - | - | 42.410 | 219.23 | - | - | - | - | - | - | 99.8 | - |
| TechLandierDistributed | - | - | 85.391 | 390.33 | 0 | 9897 | 0.53 | 55.0 | 24.02 | 99.9 | 99.9 | - |

### Signed-in load — 40 req/s, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 229.9 | 310.8 | 540.7 | - | - | - |
| CarParkerReplicas | 682.0 | 342.1 | 1024.1 | 2.14 | - | - |
| TechLandier | 280.7 | 150.4 | 431.1 | - | - | - |
| TechLandierDistributed | 1081.2 | 333.4 | 1414.7 | 1.83 | 2.08 | 22 |

### Signed-in load — 8 VUs, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 165.0 | 47.5 | 78.1 | 97.7 | 5041 | 0 | 0.00% | 152.1 | 22.76 | 79.5 | 0.02 | 49.8 | 18.3 | 9.51 | 100.0 |
| CarParkerReplicas | 143.2 | 53.4 | 85.9 | 120.1 | 4376 | 0 | 0.00% | 456.3 | 24.49 | 226.0 | 0.12 | 45.9 | 48.9 | 2.77 | 100.0 |
| TechLandier | 257.5 | 28.4 | 52.6 | 70.6 | 7819 | 0 | 0.00% | 189.8 | 24.91 | 93.9 | 0.05 | 47.1 | 24.0 | 5.27 | 100.0 |
| TechLandierDistributed | 165.7 | 41.1 | 88.4 | 123.8 | 5044 | 0 | 0.00% | 567.7 | 18.29 | 274.3 | 0.01 | 31.7 | 75.8 | 1.35 | 100.0 |

### Signed-in load — 8 VUs, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 12.17 | 12.17 | - | - | - | - | - | - | - | - | - | 100.0 |
| CarParkerReplicas | 25.73 | 25.73 | - | - | - | - | 5.00 | 99.4 | - | - | - | 100.0 |
| TechLandier | - | - | 11.375 | 50.32 | - | - | - | - | - | - | 99.9 | - |
| TechLandierDistributed | - | - | 22.676 | 112.95 | 0 | 20472 | 0.14 | 58.3 | 6.94 | 99.9 | 100.0 | - |

### Signed-in load — 8 VUs, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 231.6 | 248.1 | 479.7 | - | - | - |
| CarParkerReplicas | 682.3 | 349.9 | 1032.2 | 2.14 | - | - |
| TechLandier | 283.6 | 156.8 | 440.4 | - | - | - |
| TechLandierDistributed | 1068.2 | 356.6 | 1424.8 | 1.83 | 2.06 | 18 |

### Public load — SSR cache off, 60 req/s, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 58.7 | 14.5 | 24.4 | 35.6 | 1807 | 0 | 0.00% | 131.4 | 8.73 | 112.4 | 3.66 | 10.3 | 17.4 | 0.59 | 0.0 |
| CarParkerReplicas | 56.8 | 17.5 | 43.3 | 61.0 | 1806 | 0 | 0.00% | 412.9 | 17.02 | 286.6 | 5.97 | 11.5 | 45.0 | 0.29 | 0.0 |
| TechLandier | 54.4 | 28.3 | 76.5 | 128.6 | 1809 | 0 | 0.00% | 159.4 | 7.81 | 129.9 | 9.10 | 4.5 | 17.3 | 0.28 | 0.0 |

### Public load — SSR cache off, 60 req/s, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 35.18 | 35.18 | - | - | - | - | - | - | - | - | - | 100.0 |
| CarParkerReplicas | 65.98 | 65.98 | - | - | - | - | 13.71 | 99.4 | - | - | - | 100.0 |
| TechLandier | - | - | 51.762 | 230.10 | - | - | - | - | - | - | 99.9 | - |

### Public load — SSR cache off, 60 req/s, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 243.8 | 249.2 | 493.0 | - | - | - |
| CarParkerReplicas | 699.5 | 343.8 | 1043.3 | 1.87 | - | - |
| TechLandier | 289.2 | 131.2 | 420.4 | - | - | - |

### Public load — SSR cache off, 30 req/s, 30 s

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| TechLandierDistributed | 29.2 | 25.8 | 80.3 | 161.9 | 909 | 0 | 0.00% | 514.3 | 7.68 | 287.0 | 7.07 | 3.1 | 59.4 | 0.08 | 0.0 |

### Public load — SSR cache off, 30 req/s, 30 s

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| TechLandierDistributed | - | - | 128.809 | 638.33 | 0 | 22620 | 1.54 | 52.8 | 40.96 | 99.9 | 100.0 | - |

### Public load — SSR cache off, 30 req/s, 30 s

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| TechLandierDistributed | 1025.9 | 330.4 | 1356.2 | 1.83 | 1.86 | 9 |

### Proxy control — public 8 VUs through HAProxy

| Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed | App RSS MB | App CPU % | SSR RSS MB | SSR CPU % | Alloc MB/s | Heap MB | GC pause % | SSR cache % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParkerReplicas | 112.9 | 65.9 | 115.1 | 153.5 | 3435 | 0 | 0.00% | 451.9 | 14.52 | 172.0 | 0.12 | 22.4 | 44.2 | 1.09 | 99.9 |

### Proxy control — public 8 VUs through HAProxy

| Variant | Mongo ops/req | Mongo finds/req | PG xacts/req | PG tuples fetched/req | PgDog reads | PgDog writes | Redis cmd/req | Redis hit % | Cache cmd/req | Cache hit % | PG block hit % | Mongo WT hit % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParkerReplicas | 34.03 | 34.03 | - | - | - | - | 7.12 | 99.4 | - | - | - | 100.0 |

### Proxy control — public 8 VUs through HAProxy

| Variant | Host app+SSR+aux MB | Docker DB/cache/proxy MB | Total system MB | Redis mem MB | Cache Redis mem MB | Cache Redis keys |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParkerReplicas | 623.9 | 347.2 | 971.0 | 2.05 | - | - |

## Per-route latency


#### CarParker / public-rps100

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 503 | 10.5 | 17.4 | 27.4 |
| lots_html | 871 | 10.7 | 17.9 | 33.8 |
| lot_html | 756 | 6.1 | 10.1 | 19.8 |
| lots_json | 366 | 7.8 | 12.5 | 22.6 |
| lots_partial | 505 | 7.8 | 13.4 | 20.4 |

#### CarParker / public-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 1413 | 31.2 | 47.7 | 55.5 |
| lots_html | 2481 | 30.9 | 46.5 | 59.2 |
| lot_html | 2068 | 18.8 | 31.1 | 38.6 |
| lots_json | 1044 | 25.2 | 40.1 | 53.8 |
| lots_partial | 1414 | 24.7 | 39.6 | 51.1 |

#### CarParker / auth-rps40

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 229 | 20.3 | 30.0 | 48.3 |
| account_bookings_html | 122 | 16.5 | 25.7 | 48.9 |
| lot_html | 159 | 8.2 | 21.9 | 28.7 |
| lots_html | 136 | 12.9 | 20.6 | 43.0 |
| guard_html | 109 | 14.3 | 34.2 | 41.3 |
| admin_html | 139 | 17.0 | 33.5 | 47.4 |
| admin_analytics_json | 116 | 4.8 | 11.6 | 15.1 |
| admin_bookings_html | 122 | 20.4 | 36.4 | 49.5 |
| account_json | 68 | 17.1 | 39.6 | 49.2 |

#### CarParker / auth-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 1000 | 63.9 | 90.8 | 111.9 |
| account_bookings_html | 509 | 50.5 | 70.8 | 81.3 |
| lot_html | 759 | 25.0 | 38.5 | 48.2 |
| lots_html | 496 | 37.8 | 53.0 | 70.3 |
| guard_html | 532 | 44.7 | 62.4 | 75.6 |
| admin_html | 492 | 48.9 | 68.7 | 79.3 |
| admin_analytics_json | 489 | 16.2 | 29.9 | 42.0 |
| admin_bookings_html | 508 | 61.8 | 86.0 | 97.7 |
| account_json | 241 | 52.8 | 79.5 | 91.6 |

#### CarParkerReplicas / public-rps100

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 505 | 15.9 | 31.5 | 48.2 |
| lots_html | 910 | 15.7 | 31.4 | 49.5 |
| lot_html | 708 | 10.0 | 20.1 | 34.3 |
| lots_json | 359 | 9.5 | 22.9 | 38.8 |
| lots_partial | 518 | 9.5 | 20.6 | 42.8 |

#### CarParkerReplicas / public-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 1127 | 39.6 | 60.7 | 75.4 |
| lots_html | 1958 | 40.1 | 59.4 | 74.4 |
| lot_html | 1683 | 27.1 | 44.4 | 58.7 |
| lots_json | 825 | 26.8 | 43.1 | 58.7 |
| lots_partial | 1136 | 26.6 | 42.2 | 55.1 |

#### CarParkerReplicas / auth-rps40

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 231 | 22.2 | 40.7 | 59.0 |
| account_bookings_html | 126 | 18.9 | 37.8 | 49.3 |
| lot_html | 195 | 11.8 | 33.3 | 43.9 |
| lots_html | 109 | 15.9 | 29.1 | 44.7 |
| guard_html | 115 | 16.8 | 36.5 | 46.3 |
| admin_html | 127 | 19.1 | 36.7 | 44.4 |
| admin_analytics_json | 105 | 6.1 | 8.9 | 15.0 |
| admin_bookings_html | 121 | 22.7 | 60.6 | 71.7 |
| account_json | 72 | 18.0 | 29.6 | 36.8 |

#### CarParkerReplicas / auth-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 845 | 69.4 | 103.5 | 144.1 |
| account_bookings_html | 455 | 56.1 | 79.5 | 99.2 |
| lot_html | 667 | 36.5 | 53.1 | 62.2 |
| lots_html | 445 | 48.8 | 72.1 | 93.2 |
| guard_html | 423 | 49.6 | 73.1 | 108.2 |
| admin_html | 473 | 54.7 | 78.2 | 103.6 |
| admin_analytics_json | 442 | 19.9 | 34.6 | 46.9 |
| admin_bookings_html | 413 | 66.9 | 90.3 | 119.9 |
| account_json | 198 | 58.6 | 97.4 | 136.4 |

#### TechLandier / public-rps100

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 617 | 5.8 | 11.2 | 22.3 |
| products_html | 640 | 7.1 | 15.1 | 27.2 |
| product_html | 748 | 7.2 | 10.5 | 23.2 |
| home_json | 243 | 2.7 | 3.9 | 9.3 |
| products_json | 257 | 4.0 | 9.3 | 14.7 |
| products_partial | 495 | 4.0 | 8.2 | 17.4 |

#### TechLandier / public-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 2752 | 16.5 | 27.4 | 36.4 |
| products_html | 2643 | 18.4 | 31.4 | 41.3 |
| product_html | 3234 | 18.5 | 30.8 | 39.3 |
| home_json | 1080 | 9.8 | 21.2 | 31.1 |
| products_json | 1056 | 12.4 | 23.6 | 30.3 |
| products_partial | 2130 | 12.5 | 23.4 | 31.6 |

#### TechLandier / auth-rps40

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 268 | 10.9 | 26.0 | 43.7 |
| account_orders_html | 107 | 13.6 | 23.5 | 52.8 |
| product_html | 173 | 9.9 | 18.6 | 34.5 |
| buy_html | 133 | 14.0 | 23.6 | 64.9 |
| admin_html | 105 | 7.2 | 17.1 | 41.2 |
| admin_analytics_json | 95 | 3.5 | 6.8 | 11.6 |
| admin_orders_html | 115 | 11.6 | 30.7 | 41.8 |
| admin_catalog_html | 124 | 15.3 | 35.4 | 41.3 |
| account_json | 81 | 7.1 | 15.7 | 23.3 |

#### TechLandier / auth-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 1561 | 28.9 | 47.0 | 66.9 |
| account_orders_html | 801 | 39.7 | 59.0 | 78.1 |
| product_html | 1176 | 24.9 | 40.1 | 54.5 |
| buy_html | 764 | 37.2 | 61.8 | 90.4 |
| admin_html | 780 | 18.0 | 31.4 | 41.8 |
| admin_analytics_json | 789 | 8.9 | 18.4 | 26.0 |
| admin_orders_html | 765 | 30.5 | 49.7 | 65.4 |
| admin_catalog_html | 794 | 40.4 | 64.3 | 85.6 |
| account_json | 374 | 19.7 | 34.3 | 47.1 |

#### TechLandierDistributed / public-rps100

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 635 | 11.4 | 44.6 | 87.1 |
| products_html | 638 | 12.8 | 40.6 | 147.2 |
| product_html | 717 | 10.7 | 33.9 | 86.7 |
| home_json | 248 | 5.2 | 22.5 | 83.1 |
| products_json | 253 | 6.3 | 21.9 | 68.0 |
| products_partial | 510 | 6.6 | 29.1 | 84.9 |

#### TechLandierDistributed / public-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| home_html | 1420 | 32.4 | 60.4 | 87.0 |
| products_html | 1488 | 34.2 | 60.6 | 97.0 |
| product_html | 1734 | 27.8 | 47.0 | 68.7 |
| home_json | 549 | 16.6 | 34.3 | 51.2 |
| products_json | 564 | 19.5 | 36.7 | 58.3 |
| products_partial | 1125 | 19.1 | 37.6 | 68.5 |

#### TechLandierDistributed / auth-rps40

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 247 | 12.0 | 34.6 | 2071.0 |
| account_orders_html | 115 | 16.3 | 36.6 | 56.1 |
| product_html | 191 | 14.1 | 24.8 | 47.2 |
| buy_html | 121 | 18.0 | 22.8 | 45.3 |
| admin_html | 123 | 9.6 | 23.2 | 38.0 |
| admin_analytics_json | 118 | 5.3 | 8.4 | 17.1 |
| admin_orders_html | 124 | 13.9 | 29.8 | 42.6 |
| admin_catalog_html | 110 | 18.3 | 35.1 | 49.0 |
| account_json | 52 | 7.8 | 2054.0 | 2060.3 |

#### TechLandierDistributed / auth-vus8

| Route | Count | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| account_html | 970 | 36.4 | 73.9 | 103.0 |
| account_orders_html | 565 | 53.4 | 94.8 | 117.1 |
| product_html | 754 | 42.3 | 81.8 | 119.1 |
| buy_html | 512 | 55.8 | 113.3 | 151.0 |
| admin_html | 515 | 26.9 | 61.7 | 82.4 |
| admin_analytics_json | 493 | 17.3 | 40.7 | 73.3 |
| admin_orders_html | 502 | 43.5 | 87.0 | 114.8 |
| admin_catalog_html | 483 | 57.2 | 102.8 | 135.0 |
| account_json | 235 | 24.5 | 54.0 | 80.4 |

## Memory profiles (gcdump)


### CarParker

| PID | Role | Managed heap MB | Objects | Working set MB | GCDump MB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 21020 | app | 8.24 | 106537 | 143.4 | 3.3 |
| **total** | | **8.24** | **106537** | **143.4** | |

Top retained types (first app process):

| Type | Bytes | Count | Assembly |
| --- | ---: | ---: | --- |
| `System.Int32[] (Bytes > 10K)` | 98,352 | 1 | System.Private.CoreLib.dll |
| `System.String (Bytes > 10K)` | 67,954 | 1 | System.Private.CoreLib.dll |
| `Entry<System.Runtime.CompilerServices.VirtualDispatchHelpers+VirtualResolutionData,System.IntPtr>[] (Bytes > 10K)` | 49,224 | 1 | System.Private.CoreLib.dll |
| `VolatileNode<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceCacheKey,Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceCallSite>[] (Bytes > 10K)` | 32,416 | 1 | System.Collections.Concurrent.dll |
| `Entry<System.Int32,System.Diagnostics.Tracing.EventSource+EventMetadata>[] (Bytes > 10K)` | 22,088 | 1 | System.Private.CoreLib.dll |
| `Entry<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceIdentifier,Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory+ServiceDescriptorCacheItem>[] (Bytes > 10K)` | 17,264 | 1 | System.Private.CoreLib.dll |
| `System.Byte[] (Bytes > 10K)` | 16,845 | 25 | System.Private.CoreLib.dll |
| `System.Char[] (Bytes > 10K)` | 16,408 | 6 | System.Private.CoreLib.dll |

### CarParkerReplicas

| PID | Role | Managed heap MB | Objects | Working set MB | GCDump MB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 21160 | app | 8.65 | 108579 | 149.7 | 3.3 |
| 2400 | app | 8.50 | 108573 | 150.5 | 3.3 |
| 9148 | app | 8.12 | 106813 | 147.4 | 3.3 |
| **total** | | **25.27** | **323965** | **447.5** | |

Top retained types (first app process):

| Type | Bytes | Count | Assembly |
| --- | ---: | ---: | --- |
| `StackExchange.Redis.RawResult[] (Bytes > 100K)` | 229,400 | 4 | StackExchange.Redis.dll |
| `System.Int32[] (Bytes > 10K)` | 98,352 | 1 | System.Private.CoreLib.dll |
| `System.String (Bytes > 10K)` | 70,260 | 1 | System.Private.CoreLib.dll |
| `Entry<System.Runtime.CompilerServices.VirtualDispatchHelpers+VirtualResolutionData,System.IntPtr>[] (Bytes > 10K)` | 49,224 | 1 | System.Private.CoreLib.dll |
| `Entry<System.Int32,System.Diagnostics.Tracing.EventSource+EventMetadata>[] (Bytes > 10K)` | 22,088 | 1 | System.Private.CoreLib.dll |
| `Entry<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceIdentifier,Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory+ServiceDescriptorCacheItem>[] (Bytes > 10K)` | 17,264 | 1 | System.Private.CoreLib.dll |
| `System.Byte[] (Bytes > 10K)` | 16,845 | 14 | System.Private.CoreLib.dll |
| `System.Char[] (Bytes > 10K)` | 16,408 | 5 | System.Private.CoreLib.dll |

### TechLandier

| PID | Role | Managed heap MB | Objects | Working set MB | GCDump MB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 18584 | app | 14.98 | 230163 | 189.0 | 6.0 |
| **total** | | **14.98** | **230163** | **189.0** | |

Top retained types (first app process):

| Type | Bytes | Count | Assembly |
| --- | ---: | ---: | --- |
| `Entry<System.Runtime.CompilerServices.VirtualDispatchHelpers+VirtualResolutionData,System.IntPtr>[] (Bytes > 10K)` | 98,376 | 1 | System.Private.CoreLib.dll |
| `System.Int32[] (Bytes > 10K)` | 98,352 | 1 | System.Private.CoreLib.dll |
| `System.String (Bytes > 10K)` | 68,378 | 1 | System.Private.CoreLib.dll |
| `VolatileNode<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceCacheKey,Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceCallSite>[] (Bytes > 10K)` | 32,416 | 1 | System.Collections.Concurrent.dll |
| `Entry<System.Int32,System.Diagnostics.Tracing.EventSource+EventMetadata>[] (Bytes > 10K)` | 22,088 | 1 | System.Private.CoreLib.dll |
| `Entry<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceIdentifier,Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory+ServiceDescriptorCacheItem>[] (Bytes > 10K)` | 17,264 | 1 | System.Private.CoreLib.dll |
| `System.Byte[] (Bytes > 10K)` | 16,845 | 20 | System.Private.CoreLib.dll |
| `System.Char[] (Bytes > 10K)` | 16,408 | 8 | System.Private.CoreLib.dll |

### TechLandierDistributed

| PID | Role | Managed heap MB | Objects | Working set MB | GCDump MB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 22444 | app | 15.91 | 232345 | 185.0 | 6.1 |
| 3500 | app | 15.83 | 230997 | 187.6 | 6.0 |
| 20888 | app | 15.41 | 225557 | 191.1 | 5.9 |
| 3124 | aux | 6.03 | 78698 | 110.4 | 2.4 |
| 23620 | aux | 7.00 | 91737 | 116.6 | 2.8 |
| **total** | | **60.18** | **859334** | **790.7** | |

Top retained types (first app process):

| Type | Bytes | Count | Assembly |
| --- | ---: | ---: | --- |
| `StackExchange.Redis.RawResult[] (Bytes > 100K)` | 229,400 | 4 | StackExchange.Redis.dll |
| `Entry<System.Runtime.CompilerServices.VirtualDispatchHelpers+VirtualResolutionData,System.IntPtr>[] (Bytes > 10K)` | 98,376 | 1 | System.Private.CoreLib.dll |
| `System.Int32[] (Bytes > 10K)` | 98,352 | 1 | System.Private.CoreLib.dll |
| `System.String (Bytes > 10K)` | 71,790 | 1 | System.Private.CoreLib.dll |
| `Entry<System.Int32,System.Diagnostics.Tracing.EventSource+EventMetadata>[] (Bytes > 10K)` | 22,088 | 1 | System.Private.CoreLib.dll |
| `Entry<Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceIdentifier,Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory+ServiceDescriptorCacheItem>[] (Bytes > 10K)` | 17,264 | 1 | System.Private.CoreLib.dll |
| `System.Byte[] (Bytes > 10K)` | 16,845 | 12 | System.Private.CoreLib.dll |
| `System.Char[] (Bytes > 10K)` | 16,408 | 4 | System.Private.CoreLib.dll |
