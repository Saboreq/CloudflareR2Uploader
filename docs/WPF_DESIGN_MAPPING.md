# WPF design mapping

Maps every Paper reference in `Images/` to the WPF views, view models and styles that
implement it. The images are the visual source of truth; measurements below were read off
the references at their native resolution.

## 1. Global frame

Base window 1280 × 840, minimum 1040 × 660. Title bar is the real Windows caption with
dark mode forced through `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`, so
minimize/maximize/restore/close, the system menu, drag and Windows snap all behave
natively. No custom `WindowChrome` hit testing is used.

```
MainWindow
├─ ShellHeaderView        product mark, title, connection status, profile, bucket selector, Settings
├─ ShellNavigationView    Upload | Files | Activity, active underline, activity badge
├─ ContentControl         FilesView | UploadView | ActivityView   (page selected by ShellViewModel)
└─ (per-page status bar rendered inside each page, matching the references)
```

## 2. Design tokens

`Themes/Colors.xaml` holds the raw values from `Images/08-design-system-overview.png`;
`Themes/Brushes.xaml` exposes frozen `SolidColorBrush` resources by semantic name.
`Theme.Dark.xaml` / `Theme.Light.xaml` remap the same brush keys, and every consumer binds
with `DynamicResource`, so switching theme never rebuilds a view.

| Token | Dark value | Used for |
| --- | --- | --- |
| `Brush.Background` | `#08090E` | window ground, inputs sit directly on it |
| `Brush.Surface` | `#101119` | page panels, table body |
| `Brush.Surface2` | `#151620` | table header, footer, toolbars |
| `Brush.Elevated` | `#1A1B27` | menus, dialogs, inspector cards |
| `Brush.RowHover` | `#171826` | table row hover |
| `Brush.Border` | `#292B38` | hairlines, control borders |
| `Brush.BorderStrong` | `#3A3D4E` | menu/dialog borders, focused inputs |
| `Brush.TextPrimary` | `#F4F5F8` | primary text |
| `Brush.TextSecondary` | `#A8ACBA` | secondary cells, labels |
| `Brush.TextMuted` | `#73798C` | placeholders, hints |
| `Brush.Accent` | `#8B5CF6` | primary action, selection, progress, focus |
| `Brush.AccentHover` | `#9E76F8` | hover on accent surfaces |
| `Brush.AccentWash` | `#1C1733` | selected row fill, active nav wash |
| `Brush.AccentBorder` | `#4A3A80` | selected row edge, accent outlines |
| `Brush.Success` / `SuccessWash` | `#45D6A0` / `#0F2620` | completed badge, success toast |
| `Brush.Warning` / `WarningWash` | `#F1B85B` / `#2A2113` | paused badge, warning bar |
| `Brush.Danger` / `DangerWash` | `#FF647C` / `#2C151C` | delete, failed badge, error preview |
| `Brush.ProgressTrack` | `#23252F` | progress bar track |

Typography (`Themes/Typography.xaml`), taken from the reference table:

| Style key | Size / weight / tracking | Applied to |
| --- | --- | --- |
| `Text.PageTitle` | 26 / 600 / −0.03em | dialog and page titles |
| `Text.SectionTitle` | 20 / 600 / −0.02em | settings section titles |
| `Text.Subtitle` | 14 / 600 / −0.015em | inspector title, app name |
| `Text.Body` | 13 / 400–500 | buttons, table rows |
| `Text.Value` | 12 / 400 | values, secondary cells |
| `Text.ColumnHeader` | 11 / 600 / +0.03em | column heads, status bar |
| `Text.GroupHeader` | 10 / 700 / +0.08em | `FILE DETAILS`, `UPLOAD QUEUE`, `DESTINATION` |
| `Text.Mono` | 11–12 JetBrains Mono | keys, ETags, URLs, part counters |

`Fonts.Ui` uses the bundled Inter 4.1 Regular, Medium, SemiBold and Bold resources so the
installed app renders the same metrics even when Inter is not installed on Windows.
`Fonts.Mono` keeps the platform fallback stack `JetBrains Mono, Cascadia Mono, Consolas`.
The Inter SIL Open Font License is shipped in `licenses/Inter-LICENSE.txt`.

Spacing (`Themes/Spacing.xaml`): 4 px base step, exposed as `Space.4` … `Space.32`.
Radii: `Radius.3` menu item, `Radius.4` control, `Radius.6` menu, `Radius.8` panel,
`Radius.Pill` chips and badges. Control heights: 28 px in toolbars, 30–32 px in dialogs;
table rows 30 px.

