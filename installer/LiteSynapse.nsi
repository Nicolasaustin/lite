; LiteSynapse installer (NSIS 3, Unicode, Modern UI 2).
;
; Per-user install: no administrator rights, nothing outside the user's own folders and HKCU.
; It never installs or removes drivers or services.
;
; Build everything (tests, publish, installer):  bash tools/build-release.sh
; Compile only, after a publish to build/publish:  makensis installer/LiteSynapse.nsi
;   -DVERSION=x.y.z      version shown and used in the file name (default: AssemblyInformationalVersion)
;   -DVERSION4=a.b.c.d   numeric version for the file properties (default: AssemblyFileVersion)
;   -DPUBLISH_DIR=path   published app (default: ..\build\publish)
;   -DOUTFILE=path       output file (default: ..\dist\LiteSynapse-Setup-<VERSION>.exe)
; Relative paths are relative to this folder (makensis changes into it).

Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SetCompressor /SOLID lzma
CRCCheck on

!define APP_NAME "LiteSynapse"
!define APP_EXE "LiteSynapse.exe"
!define PUBLISHER "LiteSynapse"
!define UNINST_EXE "uninstall.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiteSynapse"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUN_VALUE "LiteSynapse"
; Task Manager's "Startup apps" switch for each HKCU Run value (REG_BINARY; odd first byte = turned off).
!define APPROVED_KEY "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
; The app holds this mutex while it runs (Program.cs).
!define APP_MUTEX "LiteSynapse.single"
; Edge WebView2 Runtime client id (Microsoft's documented detection key).
!define WEBVIEW2_KEY "Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
!define DOTNET_URL "https://dotnet.microsoft.com/download/dotnet/10.0"
!define WEBVIEW2_URL "https://developer.microsoft.com/microsoft-edge/webview2/"

!define ROOT ".."
!define APP_ICON "${ROOT}\src\LiteSynapse\app.ico"
!define SEED_DIR "seed-data"
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "${ROOT}\build\publish"
!endif
!ifndef FILE_LIST
  !define FILE_LIST "${ROOT}\build\installer-files.nsh"
!endif
!ifndef VERSION
  !searchparse /file "${ROOT}\src\LiteSynapse\Properties\AssemblyInfo.cs" 'AssemblyInformationalVersion("' VERSION '")'
!endif
!ifndef VERSION4
  !searchparse /file "${ROOT}\src\LiteSynapse\Properties\AssemblyInfo.cs" 'AssemblyFileVersion("' VERSION4 '")'
!endif
!ifndef OUTFILE
  !define OUTFILE "${ROOT}\dist\${APP_NAME}-Setup-${VERSION}.exe"
!endif

!if ! /FileExists "${PUBLISH_DIR}\${APP_EXE}"
  !error "${PUBLISH_DIR}\${APP_EXE} is missing. Run: bash tools/build-release.sh"
!endif
!if ! /FileExists "${SEED_DIR}\profiles.json"
  !error "installer\${SEED_DIR}\profiles.json is missing (copy of LiteSynapse\data\*.json)."
!endif

; LS_INSTALL_FILES / LS_UNINSTALL_FILES: the exact list of published files, written by tools/build-release.sh.
; The uninstaller deletes only those files, so it is safe even in a folder the user shares with other things.
!if /FileExists "${FILE_LIST}"
  !include "${FILE_LIST}"
!else
  !warning "${FILE_LIST} not found: installing ${PUBLISH_DIR} as a whole; the uninstaller removes the known files only."
  !macro LS_INSTALL_FILES
    SetOutPath "$INSTDIR"
    File /r "${PUBLISH_DIR}\*.*"
  !macroend
  !macro LS_UNINSTALL_FILES
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\tools\remove-razer-filter-drivers.ps1"
    RMDir "$INSTDIR\tools"
  !macroend
!endif

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "FileFunc.nsh"
!include "WinMessages.nsh"
!include "x64.nsh"

Name "${APP_NAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION4}"
VIFileVersion "${VERSION4}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"

Var StartupState      ; ${BST_CHECKED} / ${BST_UNCHECKED}: "Start LiteSynapse with Windows"
Var StartupBox
Var DotNetVersion     ; "" when the .NET 10 Desktop Runtime is missing
Var WebView2Version   ; "" when the WebView2 Runtime is missing
Var PrereqsShown      ; 1 once the missing-runtime messages were shown
Var DataSource        ; "keep", a folder to copy from, or "seed"
Var OldCopyDir        ; the old Documents\LiteSynapse folder, "" when there is none (or it is $INSTDIR)
Var FinishText

; ---- Pages -----------------------------------------------------------------------------------------------

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Install ${APP_NAME} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Setup installs ${APP_NAME} ${VERSION} for your Windows account. It does not need administrator rights.$\r$\n$\r$\nIf ${APP_NAME} is running, Setup closes it first. Your profiles and settings are kept or copied over; existing ones are never overwritten.$\r$\n$\r$\nSetup does not install or remove any drivers or services.$\r$\n$\r$\n$_CLICK"
!insertmacro MUI_PAGE_WELCOME

!define MUI_DIRECTORYPAGE_TEXT_TOP "Setup will install ${APP_NAME} in the folder below. Your profiles and settings are stored in its $\"data$\" subfolder."
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY

Page custom OptionsShow OptionsLeave

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "${APP_NAME} is installed"
!define MUI_FINISHPAGE_TEXT_LARGE
!define MUI_FINISHPAGE_TEXT "$FinishText"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp
!define MUI_PAGE_CUSTOMFUNCTION_PRE FinishPre
!define MUI_PAGE_CUSTOMFUNCTION_SHOW FinishShow
!insertmacro MUI_PAGE_FINISH

!define MUI_UNCONFIRMPAGE_TEXT_TOP "${APP_NAME} will be removed from the folder below. You can choose to keep your profiles and settings."
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---- Helpers shared by the installer and the uninstaller --------------------------------------------------

!macro SHARED_FUNCTIONS un

; Stack: haystack, needle (top) -> 1 when haystack contains needle (case-insensitive), else 0.
Function ${un}StrContains
  Exch $1
  Exch
  Exch $0
  Push $2
  Push $3
  Push $4
  StrLen $2 $1
  StrCpy $3 0
  ${If} $2 = 0
    StrCpy $0 0
    Goto done
  ${EndIf}
  loop:
    StrCpy $4 $0 $2 $3
    StrCmp $4 "" notFound
    StrCmp $4 $1 found
    IntOp $3 $3 + 1
    Goto loop
  found:
    StrCpy $0 1
    Goto done
  notFound:
    StrCpy $0 0
  done:
  Pop $4
  Pop $3
  Pop $2
  ; The stack holds the caller's $0, then $1: restore both and leave the result on top.
  Exch $0
  Exch
  Pop $1
FunctionEnd

; -> 1 when LiteSynapse.exe runs (from any folder), else 0.
Function ${un}IsAppRunning
  Push $0
  Push $1
  ; The main instance holds the mutex. Access denied means it exists but belongs to an elevated process.
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${APP_MUTEX}") p.r0 ?e'
  Pop $1
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    StrCpy $0 1
  ${ElseIf} $1 = 5
    StrCpy $0 1
  ${Else}
    ; Command-line runs of the exe do not take the mutex: ask Windows for the process by name.
    nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /NH /FO CSV /FI "IMAGENAME eq ${APP_EXE}"'
    Pop $0
    Pop $1
    ${If} $0 = 0
      Push $1
      Push '"${APP_EXE}"'
      Call ${un}StrContains
      Pop $0
    ${Else}
      StrCpy $0 0
    ${EndIf}
  ${EndIf}
  Pop $1
  Exch $0
FunctionEnd

; Waits up to $0 x 250 ms for LiteSynapse to exit. -> 1 when it still runs.
Function ${un}WaitForExit
  Push $1
  Push $2
  StrCpy $2 1
  ${For} $1 1 $0
    Call ${un}IsAppRunning
    Pop $2
    ${If} $2 = 0
      ${Break}
    ${EndIf}
    Sleep 250
  ${Next}
  StrCpy $0 $2
  Pop $2
  Pop $1
FunctionEnd

; Closes every running LiteSynapse.exe (old or new location) without a console window.
; -> "not running", "closed", "closed (forced)". Aborts when it cannot be closed and the user cancels.
Function ${un}CloseApp
  Push $0
  Push $1
  Push $2
  StrCpy $2 "not running"
  retry:
  Call ${un}IsAppRunning
  Pop $0
  ${If} $0 = 1
    DetailPrint "Closing the running ${APP_NAME}..."
    ; First the same notice Windows sends at sign-out: the app puts the mouse back on its own on-board
    ; slot and stops its keyboard hook. Then a normal close request, and after ~2 s a forced one.
    ${For} $1 1 8
      FindWindow $0 "LiteSynapseWnd$1" "${APP_NAME}"
      ${If} $0 P<> 0
        SendMessage $0 ${WM_ENDSESSION} 1 0 /TIMEOUT=3000
      ${EndIf}
    ${Next}
    nsExec::Exec '"$SYSDIR\taskkill.exe" /IM ${APP_EXE}'
    Pop $0
    StrCpy $0 8
    Call ${un}WaitForExit
    StrCpy $2 "closed"
    ${If} $0 = 1
      nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE}'
      Pop $0
      StrCpy $0 12
      Call ${un}WaitForExit
      StrCpy $2 "closed (forced)"
    ${EndIf}
    ${If} $0 = 1
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "${APP_NAME} is still running and Setup could not close it (it may have been started as administrator).$\r$\n$\r$\nRight-click the ${APP_NAME} tray icon and choose Exit, then click Retry." /SD IDCANCEL IDRETRY retry
      DetailPrint "${APP_NAME} is still running: cancelled."
      Abort
    ${EndIf}
    DetailPrint "${APP_NAME} $2."
  ${EndIf}
  StrCpy $0 $2
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

