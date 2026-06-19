# Publish Pos.Client.UI cho Windows (win-x64), single-file self-contained.
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$Out = "dist/$Rid"
Write-Host "Publishing Pos.Client.UI ($Rid) -> $Out"
dotnet publish src/Pos.Client.UI -c Release -r $Rid `
  --self-contained true -p:PublishSingleFile=true -o $Out
Write-Host "Done: $Out"
