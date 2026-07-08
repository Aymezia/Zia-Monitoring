; Installeur unique de Zia Monitoring (remplace l'ancien MSI + bootstrap +
; pipeline IExpress). Genere un seul .exe : assistant classique, entree
; propre dans "Applications installees", desinstallation automatique.
;
; L'app ne necessite plus les droits administrateur pour demarrer
; (app.manifest = asInvoker) : cet installeur s'installe donc par defaut
; par utilisateur, sans invite UAC. PrivilegesRequiredOverridesAllowed
; laisse le choix a qui veut une installation machine (tous les comptes).
;
; Compilation : installer\Build-InnoSetup.ps1 (necessite Inno Setup 6,
; https://jrsoftware.org/isdl.php)

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Zia Monitoring"
#define MyAppPublisher "Aymezia"
#define MyAppExeName "ZiaMonitoring.App.exe"

[Setup]
AppId={{7C4E9F2A-3B6D-4E1A-9C5F-2A8B6D1E4F7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Zia Monitoring
DefaultGroupName=Zia Monitoring
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\..\publish\setup
OutputBaseFilename=ZiaMonitoring-Setup

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\..\publish\portable\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Zia Monitoring"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Zia Monitoring"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Zia Monitoring}"; Flags: nowait postinstall skipifsilent
