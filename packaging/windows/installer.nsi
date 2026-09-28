; Builds the Windows installer for one architecture:
;
;   /DAPPARCH=x64|x86|arm64     selects the install directory and the registry view
;   /DVERSION=1.0.20260928      the stamp shared by every package on this build
;   /DWINVERSION=1.0.2462.0     four field version for the file version resource
;   /DPAYLOAD=<dir>             the publish tree to pack, ElectronicPointer.exe and friends
;   /DICON=<ico>                the installer and shortcut icon
;   /DOUTFILE=<path>            where the installer is written
;
; The script is UTF-8, so makensis has to be called with /INPUTCHARSET UTF8. The UI language
; is Simplified Chinese; every literal a translator would have to touch is a LangString.
;
; One build, one install: there is no beta or dev channel, so the installer never puts a
; suffix on the install directory, the registry key or the shortcut name.

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef WINVERSION
  !define WINVERSION "1.0.0.0"
!endif
!ifndef APPARCH
  !define APPARCH "x64"
!endif
!ifndef PAYLOAD
  !define PAYLOAD "artifacts\stage\windows\installer"
!endif
!ifndef ICON
  !define ICON "assets\electronicpointer.ico"
!endif
!ifndef OUTFILE
  !define OUTFILE "installer.exe"
!endif

!define APPNAME "电子教鞭"
!define APPNAME_EN "ElectronicPointer"
!define PUBLISHER "Mutantcat Working Group"
!define EXENAME "ElectronicPointer.exe"
!define UNINSTALLER "Uninstall.exe"
!define HOMEPAGE "https://github.com/Mutantcat-Working-Group/ElectronicPointer"
; Registry locations. The uninstall key is written to the native view of the machine for
; the 64 bit builds, so a x64 install never lands in Wow6432Node next to an x86 one.
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ElectronicPointer"
!define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUNVALUE "ElectronicPointer"

; One target for all three payloads, and the architecture of the payload never depends on
; the architecture of the installer binary: the installer writes the 64 bit registry view
; through SetRegView 64 and installs into $PROGRAMFILES64, so the 64 bit and the ARM64
; payloads land in the same places they would land from a native installer. NSIS ships stubs
; for the x86-unicode target only, and a hosted runner installs stock NSIS, so an amd64 or
; arm64 stub would have to be built from source first. This is also what most of the NSIS
; ecosystem ships: the familiar 32 bit installer binary, the native application inside it.
Target x86-unicode

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"

; Per machine install: the user is asked for elevation, which is also what makes the
; all users start menu and the registry key writable.
RequestExecutionLevel admin
Name "${APPNAME} ${APPNAME_EN}"
OutFile "${OUTFILE}"
BrandingText "${APPNAME} ${APPNAME_EN} ${VERSION}"

!if "${APPARCH}" == "x86"
  InstallDir "$PROGRAMFILES32\${APPNAME_EN}"
!else
  InstallDir "$PROGRAMFILES64\${APPNAME_EN}"
!endif
; An upgrade finds the previous install through the uninstall key instead of asking again.
InstallDirRegKey HKLM "${UNINSTKEY}" "InstallLocation"

Function .onInit
  ; Shortcuts and the desktop icon belong to every user of the machine, not just the one who
  ; happened to run the installer. The uninstaller sets the same context too, otherwise it
  ; would look for its shortcuts somewhere else.
  SetShellVarContext all
!if "${APPARCH}" != "x86"
  ; The key is read as a 64 bit one on a 64 bit build, which is where the installer writes it.
  SetRegView 64
!endif
FunctionEnd

VIProductVersion "${WINVERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "${APPNAME} ${APPNAME_EN}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APPNAME} 安装程序"
VIAddVersionKey /LANG=2052 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "GPL-3.0 - ${PUBLISHER}"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "ElectronicPointer"
VIAddVersionKey /LANG=1033 "FileDescription" "ElectronicPointer Installer"
VIAddVersionKey /LANG=1033 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "GPL-3.0 - ${PUBLISHER}"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"

