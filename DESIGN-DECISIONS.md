# Video Archive - Design Decisions Log

**Purpose:** Track architectural and design choices for chat continuations and future reference.

---

## Technology Stack

### UI Framework: WinUI 3
- **Decision:** WinUI 3 with .NET 9 (Windows-only desktop)
- **Rationale:** 
  - Microsoft's strategic future for Windows desktop apps
  - Active development and investment
  - Modern Fluent Design with Windows 11 integration
  - Better video playback integration (no airspace issues)
  - Similar XAML/MVVM to WPF with 1-2 week learning curve
- **Alternatives Considered:** WPF (mature but maintenance mode), Avalonia UI (cross-platform but smaller ecosystem)
- **Date:** 2026-01-31

### Video Playback: LibVLCSharp
- **Decision:** LibVLCSharp.WinUI + VideoLAN.LibVLC.Windows
- **Rationale:**
  - Extensive playback versatility needed
  - Future video editing capabilities planned
  - Hardware-accelerated D3D11 rendering
  - Supports advanced features (segments, loops, transitions)
  - ~50MB+ dependency acceptable
- **Alternatives Considered:** MediaElement (too limited), MediaPlayerElement (lacks advanced features)
- **Date:** 2026-01-31

### MVVM Framework: CommunityToolkit.Mvvm
- **Decision:** CommunityToolkit.Mvvm (official Microsoft toolkit)
- **Rationale:**
  - Source generators reduce boilerplate
  - Native WinUI 3 support
  - Dependency injection integration
  - `[ObservableProperty]` and `[RelayCommand]` attributes
- **Date:** 2026-01-31

### Database: Entity Framework Core + SQLite
- **Decision:** EF Core with SQLite provider
- **Rationale:**
  - Familiar .NET ORM
  - File-based (portable, no server setup)
  - Good performance for local data
  - Code-first migrations
- **Date:** 2026-01-31

### Metadata Strategy: TagLib# + Database Hybrid
- **Decision:** Write to container metadata where possible, fallback to database
- **Rationale:**
  - TagLib# can write to MP4/MKV container tags
  - Keeps metadata portable with video files
  - Database stores custom fields, relationships, app-specific data
- **Date:** 2026-01-31

---

## UI/UX Architecture

### View Layout: Dual-View (Gallery + Details)
- **Decision:** ItemsRepeater for gallery view, CommunityToolkit DataGrid for details view
- **Rationale:**
  - ItemsRepeater: Modern, performant, customizable thumbnail gallery
  - DataGrid: Familiar spreadsheet view for metadata-heavy browsing
  - User can toggle between visual and data-centric workflows
- **Alternatives Considered:** Single view approach (less flexible)
- **Date:** 2026-01-31

### View State Persistence
- **Decision:** Remember view mode, sort, filters between sessions
- **Rationale:** Improves user experience, reduces friction
- **Implementation:** SettingsService using Windows.Storage.ApplicationData
- **Date:** 2026-01-31

---

## Deployment & Distribution

### Development Environment
- **Decision:** VS Code exclusively on local Windows (no Dev Containers)
- **Rationale:**
  - WinUI 3 requires Windows SDK and GUI rendering
  - Containers can't provide DirectX/composition APIs
  - Windows containers lack GUI support and have poor tooling
- **Tools:** VS Code with C# Dev Kit, custom tasks.json/launch.json for build/debug
- **Key Extensions:** C# Dev Kit, NuGet Gallery, SQLite Viewer, XAML Styler, GitLens, EditorConfig, Error Lens
- **Note:** No XAML designer in VS Code; workflow is edit XAML → run → iterate
- **Alternatives Considered:** Dev Containers (not viable), Visual Studio 2022 (opted for VS Code exclusively)
- **Date:** 2026-01-31

### Packaging Mode: Unpackaged (Initially)
- **Decision:** Start with unpackaged deployment
- **Rationale:**
  - Simpler development and debugging
  - Full file system access without capabilities
  - Traditional .exe distribution
  - Supports portable hard drive scenarios
- **Future Consideration:** Add MSIX packaging after core features stable for Store distribution and auto-updates
- **Date:** 2026-01-31

### File System Access: User-Selected Folders
- **Decision:** User configures watched folders via folder picker
- **Rationale:**
  - Explicit user control over scanned locations
  - Supports portable drives
  - No need for `broadFileSystemAccess` capability
  - Works seamlessly with unpackaged deployment
- **Implementation:** Store folder paths in `LibraryFolder` table
- **Date:** 2026-01-31


---

## Performance & Storage

### Thumbnail Strategy: File System Cache
- **Decision:** Store thumbnails as .jpg files in `%AppData%/VideoArchive/Thumbnails/`
- **Rationale:**
  - Industry standard approach
  - Avoids database bloat
  - Fast file system access
  - Easy cleanup and management
  - SQLite blob storage would impact performance
- **Format:** `{VideoId}.jpg` in AppData folder
- **Generation:** LibVLC snapshot at 10% video duration
- **Alternatives Considered:** Database blob storage (rejected for performance)
- **Date:** 2026-01-31
### Thumbnail Generation Strategy
- **Decision:** Generate thumbnails during library scan (not lazy-load)
- **Rationale:**
  - Industry standard approach (Plex, Jellyfin, Windows Photos, Adobe Bridge)
  - One-time upfront cost with progress indication
  - Smooth, responsive gallery browsing afterward (no placeholder pop-in)
  - Simpler implementation than lazy-load queue management
