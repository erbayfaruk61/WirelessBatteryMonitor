[Setup]
AppName=Kablosuz Pil İzleyici
AppVersion=1.0.0
AppPublisher=WirelessBatteryMonitor
DefaultDirName={localappdata}\Programs\WirelessBatteryMonitor
DefaultGroupName=Kablosuz Pil İzleyici
PrivilegesRequired=lowest
OutputDir=dist\installer
OutputBaseFilename=WirelessBatteryMonitor-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek görevler:"; Flags: unchecked
Name: "startup"; Description: "Windows ile otomatik başlat"; GroupDescription: "Ek görevler:"

[Files]
Source: "dist\portable\WirelessBatteryMonitor.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Kablosuz Pil İzleyici"; Filename: "{app}\WirelessBatteryMonitor.exe"
Name: "{autodesktop}\Kablosuz Pil İzleyici"; Filename: "{app}\WirelessBatteryMonitor.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WirelessBatteryMonitor"; ValueData: """{app}\WirelessBatteryMonitor.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\WirelessBatteryMonitor.exe"; Description: "Kablosuz Pil İzleyici'yi başlat"; Flags: nowait postinstall skipifsilent
