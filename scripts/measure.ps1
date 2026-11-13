<#
.SYNOPSIS
    Week 45 measurement: runs the fixed k6 workload (scripts/load.js) against the running stack and records
    image sizes, memory, CPU, log events, trace spans, metric points and response times.

.EXAMPLE
    docker compose up -d --build
    ./scripts/measure.ps1 -Label baseline
#>
param(
    [Parameter(Mandatory)][string]$Label,
    [int]$SettleSeconds = 60
)

# Docker writes progress to stderr, which Windows PowerShell would turn into terminating errors under "Stop".
$ErrorActionPreference = "Continue"
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root "docs/sustainability/results"
New-Item -ItemType Directory -Force $results | Out-Null
Push-Location $root

$dotnetServices = @("article-service-1", "article-service-2", "article-service-3", "comment-service", "profanity-service",
    "draft-service", "publisher-service", "newsletter-service", "subscriber-service", "website", "webapp")
$monitoring = @("seq", "otel-collector", "jaeger", "prometheus")
$images = @("article-service", "comment-service", "profanity-service", "draft-service", "publisher-service",
    "newsletter-service", "subscriber-service", "website", "webapp")

function ToMiB([string]$size) {
    if ($size -match '^([\d.]+)\s*([KMG]i?B|B)$') {
        $value = [double]$Matches[1]
        switch -Regex ($Matches[2]) { '^K' { $value / 1024 } '^M' { $value } '^G' { $value * 1024 } default { $value / 1MB } }
    } else { 0 }
}

# One `docker stats` sample: CPU (% of one core) and memory (MiB) per compose service.
function Sample {
    docker stats --no-stream --format "{{.Name}};{{.CPUPerc}};{{.MemUsage}}" | ForEach-Object {
        $name, $cpu, $mem = $_ -split ';'
        [pscustomobject]@{
            Service = $name -replace '^happyheadlines-', '' -replace '-\d+$', ''
            Cpu     = [double]($cpu -replace '%', '')
            Mem     = ToMiB(($mem -split ' / ')[0])
        }
    }
}

function Summarize($samples, $services) {
    $perSample = $samples | Group-Object Round | ForEach-Object {
        $rows = $_.Group | Where-Object { $services -contains $_.Service }
        [pscustomobject]@{ Cpu = ($rows | Measure-Object Cpu -Sum).Sum; Mem = ($rows | Measure-Object Mem -Sum).Sum }
    }
    [ordered]@{
        cpuAvgPercent = [math]::Round(($perSample | Measure-Object Cpu -Average).Average, 1)
        memAvgMiB     = [math]::Round(($perSample | Measure-Object Mem -Average).Average, 0)
        memMaxMiB     = [math]::Round(($perSample | Measure-Object Mem -Maximum).Maximum, 0)
    }
}

function Collect([int]$rounds, [scriptblock]$while = { $true }) {
    $all = @()
    for ($r = 0; ($r -lt $rounds) -or ($rounds -eq 0 -and (& $while)); $r++) {
        $all += Sample | ForEach-Object { $_ | Add-Member Round $r -PassThru }
    }
    $all
}

function PromCounter([string]$metricRegex) {
    $query = [uri]::EscapeDataString("sum({__name__=~`"$metricRegex`"})")
    $value = (Invoke-RestMethod "http://localhost:9090/api/v1/query?query=$query").data.result
    if ($value) { [double]$value[0].value[1] } else { 0 }
}

Write-Host "[$Label] recreating .NET services and waiting $SettleSeconds s for them to settle"
docker compose up -d --force-recreate --no-build @dotnetServices *> $null
Start-Sleep $SettleSeconds

Write-Host "[$Label] image sizes"
$imageSizes = [ordered]@{}
foreach ($image in $images) {
    $bytes = [double](docker image inspect "ghcr.io/davud05/happyheadlines-${image}:latest" --format "{{.Size}}")
    $imageSizes[$image] = [math]::Round($bytes / 1MB, 0)
}

Write-Host "[$Label] idle sample"
$idle = Collect 5

$spansBefore = PromCounter "otelcol_receiver_accepted_spans.*"
$pointsBefore = PromCounter "otelcol_receiver_accepted_metric_points.*"
$start = (Get-Date).ToUniversalTime()

Write-Host "[$Label] running load (60 s)"
$k6File = "docs/sustainability/results/$Label-k6.json"
$job = Start-Job -ScriptBlock {
    param($root, $k6File)
    docker run --rm --network happyheadlines_backbone -v "${root}:/work" grafana/k6:latest `
        run --quiet --summary-export "/work/$k6File" /work/scripts/load.js 2>&1
} -ArgumentList $root, $k6File
$load = Collect 0 { $job.State -eq 'Running' }
$k6Output = Receive-Job $job -Wait -AutoRemoveJob
$end = (Get-Date).ToUniversalTime()

Write-Host "[$Label] waiting 45 s for the last metric and trace exports"
Start-Sleep 45
$spans = (PromCounter "otelcol_receiver_accepted_spans.*") - $spansBefore
$points = (PromCounter "otelcol_receiver_accepted_metric_points.*") - $pointsBefore

$logLines = 0; $logBytes = 0
foreach ($service in $dotnetServices) {
    $lines = docker compose logs --no-log-prefix --since $start.ToString("o") --until $end.ToString("o") $service 2>$null
    $logLines += @($lines).Count
    $logBytes += ($lines | Measure-Object -Property Length -Sum).Sum
}

$k6 = Get-Content $k6File | ConvertFrom-Json
$duration = $k6.metrics.http_req_duration
$result = [ordered]@{
    label           = $Label
    measuredAt      = $start.ToString("u")
    imagesMiB       = $imageSizes
    imagesTotalMiB  = ($imageSizes.Values | Measure-Object -Sum).Sum
    idleDotnet      = Summarize $idle $dotnetServices
    loadDotnet      = Summarize $load $dotnetServices
    loadMonitoring  = Summarize $load $monitoring
    logEvents       = $logLines
    logKiB          = [math]::Round($logBytes / 1KB, 0)
    traceSpans      = $spans
    metricPoints    = $points
    requests        = $k6.metrics.http_reqs.count
    failedRate      = $k6.metrics.http_req_failed.value
    responseAvgMs   = [math]::Round($duration.avg, 1)
    responseP95Ms   = [math]::Round($duration.'p(95)', 1)
}

$result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $results "$Label.json")
Pop-Location
if ($k6Output) { $k6Output | Select-String -Pattern "error|level=warn" | Select-Object -First 5 }
[pscustomobject]$result | Format-List
