; ====================================================================
; Instalador de Pakö  -  El comunicador Bribri  -  by TI2LX
; Se compila con Inno Setup 6 (gratis): https://jrsoftware.org/isdl.php
; Guarda este archivo junto a licencia.txt
; ====================================================================

#define Nombre      "Pakö"
#define Version     "2.0.1"
#define Autor       "TI2LX"
#define Exe         "Pako.exe"
; Carpeta donde Visual Studio deja el programa compilado en Release x64
; (la que contiene Pako.exe, FT8Core.dll y NAudio*.dll)
#define Carpeta     "C:\Proyecto\FT8\FT8App\FT8Win\bin\Release"
#define Icono       "C:\Proyecto\FT8\FT8App\FT8Win\Resources\Pako.ico"

[Setup]
AppId={{41C08493-B613-4823-B868-5069E2B96084}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} V.{#Version}
AppPublisher={#Autor}
AppComments=El comunicador Bribri - FT8 / FT4 / JTTY para radioafición. Software libre, GNU GPL v3.
DefaultDirName={autopf}\Pako
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
LicenseFile=licencia.txt
SetupIconFile={#Icono}
UninstallDisplayIcon={app}\{#Exe}
OutputDir=Salida
OutputBaseFilename=Pako_{#Version}_Instalador
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#Carpeta}\{#Exe}";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#Carpeta}\{#Exe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Carpeta}\*.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "licencia.txt";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#Carpeta}\cty.dat";        DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#Carpeta}\hamlib\*";      DestDir: "{app}\hamlib"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#Nombre}";   Filename: "{app}\{#Exe}"
Name: "{autodesktop}\{#Nombre}";    Filename: "{app}\{#Exe}"; Tasks: escritorio

[Run]
Filename: "{app}\{#Exe}"; Description: "Abrir {#Nombre} ahora"; Flags: nowait postinstall skipifsilent

[Code]
// Pakö necesita .NET Framework 4.7.2 (Windows 10 y 11 ya lo traen)
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net472, 0);
  if not Result then
    MsgBox('Pakö necesita Microsoft .NET Framework 4.7.2 o superior.' + #13#10 +
           'Descárgalo gratis de microsoft.com e intenta de nuevo.', mbError, MB_OK);
end;
