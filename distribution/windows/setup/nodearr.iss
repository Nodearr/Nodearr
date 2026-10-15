; The Windows installer, built with Inno Setup 6 from a staged release package (build/Build-Installer.ps1).
; Like Sonarr's, it asks how Nodearr should start: as a Windows service, from a shortcut in the
; Startup folder, which runs the tray app at login, or not at all.
;
;   ISCC.exe /DVersion=0.1.0 /DRuntime=win-x64 /DSourceDir=...\Nodearr /DOutputDir=...\_artifacts nodearr.iss

#ifndef Version
  #error Version is required: /DVersion=0.1.0
#endif
#ifndef Runtime
  #error Runtime is required: /DRuntime=win-x64
#endif
#ifndef SourceDir
  #error SourceDir is required: the staged Nodearr folder
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

; The version Windows records for the program: four numbers, without a pre-release tag.
#define NumericVersion Copy(Version, 1, Pos("-", Version + "-") - 1) + ".0"

[Setup]
AppId={{8F2A6C3E-5B7D-4E1A-9C0F-2D6B8E4A7C15}
AppName=Nodearr
AppVersion={#Version}
AppVerName=Nodearr {#Version}
AppPublisher=Nodearr
AppPublisherURL=https://github.com/Nodearr/Nodearr
AppSupportURL=https://github.com/Nodearr/Nodearr
VersionInfoVersion={#NumericVersion}
; Program Files, which only administrators can change. Sonarr installs into ProgramData, beside its
; data, and that folder is open to every user so the service can write there. A service should not
; run programs from a folder anyone can change.
DefaultDirName={autopf}\Nodearr
DisableDirPage=yes
DefaultGroupName=Nodearr
DisableProgramGroupPage=yes
PrivilegesRequired=admin
#if Runtime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputDir={#OutputDir}
OutputBaseFilename=Nodearr.{#Version}.{#Runtime}-installer
SetupIconFile=..\..\..\src\NzbDrone.Host\Nodearr.ico
UninstallDisplayIcon={app}\Nodearr.exe
UninstallDisplayName=Nodearr
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
DisableReadyPage=yes
CloseApplications=yes
SetupLogging=yes
AllowUNCPath=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupShortcut"; Description: "When I sign in to Windows, in the tray (recommended)"; GroupDescription: "Start Nodearr automatically:"; Flags: exclusive
Name: "windowsService"; Description: "When the computer starts, as a Windows service. It runs as the LocalService account, which may not be able to read your library or network shares until you change the account."; GroupDescription: "Start Nodearr automatically:"; Flags: exclusive unchecked
Name: "none"; Description: "Do not start Nodearr automatically"; GroupDescription: "Start Nodearr automatically:"; Flags: exclusive unchecked
Name: "desktopIcon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Nothing from an earlier version is left behind. The data folder, C:\ProgramData\Nodearr, is not touched.
Type: filesandordirs; Name: "{app}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Nodearr"; Filename: "{app}\Nodearr.exe"
Name: "{commondesktop}\Nodearr"; Filename: "{app}\Nodearr.exe"; Tasks: desktopIcon
; Without opening a browser at every sign-in.
Name: "{userstartup}\Nodearr"; Filename: "{app}\Nodearr.exe"; Parameters: "/nobrowser"; WorkingDir: "{app}"; Tasks: startupShortcut

[Run]
; A service from an earlier install, if any, goes first. It also takes the firewall rule out.
Filename: "{app}\Nodearr.Console.exe"; Parameters: "/u /exitimmediately"; StatusMsg: "Removing an earlier Windows service"; Flags: runhidden waituntilterminated
; Lets UDP in to nodearr-transport, which other nodes reach this one on, so Windows does not have to ask.
Filename: "{app}\Nodearr.Console.exe"; Parameters: "/registerurl /exitimmediately"; StatusMsg: "Letting other nodes reach Nodearr"; Flags: runhidden waituntilterminated; Tasks: startupShortcut none
; The same rule, and the service, which starts at once.
Filename: "{app}\Nodearr.Console.exe"; Parameters: "/i /exitimmediately"; StatusMsg: "Installing the Windows service"; Flags: runhidden waituntilterminated; Tasks: windowsService
Filename: "http://localhost:6633/"; Description: "Open Nodearr"; Flags: shellexec postinstall skipifsilent nowait; Tasks: windowsService
; The tray app runs as the person who installed it, not as the administrator the installer runs as.
Filename: "{app}\Nodearr.exe"; Description: "Start Nodearr"; Flags: postinstall skipifsilent nowait runasoriginaluser; Tasks: startupShortcut none

[UninstallRun]
Filename: "{app}\Nodearr.Console.exe"; Parameters: "/u /exitimmediately"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveService"

[Code]
// A running service holds the program's files open, so it is stopped before they are replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\net.exe'), 'stop Nodearr', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