!macroend

!insertmacro SHARED_FUNCTIONS ""
!insertmacro SHARED_FUNCTIONS "un."

; ---- Installer helpers -----------------------------------------------------------------------------------

; Stack: text -> appended to $INSTDIR\data\install.log (UTF-16) with a local timestamp, and shown in the details.
Function WriteLog
  Exch $0
  Push $1
  Push $2
  Push $3
  Push $4
  Push $5
  Push $6
  Push $7
  Push $8
  DetailPrint "$0"
  ${GetTime} "" "L" $1 $2 $3 $4 $5 $6 $7
  CreateDirectory "$INSTDIR\data"
  ClearErrors
  ${If} ${FileExists} "$INSTDIR\data\install.log"
    FileOpen $8 "$INSTDIR\data\install.log" a
    FileSeek $8 0 END
  ${Else}
    FileOpen $8 "$INSTDIR\data\install.log" w
    FileWriteWord $8 0xFEFF
  ${EndIf}
  ${IfNot} ${Errors}
    FileWriteUTF16LE $8 "$3-$2-$1 $5:$6:$7  $0$\r$\n"
    FileClose $8
  ${EndIf}
  Pop $8
  Pop $7
  Pop $6
  Pop $5
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

!macro LOG text
  Push "${text}"
  Call WriteLog
