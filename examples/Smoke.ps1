param([string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $ExpectedCommit) {
        $ExpectedCommit = git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine expected commit.' }
    }
    $runDirectory = Join-Path $root ('artifacts/smoke-' + [guid]::NewGuid().ToString('N'))
    $toolDirectory = Join-Path $runDirectory 'tool'
    dotnet tool install GroupFold.Tool --version 0.1.0 --tool-path $toolDirectory --configfile nuget.local.config --no-cache
    if ($LASTEXITCODE -ne 0) { throw 'Local tool install failed.' }
    $tool = Join-Path $toolDirectory 'groupfold'
    if ((& $tool --version) -ne '0.1.0') { throw 'Tool version mismatch.' }
    $before = (Get-FileHash examples/data.jsonl -Algorithm SHA256).Hash.ToLowerInvariant()
    $plan = & $tool plan --input examples/data.jsonl --folds 3 --seed 42 --strict | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $plan.exported -or $plan.sourceRows -ne 12 -or $plan.groups -ne 6 -or $plan.warnings.Count -ne 0 -or $plan.sourceSha256 -ne $before) { throw 'Positive preview failed.' }
    $foldDirectory = Join-Path $runDirectory 'folds'
    $report = & $tool split --input examples/data.jsonl --out-dir $foldDirectory --folds 3 --seed 42 --strict | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $report.exported -or $report.artifacts.Count -ne 6) { throw 'Export failed.' }
    foreach ($fold in $report.folds) {
        if ($fold.validationRows -ne 4 -or $fold.trainingRows -ne 8 -or $fold.validationLabels.billing -ne 2 -or $fold.validationLabels.support -ne 2) { throw 'Hand-counted fold coverage mismatch.' }
        $index = '{0:00}' -f $fold.fold
        $training = @(Get-Content (Join-Path $foldDirectory ('train-' + $index + '.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json })
        $validation = @(Get-Content (Join-Path $foldDirectory ('validation-' + $index + '.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json })
        if (@($validation | Where-Object { $_.group -in $training.group }).Count -ne 0) { throw 'Group overlap in exported pair.' }
    }
    foreach ($artifact in $report.artifacts) {
        $path = Join-Path $foldDirectory $artifact.name
        if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $artifact.sha256 -or (Get-Item $path).Length -ne $artifact.bytes) { throw 'Artifact hash/length mismatch.' }
    }
    $blockedDirectory = Join-Path $runDirectory 'blocked'
    $blocked = & $tool split --input examples/rare.jsonl --out-dir $blockedDirectory --folds 3 --strict | ConvertFrom-Json
    if ($LASTEXITCODE -ne 1 -or $blocked.exported -or (Test-Path -LiteralPath $blockedDirectory) -or -not ($blocked.warnings | Where-Object code -eq GF001)) { throw 'Strict no-write gate failed.' }
    & $tool plan --input examples/data.jsonl --folds invalid 2> (Join-Path $runDirectory 'argument-error.txt')
    if ($LASTEXITCODE -ne 2) { throw 'Argument exit contract failed.' }
    if ((Get-FileHash examples/data.jsonl -Algorithm SHA256).Hash.ToLowerInvariant() -ne $before) { throw 'Source changed.' }
    $package = Join-Path $root 'artifacts/GroupFold.Tool.0.1.0.nupkg'
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('GroupFold.Tool.nuspec').Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($manifest.package.metadata.id -ne 'GroupFold.Tool' -or $manifest.package.metadata.version -ne '0.1.0' -or $manifest.package.metadata.repository.commit -ne $ExpectedCommit) { throw 'Packaged final-commit metadata mismatch.' }
        foreach ($entry in @('README.md','LICENSE','CONTRIBUTING.md','ROADMAP.md','CHANGELOG.md','docs/DESIGN.md','docs/RESEARCH.md','docs/TROUBLESHOOTING.md','examples/README.md','examples/data.jsonl','examples/rare.jsonl')) {
            if (-not $zip.GetEntry($entry)) { throw 'Missing bundled example or guide.' }
        }
    } finally { $zip.Dispose() }
    $report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $runDirectory 'verified-export.json') -Encoding utf8
    Write-Output ('Installed-tool smoke passed; evidence: ' + $runDirectory)
} finally { Pop-Location }
