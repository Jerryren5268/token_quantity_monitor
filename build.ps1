$ErrorActionPreference = 'Stop'
$petCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$petSources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$petIcon = Join-Path $PSScriptRoot 'dragon-girl.ico'
$petExtra = @()
foreach ($pose in @('stand','magic','click','treat','drag','sleep','peektop','peekright')) {
    $petSprite = Join-Path $PSScriptRoot ('pose-' + $pose + '.png')
    $petExtra += ('/resource:' + $petSprite + ',DragonGirl_' + $pose)
}
if (Test-Path -LiteralPath $petIcon) { $petExtra += ('/win32icon:' + $petIcon) }
for ($petWalkFrame = 0; $petWalkFrame -lt 8; $petWalkFrame++) {
    $petWalkIndex = $petWalkFrame.ToString('00')
    $petExtra += ('/resource:' + (Join-Path $PSScriptRoot ('pose-walk-' + $petWalkIndex + '.png')) + ',DragonGirl_walk' + $petWalkIndex)
}
& $petCompiler /nologo /target:winexe /codepage:65001 /optimize+ ('/out:' + (Join-Path $PSScriptRoot 'LMServicePet.exe')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll $petExtra $petSources
if ($LASTEXITCODE -ne 0) { throw 'Pixel pet compilation failed.' }