## 3. Screen-by-screen mapping

### R1 — `Images/01-files-browser.png`

| Reference element | WPF |
| --- | --- |
| header, bucket selector, Settings | `Views/Shell/ShellHeaderView.xaml` → `ShellViewModel` |
| Upload / Files / Activity tabs + `12` badge | `Views/Shell/ShellNavigationView.xaml` → `ShellViewModel.Pages`, `ActivityViewModel.UnreadCount` |
| back, up, refresh, Upload, New folder | `Views/Files/FilesToolbarView.xaml` → `FilesViewModel.GoBackCommand`, `GoUpCommand`, `RefreshCommand`, `GoToUploadCommand`, `NewFolderCommand` |
| breadcrumb `storage › assets › builds` | `Controls/BreadcrumbBar.cs` styled in `Controls.Navigation.xaml` → `FilesViewModel.Breadcrumbs` (from `R2BrowserPathUtility.CreateBreadcrumbSegments`) |
| `Filter by name or prefix…` | search field style in `Controls.Inputs.xaml` → `FilesViewModel.FilterText` (250 ms debounce, current page only) |
| `Name · A→Z` sort selector | `Controls.Inputs.xaml` select style → `FilesViewModel.SortOptions` / `SelectedSort` |
| list / grid segmented control | `Controls.Navigation.xaml` segmented style → `FilesViewModel.ViewMode` |
| inspector toggle | `FilesViewModel.IsInspectorVisible` |
| table (30 px rows, hairline, checkbox lane, trailing ⋮) | `Views/Files/FilesTableView.xaml`, `Controls.Tables.xaml` (`DataGrid` with virtualization + recycling) |
| row states default / hover / selected / keyboard focus / folder | `DataGridRow` template triggers using `RowHover`, `AccentWash` + 2 px accent edge, 1 px accent ring |
| footer `17 items · 4 folders · 13 objects · 1 selected · 61.2 KB · Listed size 424.6 MB · Page 1 of 3 · Previous / Next` | `Views/Files/FilesStatusBarView.xaml` → `FilesViewModel.StatusSummary`, `PaginationState` |

### R2 — `Images/02-file-inspector.png`

| Reference element | WPF |
| --- | --- |
| docked 380 px panel, icon, name, `PNG` badge, full key, `…`, close | `Views/Files/FileInspectorView.xaml` → `FileInspectorViewModel` |
| Overview / Metadata / Sharing tabs | inspector tab style in `Controls.Navigation.xaml` → `FileInspectorViewModel.SelectedTab` |
| preview viewport with `512 × 512 · 100%` chip | `Views/Files/InspectorPreviewView.xaml` → `PreviewViewModel` (`Loading`, `Image`, `Text`, `Pdf`, `Unsupported`, `Error`, `Empty` states) |
| zoom −, +, Fit, 1:1, Open, download | `PreviewViewModel.ZoomOutCommand`, `ZoomInCommand`, `FitCommand`, `ActualSizeCommand`, `OpenCommand`, `DownloadCommand` |
| `FILE DETAILS`: Kind, Size, Dimensions, Modified | `MetadataGroup` composite, 96 px label lane (matches `Images/10`) |
| `OBJECT`: Key, ETag, Storage class, Visibility + copy buttons | same composite with `CopyValueButton`; `Visibility` renders the `● Public` badge |
| sticky footer Download / Copy link / `…` | `Views/Files/InspectorFooterView.xaml` |

### R3 — `Images/03-multi-selection-context-menu.png`

| Reference element | WPF |
| --- | --- |
| `5 objects selected`, prefix, TOTAL SIZE / OBJECTS stat tiles | `Views/Files/MultiSelectionInspectorView.xaml` → `MultiSelectionViewModel` |
| `BY TYPE` breakdown | `MultiSelectionViewModel.TypeBreakdown` (grouped from `BrowserViewService.GetDisplayedType`) |
| `SELECTED OBJECTS` list with right-aligned sizes | virtualized `ItemsControl` |
| `Download 5` / `Copy keys` / `…` | `DownloadSelectedCommand`, `CopyKeysCommand`, overflow menu |
| context menu (Download `Ctrl+D`, Download & overwrite…, Copy key `Ctrl+K`, Copy public URL, Open URL in browser, Create temporary link…, Rename… `F2`, Move to…, Delete `Del`, Refresh `F5`) | `Controls.Menus.xaml` applied to a real `ContextMenu` in `FilesTableView.xaml`; item enablement from `FilesViewModel.SelectionCapabilities` |

