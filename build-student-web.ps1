# Rebuilds the student app for the web and copies it into student-web/, which Render publishes.
# Usage:  .\build-student-web.ps1                      (uses the live server address below)
#         .\build-student-web.ps1 -ApiBaseUrl https://your-server.onrender.com
param([string]$ApiBaseUrl = "https://itsdance.co.za")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location "$root\app"
try {
    flutter build web --release --dart-define=API_BASE_URL=$ApiBaseUrl
    if ($LASTEXITCODE -ne 0) { throw "flutter build failed" }
} finally { Pop-Location }
if (Test-Path "$root\student-web") { Remove-Item "$root\student-web" -Recurse -Force }
Copy-Item "$root\app\build\web" "$root\student-web" -Recurse
Write-Host "Done. Commit and push the student-web folder, then Render republishes the student app."
