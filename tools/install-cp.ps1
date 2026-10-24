#Requires -RunAsAdministrator
# BTUnlock 人来解锁组件安装脚本（随 Release 包分发，与 DLL 同目录）
$ErrorActionPreference = 'Stop'

$dll = Join-Path $PSScriptRoot 'BTUnlock.CredentialProvider.dll'
if (-not (Test-Path $dll)) { Write-Error '当前目录找不到 BTUnlock.CredentialProvider.dll'; exit 1 }

$dest = Join-Path $env:ProgramFiles 'BTUnlock'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item $dll $dest -Force

regsvr32 /s (Join-Path $dest 'BTUnlock.CredentialProvider.dll')
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
    # regsvr32 经 cmd 调用时退出码不可靠，以注册表结果为准
}

$registered = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D}'
if (-not $registered) { Write-Error '注册失败，请查看 C:\ProgramData\BTUnlock\cp.log'; exit 1 }

New-Item -Path 'HKLM:\SOFTWARE\BTUnlock' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\BTUnlock' -Name UnlockProviderEnabled -Value 1 -Type DWord

Write-Host '安装完成。接下来：'
Write-Host '  1) 启动 PC 端主程序并保持运行（托盘）'
Write-Host '  2) 主界面「人来解锁」→ 勾选启用 → 录入 Windows 密码'
Write-Host '  3) 锁屏后把手机拿开再回来，测试自动解锁'
Write-Host '回退：管理员运行 uninstall-cp.ps1，或 reg add HKLM\SOFTWARE\BTUnlock /v UnlockProviderEnabled /t REG_DWORD /d 0 /f'
