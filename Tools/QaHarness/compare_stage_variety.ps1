param(
    [string]$Before = 'out/revamp/stage-variety-before.json',
    [string]$After = 'out/revamp/stage-variety-after.json',
    [string]$Output = 'out/revamp/stage-variety-comparison.json'
)
$ErrorActionPreference = 'Stop'
$beforePlans = (Get-Content -LiteralPath $Before -Raw | ConvertFrom-Json).plans
$afterPlans = (Get-Content -LiteralPath $After -Raw | ConvertFrom-Json).plans
if ($beforePlans.Count -ne $afterPlans.Count) { throw 'Probe counts differ.' }
$changed = @()
for ($i = 0; $i -lt $beforePlans.Count; $i++) {
    $old = $beforePlans[$i]
    $new = $afterPlans[$i]
    foreach ($key in @('seed', 'stage', 'difficulty', 'theme', 'section', 'outcome')) {
        if ($old.$key -ne $new.$key) { throw "Probe identity differs at $i / $key" }
    }
    if (!$old.planHash -or !$new.planHash) { throw 'Full-plan fingerprints are required.' }
    if (!$new.lanePathClearable -or $new.consecutiveRepeats -ne 0) {
        throw "Invalid lane path or adjacent repeat at $i"
    }
    if ($old.planHash -eq $new.planHash) { continue }
    if ($new.section -ne 'Closing' -or $new.outcome -ne 'CleanKill') {
        throw "Unexpected change outside the CleanKill closing route at $i"
    }
    if ($old.BossId -ne $new.BossId -or $new.SegmentReuseCount -ne 0) {
        throw "Boss changed or a segment was reused at $i"
    }
    for ($slot = 0; $slot -lt 2; $slot++) {
        if ($old.sequence[$slot].id -ne $new.sequence[$slot].id) {
            throw "The initial outcome pieces changed at $i"
        }
    }
    $changed += [ordered]@{
        seed = $new.seed; stage = $new.stage; theme = $new.theme
        before = $old; after = $new
        secondsDelta = ($new.durationTicks - $old.durationTicks) / 60.0
        enemiesDelta = $new.enemySpawns - $old.enemySpawns
        obstaclesDelta = $new.obstacles - $old.obstacles
    }
}
function Mean($Items, [string]$Property, [double]$Divisor = 1) {
    return [Math]::Round(($Items | Measure-Object -Property $Property -Average).Average / $Divisor, 2)
}
$groups = @($changed | Group-Object { $_.theme } | ForEach-Object {
    $old = @($_.Group | ForEach-Object { $_.before })
    $new = @($_.Group | ForEach-Object { $_.after })
    [ordered]@{
        theme = $_.Name; count = $_.Count
        beforeSeconds = Mean $old 'durationTicks' 60; afterSeconds = Mean $new 'durationTicks' 60
        beforeEnemies = Mean $old 'enemySpawns'; afterEnemies = Mean $new 'enemySpawns'
        beforeObstacles = Mean $old 'obstacles'; afterObstacles = Mean $new 'obstacles'
    }
})
$summary = [ordered]@{
    plans = $afterPlans.Count; changed = $changed.Count; unchanged = $afterPlans.Count - $changed.Count
    beforePlansWithReuse = @($beforePlans | Where-Object SegmentReuseCount -gt 0).Count
    afterPlansWithReuse = @($afterPlans | Where-Object SegmentReuseCount -gt 0).Count
    lanePathsClearable = @($afterPlans | Where-Object lanePathClearable -eq $true).Count
    themes = $groups
    note = 'Direct generator probes, Normal encounters. Lane connectivity does not prove combat clearability or difficulty.'
}
$report = [ordered]@{ summary = $summary; changes = $changed }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),
    ($report | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
$summary | ConvertTo-Json -Depth 5
