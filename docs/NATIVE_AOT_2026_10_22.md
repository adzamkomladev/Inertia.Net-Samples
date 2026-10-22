# Native AOT experiment: Inkwell, CarParker and TechLandier

Test date: 2026-10-22. Source: commit `e2a93ef` on `main`.

This experiment publishes the full applications without removing features or changing their database and object-storage providers. A publish must succeed, the native executable must start, and real Inertia responses must pass correctness checks before request latency or application memory measurements are meaningful.

## Environment and reproduction

- Windows host: Intel Core i5-8265U, 4 cores / 8 logical processors.
- Docker Desktop Linux x64: 8 CPUs, 8,268,738,560 bytes available memory.
- Build image: `mcr.microsoft.com/dotnet/sdk:10.0`, SDK `10.0.402`, Ubuntu Noble, Clang `18.1.3`, `zlib1g-dev`.
- Native runtime/compiler: `10.0.12`, explicitly pinned to match the locally cached Linux toolchain. This is not a test of every .NET patch or platform.
- Clean tracked source exported with `git archive`, excluding local `.env`, dependencies, build outputs and data. No changes to application source for the experiment.
- Disposable MongoDB 8 single-node replica set, PostgreSQL 18 and RustFS 1.0.1, reachable on a private Docker network. SQLite uses a disposable database file.
- Production environment; `CRON_ENABLED=false` and `JOBS_ENABLED=false` to isolate web serving. S3 endpoint and credentials point to the disposable RustFS instance. SSR is disabled through `Inertia__Ssr=false` where the sample supports that setting; no frontend/SSR bundles are included in the tracked source export.

Install the compiler prerequisites inside the disposable SDK container:

```sh
apt-get update
apt-get install -y --no-install-recommends clang zlib1g-dev
```

From the exported `samples` directory, run each publish independently:

```sh
for sample in Inkwell CarParker TechLandier; do
  dotnet publish "$sample/src/$sample/$sample.csproj" \
    -c Release -r linux-x64 \
    -p:PublishAot=true -p:RuntimeFrameworkVersion=10.0.12 \
    -o "/out/$sample" > "$sample.publish.log" 2>&1
done
```

The local run additionally used `-p:RestoreFallbackFolders=/packages` with the host NuGet package cache mounted read-only at `/packages`. That changes package lookup, not application source. The default Ubuntu download server stalled; the successful prerequisite installation used the kernel.org Ubuntu mirror. Missing compiler prerequisites from preliminary attempts are not application compatibility findings.

## Results

| Sample | Native publish | Full application startup | p50 / p95 | Serving memory / CPU profile |
|---|---|---|---|---|
| Inkwell | Succeeded | Aborted, exit 134: EF requires a compiled model | Unavailable | Unavailable |
| CarParker | Succeeded | Aborted, exit 134: MongoDB enum serializer lacks native code/metadata | Unavailable | Unavailable |
| TechLandier | Failed: 21 generated `CS8639` errors | No executable | Unavailable | Unavailable |

### Inkwell

The native publish completed in 203 seconds in this local experiment. The executable then failed in `Startup.InitializeAsync`, while resolving the EF context for migrations:

```text
System.InvalidOperationException: Model building is not supported when publishing with NativeAOT. Use a compiled model.
at Microsoft.EntityFrameworkCore.Internal.DbContextServices.CreateModel(Boolean)
at Inkwell.Infrastructure.Startup.<InitializeAsync>d__0.MoveNext()
```

This failure precedes HTTP serving and object-storage initialization. A compiled EF model is the first observed blocker, not proof that resolving it will make the remaining application AOT-compatible. Publication also reports dynamic-code/trim warnings in queries, serialization and dependencies.

The stripped native executable is 52,390,832 bytes (49.96 MiB); its separate debug symbols are 136,088,696 bytes (129.78 MiB). Binary size is not process memory usage.

### CarParker

The native publish completed in 187 seconds. Startup then aborted while creating MongoDB document serializers:

```text
System.NotSupportedException: 'MongoDB.Bson.Serialization.Serializers.EnumSerializer`1[CarParker.Data.UserRole]' is missing native code or metadata.
at MongoDB.Bson.Serialization.BsonSerializationProviderBase.CreateSerializer(...)
at MongoDB.Driver.MongoDatabase.GetCollection[TDocument](...)
```

This is the first observed runtime blocker, not an exhaustive assessment of MongoDB driver compatibility. Explicitly preserving/registering serializers may address this particular failure, but document maps, LINQ translation, JSON props, authentication and jobs would still require validation. No listener was available for the attempted `/login` Inertia request.

### TechLandier

The publish fails while compiling ASP.NET RequestDelegateGenerator output, with 21 instances of:

```text
error CS8639: The typeof operator cannot be used on a nullable reference type
```

The diagnostics point into `GeneratedRouteBuilderExtensions.g.cs` (lines 9490 onward for this build). No native executable is produced. Fixing that generated-delegate compatibility problem is the first gate; EF compiled models/queries and application JSON metadata still need their own validation afterward.

### Interpretation

An unavailable measurement is not zero latency or zero memory. Neither a crash nor a compiler error supplies an application performance profile. These results establish compatibility failures for this source/toolchain combination; they do not establish whether Native AOT is faster or smaller for the working samples.

## What a subsequent performance run must measure

After resolving compatibility failures, verify anonymous Inertia JSON responses on `/posts`, `/lots` and `/products`, plus a representative authenticated mutation and its queued job. Check HTTP status, component, props and database effects before accepting timing samples. Rebuild frontend and SSR bundles separately for a full HTML/SSR comparison; those processes have their own CPU and memory costs.

Use a fixed documented dataset, warm-up, workload and concurrency. Report p50/p95 of successful requests alongside throughput and error rate. Sample process RSS/PSS and peak RSS during idle and load; distinguish application memory from database, object storage, SSR, load generator and compiler memory. Run CPU tracing separately from the latency run to avoid presenting profiling overhead as ordinary performance.

Do not substitute health-check timings, crash memory, compiler memory or an adapter-only microbenchmark for full-application results.

## References

- [Inertia.Net Native AOT guidance](https://github.com/adzamkomladev/Inertia.Net#native-aot): applications need source-generated JSON metadata for their own prop types.
- [EF Core Native AOT and precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries): experimental support requires explicit model/query precompilation; dynamic query composition has restrictions.
- [.NET Native AOT diagnostics](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/diagnostics): profiling support differs from the full runtime; EventPipe requires the appropriate build configuration.
