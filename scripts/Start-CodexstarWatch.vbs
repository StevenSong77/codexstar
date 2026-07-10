Set shell = CreateObject("WScript.Shell")
shell.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & Replace(WScript.ScriptFullName, "Start-CodexstarWatch.vbs", "Watch-Codexstar.ps1") & """", 0, False