!macroend

; Sets $DotNetVersion and $WebView2Version ("" = missing).
Function DetectPrereqs
  Push $0
  Push $1
  ; .NET 10 Desktop Runtime (x64). $PROGRAMFILES64 is the 64-bit Program Files even in this 32-bit installer.
  StrCpy $DotNetVersion ""
  FindFirst $0 $1 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  ${DoWhile} $1 != ""
    ${If} ${FileExists} "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\$1\*.*"
      StrCpy $DotNetVersion $1
      ${Break}
    ${EndIf}
    FindNext $0 $1
  ${Loop}
  FindClose $0
  ; WebView2 Runtime: machine-wide install (32-bit registry view on 64-bit Windows), then per-user.
  SetRegView 64
  ReadRegStr $1 HKLM "SOFTWARE\WOW6432Node\${WEBVIEW2_KEY}" "pv"
  SetRegView 32
  ${If} $1 == ""
  ${OrIf} $1 == "0.0.0.0"
    ReadRegStr $1 HKLM "SOFTWARE\${WEBVIEW2_KEY}" "pv"
  ${EndIf}
  ${If} $1 == ""
  ${OrIf} $1 == "0.0.0.0"
    ReadRegStr $1 HKCU "Software\${WEBVIEW2_KEY}" "pv"
  ${EndIf}
  ${If} $1 == "0.0.0.0"
    StrCpy $1 ""
  ${EndIf}
  StrCpy $WebView2Version $1
  Pop $1
  Pop $0
