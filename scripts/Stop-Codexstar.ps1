$ErrorActionPreference = 'SilentlyContinue'

Get-Process -Name 'CodexStatusLight' | Stop-Process -Force
