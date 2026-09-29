; Per-user installer for one host. Package-Installers.ps1 passes the /D values.
#ifndef Host
  #error Define Host (AutoCAD, BricsCAD, or ZWCAD)
#endif
#ifndef AppId
  #error Define AppId
#endif
#ifndef PluginsRoot
  #error Define PluginsRoot, for example Autodesk\ApplicationPlugins
#endif
#ifndef StageDir
  #error Define StageDir
#endif

#define AppVersion "1.0.0"
#define AppName "hcwCAD-KIT for " + Host
#define Publisher "Holgundi Consulting Works"

[Setup]
AppId={{{#AppId}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://github.com/HolagundiWorks/hcwCAD-KIT
DefaultDirName={userappdata}\{#PluginsRoot}\hcwCAD-KIT.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=hcwCAD-KIT-{#Host}-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName={#AppName}
WizardStyle=modern
CloseApplications=no

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Messages]
FinishedLabel=hcwCAD-KIT is installed for {#Host}.%n%nClose {#Host} if it is open, then start it again. The hcwCAD-KIT tabs load with the program.