FunctionEnd

; Sets $DataSource for $INSTDIR: "keep" (data already there), an old data folder to copy, or "seed".
Function DetectDataSource
  ${If} ${FileExists} "$INSTDIR\data\profiles.json"
    StrCpy $DataSource "keep"
  ${ElseIf} ${FileExists} "$DOCUMENTS\${APP_NAME}\data\profiles.json"
    StrCpy $DataSource "$DOCUMENTS\${APP_NAME}\data"
  ${ElseIf} ${FileExists} "$PROFILE\Documents\${APP_NAME}\data\profiles.json"
    StrCpy $DataSource "$PROFILE\Documents\${APP_NAME}\data"
  ${ElseIf} ${FileExists} "$APPDATA\${APP_NAME}\profiles.json"
    ; Older versions kept the files here (the app itself still imports this folder).
    StrCpy $DataSource "$APPDATA\${APP_NAME}"
  ${Else}
    StrCpy $DataSource "seed"
  ${EndIf}
FunctionEnd

; Sets $OldCopyDir to the old Documents\LiteSynapse folder, unless it is where we install.
Function DetectOldCopy
  StrCpy $OldCopyDir ""
  ${If} ${FileExists} "$DOCUMENTS\${APP_NAME}\*.*"
    StrCpy $OldCopyDir "$DOCUMENTS\${APP_NAME}"
  ${ElseIf} ${FileExists} "$PROFILE\Documents\${APP_NAME}\*.*"
    StrCpy $OldCopyDir "$PROFILE\Documents\${APP_NAME}"
  ${EndIf}
  ${If} $OldCopyDir == $INSTDIR
    StrCpy $OldCopyDir ""
  ${EndIf}
FunctionEnd

; Stack: source folder, target folder (top) -> number of items copied.
; Copies files and folders from source that the target does not have yet. Never overwrites, skips logs.
Function CopyMissing
  Exch $1
  Exch
  Exch $0
  Push $2
  Push $3
  Push $4
  StrCpy $4 0
  CreateDirectory "$1"
  FindFirst $2 $3 "$0\*.*"
  ${DoWhile} $3 != ""
    ${If} $3 == "."
    ${OrIf} $3 == ".."
    ${OrIf} $3 == "log.txt"
    ${OrIf} $3 == "log.old.txt"
      ; skip
    ${ElseIf} ${FileExists} "$0\$3\*.*"
      ${IfNot} ${FileExists} "$1\$3\*.*"
        ClearErrors
        CopyFiles /SILENT "$0\$3" "$1"
        ${If} ${Errors}
          DetailPrint "Could not copy the folder $0\$3"
        ${Else}
          IntOp $4 $4 + 1
        ${EndIf}
      ${EndIf}
    ${ElseIfNot} ${FileExists} "$1\$3"
      ClearErrors
      CopyFiles /SILENT "$0\$3" "$1\$3"
      ${If} ${Errors}
        DetailPrint "Could not copy $0\$3"
      ${Else}
        IntOp $4 $4 + 1
      ${EndIf}
    ${EndIf}
    FindNext $2 $3
  ${Loop}
  FindClose $2
  StrCpy $0 $4
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; Stack: folder -> 1 when it has any file or subfolder, else 0.
Function DirHasEntries
  Exch $0
  Push $1
  Push $2
  Push $3
  StrCpy $3 0
  FindFirst $1 $2 "$0\*.*"
  ${DoWhile} $2 != ""
    ${If} $2 != "."
    ${AndIf} $2 != ".."
      StrCpy $3 1
      ${Break}
    ${EndIf}
    FindNext $1 $2
  ${Loop}
  FindClose $1
  StrCpy $0 $3
  Pop $3
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; ---- Installer callbacks ---------------------------------------------------------------------------------