### R4 — `Images/04-upload-queue.png`

| Reference element | WPF |
| --- | --- |
| drop zone, `Multipart enabled · no size limit · resumes on reconnect`, Browse files / Browse folder | `Views/Upload/UploadDropZoneView.xaml` → `UploadViewModel` |
| `DESTINATION`: folder/prefix, object name, `If the key exists`, `Keep source folder structure` | `Views/Upload/UploadDestinationView.xaml` |
| `UPLOAD QUEUE 5` header + Upload all / Pause / Cancel / Retry failed / Clear completed | `Views/Upload/UploadQueueHeaderView.xaml` |
| queue rows in five states with badge, part counter, sizes, speed, ETA, 4 px bar, row actions | `Views/Upload/UploadQueueRowView.xaml` → `UploadQueueItemViewModel`; matches `Images/10` composites |
| overall progress block with elapsed, remaining, counts and `51%` | `Views/Upload/UploadOverallProgressView.xaml` |
| bottom status `5 in queue · destination assets/builds/ on storage · 2 parallel transfers · 8 MB parts` | `Views/Upload/UploadStatusBarView.xaml` |
| `✓ Link copied to clipboard` toast | `Controls/ToastHost.cs` + `Controls.Dialogs.xaml` |

### R5 — `Images/05-activity-history.png`

| Reference element | WPF |
| --- | --- |
| filter chips All / Uploads / Downloads / Changes / Errors `2` | `Views/Activity/ActivityFiltersView.xaml` → `ActivityViewModel.Filters` |
| `Last 7 days` range, `Search activity…`, Export CSV, Clear history | `ActivityViewModel.DateRange`, `SearchText`, `ExportCsvCommand`, `ClearHistoryCommand` |
| date group headers `TODAY · 4 AUGUST 2026  8 events · 224.6 MB transferred` | `CollectionViewSource` grouping with a styled `GroupStyle` |
| rows: action icon+label, mono object, bucket, size, duration, time, result badge | `Views/Activity/ActivityTableView.xaml` |
| footer `13 events · 11 succeeded · 2 failed · Transferred in range 773.7 MB · History kept for 30 days` | `Views/Activity/ActivityStatusBarView.xaml` |

### R6 / R6b — `Images/06-settings-connection.png`, `Images/07-settings-transfers-application.png`

Modal `SettingsWindow`, 880 × 610 at 100 %, owner-modal, focus-trapped, Escape closes,
focus returns to the invoking control.

| Reference element | WPF |
| --- | --- |
| left nav Buckets `2` / Connection / Transfers / Application / Notifications / Updates `•` / About | `Views/Settings/SettingsNavigationView.xaml` → `SettingsViewModel.Sections` |
| fixed header with title and close | `SettingsWindow.xaml` |
| scrollable content region | `ScrollViewer` hosting the section view |
| fixed footer Test connection + state chip + Cancel / Save | `Views/Settings/SettingsFooterView.xaml` |
| S3 credentials, endpoint with inline validation error | `ConnectionSettingsViewModel` (`AccountId`, `AccessKeyId`, `SecretAccessKey`, `IsSecretRevealed`, `Endpoint`, `EndpointError`) |
| Public access, temporary link expiry, prefer public URLs | `ConnectionSettingsViewModel.PublicBaseUrl`, `TemporaryLinkExpiry`, `PreferPublicUrls` |
| Credential storage toggle + `DPAPI encrypted` badge | `ConnectionSettingsViewModel.ProtectCredentials`. **Label wording differs from the mock on purpose**: the implementation is Windows DPAPI (`CurrentUser`), not Windows Credential Manager, so the UI says "Protect saved credentials with Windows DPAPI". Claiming Credential Manager would be false. |
| Session — forget credentials on exit | `ConnectionSettingsViewModel.ForgetCredentialsOnExit` |
| Transfer settings: parallel, threshold, part size, retries, integrity | `TransferSettingsViewModel` with R2 multipart validation |
| Application behaviour: startup, close button, background transfers, confirmations, theme segmented control | `ApplicationSettingsViewModel` |

The example credentials, account IDs and endpoints in the references are **never** written
into production defaults. They appear only in `DesignTime/DesignData.cs`, which is guarded
by `#if DEBUG` and by `d:DataContext` so it cannot enter a Release build.

### R7–R9 — `Images/08`, `09`, `10`

Component reference for the resource dictionaries:

| Reference block | Dictionary | Keys |
| --- | --- | --- |
| Buttons default/hover/pressed/focused/disabled | `Controls.Buttons.xaml` | `Button.Primary`, `Button.Secondary`, `Button.Ghost`, `Button.Danger`, `Button.Icon` |
| Form controls, select, open select, toggle, checkbox, search | `Controls.Inputs.xaml`, `Controls.Toggles.xaml` | `TextBox.Default`, `TextBox.Error`, `ComboBox.Default`, `ToggleSwitch`, `CheckBox.Default`, `SearchBox` |
| Nav tabs, inspector tabs, segmented, filter chips | `Controls.Navigation.xaml` | `Tabs.Navigation`, `Tabs.Inspector`, `SegmentedControl`, `Chip.Filter` |
| Badges | `Controls.Badges.xaml` | `Badge.Queued/Uploading/Completed/Paused/Failed/Neutral/Public/Count` |
| Progress 4 px row bar and 6 px overall bar in four colours | `Controls.Progress.xaml` | `ProgressBar.Row`, `ProgressBar.Overall`, state brushes |
| Table | `Controls.Tables.xaml` | `DataGrid.Default`, `DataGridRow.Default`, `DataGridColumnHeader.Default` |
| Context menu | `Controls.Menus.xaml` | `ContextMenu.Default`, `MenuItem.Default`, `MenuItem.Danger` |
| Toasts, dialog anatomy | `Controls.Dialogs.xaml` | `Toast.Success/Error/Progress`, `Dialog.Window`, `Dialog.Footer` |
| Inspector preview states, empty folder, skeleton rows | `Controls.Placeholders.xaml` | `Placeholder.Loading/Unsupported/Error/Empty`, `Skeleton.Row` |
| Metadata groups (96 px label lane) | `Controls.Metadata.xaml` | `MetadataGroup`, `MetadataRow`, `CopyValueButton` |
| Tooltips | `Controls.Tooltips.xaml` | `ToolTip.Default` |

## 4. Accessibility mapping

* Every icon-only control carries `ToolTip` **and** `AutomationProperties.Name`; destructive
  and stateful ones also carry `AutomationProperties.HelpText`.
* Status is never colour-only: badges show text (`Completed`, `Failed`, `Paused`), the
  connection dot is paired with the word `Connected`, and progress exposes
  `AutomationProperties.Name` such as "Uploading app-1.7.5-win-x64.zip, 57 percent".
* Focus visuals are a 1 px `Brush.Accent` ring plus a 1 px offset, defined once in
  `Controls.Focus.xaml` and referenced by every template. No template removes the focus
  visual.
* Access keys: `_Upload`, `_Files`, `_Activity`, `_Save`, `_Cancel`, `_Test connection`.
* `KeyboardNavigation.TabNavigation=Cycle` on dialogs traps focus; `IsDefault`/`IsCancel`
  give Enter/Escape; `ContextMenu` opens with the Menu key or Shift+F10 at the focused row.

## 5. Keyboard map preserved from the WinForms build

| Key | Action | View model member |
| --- | --- | --- |
| `Delete` | delete selection | `FilesViewModel.DeleteCommand` |
| `Ctrl+C` | copy object keys | `CopyKeysCommand` |
| `Ctrl+Shift+C` | copy public URLs | `CopyPublicUrlsCommand` |
| `Ctrl+D` | download selection | `DownloadCommand` |
| `Ctrl+F` | focus filter | `FocusFilterCommand` |
| `Escape` | clear filter, else clear selection | `EscapeCommand` |
| `Enter` | open the single selected folder | `OpenCommand` |
| `F5` | refresh | `RefreshCommand` |
| `Backspace` | parent prefix | `GoUpCommand` |
| `Ctrl+A` | select all | `SelectAllCommand` |
| `F2` | rename (single selection) | `RenameCommand` |
| `Ctrl+K` | copy key (menu accelerator shown in R3) | `CopyKeysCommand` |
| `Alt+U` / `Alt+B` / `Alt+A` | Upload / Files / Activity | `ShellViewModel.NavigateCommand` |
| `Alt+O` / `Alt+D` | Browse files / browse folder from anywhere | `ShellViewModel.BrowseFilesCommand`, `BrowseFolderCommand` |
| `Alt+P` | Start every queued upload | `ShellViewModel.StartUploadsCommand` |
| `Ctrl+O` / `Ctrl+Shift+O` | Browse files / browse folder on Upload | `UploadViewModel.BrowseFilesCommand`, `BrowseFolderCommand` |
| `Ctrl+,` | Settings | `ShellViewModel.OpenSettingsCommand` |

All of these are `KeyBinding`s on `InputBindings`, so they are real commands with
enablement, not key handlers.
