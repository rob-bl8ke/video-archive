## Full Build Plan

### Phase 1 — Stabilize & Structure (Next immediate steps)

**Step 1.1 — Restore missing packages and clean up**
- Re-add `CommunityToolkit.WinUI.UI.Controls.DataGrid` — with the CsWinRT 2.2.0 pin already in place, the conflict should now be resolved
- Re-add `Microsoft.EntityFrameworkCore.Tools`
- Clean up App.xaml.cs (remove temp debug handler, add proper DI host wiring)
- Restore `{ThemeResource}` styles in MainWindow.xaml now that the conflict is fixed

**Step 1.2 — Establish folder structure**
```
src/VideoArchive/
├── Data/
│   ├── VideoArchiveContext.cs      # EF Core DbContext
│   └── Migrations/                 # EF Core migrations
├── Models/
│   ├── Video.cs
│   ├── Tag.cs
│   ├── VideoTag.cs
│   ├── VideoSegment.cs
│   ├── LibraryFolder.cs
│   └── AppSettings.cs
├── Services/
│   ├── IVideoRepository.cs / VideoRepository.cs
│   ├── ITagService.cs / TagService.cs
│   ├── ILibraryScanner.cs / LibraryScanner.cs
│   ├── IThumbnailService.cs / ThumbnailService.cs
│   └── ISettingsService.cs / SettingsService.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── VideoPlayerViewModel.cs
│   └── SettingsViewModel.cs
└── Views/
    ├── MainWindow.xaml / .cs
    ├── GalleryView.xaml / .cs      # ItemsRepeater
    ├── DetailsView.xaml / .cs      # DataGrid
    ├── PlayerPanel.xaml / .cs      # LibVLCSharp VideoView
    └── SettingsPage.xaml / .cs
```

---

### Phase 2 — Data Layer

**Step 2.1 — EF Core models**
```csharp
// Key entities and relationships:
Video        → Id, FilePath, Title, Duration, Format, Resolution,
               Codec, ThumbnailPath, DateAdded, FileSize
Tag          → Id, Name, Color (hex string)
VideoTag     → VideoId (FK), TagId (FK)  [junction]
VideoSegment → Id, VideoId (FK), Name, StartTime, EndTime, Description
LibraryFolder→ Id, Path, IsActive, LastScanned
AppSettings  → Id, ViewMode, SortColumn, SortDirection, LastFilterJson,
               WindowWidth, WindowHeight, WindowLeft, WindowTop
```

**Step 2.2 — DbContext + migrations**
- Configure `VideoArchiveContext` with `OnConfiguring` pointing to `%AppData%/VideoArchive/videoarchive.db`
- Run initial `dotnet ef migrations add InitialCreate`
- Run `dotnet ef database update` to verify schema

---

### Phase 3 — DI and App Wiring

**Step 3.1 — DI host in App.xaml.cs**
```csharp
// Register all services as singletons, ViewModels as transients
// Store IServiceProvider as static App.Services for ViewModel resolution
// Initialize EF Core migration on startup (ensure DB exists)
// Call LibVLC Core.Initialize() here
```

**Step 3.2 — SettingsService**
- Wrap `ApplicationData.Current.LocalSettings` for key/value persistence
- Save/restore: ViewMode, SortColumn, SortDirection, active tag filters (JSON), window size/position

---

### Phase 4 — Main Window Shell

**Step 4.1 — NavigationView shell**
- Root layout: `NavigationView` with left pane
- Pane items: Library, Settings
- Top area: `AutoSuggestBox` (search), view toggle buttons (gallery/details icons), "Refresh Library" button
- Right panel: tag filter sidebar with CheckBoxes
- Content area: hosts `GalleryView` or `DetailsView` based on toggle

**Step 4.2 — Split layout for player**
- `SplitView` or `Grid` row split: library view (top/left) + player panel (bottom/right)
- Player panel collapsible

---

### Phase 5 — Library Scanner

**Step 5.1 — LibraryScanner service**
- Accept list of `LibraryFolder` paths
- Recursively enumerate `.mp4` and `.mkv` files
- For each file: extract metadata via TagLib# (title, duration, codec, resolution from container)
- Skip files already in DB (check by FilePath)
- Remove DB entries for files that no longer exist
- Run on `Task.Run` background thread with `CancellationToken`

