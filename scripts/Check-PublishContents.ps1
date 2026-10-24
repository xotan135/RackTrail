param([switch]$Push)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProblems = [Collections.Generic.HashSet[string]]::new()
$taskAllowedMaps = @('samples/sample.networkmap.json', 'samples/stacked-switch.networkmap.json')
$taskPatterns = @{
    'private key' = '-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----'
    'GitHub credential' = '(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})'
    'AWS access key' = '(?:AKIA|ASIA)[A-Z0-9]{16}'
    'credential in URL' = 'https?://[^\s/@:]+:[^\s/@]+@'
    'service credential' = '(?:xox[baprs]-[A-Za-z0-9-]{15,}|sk-(?:proj-)?[A-Za-z0-9_-]{25,}|AIza[0-9A-Za-z_-]{30,})'
    'storage account key' = 'AccountKey=[A-Za-z0-9+/]{40,}={0,2}'
    'personal machine path' = '(?i)[A-Z]:[\\/]+Users[\\/]+(?!Public[\\/]|Default[\\/])[^\s"<>]+'
}
function Test-PublishPath([string]$taskPath) {
    $taskPath = $taskPath.Replace('\','/')
    if ($taskPath -match '(^|/)(artifacts|bin|obj|private|hosts|local|exports)/' -or
        $taskPath -match '(^|/)\.(?!github/|githooks/)[^/]+/' -or
        $taskPath -match '(?i)(^|/)(\.env(?:\..*)?|credentials(?:\.json)?|secrets\.json|id_rsa|id_ed25519)$' -or
        $taskPath -match '(?i)\.(pem|key|pfx|p12|kdbx|log|dmp|pdb|exe|zip)$' -or
        ($taskPath -match '(?i)\.networkmap\.json$' -and $taskPath -notin $taskAllowedMaps) -or
        ($taskPath -match '(?i)^[^/]+\.(json|html?)$')) {
        [void]$taskProblems.Add("$taskPath : private or generated file is not approved for publication")
    }
}
function Test-PublishText([string]$taskPath, [string]$taskText) {
    foreach ($taskRule in $taskPatterns.GetEnumerator()) {
        if ([regex]::IsMatch($taskText, $taskRule.Value)) {
            # Report only location and category; never print the matched credential.
            [void]$taskProblems.Add("$taskPath : possible $($taskRule.Key)")
        }
    }
}
Push-Location -LiteralPath $taskRoot
try {
    if ($Push) {
        $taskLines = [Console]::In.ReadToEnd() -split '\r?\n' | Where-Object { $_.Trim() }
        foreach ($taskLine in $taskLines) {
            $taskParts = $taskLine -split '\s+'
            if ($taskParts.Count -ne 4) { throw 'Invalid pre-push input.' }
            $taskLocalRef, $taskSha, $taskRemoteRef, $taskRemoteSha = $taskParts
            if ($taskRemoteRef -notmatch '^refs/(heads|tags)/') {
                [void]$taskProblems.Add("$taskRemoteRef : only branches and tags may be published; recovery references must stay local")
                continue
            }
            if ($taskSha -match '^0+$') { continue }
            $taskEmails = & git log $taskSha --not --remotes '--format=%ae%n%ce'
            if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect outgoing commit identity.' }
            if ($taskEmails | Where-Object { $_ -and $_ -notmatch '@users\.noreply\.github\.com$' }) {
                [void]$taskProblems.Add("$taskLocalRef : new commits contain a non-noreply author or committer email")
            }
            # Inspect all reachable blobs, including data removed in earlier commits.
            $taskObjects = & git rev-list --objects $taskSha
            if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect outgoing history.' }
            foreach ($taskObject in $taskObjects) {
                $taskObjectParts = $taskObject -split ' ',2
                if ($taskObjectParts.Count -ne 2) { continue }
                $taskOid, $taskPath = $taskObjectParts
                Test-PublishPath $taskPath
                $taskKind = & git cat-file -t $taskOid
                if ($LASTEXITCODE -ne 0) { throw 'Cannot read outgoing object.' }
                if ($taskKind -ne 'blob') { continue }
                $taskText = (& git cat-file blob $taskOid 2>$null) -join "`n"
                if ($LASTEXITCODE -ne 0) { throw 'Cannot scan outgoing file.' }
                Test-PublishText $taskPath $taskText
            }
        }
    } else {
        $taskPaths = & git -c core.quotepath=false ls-files --cached --others --exclude-standard
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect repository files.' }
        foreach ($taskPath in $taskPaths | Sort-Object -Unique) {
            Test-PublishPath $taskPath
            $taskAbsolutePath = Join-Path $taskRoot $taskPath
            if (!(Test-Path -LiteralPath $taskAbsolutePath -PathType Leaf)) { continue }
            Test-PublishText $taskPath ([IO.File]::ReadAllText($taskAbsolutePath))
        }
    }
    if ($taskProblems.Count -gt 0) {
        $taskProblems | Sort-Object | ForEach-Object { Write-Output "BLOCKED: $_" }
        throw 'Publication content check failed. Remove private data before publishing.'
    }
    Write-Output 'PASS publication content check: no prohibited paths or recognized credential patterns.'
} finally { Pop-Location }
