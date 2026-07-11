; ============================================================
;  Trion Control Panel — Inno Setup 6.7
;  Modern dark theme — compact 540×420, no wizard chrome
;  Code-signed with installer\certs\TrionCodeSign.cer
;  Installs to %APPDATA%\Trion — no UAC required
; ============================================================

#define AppName       "Trion Control Panel"
; AppVersion is injected by build-installer.ps1 via /DAppVersion=X.X.X.X
; The #ifndef guard lets the command-line value take precedence when ISCC
; is invoked through the build script; the literal below is the fallback.
#ifndef AppVersion
  #define AppVersion "0.0.0.0"
#endif
#define AppPublisher  "FlyingPhoenix"
#define AppExeName    "Trion.Desktop.exe"
#define AppURL        "https://flying-phoenix.dev/"
#define AppSupportURL "https://flying-phoenix.dev/support"

; SourceDir / AppVersion can be overridden from the command line:
;   ISCC /DSourceDir="C:\...\publish" /DAppVersion=0.5.1 TrionSetup.iss
#ifndef SourceDir
  #define SourceDir "..\src\Trion.Desktop\bin\Release\net10.0-windows10.0.17763.0"
#endif

[Setup]
AppId                              = {{F3A2D1B0-7C4E-4F8A-9B5C-2E6D3A1F8E7D}
AppName                            = {#AppName}
AppVersion                         = {#AppVersion}
AppPublisher                       = {#AppPublisher}
AppPublisherURL                    = {#AppURL}
AppSupportURL                      = {#AppSupportURL}
AppUpdatesURL                      = {#AppURL}
DefaultDirName                     = {userappdata}\Trion
DefaultGroupName                   = {#AppName}
DisableProgramGroupPage            = yes
PrivilegesRequired                 = lowest
PrivilegesRequiredOverridesAllowed = dialog
OutputDir                          = ..\Build
OutputBaseFilename                 = TrionSetup-{#AppVersion}
SetupIconFile                      = ..\res\icons\trion-logo\trion.ico
WizardImageFile                    = Assets\WizardSidebar.bmp
WizardSmallImageFile               = Assets\WizardSmall.bmp
Compression                        = lzma2/ultra64
SolidCompression                   = yes
WizardStyle                        = modern
WizardSizePercent                  = 100
ShowLanguageDialog                 = no
UninstallDisplayIcon               = {app}\{#AppExeName}
UninstallDisplayName               = {#AppName}
VersionInfoVersion                 = {#AppVersion}
VersionInfoCompany                 = {#AppPublisher}
VersionInfoDescription             = {#AppName} Installer
VersionInfoProductName             = {#AppName}
VersionInfoProductVersion          = {#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#SourceDir}\{#AppExeName}";     DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Trion.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\appsettings.json";  DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist skipifsourcedoesntexist
Source: "{#SourceDir}\Resources\Localization\Strings.en-US.json"; DestDir: "{app}\Resources\Localization"; Flags: ignoreversion onlyifdoesntexist skipifsourcedoesntexist
Source: "{#SourceDir}\Resources\Localization\Strings.de-DE.json"; DestDir: "{app}\Resources\Localization"; Flags: ignoreversion onlyifdoesntexist skipifsourcedoesntexist
Source: "certs\TrionCodeSign.cer"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"

[Run]
Filename: "certutil.exe"; Parameters: "-user -addstore TrustedPublisher ""{tmp}\TrionCodeSign.cer"""; Flags: runhidden waituntilterminated skipifdoesntexist; StatusMsg: "Trusting Trion certificate..."
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName,'&','&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{#AppSupportURL}"; Description: "Support Trion development"; Flags: postinstall skipifsilent shellexec unchecked

[UninstallRun]
Filename: "certutil.exe"; Parameters: "-user -delstore TrustedPublisher ""CN=FlyingPhoenix Code Signing"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCert"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Trion\logs"
Type: filesandordirs; Name: "{userappdata}\Trion\cache"

; ============================================================
;  [Code]
;
;  Modern dark design — TrionBlue palette (BGR COLORREF):
;    BG_PRIMARY   #141414  $141414  main content
;    BG_SECONDARY #1C1C1C  $1C1C1C  header / button-bar
;    BG_ELEVATED  #2C2C2C  $2C2C2C  raised card / hero
;    BG_INPUT     #242424  $242424  text inputs
;    BG_BORDER    #2E2E2E  $2E2E2E  separator lines
;    FG_PRIMARY   #E8E8E8  $E8E8E8  primary text
;    FG_SECONDARY #9A9A9A  $9A9A9A  description text
;    FG_MUTED     #6A6A6A  $6A6A6A  hints / version
;    ACCENT_CYAN  #00D4FF  $FFD400  accent (BGR reversed)
;
;  Page flow:
;    [OptionsPage]  ←  our custom page (replaces 4 default wizard pages)
;    [wpInstalling] ←  styled cyan progress bar
;    [wpFinished]   ←  custom completion message + support link
; ============================================================
[Code]

const
  BG_PRIMARY   = $141414;
  BG_SECONDARY = $1C1C1C;
  BG_ELEVATED  = $2C2C2C;
  BG_INPUT     = $242424;
  BG_BORDER    = $2E2E2E;
  FG_PRIMARY   = $E8E8E8;
  FG_SECONDARY = $9A9A9A;
  FG_MUTED     = $6A6A6A;
  ACCENT_CYAN  = $FFD400;

  PBM_SETBKCOLOR  = $2001;
  PBM_SETBARCOLOR = $0408;

var
  UpdateMode:    Boolean;
  DeleteAllData: Boolean;
  OptionsPage:   TWizardPage;
  DirEditCtrl:   TNewEdit;
  DeskIconChk:   TNewCheckBox;
  StartMenuChk:  TNewCheckBox;

// ── Win32 / UxTheme ───────────────────────────────────────────────────────────
function SendMessage(hWnd, Msg, wParam, lParam: Integer): Integer;
  external 'SendMessageW@user32.dll stdcall';

// Strip visual-style theming from a control (needed so PBM_SETBARCOLOR works).
// ('','') = no theme; ('DarkMode_Explorer','') = dark button rendering on Win10+
function SetWindowTheme(hWnd: Integer; pszSubAppName, pszSubIdList: String): Integer;
  external 'SetWindowTheme@uxtheme.dll stdcall';

// ── Helpers ───────────────────────────────────────────────────────────────────
procedure OpenUrl(Url: String);
var Code: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure SupportLinkClick(Sender: TObject);
begin
  OpenUrl('{#AppSupportURL}');
end;

function CompareVersionStr(V1, V2: String): Integer;
var P, N1, N2: Integer; S1, S2: String;
begin
  Result := 0;
  repeat
    P := Pos('.', V1);
    if P > 0 then begin S1 := Copy(V1,1,P-1); V1 := Copy(V1,P+1,Length(V1)); end
             else begin S1 := V1; V1 := ''; end;
    P := Pos('.', V2);
    if P > 0 then begin S2 := Copy(V2,1,P-1); V2 := Copy(V2,P+1,Length(V2)); end
             else begin S2 := V2; V2 := ''; end;
    if S1 = '' then N1 := 0 else N1 := StrToInt(S1);
    if S2 = '' then N2 := 0 else N2 := StrToInt(S2);
    if N1 > N2 then begin Result :=  1; Break; end;
    if N1 < N2 then begin Result := -1; Break; end;
  until (V1 = '') and (V2 = '');
end;

// ── Base theme — applied once at form creation ────────────────────────────────
procedure ApplyBaseTheme();
begin
  // WizardForm.Color = BG_SECONDARY so the button-bar area (below InnerPage)
  // matches the header, giving a consistent dark frame.
  WizardForm.Color      := BG_SECONDARY;
  WizardForm.Font.Name  := 'Segoe UI';
  WizardForm.Font.Size  := 9;
  WizardForm.Font.Color := FG_PRIMARY;

  // Header strip
  WizardForm.MainPanel.Color  := BG_SECONDARY;

  // Hide IS6 default page-name labels and BMP images — replaced by custom header
  WizardForm.PageNameLabel.Visible        := False;
  WizardForm.PageDescriptionLabel.Visible := False;
  WizardForm.WizardBitmapImage.Visible    := False;
  WizardForm.WizardBitmapImage2.Visible   := False;

  // Remove Win95-style bevel separators
  WizardForm.Bevel.Visible  := False;
  WizardForm.Bevel1.Visible := False;

  // Inner content notebook — all named pages dark.
  // IS6 Pascal Script does not expose TNewNotebook.PageCount; access each
  // page through its named TWizardForm property instead.
  WizardForm.InnerPage.Color                := BG_PRIMARY;
  WizardForm.SelectDirPage.Color            := BG_PRIMARY;
  WizardForm.SelectComponentsPage.Color     := BG_PRIMARY;
  WizardForm.SelectTasksPage.Color          := BG_PRIMARY;
  WizardForm.ReadyPage.Color                := BG_PRIMARY;
  WizardForm.InstallingPage.Color           := BG_PRIMARY;

  // Welcome / Finished outer pages (IS6 keeps these separate from InnerPage)
  WizardForm.WelcomePage.Color  := BG_PRIMARY;
  WizardForm.FinishedPage.Color := BG_PRIMARY;
  WizardForm.RunList.Color      := BG_PRIMARY;
  WizardForm.RunList.Font.Color := FG_PRIMARY;

  // Nav buttons — dark mode rendering (Win10 1809+; falls back to classic)
  SetWindowTheme(WizardForm.BackButton.Handle,   'DarkMode_Explorer', '');
  SetWindowTheme(WizardForm.NextButton.Handle,   'DarkMode_Explorer', '');
  SetWindowTheme(WizardForm.CancelButton.Handle, 'DarkMode_Explorer', '');
end;

// ── Persistent brand strip inside MainPanel ───────────────────────────────────
//   [TRION]  [Control Panel]   — shown on every page, no step-title clutter
procedure BuildBrandHeader();
var
  TrionLbl:   TLabel;
  SubLbl:     TLabel;
  BottomLine: TPanel;
begin
  TrionLbl            := TLabel.Create(WizardForm);
  TrionLbl.Parent     := WizardForm.MainPanel;
  TrionLbl.Caption    := 'TRION';
  TrionLbl.Font.Name  := 'Segoe UI';
  TrionLbl.Font.Size  := 13;
  TrionLbl.Font.Style := [fsBold];
  TrionLbl.Font.Color := ACCENT_CYAN;
  TrionLbl.AutoSize   := True;
  TrionLbl.Left       := ScaleX(20);
  TrionLbl.Top        := ScaleY(14);

  SubLbl            := TLabel.Create(WizardForm);
  SubLbl.Parent     := WizardForm.MainPanel;
  SubLbl.Caption    := 'Control Panel';
  SubLbl.Font.Name  := 'Segoe UI';
  SubLbl.Font.Size  := 9;
  SubLbl.Font.Color := FG_SECONDARY;
  SubLbl.AutoSize   := True;
  SubLbl.Left       := ScaleX(80);
  SubLbl.Top        := ScaleY(18);

  // 1 px bottom border on the header strip
  BottomLine            := TPanel.Create(WizardForm);
  BottomLine.Parent     := WizardForm.MainPanel;
  BottomLine.Left       := 0;
  BottomLine.Width      := WizardForm.MainPanel.ClientWidth;
  BottomLine.Height     := 1;
  BottomLine.Top        := WizardForm.MainPanel.ClientHeight - 1;
  BottomLine.BevelOuter := bvNone;
  BottomLine.Color      := BG_BORDER;
end;

// ── Custom "Welcome + Options" page ──────────────────────────────────────────
// Inserted after wpWelcome (which is skipped).
// Replaces:  wpWelcome · wpSelectDir · wpSelectComponents · wpSelectTasks · wpReady
//
// Visual layout (inside SurfaceHeight ≈ 290 px):
//   0 .. 87   Hero panel  — app name, version badge, tagline
//   88        1 px divider
//   96 ..108  "Install location" label
//  112 ..137  Directory path edit (full width - 48 px margin)
//  148 ..160  "Shortcuts" label
//  164 ..183  Desktop icon checkbox
//  188 ..207  Start Menu checkbox
procedure BuildOptionsPage();
var
  PW:          Integer;
  Hero:        TPanel;
  AppNameLbl:  TLabel;
  VerLbl:      TLabel;
  TagLbl:      TLabel;
  Sep:         TPanel;
  DirLbl:      TLabel;
  OptLbl:      TLabel;
begin
  OptionsPage := CreateCustomPage(wpWelcome, '', '');
  OptionsPage.Surface.Color := BG_PRIMARY;
  PW := OptionsPage.SurfaceWidth;

  // ── Hero zone ─────────────────────────────────────────────────────────────
  Hero            := TPanel.Create(OptionsPage);
  Hero.Parent     := OptionsPage.Surface;
  Hero.Left       := 0;
  Hero.Top        := 0;
  Hero.Width      := PW;
  Hero.Height     := ScaleY(88);
  Hero.BevelOuter := bvNone;
  Hero.Color      := BG_ELEVATED;

  AppNameLbl            := TLabel.Create(OptionsPage);
  AppNameLbl.Parent     := Hero;
  AppNameLbl.Caption    := '{#AppName}';
  AppNameLbl.Font.Name  := 'Segoe UI';
  AppNameLbl.Font.Size  := 18;
  AppNameLbl.Font.Style := [fsBold];
  AppNameLbl.Font.Color := FG_PRIMARY;
  AppNameLbl.AutoSize   := True;
  AppNameLbl.Left       := ScaleX(24);
  AppNameLbl.Top        := ScaleY(16);

  VerLbl            := TLabel.Create(OptionsPage);
  VerLbl.Parent     := Hero;
  VerLbl.Caption    := 'v{#AppVersion}';
  VerLbl.Font.Name  := 'Segoe UI';
  VerLbl.Font.Size  := 9;
  VerLbl.Font.Color := ACCENT_CYAN;
  VerLbl.AutoSize   := True;
  VerLbl.Left       := ScaleX(24);
  VerLbl.Top        := ScaleY(52);

  TagLbl            := TLabel.Create(OptionsPage);
  TagLbl.Parent     := Hero;
  TagLbl.Caption    := 'WoW emulator management suite';
  TagLbl.Font.Name  := 'Segoe UI';
  TagLbl.Font.Size  := 9;
  TagLbl.Font.Color := FG_MUTED;
  TagLbl.AutoSize   := True;
  TagLbl.Left       := ScaleX(100);
  TagLbl.Top        := ScaleY(53);

  // ── 1 px separator ────────────────────────────────────────────────────────
  Sep            := TPanel.Create(OptionsPage);
  Sep.Parent     := OptionsPage.Surface;
  Sep.Left       := 0;
  Sep.Top        := ScaleY(88);
  Sep.Width      := PW;
  Sep.Height     := 1;
  Sep.BevelOuter := bvNone;
  Sep.Color      := BG_BORDER;

  // ── Install location ──────────────────────────────────────────────────────
  DirLbl            := TLabel.Create(OptionsPage);
  DirLbl.Parent     := OptionsPage.Surface;
  DirLbl.Caption    := 'Install location';
  DirLbl.Font.Name  := 'Segoe UI';
  DirLbl.Font.Size  := 9;
  DirLbl.Font.Color := FG_SECONDARY;
  DirLbl.AutoSize   := True;
  DirLbl.Left       := ScaleX(24);
  DirLbl.Top        := ScaleY(104);

  DirEditCtrl            := TNewEdit.Create(OptionsPage);
  DirEditCtrl.Parent     := OptionsPage.Surface;
  DirEditCtrl.Left       := ScaleX(24);
  DirEditCtrl.Top        := ScaleY(120);
  DirEditCtrl.Width      := PW - ScaleX(48);
  DirEditCtrl.Height     := ScaleY(24);
  DirEditCtrl.Text       := WizardDirValue();
  DirEditCtrl.Color      := BG_INPUT;
  DirEditCtrl.Font.Color := FG_PRIMARY;
  DirEditCtrl.Font.Name  := 'Segoe UI';
  DirEditCtrl.Font.Size  := 9;
  SetWindowTheme(DirEditCtrl.Handle, 'DarkMode_Explorer', '');

  // ── Shortcut options ──────────────────────────────────────────────────────
  OptLbl            := TLabel.Create(OptionsPage);
  OptLbl.Parent     := OptionsPage.Surface;
  OptLbl.Caption    := 'Shortcuts';
  OptLbl.Font.Name  := 'Segoe UI';
  OptLbl.Font.Size  := 9;
  OptLbl.Font.Color := FG_SECONDARY;
  OptLbl.AutoSize   := True;
  OptLbl.Left       := ScaleX(24);
  OptLbl.Top        := ScaleY(160);

  DeskIconChk              := TNewCheckBox.Create(OptionsPage);
  DeskIconChk.Parent       := OptionsPage.Surface;
  DeskIconChk.Caption      := 'Create a desktop shortcut';
  DeskIconChk.Left         := ScaleX(24);
  DeskIconChk.Top          := ScaleY(178);
  DeskIconChk.Width        := PW - ScaleX(48);
  DeskIconChk.Height       := ScaleY(20);
  DeskIconChk.Color        := BG_PRIMARY;
  DeskIconChk.Font.Color   := FG_PRIMARY;
  DeskIconChk.ParentColor  := False;
  DeskIconChk.Checked      := False;
  SetWindowTheme(DeskIconChk.Handle, 'DarkMode_Explorer', '');

  StartMenuChk             := TNewCheckBox.Create(OptionsPage);
  StartMenuChk.Parent      := OptionsPage.Surface;
  StartMenuChk.Caption     := 'Add to Start Menu';
  StartMenuChk.Left        := ScaleX(24);
  StartMenuChk.Top         := ScaleY(202);
  StartMenuChk.Width       := PW - ScaleX(48);
  StartMenuChk.Height      := ScaleY(20);
  StartMenuChk.Color       := BG_PRIMARY;
  StartMenuChk.Font.Color  := FG_PRIMARY;
  StartMenuChk.ParentColor := False;
  StartMenuChk.Checked     := True;
  SetWindowTheme(StartMenuChk.Handle, 'DarkMode_Explorer', '');
end;

// ── Finished page — custom completion content ─────────────────────────────────
procedure BuildFinishedPage();
var
  BaseLeft:  Integer;
  BaseTop:   Integer;
  PageWidth: Integer;
  SepPanel:  TPanel;
  HeartLbl:  TLabel;
  SubLbl:    TLabel;
  LinkLbl:   TLabel;
begin
  // Override IS6's generic finished text
  WizardForm.FinishedHeadingLabel.Caption    := 'Installation complete!';
  WizardForm.FinishedHeadingLabel.Font.Color := ACCENT_CYAN;
  WizardForm.FinishedHeadingLabel.Font.Size  := 16;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];
  WizardForm.FinishedLabel.Caption :=
    '{#AppName} {#AppVersion} was installed successfully.' + #13#10 +
    'Click Finish to close the installer.';
  WizardForm.FinishedLabel.Font.Color := FG_SECONDARY;
  WizardForm.FinishedLabel.Font.Size  := 9;

  // Support / donate section below the RunList
  BaseLeft  := WizardForm.FinishedLabel.Left;
  PageWidth := WizardForm.FinishedPage.ClientWidth - BaseLeft * 2;
  BaseTop   := WizardForm.RunList.Top + WizardForm.RunList.Height + 20;

  SepPanel            := TPanel.Create(WizardForm);
  SepPanel.Parent     := WizardForm.FinishedPage;
  SepPanel.Left       := BaseLeft;
  SepPanel.Top        := BaseTop;
  SepPanel.Width      := PageWidth;
  SepPanel.Height     := 1;
  SepPanel.BevelOuter := bvNone;
  SepPanel.Color      := BG_BORDER;

  BaseTop := BaseTop + 14;

  HeartLbl            := TLabel.Create(WizardForm);
  HeartLbl.Parent     := WizardForm.FinishedPage;
  HeartLbl.Caption    := #$2665 + '  Support Trion';
  HeartLbl.Font.Name  := 'Segoe UI';
  HeartLbl.Font.Size  := 10;
  HeartLbl.Font.Style := [fsBold];
  HeartLbl.Font.Color := ACCENT_CYAN;
  HeartLbl.AutoSize   := True;
  HeartLbl.Left       := BaseLeft;
  HeartLbl.Top        := BaseTop;
  HeartLbl.Cursor     := crHand;
  HeartLbl.OnClick    := @SupportLinkClick;

  BaseTop := BaseTop + 22;

  SubLbl            := TLabel.Create(WizardForm);
  SubLbl.Parent     := WizardForm.FinishedPage;
  SubLbl.Caption    := 'Enjoying Trion? Support continued development:';
  SubLbl.Font.Name  := 'Segoe UI';
  SubLbl.Font.Size  := 9;
  SubLbl.Font.Color := FG_SECONDARY;
  SubLbl.AutoSize   := True;
  SubLbl.Left       := BaseLeft;
  SubLbl.Top        := BaseTop;

  BaseTop := BaseTop + 18;

  LinkLbl            := TLabel.Create(WizardForm);
  LinkLbl.Parent     := WizardForm.FinishedPage;
  LinkLbl.Caption    := 'flying-phoenix.dev/support';
  LinkLbl.Font.Name  := 'Segoe UI';
  LinkLbl.Font.Size  := 9;
  LinkLbl.Font.Style := [fsUnderline];
  LinkLbl.Font.Color := ACCENT_CYAN;
  LinkLbl.AutoSize   := True;
  LinkLbl.Cursor     := crHand;
  LinkLbl.Left       := BaseLeft;
  LinkLbl.Top        := BaseTop;
  LinkLbl.OnClick    := @SupportLinkClick;
end;

// ── Installing page — called on first page-change to wpInstalling ─────────────
// Progress gauge handle is only valid once the page is shown for the first time.
procedure StyleInstallingPage();
begin
  SetWindowTheme(WizardForm.ProgressGauge.Handle, '', '');
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR,  0, BG_BORDER);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, ACCENT_CYAN);
  WizardForm.StatusLabel.Font.Color   := FG_PRIMARY;
  WizardForm.StatusLabel.Font.Size    := 10;
  WizardForm.FilenameLabel.Font.Color := FG_MUTED;
end;

// ── Update-mode: slim 460×80 progress-only window ────────────────────────────
// Launched with /update=1 by Trion.Updater.exe — skips all wizard pages.
procedure SetupUpdateModeUI();
var W, H: Integer;
begin
  W := ScaleX(460);
  H := ScaleY(80);
  WizardForm.Caption      := 'Updating Trion Control Panel...';
  WizardForm.ClientWidth  := W;
  WizardForm.ClientHeight := H;
  WizardForm.Position     := poScreenCenter;
  WizardForm.Color        := BG_PRIMARY;
  WizardForm.MainPanel.Visible    := False;
  WizardForm.Bevel.Visible        := False;
  WizardForm.Bevel1.Visible       := False;
  WizardForm.BackButton.Visible   := False;
  WizardForm.NextButton.Visible   := False;
  WizardForm.CancelButton.Visible := False;
  WizardForm.InnerPage.Left   := 0;
  WizardForm.InnerPage.Top    := 0;
  WizardForm.InnerPage.Width  := W;
  WizardForm.InnerPage.Height := H;
  WizardForm.StatusLabel.Left       := ScaleX(16);
  WizardForm.StatusLabel.Top        := ScaleY(10);
  WizardForm.StatusLabel.Width      := W - ScaleX(32);
  WizardForm.StatusLabel.Font.Color := FG_PRIMARY;
  WizardForm.StatusLabel.Font.Size  := 9;
  WizardForm.StatusLabel.Caption    := 'Updating Trion Control Panel...';
  WizardForm.ProgressGauge.Left     := ScaleX(16);
  WizardForm.ProgressGauge.Top      := ScaleY(38);
  WizardForm.ProgressGauge.Width    := W - ScaleX(32);
  WizardForm.ProgressGauge.Height   := ScaleY(20);
  WizardForm.FilenameLabel.Visible  := False;
  SetWindowTheme(WizardForm.ProgressGauge.Handle, '', '');
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR,  0, BG_BORDER);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, ACCENT_CYAN);
end;

// ── Wizard event hooks ────────────────────────────────────────────────────────

procedure InitializeWizard();
begin
  UpdateMode := ExpandConstant('{param:update|0}') = '1';

  if UpdateMode then
  begin
    SetupUpdateModeUI();
    Exit;
  end;

  // Compact modern form: 540 × 420 — no sidebar, no wizard-step chrome
  WizardForm.ClientWidth  := ScaleX(540);
  WizardForm.ClientHeight := ScaleY(420);
  WizardForm.Position     := poScreenCenter;

  ApplyBaseTheme();
  BuildBrandHeader();
  BuildOptionsPage();
  BuildFinishedPage();

  WizardForm.NextButton.Caption := 'Install';
  WizardForm.BackButton.Enabled := False;
end;

// Skip all standard pages — only show OptionsPage → wpInstalling → wpFinished
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if UpdateMode then
    Result := (PageID <> wpInstalling)
  else
    Result := (PageID = wpWelcome)          or
              (PageID = wpSelectDir)        or
              (PageID = wpSelectComponents) or
              (PageID = wpSelectTasks)      or
              (PageID = wpReady);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Style the progress bar whenever the installing page is entered — do this
  // before the update-mode guard so it also applies to silent update installs.
  if CurPageID = wpInstalling then StyleInstallingPage();

  if UpdateMode then Exit;

  if CurPageID = OptionsPage.ID then
  begin
    WizardForm.NextButton.Caption   := 'Install';
    WizardForm.BackButton.Enabled   := False;
    WizardForm.CancelButton.Visible := True;
  end
  else if CurPageID = wpInstalling then
  begin
    WizardForm.BackButton.Enabled   := False;
    WizardForm.CancelButton.Enabled := False;
  end
  else if CurPageID = wpFinished then
  begin
    WizardForm.NextButton.Caption   := 'Finish';
    WizardForm.BackButton.Visible   := False;
    WizardForm.CancelButton.Visible := False;
  end;
end;

// Validate options page and push user choices back into IS6's internal state
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // OptionsPage is nil in update mode (BuildOptionsPage is never called)
  if (OptionsPage = nil) or (CurPageID <> OptionsPage.ID) then Exit;

  if Trim(DirEditCtrl.Text) = '' then
  begin
    MsgBox('Please enter an installation folder.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Sync install dir to IS6's internal DirEdit
  WizardForm.DirEdit.Text := DirEditCtrl.Text;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if UpdateMode then
  begin
    // Update mode: no UI interaction — auto-close once everything is done.
    // ShouldSkipPage already skips wpFinished, but Close() is a safety net
    // in case IS6 tries to show it anyway before exiting.
    if CurStep = ssDone then WizardForm.Close();
    Exit;
  end;

  // Normal install: remove shortcuts the user opted out of.
  // [Icons] always creates all three; we delete the unwanted ones here because
  // TCheckItemState / CheckItem are not callable from IS6 Pascal Script.
  if CurStep <> ssPostInstall then Exit;
  if not DeskIconChk.Checked then
    DeleteFile(ExpandConstant('{autodesktop}\{#AppName}.lnk'));
  if not StartMenuChk.Checked then
  begin
    DeleteFile(ExpandConstant('{group}\{#AppName}.lnk'));
    DeleteFile(ExpandConstant('{group}\Uninstall {#AppName}.lnk'));
  end;
end;

// ── Detect existing install: upgrade / repair / downgrade guard ───────────────
function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  Cmp:             Integer;
begin
  Result := True;
  // UpdateMode global is set in InitializeWizard which runs later; check param directly.
  if ExpandConstant('{param:update|0}') = '1' then Exit;

  if not RegQueryStringValue(HKCU,
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
        '{F3A2D1B0-7C4E-4F8A-9B5C-2E6D3A1F8E7D}_is1',
        'DisplayVersion', InstalledVersion) then Exit;

  Cmp := CompareVersionStr(InstalledVersion, '{#AppVersion}');

  if Cmp = 0 then
    Result := MsgBox(
      '{#AppName} ' + InstalledVersion + ' is already installed.' + #13#10 + #13#10 +
      'Do you want to repair or reinstall it?',
      mbConfirmation, MB_YESNO) = IDYES
  else if Cmp < 0 then
    MsgBox(
      'Upgrading {#AppName} from ' + InstalledVersion + ' to {#AppVersion}.' + #13#10 + #13#10 +
      'Your servers, database, and settings will not be touched.' + #13#10 +
      'Click OK to continue.',
      mbInformation, MB_OK)
  else
  begin
    MsgBox(
      'A newer version (' + InstalledVersion + ') of {#AppName} is already installed.' + #13#10 +
      'This installer ({#AppVersion}) will not downgrade.',
      mbInformation, MB_OK);
    Result := False;
  end;
end;

// ── Uninstall: optionally wipe all user data from {app} ──────────────────────
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir:   String;
  DataDirs: array[0..4] of String;
  I:        Integer;
begin
  if CurUninstallStep = usAppMutexCheck then
    DeleteAllData := MsgBox(
      'Do you want to delete all Trion data from the installation folder?' + #13#10 + #13#10 +
      'This includes:' + #13#10 +
      '  •  Installed repack servers  (servers\)' + #13#10 +
      '  •  Portable MySQL database   (database\)' + #13#10 +
      '  •  nginx / PHP web server    (webserver\)' + #13#10 +
      '  •  Extracted WoW game maps   (Data\)' + #13#10 + #13#10 +
      'Choose No to keep your servers and maps.',
      mbConfirmation, MB_YESNO) = IDYES;

  if (CurUninstallStep = usPostUninstall) and DeleteAllData then
  begin
    AppDir      := ExpandConstant('{app}');
    DataDirs[0] := AppDir + '\servers';
    DataDirs[1] := AppDir + '\database';
    DataDirs[2] := AppDir + '\webserver';
    DataDirs[3] := AppDir + '\Data';
    DataDirs[4] := AppDir + '\cache';
    for I := 0 to 4 do
      if DirExists(DataDirs[I]) then DelTree(DataDirs[I], True, True, True);
    RemoveDir(AppDir);
  end;
end;

// ── Kill running app before overwriting files ─────────────────────────────────
function PrepareToInstall(var NeedsRestart: Boolean): String;
var ResultCode: Integer;
begin
  Result := '';
  Exec('taskkill.exe', '/IM "{#AppExeName}" /F',             '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM "TrionControlPanel.WPF.exe" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM "Trion.Updater.exe" /F',         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;
