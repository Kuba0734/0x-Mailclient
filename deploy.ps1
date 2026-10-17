# Build → commit → tag → push → GitHub release (with the exe as ZIP + SHA-256).
# The app checks the latest release on start and updates itself.
# Called by deploy.bat. Requires: git, .NET SDK, GitHub CLI (gh) logged in.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Project   = 'MailClient.App\MailClient.App.csproj'
$AssetName = 'MailClient-win-x64.zip'
$Artifacts = Join-Path $PSScriptRoot 'artifacts'

function Fail($msg) { Write-Host "`n[FEHLER] $msg" -ForegroundColor Red; exit 1 }
function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# ── Checks ────────────────────────────────────────────────────────────────
foreach ($tool in 'git', 'dotnet', 'gh') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        if ($tool -eq 'gh') {
            Fail "GitHub CLI fehlt. Einmalig installieren und anmelden:`n  winget install GitHub.cli`n  gh auth login"
        }
        Fail "$tool wurde nicht gefunden."
    }
}
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Fail "GitHub CLI ist nicht angemeldet. Bitte einmal 'gh auth login' ausführen." }

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "Ordner : $PSScriptRoot"
Write-Host "Branch : $branch"

# ── Version ───────────────────────────────────────────────────────────────
$lastTag = (git tag --list 'v*' --sort=-v:refname | Select-Object -First 1)
if ($lastTag) {
    $v = [version]($lastTag.TrimStart('v'))
    $suggested = '{0}.{1}.{2}' -f $v.Major, $v.Minor, ([Math]::Max($v.Build, 0) + 1)
} else {
    $suggested = '1.0.1'
}
Write-Host "Letzte Version: $(if ($lastTag) { $lastTag } else { '(keine)' })"
$answer = Read-Host "Neue Version [$suggested]"
$version = if ([string]::IsNullOrWhiteSpace($answer)) { $suggested } else { $answer.Trim().TrimStart('v') }
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "Ungültige Version '$version' (Format: 1.2.3)." }
$tag = "v$version"
if (git tag --list $tag) { Fail "Tag $tag existiert bereits." }
if ($lastTag -and ([version]$version -le [version]($lastTag.TrimStart('v')))) {
    Fail "$tag ist nicht neuer als $lastTag – die App würde das Update ignorieren."
}

do { $msg = Read-Host 'Was wurde geändert?' } while ([string]::IsNullOrWhiteSpace($msg))

# ── Build (before committing, so a broken build never gets released) ─────
Step "Baue Version $version …"
$publish = Join-Path $Artifacts 'publish'
if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
dotnet publish $Project -c Release -r win-x64 -p:Platform=x64 `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
    -p:Version=$version -o $publish -v quiet
if ($LASTEXITCODE -ne 0) { Fail 'Build fehlgeschlagen.' }
if (-not (Test-Path (Join-Path $publish 'MailClient.App.exe'))) { Fail 'MailClient.App.exe fehlt im Build.' }

Step 'Packe ZIP …'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $Artifacts $AssetName
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$shaFile = "$zip.sha256"
[IO.File]::WriteAllText($shaFile, "$hash  $AssetName`n")
Write-Host ("ZIP: {0:N1} MB, SHA-256 {1}" -f ((Get-Item $zip).Length / 1MB), $hash)

# ── Commit, tag, push ─────────────────────────────────────────────────────
Step 'Commit & Push …'
git add -A
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    $msgFile = New-TemporaryFile
    [IO.File]::WriteAllText($msgFile, "$msg`n`nRelease $tag")
    git commit -F $msgFile
    $err = $LASTEXITCODE
    Remove-Item $msgFile
    if ($err -ne 0) { Fail 'Commit fehlgeschlagen.' }
} else {
    Write-Host 'Keine Änderungen – es wird nur ein Release erstellt.'
}

git tag -a $tag -m $msg
if ($LASTEXITCODE -ne 0) { Fail "Tag $tag konnte nicht erstellt werden." }

git push -u origin $branch
if ($LASTEXITCODE -ne 0) { git tag -d $tag | Out-Null; Fail 'Push fehlgeschlagen. Evtl. vorher "git pull" nötig?' }
git push origin $tag
if ($LASTEXITCODE -ne 0) { Fail "Tag $tag konnte nicht gepusht werden." }

# ── GitHub release ────────────────────────────────────────────────────────
Step "Erstelle GitHub-Release $tag …"
$notes = "$msg`n`nInstallation: ZIP entpacken (z. B. nach %LOCALAPPDATA%\Programs\Mail) und MailClient.App.exe starten. Danach aktualisiert sich die App selbst."
gh release create $tag $zip $shaFile --title "Mail $tag" --notes $notes --latest
if ($LASTEXITCODE -ne 0) { Fail 'Release konnte nicht erstellt werden (Commit und Tag sind bereits gepusht).' }

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " Fertig! $tag ist veröffentlicht."           -ForegroundColor Green
Write-Host " Laufende Apps aktualisieren sich beim nächsten Start bzw. innerhalb von 6 Stunden." -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
