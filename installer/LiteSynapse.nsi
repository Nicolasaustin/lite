; LiteSynapse installer (NSIS 3, Unicode, Modern UI 2).
;
; Installs into Program Files (only an administrator can change it: LiteSynapse runs as administrator, so its own
; files must not be writable by programs that do not), with this account's profiles and settings in
; %LOCALAPPDATA%\LiteSynapse\data, this account's shortcuts and LiteSynapse's own sign-in task. It runs as
; administrator for Program Files, for closing a running copy and for the sign-in task. It never installs or
; removes drivers or services. An older per-user install (%LOCALAPPDATA%\Programs\LiteSynapse) is moved over:
; its profiles are copied, its program files removed, its data folder kept as a backup.
;
; Build everything (tests, publish, installer):  bash tools/build-release.sh
; Compile only, after a publish to build/publish:  makensis installer/LiteSynapse.nsi
;   -DVERSION=x.y.z      version shown and used in the file name (default: AssemblyInformationalVersion)
;   -DVERSION4=a.b.c.d   numeric version for the file properties (default: AssemblyFileVersion)
;   -DPUBLISH_DIR=path   published app (default: ..\build\publish)
;   -DOUTFILE=path       output file (default: ..\dist\LiteSynapse-Setup-<VERSION>.exe)
; Relative paths are relative to this folder (makensis changes into it).

Unicode true
; The installer and uninstaller run as administrator: NSIS before 3.09 had an elevated-uninstaller flaw
; (CVE-2023-37378).
!if ${NSIS_PACKEDVERSION} < 0x03009000
  !error "NSIS 3.09 or newer is required (the installer and uninstaller run as administrator)."
!endif
ManifestDPIAware true
RequestExecutionLevel admin
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
; LiteSynapse's own HKCU key: StartWithWindows mirrors the sign-in task for the checkbox below, and StartupRequest
; asks the app to set the task on its next start when it could not be done here (StartupTask.cs).
!define APP_REG_KEY "Software\LiteSynapse"
!define DOTNET_URL "https://dotnet.microsoft.com/download/dotnet/10.0"

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
InstallDir "$PROGRAMFILES64\${APP_NAME}"
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

Var DataDir           ; this account's profiles and settings: $LOCALAPPDATA\LiteSynapse\data (Cfg.Dir)
Var OldInstall        ; an older per-user install folder to move away from, or ""
Var StartupState      ; ${BST_CHECKED} / ${BST_UNCHECKED}: "Start LiteSynapse with Windows"
Var StartupBox
Var DotNetVersion     ; "" when the .NET 10 Desktop Runtime is missing
Var PrereqsShown      ; 1 once the missing-runtime messages were shown
Var DataSource        ; "keep", a folder to copy from, or "seed"
Var OldCopyDir        ; the old Documents\LiteSynapse folder, "" when there is none (or it is $INSTDIR)
Var FinishText

; ---- Pages -----------------------------------------------------------------------------------------------

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Install ${APP_NAME} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Setup installs ${APP_NAME} ${VERSION} in Program Files, with your profiles and settings in your own AppData folder. It asks for administrator rights because ${APP_NAME} runs as administrator: that is what lets its macros and Snap Tap reach games that run as administrator.$\r$\n$\r$\nIf ${APP_NAME} is running, Setup closes it first. Your profiles and settings are kept or copied over; existing ones are never overwritten.$\r$\n$\r$\nSetup does not install or remove any drivers or services.$\r$\n$\r$\n$_CLICK"
!insertmacro MUI_PAGE_WELCOME


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
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "${APP_NAME} is still running and Setup could not close it.$\r$\n$\r$\nRight-click the ${APP_NAME} tray icon and choose Exit, then click Retry." /SD IDCANCEL IDRETRY retry
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

; Stack: text -> appended to $DataDir\install.log (UTF-16) with a local timestamp, and shown in the details.
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
  CreateDirectory "$DataDir"
  ClearErrors
  ${If} ${FileExists} "$DataDir\install.log"
    FileOpen $8 "$DataDir\install.log" a
    FileSeek $8 0 END
  ${Else}
    FileOpen $8 "$DataDir\install.log" w
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

; Sets $DotNetVersion ("" = the .NET 10 Desktop Runtime is missing).
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
  Pop $1
  Pop $0
FunctionEnd