SetCompressor /SOLID lzma

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "运行${APPNAME}"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

; Everything the installer itself says, outside the strings the language file already
; carries for the standard pages.
LangString DESC_SecMain ${LANG_SIMPCHINESE} "主程序文件。这是必须安装的部分。"
LangString DESC_SecStartMenu ${LANG_SIMPCHINESE} "在开始菜单创建「${APPNAME}」文件夹，含卸载入口。"
LangString DESC_SecDesktop ${LANG_SIMPCHINESE} "为所有用户创建桌面快捷方式。"
LangString DESC_SecAutoStart ${LANG_SIMPCHINESE} "登录 Windows 时自动启动。装完也能在软件设置里随时开关。"
LangString REG_APPRUNNING_ASK ${LANG_SIMPCHINESE} "${APPNAME} 正在运行，卸载前需要先退出它。请关闭后点击“确定”继续。"
LangString REG_APPRUNNING ${LANG_SIMPCHINESE} "${APPNAME} 仍在运行，卸载已取消。关闭软件后可重新运行此卸载程序。"

Section "${APPNAME}（必需）" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  ; A bare asterisk rather than "*.*": the dot in the second wildcard has to be matched
  ; literally, so an extensionless file in the payload would be left out of the installer.
  File /r "${PAYLOAD}\*"

  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME} ${APPNAME_EN}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME},0"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\${UNINSTALLER}"'
  WriteRegStr HKLM "${UNINSTKEY}" "URLInfoAbout" "${HOMEPAGE}"
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
  ; EstimatedSize is what "Apps and Features" shows next to the entry, in kilobytes.
  SectionGetSize ${SecMain} $0
  IntOp $0 $0 / 1024
  WriteRegDWORD HKLM "${UNINSTKEY}" "EstimatedSize" $0

  WriteUninstaller "$INSTDIR\${UNINSTALLER}"
SectionEnd

Section "开始菜单" SecStartMenu
  CreateDirectory "$SMPROGRAMS\${APPNAME_EN}"
  CreateShortCut "$SMPROGRAMS\${APPNAME_EN}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
  CreateShortCut "$SMPROGRAMS\${APPNAME_EN}\卸载${APPNAME}.lnk" '"$INSTDIR\${UNINSTALLER}"'
SectionEnd

Section /o "桌面快捷方式" SecDesktop
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
SectionEnd

Section /o "开机自启" SecAutoStart
  ; Same value name and same quoted path the application writes when the user flips the
  ; switch in its own settings, so the two never disagree.
  WriteRegStr HKCU "${RUNKEY}" "${RUNVALUE}" '"$INSTDIR\${EXENAME}"'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DESC_SecMain)
!insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_SecStartMenu)
!insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
!insertmacro MUI_DESCRIPTION_TEXT ${SecAutoStart} $(DESC_SecAutoStart)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetShellVarContext all
!if "${APPARCH}" != "x86"
  SetRegView 64
!endif

  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME_EN}"

  ; Only remove the run value if it still points at this install, so uninstalling never
  ; steals an autostart entry that belongs to a different copy.
  ReadRegStr $0 HKCU "${RUNKEY}" "${RUNVALUE}"
  ${If} $0 == '"$INSTDIR\${EXENAME}"'
    DeleteRegValue HKCU "${RUNKEY}" "${RUNVALUE}"
  ${EndIf}

  ClearErrors
  Delete "$INSTDIR\${EXENAME}"
  ${If} ${Errors}
    ; The application keeps its own files open while it runs, so the delete is the lightest
    ; way to ask "is it still running?" and say so in the user's own language.
    MessageBox MB_OKCANCEL|MB_ICONSTOP "$(REG_APPRUNNING_ASK)" IDOK appClosed
    Quit
    appClosed:
    ClearErrors
    Delete "$INSTDIR\${EXENAME}"
    ${If} ${Errors}
      MessageBox MB_OK|MB_ICONSTOP "$(REG_APPRUNNING)"
      Quit
    ${EndIf}
  ${EndIf}

  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTKEY}"
SectionEnd
