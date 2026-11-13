# Week 45 – Green architecture tactics

Happy Headlines was improved with three tactics from Bærbak's Green Architecture Framework (GAF), none of which had
been used in the project before:

| # | GAF tactic | What it targets in Happy Headlines |
|---|------------|------------------------------------|
| 1 | **Reduce Bundle Size** | The Docker images of the nine .NET services: stored in GHCR, pulled on every deploy |
| 2 | **Reduce Logging** | Log events, trace spans, metric points and health-check traffic produced by every request |
| 3 | **Set Configuration Parameters Optimally** | The .NET garbage collector, configured for one big server but running as 11 small services on one host |

Each tactic is a separate commit on the `week-45` branch, so the before/after diff of each one can be inspected on its own.

## How we measured ("Measure and Experiment")

A laptop cannot measure joules per service, so we measured stand-ins that energy use scales with: bytes stored and
transferred, CPU time, memory held, and the volume of telemetry that has to be sent, processed and stored.

- `scripts/load.js` is a fixed [k6](https://k6.io) workload: 20 reader visits per second for 60 s (front page plus
  an article), a comment on every 5th visit and a published article on every 50th. Same workload for every run.
- `scripts/measure.ps1 -Label <name>` recreates the .NET services, waits 60 s, then records:
  image sizes (`docker image inspect`), idle and under-load CPU and memory (`docker stats`),
  log lines written during the load (.NET services and infrastructure containers separately),
  trace spans and metric points received by the OpenTelemetry Collector (its own `otelcol_receiver_accepted_*`
  counters in Prometheus) and k6's response times. Results are in [`results/`](results/).
- The baseline was measured twice to know the noise: CPU, spans and metric points vary about **±10 %** between
  identical runs; memory and log counts vary about **±1–2 %**. Differences smaller than that are not real.

Host: Docker Desktop on Windows, 16 CPUs, 15 GiB RAM. CPU is "% of one core", summed over the 11 .NET containers.

### Results

| Run | Images (MiB) | Idle CPU | Idle memory | Load CPU | Load memory (avg / max) | .NET log lines | Infra log lines | Trace spans | Metric points | p95 response |
|-----|-------------:|---------:|------------:|---------:|------------------------:|---------------:|----------------:|------------:|--------------:|-------------:|
| Baseline | 3095 | 6.0 % | 639 MiB | 58.3 % | 890 / 974 MiB | 7954 | – ¹ | 16830 | 6130 | 5.4 ms |
| Baseline (repeat) | 3095 | 5.7 % | 642 MiB | 64.4 % | 910 / 965 MiB | 7939 | – ¹ | 15208 | 5629 | 5.3 ms |
| 1 · Reduce Bundle Size | **1704** | 5.3 % | 633 MiB | 61.7 % | 884 / 945 MiB | 7954 | 3633 | 14420 | 5646 | 5.2 ms |
| 2 · Reduce Logging | 1704 | **1.6 %** | **561 MiB** | **53.4 %** | 835 / 920 MiB | **341** | **14** | **1327** | **793** | 4.4 ms |
| 3 · GC configuration | 1704 | 1.2 % | 550 MiB | 52.8 % | 855 / 944 MiB | 341 | 22 | 1335 | 793 | 4.5 ms |
| Experiment: Server GC without DATAS | 1704 | 2.2 % | 555 MiB | 53.9 % | 954 / **1165** MiB | 336 | 7 | 1522 | 878 | 4.6 ms |

All runs served the same 2,669 requests with 0 failures.

¹ The load balancer container was recreated after the baseline runs, which deleted its logs. Tactic 1 does not touch
the infrastructure, so the tactic 1 run is the "before" for infrastructure logs.

Overall, from baseline to the final state: **−45 % image size, −91 % trace spans, −86 % metric points, −96 % .NET log
lines, −99.6 % infrastructure log lines, −14 % idle memory, about −10–15 % CPU under load, and a slightly faster p95.**

---

## Tactic 1 – Reduce Bundle Size

**Problem.** Every service image was built on `mcr.microsoft.com/dotnet/aspnet:10.0`, a full Debian with shell, package
manager and ICU. The images were 335–353 MiB each (3.1 GB for nine), while our own published code is only about 11 MB.
More than 95 % of every image was operating system we never use, and it is stored in GHCR, cached by GitHub Actions
and pulled onto every machine that deploys Happy Headlines – nine times per release.

**Change.** Run the services on Microsoft's *chiseled* Ubuntu image: only the .NET runtime and the libraries it needs, no
shell, no package manager, and a non-root user by default. Chiseled images leave out ICU, so the services use
invariant globalization (none of them format or compare culture-specific text). Because there is no shell, the
entrypoint can no longer be a `sh -c` command, so the published executable is renamed to a fixed name instead.

Before (`Dockerfile`):

```dockerfile
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SERVICE
ENV SERVICE_DLL=${SERVICE}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet $SERVICE_DLL"]
```

