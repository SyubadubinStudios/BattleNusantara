; Windows installer for Battle of Nusantara. Build it with scripts/package.ps1, which publishes the
; desktop game and the LAN server first and passes the version.
; Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{6C1F2E4B-8A57-4E0B-9F1A-2B7D5C3E9A10}
AppName=Battle of Nusantara
AppVersion={#AppVersion}
AppVerName=Battle of Nusantara {#AppVersion}
AppPublisher=Syubadubin Studios
AppPublisherURL=https://github.com/SyubadubinStudios/BattleNusantara
AppSupportURL=https://github.com/SyubadubinStudios/BattleNusantara/issues
AppComments=Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios
; Per-user install: the LAN server keeps its account database next to itself, so the folder must be writable.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Battle of Nusantara
DefaultGroupName=Battle of Nusantara
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BattleNusantara-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayName=Battle of Nusantara {#AppVersion}
UninstallDisplayIcon={app}\Desktop\BattleNusantara.Desktop.exe

[Languages]
; Inno Setup ships no Indonesian translation, so the wizard is in English and our own texts are bilingual.
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ComponentGame=Game (solo and LAN client) / Permainan (solo dan klien LAN)
ComponentServer=LAN server, to host other players / Server LAN untuk menjadi host
ServerShortcut=Battle of Nusantara LAN server
DesktopIcon=Create a desktop icon / Buat ikon di desktop
NeedWebView2=The desktop game needs the Microsoft Edge WebView2 Runtime, which is not installed. Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start the game again.%n%nPermainan desktop memerlukan Microsoft Edge WebView2 Runtime yang belum terpasang. Pasang dari tautan di atas lalu jalankan permainan lagi.

[Types]
Name: "full"; Description: "Game + LAN server / Permainan + server LAN"
Name: "game"; Description: "Game only / Permainan saja"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "game"; Description: "{cm:ComponentGame}"; Types: full game custom; Flags: fixed
Name: "server"; Description: "{cm:ComponentServer}"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Desktop\*"; DestDir: "{app}\Desktop"; Components: game; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Server\*"; DestDir: "{app}\Server"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\GUIDE-EN.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\PANDUAN-ID.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\ASSETS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\OPERATIONS.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\Battle of Nusantara"; Filename: "{app}\Desktop\BattleNusantara.Desktop.exe"; WorkingDir: "{app}\Desktop"; Components: game
Name: "{group}\{cm:ServerShortcut}"; Filename: "{app}\Server\BattleNusantara.Server.exe"; Parameters: "--urls http://0.0.0.0:5080"; WorkingDir: "{app}\Server"; Components: server
Name: "{group}\{cm:UninstallProgram,Battle of Nusantara}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Battle of Nusantara"; Filename: "{app}\Desktop\BattleNusantara.Desktop.exe"; WorkingDir: "{app}\Desktop"; Tasks: desktopicon; Components: game

[Run]
Filename: "{app}\Desktop\BattleNusantara.Desktop.exe"; WorkingDir: "{app}\Desktop"; Description: "{cm:LaunchProgram,Battle of Nusantara}"; Flags: nowait postinstall skipifsilent; Components: game

[UninstallDelete]
; The server's account database and backups are created at run time.
Type: filesandordirs; Name: "{app}\Server\data"

[Code]
function WebView2Installed(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
  Result := Result and (Version <> '') and (Version <> '0.0.0.0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WebView2Installed() then
    MsgBox(CustomMessage('NeedWebView2'), mbInformation, MB_OK);
end;