; Sets $DataSource for $DataDir: "keep" (data already there), an old data folder to copy, or "seed".
Function DetectDataSource
  ${If} ${FileExists} "$DataDir\profiles.json"
    StrCpy $DataSource "keep"
  ${ElseIf} $OldInstall != ""
  ${AndIf} ${FileExists} "$OldInstall\data\profiles.json"
    StrCpy $DataSource "$OldInstall\data"
  ${ElseIf} ${FileExists} "$INSTDIR\data\profiles.json"
    ; A 1.2.6 copy installed into this same folder (the old build had a folder choice), with data beside the exe.
    StrCpy $DataSource "$INSTDIR\data"
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
  SetRegView 64
  StrCpy $DataDir "$LOCALAPPDATA\${APP_NAME}\data"
  ; A per-user install from 1.2.6 or earlier, to move away from.
  ReadRegStr $OldInstall HKCU "${UNINST_KEY}" "InstallLocation"
  ${If} $OldInstall == ""
    StrCpy $OldInstall "$LOCALAPPDATA\Programs\${APP_NAME}"
  ${EndIf}
  ${IfNot} ${FileExists} "$OldInstall\${APP_EXE}"
  ${AndIfNot} ${FileExists} "$OldInstall\data\profiles.json"
    StrCpy $OldInstall ""
  ${EndIf}
  ${If} $OldInstall == $INSTDIR
    StrCpy $OldInstall ""
  ${EndIf}
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
  ; "Start with Windows" starts as the sign-in task is now (mirrored by the app), or, coming from a version that
  ; used the Run key, checked when that value exists and Task Manager's Startup apps has not turned it off.
  ReadRegStr $0 HKCU "${APP_REG_KEY}" "StartWithWindows"
  ${If} $0 == ""
    ; A choice an earlier Setup left for LiteSynapse to carry out, not done yet.
    ReadRegStr $0 HKCU "${APP_REG_KEY}" "StartupRequest"
  ${EndIf}
  ReadRegStr $1 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $0 == "1"
    StrCpy $StartupState ${BST_CHECKED}
  ${ElseIf} $0 == "0"
    StrCpy $StartupState ${BST_UNCHECKED}
  ${ElseIf} $1 != ""
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
    StrCpy $1 "The profiles and settings already in $DataDir are kept as they are."
  ${ElseIf} $DataSource == "seed"
    StrCpy $1 "No existing profiles were found. ${APP_NAME} starts with the bundled profiles."
  ${Else}
    StrCpy $1 "Your profiles and settings are copied from $DataSource. That folder is not changed."
  ${EndIf}
  ${NSD_CreateLabel} 8u 40u 284u 24u "$1"
  Pop $0

  ${NSD_CreateGroupBox} 0 74u 100% 34u "Requirements"
  Pop $0
  ${If} $DotNetVersion != ""
    StrCpy $1 ".NET 10 Desktop Runtime: installed ($DotNetVersion)."
  ${Else}
    StrCpy $1 ".NET 10 Desktop Runtime: NOT installed. ${APP_NAME} needs it to start."
  ${EndIf}
  ${NSD_CreateLabel} 8u 86u 284u 16u "$1"
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
    ; Through Explorer, which is not elevated, so the browser does not start with Setup's administrator rights.
    Exec '"$WINDIR\explorer.exe" "${DOTNET_URL}"'
  skipDotNet:
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
  ${If} $OldInstall != ""
  ${AndIf} ${FileExists} "$OldInstall\data\*.*"
    StrCpy $FinishText "$FinishText$\r$\n$\r$\nYour profiles and settings now live in $DataDir. The old copy in $OldInstall\data is kept as a backup; delete it once you have checked your profiles."
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
  ; LiteSynapse.exe is a single file. A DLL left in its folder by an older version (the WebView2 ones) would make
  ; the sign-in task refuse to start it (StartupTask.cs), and nothing current needs one.
  Delete "$INSTDIR\*.dll"
  !insertmacro LS_INSTALL_FILES
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\${UNINST_EXE}"

  ; Profiles and settings: first match wins; nothing that exists in $DataDir is overwritten.
  Call DetectDataSource
  ${If} $DataSource == "keep"
    !insertmacro LOG "Profiles and settings: kept the existing $DataDir"
  ${ElseIf} $DataSource == "seed"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR\seed"
    File "${SEED_DIR}\*.json"
    SetOutPath "$INSTDIR"
    Push "$PLUGINSDIR\seed"
    Push "$DataDir"
    Call CopyMissing
    Pop $0
    !insertmacro LOG "Profiles and settings: none found; installed the bundled profiles ($0 files)"
  ${Else}
    Push "$DataSource"
    Push "$DataDir"
    Call CopyMissing
    Pop $0
    !insertmacro LOG "Profiles and settings: copied $0 files/folders from $DataSource (log files skipped; the old folder was not changed)"
  ${EndIf}

  ; The per-user copy 1.2.6 and earlier installed: its program files go (any program could replace them, and they
  ; were started as administrator), its data folder stays as a backup of the profiles copied above.
  ${If} $OldInstall != ""
    Delete "$OldInstall\${APP_EXE}"
    Delete "$OldInstall\${UNINST_EXE}"
    ; Only LiteSynapse's own DLLs (a shared install folder may hold other programs' files).
    Delete "$OldInstall\LiteSynapse.dll"
    Delete "$OldInstall\WebView2Loader.dll"
    Delete "$OldInstall\Microsoft.Web.WebView2.Core.dll"
    Delete "$OldInstall\Microsoft.Web.WebView2.WinForms.dll"
    Delete "$OldInstall\LiteSynapse.deps.json"
    Delete "$OldInstall\LiteSynapse.runtimeconfig.json"
    Delete "$OldInstall\LiteSynapse.pdb"
    Delete "$OldInstall\tools\remove-razer-filter-drivers.ps1"
    RMDir "$OldInstall\tools"
    RMDir "$OldInstall"
    ${If} ${FileExists} "$OldInstall\${APP_EXE}"
      !insertmacro LOG "Old per-user install in $OldInstall: LiteSynapse.exe could not be removed (in use?)"
    ${Else}
      !insertmacro LOG "Moved from the per-user install in $OldInstall: program files removed, data folder kept as a backup"
    ${EndIf}
  ${EndIf}
  ; The old per-user Apps & features entry (the new one is under HKLM): always, including a build-17 install that
  ; was in this same Program Files folder, so the list never shows two LiteSynapse entries.
  DeleteRegKey HKCU "${UNINST_KEY}"

  ; Shortcuts (they replace the old copy's shortcuts of the same name).
  SetOutPath "$INSTDIR"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "Profiles for the Razer Naga Left-Handed and BlackWidow Lite"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "Profiles for the Razer Naga Left-Handed and BlackWidow Lite"
  !insertmacro LOG "Shortcuts: Start Menu and Desktop"

  ; Start with Windows: a Task Scheduler task that starts LiteSynapse at sign-in as administrator - the Run key
  ; cannot start a program that asks for administrator rights. LiteSynapse registers it itself, so the task is
  ; defined in one place; when it cannot run yet (no .NET runtime), it does it on its next start instead.
  ReadRegStr $1 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $1 != ""
    ; An older version's Run value, only when it starts a LiteSynapse.exe.
    Push $1
    Push "\${APP_EXE}"
    Call StrContains
    Pop $0
    ${If} $0 = 1
      DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
      DeleteRegValue HKCU "${APPROVED_KEY}" "${RUN_VALUE}"
      !insertmacro LOG "Start with Windows: removed the old Run value $1"
    ${EndIf}
  ${EndIf}
  ${If} $StartupState == ${BST_CHECKED}
    StrCpy $2 "--register-startup"
    StrCpy $3 "1"
  ${Else}
    StrCpy $2 "--unregister-startup"
    StrCpy $3 "0"
  ${EndIf}
  ${If} $DotNetVersion == ""
    ; LiteSynapse cannot run yet: it does it on its first start, once the runtime is installed.
    WriteRegStr HKCU "${APP_REG_KEY}" "StartupRequest" "$3"
    !insertmacro LOG "Start with Windows: left for LiteSynapse's first start (no .NET runtime yet)"
  ${Else}
    ; No ".NET is required" dialog from the app host if something is still missing: it would block Setup.
    System::Call 'kernel32::SetEnvironmentVariable(t "DOTNET_DISABLE_GUI_ERRORS", t "1")'
    ClearErrors
    ExecWait '"$INSTDIR\${APP_EXE}" $2' $0
    ${If} ${Errors}
      WriteRegStr HKCU "${APP_REG_KEY}" "StartupRequest" "$3"
      !insertmacro LOG "Start with Windows: left for LiteSynapse's next start ($2 could not be run)"
    ${ElseIf} $0 != 0
      WriteRegStr HKCU "${APP_REG_KEY}" "StartupRequest" "$3"
      !insertmacro LOG "Start with Windows: left for LiteSynapse's next start ($2 returned $0)"
    ${Else}
      DeleteRegValue HKCU "${APP_REG_KEY}" "StartupRequest"
      !insertmacro LOG "Start with Windows: $2"
    ${EndIf}
    System::Call 'kernel32::SetEnvironmentVariable(t "DOTNET_DISABLE_GUI_ERRORS", p 0)'
  ${EndIf}

  ; Apps & features entry (for the machine: the program is in Program Files).
  ${GetSize} "$INSTDIR" "/S=0K /G=0" $0 $1 $2
  ${GetSize} "$INSTDIR\tools" "/S=0K /G=1" $3 $1 $2
  IntOp $0 $0 + $3
  ${GetTime} "" "L" $3 $4 $5 $6 $7 $8 $9
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\${UNINST_EXE}"'
  WriteRegStr HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\${UNINST_EXE}" /S'
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallDate" "$5$4$3"
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" $0
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1

  ${If} $DotNetVersion != ""
    StrCpy $1 "$DotNetVersion"
  ${Else}
    StrCpy $1 "NOT installed"
  ${EndIf}
  !insertmacro LOG "Runtimes: .NET 10 Desktop $1"
  !insertmacro LOG "Done"
SectionEnd

; ---- Uninstall -------------------------------------------------------------------------------------------

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $DataDir "$LOCALAPPDATA\${APP_NAME}\data"
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  ; Guard against a broken $INSTDIR (the uninstaller always runs from the install folder).
  StrLen $0 "$INSTDIR"
  ${If} $0 < 4
    Abort "Unexpected install folder: $INSTDIR"
  ${EndIf}

  StrCpy $8 0
  ${If} ${FileExists} "$DataDir\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Also delete your profiles and settings?$\r$\n$\r$\n$DataDir$\r$\n$\r$\nChoose No to keep them for a later reinstall." /SD IDNO IDNO keepData
    StrCpy $8 1
  keepData:
  ${EndIf}

  Call un.CloseApp
  Pop $9

  ; The sign-in task runs as administrator: LiteSynapse removes it (before its files go). When it cannot run
  ; (no runtime, file missing), the task is deleted by the name LiteSynapse recorded. Running it may create
  ; data\log.txt, so whether data existed before is noted for the clean-up below.
  StrCpy $7 0
  ${IfNot} ${FileExists} "$DataDir\*.*"
    StrCpy $7 1
  ${EndIf}
  System::Call 'kernel32::SetEnvironmentVariable(t "DOTNET_DISABLE_GUI_ERRORS", t "1")'
  ClearErrors
  StrCpy $0 1
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    ExecWait '"$INSTDIR\${APP_EXE}" --unregister-startup' $0
  ${EndIf}
  ${If} ${Errors}
  ${OrIf} $0 != 0
    ReadRegStr $1 HKCU "${APP_REG_KEY}" "TaskName"
    ${If} $1 != ""
      nsExec::Exec '"$SYSDIR\schtasks.exe" /Delete /TN "$1" /F'
      Pop $0
    ${EndIf}
  ${EndIf}
  DeleteRegKey HKCU "${APP_REG_KEY}"
  ${If} $7 = 1
    ; There was no data before: anything there now is the log LiteSynapse just wrote.
    RMDir /r "$DataDir"
  ${EndIf}

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
  DeleteRegKey HKLM "${UNINST_KEY}"
  ; And an older per-user install's entry, if one is still there.
  DeleteRegKey HKCU "${UNINST_KEY}"

  ; The browser cache an older LiteSynapse left behind, when its window still used WebView2. Nothing writes
  ; this any more; it is removed so uninstalling leaves nothing of it.
  ${If} $9 != "not running"
    Sleep 1000
  ${EndIf}
  RMDir /r "$LOCALAPPDATA\${APP_NAME}\WebView2"

  ${If} $8 = 1
    RMDir /r "$DataDir"
    DetailPrint "Deleted $DataDir"
  ${ElseIf} ${FileExists} "$DataDir\*.*"
    DetailPrint "Kept your profiles and settings in $DataDir"
    ${GetTime} "" "L" $1 $2 $3 $4 $5 $6 $7
    ClearErrors
    ${If} ${FileExists} "$DataDir\install.log"
      FileOpen $0 "$DataDir\install.log" a
      FileSeek $0 0 END
    ${Else}
      FileOpen $0 "$DataDir\install.log" w
      FileWriteWord $0 0xFEFF
    ${EndIf}
    ${IfNot} ${Errors}
      FileWriteUTF16LE $0 "$3-$2-$1 $5:$6:$7  Uninstalled ${APP_NAME} ${VERSION}; kept this data folder$\r$\n"
      FileClose $0
    ${EndIf}
  ${EndIf}
  ; After the data folder is gone (when the user chose to delete it), the now-empty parent too.
  RMDir "$LOCALAPPDATA\${APP_NAME}"
  RMDir "$INSTDIR"
SectionEnd