; $0 = 1 when Task Manager's Startup apps has turned the LiteSynapse Run value off (the value stays, Windows
; skips it at sign-in), otherwise 0.
Function StartupTurnedOff
  Push $1
  Push $2
  StrCpy $0 0
  System::Call '*(l 0, l 0) p.r1'
  ; HKEY_CURRENT_USER, RRF_RT_REG_BINARY, 16-byte buffer.
  System::Call 'advapi32::RegGetValueW(p -2147483647, w "${APPROVED_KEY}", w "${RUN_VALUE}", i 0x8, p 0, p r1, *i 16) i.r2'
  ${If} $2 = 0
    System::Call '*$1(&i1 .r0)'
    IntOp $0 $0 & 1
  ${EndIf}
  System::Free $1
  Pop $2
  Pop $1
FunctionEnd

Function .onInit
  SetShellVarContext current
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "LiteSynapse.setup") p.r1 ?e'
  Pop $0
  ${If} $0 = 183
    MessageBox MB_OK|MB_ICONEXCLAMATION "${APP_NAME} Setup is already running." /SD IDOK
    Abort
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "${APP_NAME} needs 64-bit Windows." /SD IDOK
    Abort
  ${EndIf}
  ; "Start with Windows" starts checked when a LiteSynapse Run value already exists and Task Manager's
  ; Startup apps has not turned it off.
  ReadRegStr $0 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $0 != ""
    Call StartupTurnedOff
    ${If} $0 = 1
      StrCpy $StartupState ${BST_UNCHECKED}
    ${Else}
      StrCpy $StartupState ${BST_CHECKED}
    ${EndIf}
  ${Else}
    StrCpy $StartupState ${BST_UNCHECKED}
  ${EndIf}
  StrCpy $PrereqsShown 0
  Call DetectPrereqs
FunctionEnd

Function DirectoryLeave
  ; LiteSynapse should have its own folder: the uninstaller removes it only when it is empty, and
  ; "data" inside it holds the profiles. Ask before using a folder that already holds other things.
  StrCpy $0 "$INSTDIR" "" -12
  ${If} $0 != "\${APP_NAME}"
  ${AndIfNot} ${FileExists} "$INSTDIR\${APP_EXE}"
  ${AndIfNot} ${FileExists} "$INSTDIR\${UNINST_EXE}"
  ${AndIfNot} ${FileExists} "$INSTDIR\data\profiles.json"
    Push "$INSTDIR"
    Call DirHasEntries
    Pop $0
    ${If} $0 = 1
      MessageBox MB_YESNO|MB_ICONQUESTION "$INSTDIR already contains other files.$\r$\n$\r$\nInstall into $INSTDIR\${APP_NAME} instead? (recommended)" IDNO checkWritable
      StrCpy $INSTDIR "$INSTDIR\${APP_NAME}"
      ; Show the new folder in the page's text box (control 1019) and stay on the page.
      FindWindow $0 "#32770" "" $HWNDPARENT
      GetDlgItem $0 $0 1019
      SendMessage $0 ${WM_SETTEXT} 0 "STR:$INSTDIR"
      Abort
    ${EndIf}
  ${EndIf}
  checkWritable:
  ; Per-user install: the folder must be writable without administrator rights.
  StrCpy $1 0
  ${IfNot} ${FileExists} "$INSTDIR\*.*"
    StrCpy $1 1
  ${EndIf}
  ClearErrors
  CreateDirectory "$INSTDIR"
  FileOpen $0 "$INSTDIR\litesynapse-write-test.tmp" w
  ${If} ${Errors}
    StrCpy $2 0
  ${Else}
    FileClose $0
    Delete "$INSTDIR\litesynapse-write-test.tmp"
    StrCpy $2 1
  ${EndIf}
  ${If} $1 = 1
    RMDir "$INSTDIR"
  ${EndIf}
  ${If} $2 = 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "Setup cannot write to $INSTDIR without administrator rights.$\r$\n$\r$\nChoose a folder in your user profile, for example the default one." /SD IDOK
    Abort
  ${EndIf}
FunctionEnd

