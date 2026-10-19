param([string]$OutputName = 'Immersion.exe')
$ErrorActionPreference = 'Stop'
$taskDir = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
& (Join-Path $taskDir 'icons\package-icon.ps1')
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:' + (Join-Path $taskDir $OutputName)),('/win32icon:' + (Join-Path $taskDir 'icons\Immersion.ico')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += Join-Path $taskDir 'Immersion.cs'
$compilerArgs += Join-Path $taskDir 'LavaFluid.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output ('Built ' + (Join-Path $taskDir $OutputName))
