
; Inno Setup Compiler 6.0.4

;APSIM setup script

#include  "ISPPBuiltins.iss"
#define AppVerNo GetStringFileInfo("..\bin\Release\net472\Models.exe", PRODUCT_VERSION) 

[Setup]
AppName=APSIM
AppVerName=APSIM v{#AppVerNo}
AppPublisherURL=https://www.apsim.info
ArchitecturesInstallIn64BitMode=x64
OutputBaseFilename=APSIMSetup
VersionInfoVersion={#AppVerNo}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
;LicenseFile=..\license.txt
AppVersion={#AppVerNo}
AppID=APSIM{#AppVerNo}
DefaultDirName={autopf}\APSIM{#AppVerNo}
DefaultGroupName=APSIM{#AppVerNo}
UninstallDisplayIcon={app}\bin\ApsimNG.exe
Compression=lzma/Max
ChangesAssociations=true
WizardSmallImageFile=apsim_logo32.bmp
WizardImageFile=.\APSIMInitiativeBanner.bmp
;InfoBeforeFile=
VersionInfoCompany=APSIM Initiative4
VersionInfoDescription=Apsim Modelling
VersionInfoProductName=Apsim
VersionInfoProductVersion={#AppVerNo}


[Code]
function InitializeSetup(): Boolean;
var
  answer: integer;
  ErrorCode: Integer;
begin
    //check for the .net runtime. If it is not found then show a message.
    if not IsDotNetInstalled(net472, 0) then 
    begin
        answer := MsgBox('The Microsoft .NET Framework 4.6 or above is required.' + #13#10 + #13#10 +
        'Click OK to go to the web site or Cancel to quit', mbInformation, MB_OKCANCEL);        
        result := false;
        if (answer = MROK) then
        begin
          ShellExecAsOriginalUser('open', 'https://go.microsoft.com/fwlink/?LinkID=863265', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
        end;
    end
    else
      result := true;
end; 

[InstallDelete]
Name: {localappdata}\VirtualStore\Apsim\*.*; Type: filesandordirs
Name: {localappdata}\VirtualStore\Apsim; Type: dirifempty

[Files]
Source: ..\bin\Release\net472\*.exe; DestDir: {app}\bin; Flags: ignoreversion; 
Source: ..\bin\Release\net472\*.dll; DestDir: {app}\bin; Flags: ignoreversion; 
Source: ..\bin\Release\net472\Tools\*.*; DestDir: {app}\bin\Tools; Flags: ignoreversion;
Source: ..\DeploymentSupport\Windows\Bin64\*.dll; DestDir: {app}\bin; Flags: ignoreversion; Check: Is64BitInstallMode
Source: ..\DeploymentSupport\Windows\Bin64\lib\gtk-2.0\2.10.0\engines\*.dll; DestDir: {app}\lib\gtk-2.0\2.10.0\engines; Flags: ignoreversion; Check: Is64BitInstallMode
Source: ..\bin\Release\net472\ApsimNG.exe.config; DestDir: {app}\bin; Flags: ignoreversion; 
Source: ..\bin\Release\net472\Models.xml; DestDir: {app}\bin; Flags: ignoreversion; 
Source: ..\APSIM.bib; DestDir: {app}; Flags: ignoreversion;
Source: ..\ApsimNG\Resources\world\*; DestDir: {app}\ApsimNG\Resources\world; Flags: recursesubdirs

;Sample files 
Source: ..\Examples\*; DestDir: {app}\Examples; Flags: recursesubdirs
Source: ..\Examples\*; DestDir: {autodocs}\Apsim\Examples; Flags: recursesubdirs
Source: ..\Tests\UnderReview\*; DestDir: {app}\UnderReview; Flags: recursesubdirs; skipifsourcedoesntexist
Source: ..\Tests\UnderReview\*; DestDir: {autodocs}\Apsim\UnderReview; Flags: recursesubdirs; skipifsourcedoesntexist

[Tasks]
Name: desktopicon; Description: Create a &desktop icon; GroupDescription: Additional icons:; Flags: unchecked
Name: associate; Description: &Associate .apsimx with Apsim Next Generation; GroupDescription: Other tasks:

[UninstallDelete]
Type: files; Name: "{app}\apsim.url"

[INI]
Filename: "{app}\apsim.url"; Section: "InternetShortcut"; Key: "URL"; String: "https://apsimnextgeneration.netlify.com/" 

[Icons]
;Name: {autoprograms}\APSIM{#AppVerNo}; Filename: {app}\bin\ApsimNG.exe
Name: "{group}\APSIM User Interface"; Filename: {app}\bin\ApsimNG.exe
Name: "{group}\APSIM Next Generation home page"; Filename: "{app}\apsim.url";
Name: {autodesktop}\APSIM{#AppVerNo}; Filename: {app}\bin\ApsimNG.exe; Tasks: desktopicon

[Registry]
;do the associations
Root: HKA; Subkey: "Software\Classes\.apsimx"; ValueType: string; ValueName: ""; ValueData: APSIMXFile; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile"; ValueType: string; ValueName: ""; ValueData: APSIM Next Generation File; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: {app}\bin\ApsimNG.exe,0; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\bin\ApsimNG.exe"" ""%1"""; Tasks: associate

[Run]
Filename: {app}\bin\ApsimNG.exe; Description: Launch APSIM; Flags: postinstall nowait skipifsilent