Function OptionsShow
  !insertmacro MUI_HEADER_TEXT "Options" "Choose how ${APP_NAME} starts and check what it needs."
  Call DetectDataSource
  Call DetectPrereqs
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateCheckbox} 0 0 100% 12u "&Start ${APP_NAME} with Windows (in the tray)"
  Pop $StartupBox
  ${NSD_SetState} $StartupBox $StartupState
  ${NSD_CreateLabel} 12u 13u 288u 10u "You can change this later from the tray menu."
  Pop $0

  ${NSD_CreateGroupBox} 0 28u 100% 40u "Profiles and settings"
  Pop $0
  ${If} $DataSource == "keep"
    StrCpy $1 "The profiles and settings already in this folder are kept as they are."
  ${ElseIf} $DataSource == "seed"
    StrCpy $1 "No existing profiles were found. ${APP_NAME} starts with the bundled profiles."
  ${Else}
    StrCpy $1 "Your profiles and settings are copied from $DataSource. That folder is not changed."
  ${EndIf}
  ${NSD_CreateLabel} 8u 40u 284u 24u "$1"
  Pop $0

  ${NSD_CreateGroupBox} 0 74u 100% 52u "Requirements"
  Pop $0
  ${If} $DotNetVersion != ""
    StrCpy $1 ".NET 10 Desktop Runtime: installed ($DotNetVersion)."
  ${Else}
    StrCpy $1 ".NET 10 Desktop Runtime: NOT installed. ${APP_NAME} needs it to start."
  ${EndIf}
  ${NSD_CreateLabel} 8u 86u 284u 16u "$1"
  Pop $0
  ${If} $WebView2Version != ""
    StrCpy $1 "WebView2 Runtime: installed ($WebView2Version)."
  ${Else}
    StrCpy $1 "WebView2 Runtime: not installed. ${APP_NAME} works, but shows its classic window until it is."
  ${EndIf}
  ${NSD_CreateLabel} 8u 104u 284u 18u "$1"
  Pop $0

  nsDialogs::Show
FunctionEnd

Function OptionsLeave
  ${NSD_GetState} $StartupBox $StartupState
  ${If} $PrereqsShown = 1
    Return
  ${EndIf}
  StrCpy $PrereqsShown 1
  ${If} $DotNetVersion == ""
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "${APP_NAME} needs the .NET 10 Desktop Runtime (x64), which is not installed.$\r$\n$\r$\nYes: open the official download page (pick $\".NET Desktop Runtime$\" for Windows x64). Setup continues; install the runtime before you start ${APP_NAME}.$\r$\n$\r$\nNo: continue without opening the page." /SD IDNO IDNO skipDotNet
    ExecShell "open" "${DOTNET_URL}"
  skipDotNet:
  ${EndIf}
  ${If} $WebView2Version == ""
    MessageBox MB_YESNO|MB_ICONINFORMATION "The Microsoft Edge WebView2 Runtime is not installed. ${APP_NAME} works without it, but shows its classic window instead of the new one.$\r$\n$\r$\nYes: open the official download page (the $\"Evergreen Bootstrapper$\" is the small download). Setup continues.$\r$\n$\r$\nNo: continue without opening the page." /SD IDNO IDNO skipWebView2
    ExecShell "open" "${WEBVIEW2_URL}"
  skipWebView2:
  ${EndIf}
FunctionEnd

Function FinishPre
  Call DetectPrereqs
  Call DetectOldCopy
  StrCpy $FinishText "${APP_NAME} ${VERSION} is installed."
  ${If} $DotNetVersion == ""
    StrCpy $FinishText "$FinishText$\r$\n$\r$\nInstall the .NET 10 Desktop Runtime (x64) before you start ${APP_NAME}: ${DOTNET_URL}"
  ${EndIf}
  StrCpy $FinishText "$FinishText$\r$\n$\r$\nTo remove Razer leftovers (drivers and services that can add input latency), open ${APP_NAME} > Settings > Performance."
  ${If} $OldCopyDir != ""
    StrCpy $FinishText "$FinishText$\r$\n$\r$\nThe old copy in $OldCopyDir is no longer used. Once you have checked your profiles, you can delete that folder yourself."
  ${EndIf}
FunctionEnd

