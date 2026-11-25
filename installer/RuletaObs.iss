#define AppName "Ruleta OBS"
#define AppExe "RuletaObs.exe"
#define PublishDir "..\artifacts\publish"
#define OutputDir "..\artifacts\installer"

[Setup]
AppId={{3B58B7B8-A0FA-4E6A-9E0C-52B145673C30}
AppName={#AppName}
AppVersion=0.1.5
DefaultDirName={localappdata}\Programs\RuletaObs
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=RuletaObs-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "startup"; Description: "Abrir Ruleta OBS al iniciar Windows"; GroupDescription: "Inicio:"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RuletaObs"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir Ruleta OBS ahora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{localappdata}\Programs\RuletaObs\{#AppExe}"
; Los datos de %AppData%\RuletaObs (cola, configuración y respaldo) se conservan al desinstalar.
