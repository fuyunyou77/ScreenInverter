$fxc = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x86\fxc.exe"
& $fxc /T ps_2_0 /E main /Fo InvertEffect.ps InvertEffect.fx
if ($LASTEXITCODE -eq 0) {
    Write-Host "Compile Success"
} else {
    Write-Host "Compile Failed"
}