**Step 5.2 — ThumbnailService**
- For each new video, spin up a headless LibVLC `MediaPlayer`
- Seek to 10% of duration
- Call `TakeSnapshot()` → save to `%AppData%/VideoArchive/Thumbnails/{VideoId}.jpg`
- Report progress via `IProgress<(int current, int total)>`

**Step 5.3 — Progress dialog**
- Modal `ContentDialog` showing "Scanning: X / Y videos processed"
- Cancel button wired to `CancellationTokenSource`

---

### Phase 6 — Gallery View (ItemsRepeater)

**Step 6.1 — ItemsRepeater layout**
- `ScrollViewer` > `ItemsRepeater` with `UniformGridLayout` (auto columns based on width)
- `DataTemplate` per card:
  - 200×112px `Image` (thumbnail, bound to `ThumbnailPath`)
  - `TextBlock` title (1 line, ellipsis trim)
  - `TextBlock` duration badge
  - `ItemsRepeater` for tag chips (small colored pills)
- Selection: track `SelectedVideo` in `MainViewModel`, highlight via `VisualState`

**Step 6.2 — Thumbnail image loading**
- Bind `Image.Source` to `ThumbnailPath` via converter (`StringToImageSourceConverter`)
- Show placeholder image if file missing

---

### Phase 7 — Details View (DataGrid)

**Step 7.1 — DataGrid columns**
```
Thumbnail (Image, 80px) | Title | Duration | Format | Resolution | Tags | Date Added | File Size
```
- Sortable columns bound to `MainViewModel.SortCommand`
- Row selection updates `SelectedVideo`
- Context menu: "Play", "Add Tags", "Edit Metadata", "Remove from Library"

---

### Phase 8 — Tag Management

**Step 8.1 — Tag manager UI**
- Accessible from toolbar button or Settings page
- `ListView` of existing tags with color swatch, name, edit/delete buttons
- Add tag: `TextBox` + `ColorPicker` + Save
- Write tag names to MP4/MKV container via TagLib# `Tag.Comment` or custom field as semicolon-delimited string (e.g. `"Action;Favourite;Review"`)
- Sync bidirectionally: scan reads container tags → creates missing tag records in DB

**Step 8.2 — Apply tags to videos**
- Multi-select in gallery or DataGrid → context menu "Add Tags" → `CheckBox` list of tags → confirm

---

### Phase 9 — Video Player

**Step 9.1 — LibVLC integration**
- `Core.Initialize()` in App.xaml.cs DI setup
- `PlayerPanel.xaml`: `LibVLCSharp.Platforms.Windows.VideoView` control
- `VideoPlayerViewModel`: owns `LibVLC` and `MediaPlayer` instances (singletons)
- Dispose both in `App` shutdown via `IDisposable` service registration

**Step 9.2 — Transport controls**
- Play/Pause toggle
- Seek `Slider` bound to `MediaPlayer.Time` via DispatcherQueue timer
- Volume `Slider`
- Duration / current time `TextBlock`
- Fullscreen button

**Step 9.3 — Segment bookmark UI (data only, no playback logic yet)**
- ListView below player showing `VideoSegment` records for current video
- Add/Edit/Delete segment (name, start time, end time)
- "Set Start"/"Set End" buttons capture current `MediaPlayer.Time`
- Playback loop/transition logic deferred to future phase

---

### Phase 10 — Settings Page

- Folder management: `ListView` of `LibraryFolder` paths, add (FolderPicker), remove
- "Refresh Library" triggers scanner
- "Clean Thumbnail Cache" — delete orphaned `.jpg` files
- "Rebuild All Thumbnails" — regenerate all
- Theme toggle (Light/Dark) via `Application.RequestedTheme`

---

### Phase 11 — Polish

- Window size/position persistence via `SettingsService`
- Empty state UI (first run: "Add a folder to get started")
- Error handling for corrupted video files (TagLib# throws on bad containers)
- Search: filter `MainViewModel.Videos` collection by title/filename via `AutoSuggestBox`