Function FinishShow
  ${If} $DotNetVersion == ""
    SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_UNCHECKED} 0
  ${EndIf}
  ; More room for the text: the Launch checkbox moves to the bottom of the page and the text grows down to it.
  System::Call '*(i 0, i 0, i 0, i 0) p.r0'
  ${If} $0 P<> 0
    System::Call 'user32::GetClientRect(p $mui.FinishPage, p r0)'
    System::Call '*$0(i, i, i, i .r4)'
    System::Call 'user32::GetWindowRect(p $mui.FinishPage.Run, p r0)'
    System::Call 'user32::MapWindowPoints(p 0, p $mui.FinishPage, p r0, i 2)'
    System::Call '*$0(i .r5, i .r6, i .r7, i .r8)'
    System::Call 'user32::GetWindowRect(p $mui.FinishPage.Text, p r0)'
    System::Call 'user32::MapWindowPoints(p 0, p $mui.FinishPage, p r0, i 2)'
    System::Call '*$0(i .r1, i .r2, i .r3, i)'
    System::Free $0
    IntOp $9 $8 - $6     ; checkbox height
    IntOp $8 $4 - $9
    IntOp $8 $8 - $9     ; new checkbox top: one checkbox height above the bottom
    IntOp $7 $8 - $2
    IntOp $7 $7 - $9     ; new text height, ending one checkbox height above it
    ${If} $8 > $6
    ${AndIf} $7 > 0
      System::Call 'user32::SetWindowPos(p $mui.FinishPage.Run, p 0, i r5, i r8, i 0, i 0, i 0x15)'
      IntOp $3 $3 - $1
      System::Call 'user32::SetWindowPos(p $mui.FinishPage.Text, p 0, i 0, i 0, i r3, i r7, i 0x16)'
    ${EndIf}
  ${EndIf}
FunctionEnd

Function LaunchApp
  SetOutPath "$INSTDIR"
  Exec '"$INSTDIR\${APP_EXE}"'
FunctionEnd

; ---- Install ---------------------------------------------------------------------------------------------

Section "${APP_NAME}" SecMain
  SectionIn RO
  SetShellVarContext current

  Call CloseApp
  Pop $9
  !insertmacro LOG "Setup ${APP_NAME} ${VERSION} into $INSTDIR (running app: $9)"

  ; Files of the old (not single-file) build, only when installing over that old folder.
  ${If} ${FileExists} "$INSTDIR\LiteSynapse.dll"
    Delete "$INSTDIR\LiteSynapse.dll"
    Delete "$INSTDIR\LiteSynapse.deps.json"
    Delete "$INSTDIR\LiteSynapse.runtimeconfig.json"
    Delete "$INSTDIR\LiteSynapse.pdb"
    !insertmacro LOG "Removed the old build's LiteSynapse.dll, .deps.json, .runtimeconfig.json and .pdb"
  ${EndIf}

  SetOverwrite on
  !insertmacro LS_INSTALL_FILES
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\${UNINST_EXE}"

  ; Profiles and settings: first match wins; nothing that exists in $INSTDIR\data is overwritten.
  Call DetectDataSource
  ${If} $DataSource == "keep"
    !insertmacro LOG "Profiles and settings: kept the existing $INSTDIR\data"
  ${ElseIf} $DataSource == "seed"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR\seed"
    File "${SEED_DIR}\*.json"
    SetOutPath "$INSTDIR"
    Push "$PLUGINSDIR\seed"
    Push "$INSTDIR\data"
    Call CopyMissing
    Pop $0
    !insertmacro LOG "Profiles and settings: none found; installed the bundled profiles ($0 files)"
  ${Else}
    Push "$DataSource"
    Push "$INSTDIR\data"
    Call CopyMissing
    Pop $0
    !insertmacro LOG "Profiles and settings: copied $0 files/folders from $DataSource (log files skipped; the old folder was not changed)"
  ${EndIf}

  ; Shortcuts (the Desktop one replaces the old copy's shortcut of the same name).
  SetOutPath "$INSTDIR"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "Profiles for the Razer Naga Left-Handed and BlackWidow Lite"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "Profiles for the Razer Naga Left-Handed and BlackWidow Lite"
  !insertmacro LOG "Shortcuts: Start Menu and Desktop"

  ; Start with Windows.
  ReadRegStr $1 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $StartupState == ${BST_CHECKED}
    WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}" --tray'
    ; Turned off in Task Manager's Startup apps before: the ticked box turns it back on.
    DeleteRegValue HKCU "${APPROVED_KEY}" "${RUN_VALUE}"
    !insertmacro LOG "Start with Windows: on"
  ${ElseIf} $1 != ""
    ; Only remove the value when it starts a LiteSynapse.exe (this or an older copy).
    Push $1
    Push "\${APP_EXE}"
    Call StrContains
    Pop $0
    ${If} $0 = 1
      DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
      DeleteRegValue HKCU "${APPROVED_KEY}" "${RUN_VALUE}"
      !insertmacro LOG "Start with Windows: off (removed $1)"
    ${Else}
      !insertmacro LOG "Start with Windows: off (left the unrelated Run value $1)"
    ${EndIf}
  ${Else}
    !insertmacro LOG "Start with Windows: off"
  ${EndIf}

  ; Apps & features entry.
  ${GetSize} "$INSTDIR" "/S=0K /G=0" $0 $1 $2
  ${GetSize} "$INSTDIR\tools" "/S=0K /G=1" $3 $1 $2
  IntOp $0 $0 + $3
  ${GetTime} "" "L" $3 $4 $5 $6 $7 $8 $9
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\${UNINST_EXE}"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\${UNINST_EXE}" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallDate" "$5$4$3"
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ${If} $DotNetVersion != ""
    StrCpy $1 "$DotNetVersion"
  ${Else}
    StrCpy $1 "NOT installed"
  ${EndIf}
  ${If} $WebView2Version != ""
    StrCpy $2 "$WebView2Version"
  ${Else}
    StrCpy $2 "not installed (classic window)"
  ${EndIf}
  !insertmacro LOG "Runtimes: .NET 10 Desktop $1; WebView2 $2"
  !insertmacro LOG "Done"
