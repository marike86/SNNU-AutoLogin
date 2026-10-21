param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\SNNUAutoLogin.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "SNNUAutoLogin-Portable-$version-win-x64"
$destination = [IO.Path]::GetFullPath($OutputDirectory)
# Unique staging avoids packaging stale files from an earlier publish.
$staging = Join-Path $destination ("staging-" + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $staging $name
New-Item -ItemType Directory -Path $publish -Force | Out-Null
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\README.md') -Destination $publish
$archive = Join-Path $destination "$name.zip"
Compress-Archive -LiteralPath $publish -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  $name.zip`n")
Write-Output "Portable archive: $archive"
Write-Output "SHA256: $hash"
