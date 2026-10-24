#Requires -RunAsAdministrator
# BTUnlock 人来解锁组件卸载脚本
$ErrorActionPreference = 'Stop'

$dll = Join-Path $env:ProgramFiles 'BTUnlock\BTUnlock.CredentialProvider.dll'
if (Test-Path $dll) {
    regsvr32 /u /s $dll
}

# 兜底直接清注册表（LogonUI 已加载 DLL 时文件占用，重启后自动释放删除）
Remove-Item 'HKLM:\SOFTWARE\Classes\CLSID\{D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramFiles 'BTUnlock') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '已卸载（文件若被占用，重启后由目录清理完成）。密码文件在 C:\ProgramData\BTUnlock\credential.json，可手动删除。'
