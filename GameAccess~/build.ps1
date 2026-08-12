# Builds MDS.GameAccess.dll - the one piece of MDS that reads the game's own internals.
#
# Compiled outside Unity because it references the game's Assembly-CSharp, and there is no way to hand Unity that
# reference without breaking the project: Auto Reference gives it to every assembly and collides with bundled
# libraries, an asmdef hides the scripts from UMod (which only ever compiles the project named "Assembly-CSharp"),
# and Assets/csc.rsp leaks into every assembly. Compiling separately and letting UMod bundle the DLL sidesteps all
# three - its AssemblyScriptProcessor is registered for ".dll" and registers any DLL in the mod folder.
#
# The sources sit in a folder ending in "~", which Unity ignores completely, so they can live inside the git repo
# without ever being compiled into Assembly-CSharp.
#
# Run this after editing GameAccess.cs. The DLL is committed, so nothing will warn you if the two drift apart.

$ErrorActionPreference = 'Stop'

$ModRoot   = Split-Path -Parent $PSScriptRoot
$Managed   = 'D:\SteamLibrary\steamapps\common\Holdfast Nations At War - Beta\Holdfast NaW_Data\Managed'
$OutFile   = Join-Path $ModRoot 'MDS.GameAccess.dll'

if (-not (Test-Path $Managed)) { throw "Game Managed folder not found: $Managed" }

$csc = Get-ChildItem -Path 'C:\Program Files\dotnet\sdk' -Recurse -Filter 'csc.dll' -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -like '*Roslyn\bincore*' } |
       Sort-Object FullName | Select-Object -Last 1
if ($null -eq $csc) { throw 'Could not find the Roslyn compiler under the dotnet SDK.' }

$refs = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll',
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll',
    'Assembly-CSharp.dll'
)

$cscArgs = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', "-out:$OutFile")
foreach ($r in $refs) {
    $path = Join-Path $Managed $r
    if (-not (Test-Path $path)) { throw "Missing reference assembly: $path" }
    $cscArgs += "-r:$path"
}

$sources = Get-ChildItem -Path $PSScriptRoot -Filter '*.cs' -File
if ($sources.Count -eq 0) { throw "No .cs files found in $PSScriptRoot" }
foreach ($s in $sources) { $cscArgs += $s.FullName }

Write-Host "Compiling $($sources.Count) file(s) -> $OutFile"
& dotnet exec $csc.FullName @cscArgs

if ($LASTEXITCODE -ne 0) { throw "Compile failed (exit $LASTEXITCODE)" }

Write-Host 'Build succeeded.'
Write-Host 'Switch to Unity so it imports the DLL, then build the mod as usual.'