SectionEnd

; ---- Uninstall -------------------------------------------------------------------------------------------

Function un.onInit
  SetShellVarContext current
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  ; Guard against a broken $INSTDIR (the uninstaller always runs from the install folder).
  StrLen $0 "$INSTDIR"
  ${If} $0 < 4
    Abort "Unexpected install folder: $INSTDIR"
  ${EndIf}

  StrCpy $8 0
  ${If} ${FileExists} "$INSTDIR\data\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Also delete your profiles and settings?$\r$\n$\r$\n$INSTDIR\data$\r$\n$\r$\nChoose No to keep them for a later reinstall." /SD IDNO IDNO keepData
    StrCpy $8 1
  keepData:
  ${EndIf}

  Call un.CloseApp
  Pop $9

  !insertmacro LS_UNINSTALL_FILES
  Delete "$INSTDIR\${UNINST_EXE}"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; The Run value, only when it starts this copy.
  ReadRegStr $1 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $1 != ""
    Push $1
    Push "$INSTDIR\${APP_EXE}"
    Call un.StrContains
    Pop $0
    ${If} $0 = 1
      DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
      DeleteRegValue HKCU "${APPROVED_KEY}" "${RUN_VALUE}"
    ${EndIf}
  ${EndIf}
  DeleteRegKey HKCU "${UNINST_KEY}"

  ; The new window's WebView2 cache (WebWindow.cs). Its helper processes exit with the app.
  ${If} $9 != "not running"
    Sleep 1000
  ${EndIf}
  RMDir /r "$LOCALAPPDATA\${APP_NAME}\WebView2"
  RMDir "$LOCALAPPDATA\${APP_NAME}"

  ${If} $8 = 1
    RMDir /r "$INSTDIR\data"
    DetailPrint "Deleted $INSTDIR\data"
  ${ElseIf} ${FileExists} "$INSTDIR\data\*.*"
    DetailPrint "Kept your profiles and settings in $INSTDIR\data"
    ${GetTime} "" "L" $1 $2 $3 $4 $5 $6 $7
    ClearErrors
    ${If} ${FileExists} "$INSTDIR\data\install.log"
      FileOpen $0 "$INSTDIR\data\install.log" a
      FileSeek $0 0 END
    ${Else}
      FileOpen $0 "$INSTDIR\data\install.log" w
      FileWriteWord $0 0xFEFF
    ${EndIf}
    ${IfNot} ${Errors}
      FileWriteUTF16LE $0 "$3-$2-$1 $5:$6:$7  Uninstalled ${APP_NAME} ${VERSION}; kept this data folder$\r$\n"
      FileClose $0
    ${EndIf}
  ${EndIf}
  RMDir "$INSTDIR"
SectionEnd
