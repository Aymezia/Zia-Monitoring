Zia Monitoring - Setup

1) Build recommended portable executable bundle (reliable)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-Portable.ps1

2) Optional experimental one-file build
   powershell -ExecutionPolicy Bypass -File .\installer\Build-OneFile.ps1

3) Build Setup executable (.exe installer)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-SetupExe.ps1

4) Build Setup MSI (.msi installer)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-Msi.ps1

5) Build Setup Bootstrap EXE (launches MSI)
   powershell -ExecutionPolicy Bypass -File .\installer\Build-SetupBootstrapExe.ps1

6) Install per-user (no admin required)
   powershell -ExecutionPolicy Bypass -File .\installer\Install-ZiaMonitoring.ps1

   You can also pass a custom source path:
   powershell -ExecutionPolicy Bypass -File .\installer\Install-ZiaMonitoring.ps1 -SourcePath .\publish\portable

7) Uninstall
   powershell -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\ZiaMonitoring\Uninstall-ZiaMonitoring.ps1

Notes:
- Recommended executable generated in: .\publish\portable\ZiaMonitoring.App.exe
- Setup executable generated in: .\publish\setup\ZiaMonitoring-Setup.exe
- Setup MSI generated in: .\publish\setup\ZiaMonitoring-Setup.msi
- Setup Bootstrap EXE generated in: .\publish\setup\ZiaMonitoring-SetupBootstrap.exe
- The bootstrap EXE must stay in the same folder as ZiaMonitoring-Setup.msi.
- Installer copies app folder to: %LOCALAPPDATA%\ZiaMonitoring\app
- Creates Start Menu and optional Desktop shortcuts.