- **Implementation:** Progress dialog "Scanning library: X/Y videos processed", parallelized on background thread pool with cancellation support
- **Alternatives Considered:** Lazy-loading on scroll (adds complexity, worse browsing experience)
- **Date:** 2026-01-31

### Library Folder Monitoring
- **Decision:** Manual refresh only (button/menu item)
- **Rationale:**
  - Video files don't change frequently in this use case
  - Avoids FileSystemWatcher performance overhead
  - User has explicit control over scan timing
- **Implementation:** "Refresh Library" button in toolbar
- **Alternatives Considered:** Real-time FileSystemWatcher (rejected for unnecessary complexity)
- **Date:** 2026-01-31

### Thumbnail Cache Management
- **Decision:** Implement cleanup tools for orphaned thumbnails
- **Rationale:** Maintain disk space efficiency when videos are removed
- **Implementation:**
  - "Clean Thumbnail Cache" — removes orphaned .jpg files with no matching Video.Id
  - "Rebuild All Thumbnails" — regenerates missing/corrupted thumbnails on demand
  - Both accessible in settings page
- **Date:** 2026-01-31

---

## Resolved Issues

### WinRT.Runtime Version Conflict (LibVLCSharp + WindowsAppSDK 1.8)
- **Symptom:** App exits silently with no window. `TypeInitializationException` on `XamlControlsResources` at startup.
- **Root Cause:** `LibVLCSharp 3.9.6` transitively depends on `microsoft.windows.sdk.net.ref` which ships `WinRT.Runtime` 2.1.0. WindowsAppSDK 1.8 requires `WinRT.Runtime` 2.2.0. MSBuild chose 2.1.0 as "primary", causing the XAML resource system to fail at runtime.
- **Diagnosis:** MSB3277 warnings in build output showed the conflict. Bisecting packages one-by-one confirmed LibVLCSharp as the trigger.
- **Fix:** Pin `Microsoft.Windows.CsWinRT` 2.2.0 with build assets excluded:
  ```xml
  <PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.2.0" ExcludeAssets="build;buildTransitive" />
  ```
  `ExcludeAssets` is required because `cswinrt.exe` needs `Platform.xml` from a full Visual Studio install, which is not available in a VS Code-only environment.
- **Key Lesson:** MSB3277 assembly conflict warnings are **not benign** — they indicate potential runtime crashes. Always resolve `WinRT.Runtime` version conflicts explicitly.
- **Date:** 2026-02-28

---

## Future Features (Deferred)

### Video Segments & Playback Enhancements
- **Planned:** Loop sections, play specific parts, transition between segments
- **Implementation:** `VideoSegment` entity with StartTime/EndTime/Name
- **Data Model:** Ready in initial schema, UI/playback logic deferred
- **Date:** TBD

---

## Project Structure

```
video-archive/
├── .vscode/
│   ├── tasks.json          # Build/clean/restore tasks (Ctrl+Shift+B)
│   └── launch.json         # Debug launch configs (F5)
├── src/
│   └── VideoArchive/
│       ├── VideoArchive.csproj
│       ├── App.xaml / App.xaml.cs
│       └── MainWindow.xaml / MainWindow.xaml.cs
├── VideoArchive.sln
└── DESIGN-DECISIONS.MD
```

## NuGet Packages

| Package | Purpose |
|---------|---------|
| Microsoft.WindowsAppSDK 1.8.x | WinUI 3 framework |
| Microsoft.Windows.SDK.BuildTools | Windows SDK build support |
| Microsoft.EntityFrameworkCore.Sqlite | Database ORM + SQLite |
| Microsoft.EntityFrameworkCore.Tools | EF Core migrations CLI |
| CommunityToolkit.Mvvm | MVVM source generators |
| Microsoft.Extensions.DependencyInjection | DI container |
| Microsoft.Windows.CsWinRT 2.2.0 | Pin WinRT.Runtime 2.2.0 (`ExcludeAssets="build;buildTransitive"`) |
| LibVLCSharp + LibVLCSharp.WinUI | Video playback integration |
| VideoLAN.LibVLC.Windows | LibVLC native binaries |
| TagLibSharp | MP4/MKV container metadata |

---

## Notes for Future Development

- .NET 9 SDK (9.0.101) with TFM `net9.0-windows10.0.19041.0`
- Unpackaged mode: `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`
- Build with `-p:Platform=x64` (project configured for x64 and ARM64)
- All ViewModels should use CommunityToolkit.Mvvm source generators
- Keep business logic in services, not ViewModels or code-behind
- Use async/await for all I/O operations (file scanning, database, video loading)
- LibVLC requires `Core.Initialize()` call before any video operations
- TagLib# File handles must be disposed properly to release file locks
- WinUI 3 uses DispatcherQueue (not WPF Dispatcher) for UI thread marshalling
- MSB3277 warnings (WinRT.Runtime version conflict) are **not benign** — they cause runtime crashes. Resolved by pinning `Microsoft.Windows.CsWinRT` 2.2.0 with `ExcludeAssets="build;buildTransitive"`