After:

```dockerfile
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release -o /app \
    && mv /app/${SERVICE} /app/service

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["./service"]
```

New `Directory.Build.props` (applies to every project):

```xml
<InvariantGlobalization>true</InvariantGlobalization>
```

We also stopped shipping code to services that never run it. The FeatureHub SDK (with its own dependencies) was
referenced by `HappyHeadlines.Shared`, which every service uses, although only SubscriberService, NewsletterService
and the Website read feature toggles. The toggle code moved to its own project,
`src/Shared/HappyHeadlines.Shared.FeatureToggles`, referenced only by those three:

```xml
<!-- Before: HappyHeadlines.Shared.csproj (used by all nine services) -->
<PackageReference Include="FeatureHub.SDK" Version="3.0.0" />

<!-- After: only in SubscriberService, NewsletterService and Website -->
<ProjectReference Include="..\Shared\HappyHeadlines.Shared.FeatureToggles\HappyHeadlines.Shared.FeatureToggles.csproj" />
```

**Result.** Each image went from 335–353 MiB to 179–201 MiB; 3,095 → 1,704 MiB in total (**−45 %**). In GHCR, where
layers are stored gzip-compressed, the nine baseline images (tag `1.0.2`) take 882 MiB. Runtime CPU, memory and response
times did not change (all within noise), which is expected: the same code runs, just without the unused OS around it.
The image is also safer: no shell or package manager for an attacker to use, and the process no longer runs as root.

**Trade-offs.** You can no longer `docker exec … sh` into a running service for debugging (use logs, traces, or a
debug sidecar). Culture-specific formatting (e.g. Danish dates) would need ICU back – if that is ever needed, switch to the
`-extra` chiseled variant.

---

## Tactic 2 – Reduce Logging

**Problem.** The measurement showed that monitoring, not the business logic, produced most of the system's work:

- **96 % of all .NET log events** were the "HTTP GET … responded 200 in 3 ms" request log – one event per request in
  every service a request passed through. Each one is formatted, written to the console, sent to Seq, indexed and stored.
  The same information already exists as metrics (request counts and durations) and as traces.
- **Every request was traced** (16,830 spans in one minute), although traces are only used to look at individual
  examples when something is slow or broken.
- **Metrics were exported every 5 s** and scraped by Prometheus every 5 s, with the Grafana dashboard refreshing every
  5 s – far finer than anyone reads a cache hit ratio.
- **The article load balancer (nginx)** wrote an access-log line for every proxied request (~3,500 per minute) –
  duplicates of what ArticleService already traces.
- **A bug in the database health checks**: `pg_isready -U hh` connects to a database named after the user (`hh`), which
  does not exist, so each of the 13 Postgres containers logged `FATAL: database "hh" does not exist` every 5 seconds –
  about 9,000 useless error lines per hour. All health checks also kept running every 5 s forever, although the fast
  interval is only needed during startup (RabbitMQ's check starts a new Erlang VM each time).

**Changes.**

Request logging – only requests that failed or were slow are logged
(`src/Shared/HappyHeadlines.Shared/Observability/ObservabilityExtensions.cs`).
Before:

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : IsInfrastructurePath(ctx) ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});
```

After:

```csharp
// Successful requests are already counted by the metrics and sampled by the traces,
// so only failed and slow requests are worth a log event.
var slowRequestMs = app.Configuration.GetValue("Logging:SlowRequestMs", 1000.0);
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (ctx, elapsedMs, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : elapsedMs > slowRequestMs && !IsInfrastructurePath(ctx) ? LogEventLevel.Warning
        : LogEventLevel.Debug;
});
```

Trace sampling – 10 % of traces, configurable per environment (`Otlp__SampleRatio`, default 1.0 = everything).
The sampler is *parent-based*: the first service decides, and every downstream service follows that decision, so a
sampled trace is still complete from the Website through ArticleService and CommentService.

```csharp
// Before
.WithTracing(tracing => tracing
    .AddSource(serviceName)

// After
var sampleRatio = builder.Configuration.GetValue("Otlp:SampleRatio", 1.0);
...
.WithTracing(tracing => tracing
    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
    .AddSource(serviceName)
```

Metrics export, scraping and dashboard refresh (`docker-compose.yml`, `infra/prometheus.yml`, Grafana):

```yaml
# Before                                  # After
OTEL_METRIC_EXPORT_INTERVAL: "5000"       OTEL_METRIC_EXPORT_INTERVAL: "30000"
                                          Otlp__SampleRatio: "0.1"
scrape_interval: 5s                       scrape_interval: 15s
"refresh": "5s"                           "refresh": "30s"
```

Health checks – correct database, and fast polling only while a container starts (`docker-compose.yml`):

```yaml
# Before
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U hh"]
  interval: 5s
  timeout: 3s
  retries: 20

# After
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U hh -d $${POSTGRES_DB}"]
  interval: 30s
  start_period: 60s
  start_interval: 2s
  timeout: 3s
  retries: 3
```

(Redis and RabbitMQ got the same `interval`/`start_interval` treatment.) Load balancer (`infra/nginx/article-lb.conf`):

```nginx
server {
    listen 80;
    access_log off;   # errors still go to the error log
```

**Result.** .NET log lines 7,954 → 341 (**−96 %**, 1,067 → 59 KiB), infrastructure log lines 3,633 → 14
(**−99.6 %**), trace spans ~15,500 → 1,327 (**−91 %**), metric points ~5,900 → 793 (**−86 %**). Because the services
do less formatting, serializing and exporting, CPU under load fell from 58–64 % to 53 % and idle CPU from ~5.5 % to
1.6 %; idle memory fell from ~635 to 561 MiB (fewer buffered log events and spans). p95 response time improved from
5.2–5.4 ms to 4.4 ms. The stack still starts as fast as before thanks to `start_interval`.

The 341 remaining log lines are business events, one per write: "Stored comment … as Approved", "Published article …",
"Sent breaking news … to N subscriber(s)". They are rare, and they are the lines worth reading.

**Trade-offs.** Successful requests are no longer individually visible in Seq; you see them as metrics and in the
10 % of sampled traces. A rare problem that does not cause an error or a slow response may not have a trace – lower the
ratio only where that is acceptable, and raise `Otlp__SampleRatio` temporarily when debugging. Dashboards update every
30 s instead of 5 s, and an unhealthy dependency is detected after up to ~90 s instead of a few seconds (startup is
unaffected).

---

## Tactic 3 – Set Configuration Parameters Optimally (garbage collector)

**Problem.** ASP.NET Core defaults to *Server GC* with *background (concurrent) GC*. Server GC is designed for one
application that owns the whole machine: it creates one heap and one GC thread per CPU core and lets heaps grow large
to collect less often. Happy Headlines runs 11 small .NET services on the same host (16 cores), each with a heap of a
few tens of MB. The configuration does not match the deployment.

**Change.** *Workstation GC* (one heap, collection on the allocating thread) without the extra background-GC thread,
for every service (`Directory.Build.props`):

```xml
<!-- Before: nothing set, i.e. the ASP.NET Core defaults
     System.GC.Server = true, System.GC.Concurrent = true -->

<!-- After -->
<ServerGarbageCollection>false</ServerGarbageCollection>
<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
```

This ends up in each service's `runtimeconfig.json` (`"System.GC.Server": false, "System.GC.Concurrent": false`) and can
still be overridden per container with the `DOTNET_gcServer` environment variable.

**Result – smaller than expected, and why.** Compared with tactic 2, idle memory went 561 → 550 MiB and CPU 53.4 → 52.8 %;
memory under load went 835 → 855 MiB. All of that is inside the measurement noise: on .NET 10, the GC change alone made
no measurable difference.

To understand why, we ran one extra experiment (`results/experiment-server-gc-no-datas.json`): Server GC with
*DATAS* (Dynamic Adaptation To Application Sizes) switched off, which is how Server GC behaved up to .NET 8. Under load,
memory then grew to 954 MiB on average and **1,165 MiB at peak** – 12 % / 23 % more than with Workstation GC
(855 / 944 MiB). So the tactic is correct, but since .NET 9 the runtime applies most of it by default: DATAS starts
Server GC with one heap and only adds heaps when an application actually needs them.

**Conclusion.** We keep the explicit Workstation GC setting: it documents the decision, it makes the memory profile
independent of runtime defaults that have already changed once, and it makes the services behave the same regardless
of how many cores the host has. But the honest result is that on .NET 10 the gain is within noise, and most of the
saving came from the platform, not from us. This is the point of "Measure and Experiment": without measuring we would have
claimed a saving that does not exist.

**Trade-offs.** Workstation GC without background GC can give longer gen-2 pauses on large heaps, and less throughput
for a busy service on many cores. Neither applies to Happy Headlines today (p95 stayed at 4.5 ms); a service that grows
large can switch back with `DOTNET_gcServer=1` without a rebuild.

---

## Limitations

- These are proxies, not energy measurements. Fewer bytes, CPU-seconds and stored events mean less energy, but we did not
  measure joules, and Docker Desktop's VM adds overhead we cannot separate out.
- One host, one workload, short runs. CPU and telemetry counts vary about ±10 % between identical runs, so only the
  large differences (image size, logs, spans, metric points, idle CPU, idle memory) are clear results; the CPU-under-load
  improvement is at the edge of the noise.
- The savings at the Seq/Jaeger/Prometheus side (less indexing, storage and disk I/O over time) are real but grow over
  days, so a one-minute run does not show them; the monitoring containers' CPU went from ~2.8 % to ~1.8–2.5 %.

## Reproducing

```powershell
docker compose up -d --build
./scripts/measure.ps1 -Label my-run      # writes docs/sustainability/results/my-run.json
```
