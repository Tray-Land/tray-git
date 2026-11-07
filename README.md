# Tray Git

![The Tray Git flyout open above the taskbar, filtered to "tray", listing repos with terminal, folder, and remote buttons](docs/flyout.png)

Every git repo in your source folder, one click from the taskbar: open a terminal in it, open it in File Explorer, or open its remote in the browser.

Tray Git is a WinUI 3 app that lives in the Windows notification area. It has no main window: the tray icon opens a small, always-on-top flyout next to the icon with a filterable list of your repos.

## Features

- **Source folder:** pick the folder that holds your repos (for example `D:\source`). Tray Git finds every working copy up to four folders deep. It stops at each repo, so submodules and vendored checkouts don't clutter the list, and it skips `node_modules`, `bin`, `obj`, dot-folders and similar trees.
- **Three one-click actions per repo:**
  - **Terminal** opens Windows Terminal in the repo (`wt -d`), following your Terminal settings for new tab vs. new window. Without Windows Terminal it falls back to PowerShell 7, then Windows PowerShell.
  - **Folder** opens the repo in File Explorer. Double-clicking a row does the same.
  - **Remote** opens the `origin` remote (or the first remote) in your browser. GitHub, GitLab, Bitbucket, Azure DevOps and self-hosted servers work, whether the remote is https, ssh (`git@host:owner/repo.git`) or `git://`. Credentials and ssh ports are stripped. The button is disabled for repos with no remote or a local-path remote.
- **Filter:** start typing as soon as the flyout opens. Every word must match the repo name or its path under the source folder. Press **Enter** to open a terminal in the top match.
- **Recent first:** repos you've used through Tray Git sort to the top. Otherwise the list is alphabetical.
- **Branch at a glance:** each row shows the checked-out branch (or short commit when detached), then its status, then the parent folder. Status is lines added in green and removed in red (working tree and index versus `HEAD`), `↓n` commits to pull and `↑n` commits to push.
- **Fetch:** right-click a row, or swipe it left on a touch screen or pen, and choose **Fetch** to run `git fetch` and refresh that row's status. A spinner shows while it runs, and errors appear in the flyout.
- **Repository details:** use the chevron on a repo row, or choose **View details** from its context menu, to open a dedicated page with the current branch, path, remote, ahead/behind state and changed files. It also has one-tap **Fetch**, **Pull**, **Push** and **Sync** controls, plus the 30 most recent commits with authors and dates and a complete list of local and remote branches. **View full diff** opens a second page with the selectable, monospaced patch relative to `HEAD`.
- **Favorites:** right-click a row, or swipe it left, and choose **Favorite** (or **Unfavorite**). Favorites get a star next to the name. The star button in the header narrows the list to favorites only; it stays on until you turn it off, and the filter box still applies within it.
- **Dirty only:** the edit button between Favorites and Rescan narrows the list to repos with local working-copy changes, including staged, modified, binary, and untracked files. It can be combined with Favorites and the filter box; repos that are only ahead or behind are not considered dirty.
- **Worktrees:** `git worktree` checkouts are listed as their own entries, with the remote read from the main repo.
- **Light dismiss:** the flyout hides when it loses focus or when you press Escape, and clicking the tray icon toggles it.
- **Tray menu:** right-click the icon for Open, Settings and Exit.
- **Settings:** a page inside the flyout (gear button or the tray menu) with the source folder and Start with Windows. Escape, Alt+Left or the mouse back button returns to the list.
- **Theme-aware tray icon:** a dark glyph on a light taskbar, a white glyph on a dark one, switching live with the Windows theme.
- **Single instance:** launching the app again opens the running instance's flyout.

## Install

Download and open **[TrayGit.appinstaller](https://github.com/Tray-Land/tray-git/releases/latest/download/TrayGit.appinstaller)** with Windows App Installer.

Windows picks the right build for your PC (x64 or ARM64), installs the Windows App Runtime if it is missing, and keeps Tray Git up to date from the newest stable release: it checks when the app starts and in the background, and downloads only the parts of the package that changed.

**Current release issue:** the latest published feed points to a different release tag, so its package downloads fail. The release also does not include the certificate needed to trust its self-signed package. A corrected release is needed before this install link can work.

Requirements: Windows 10 1809 (build 17763) or later, or Windows 11, on x64 or ARM64. [Windows Terminal](https://aka.ms/terminal) is recommended but not required. [Git](https://git-scm.com/download/win) must be on the PATH for status, repository details and Git actions; without it the list still works, just without those features.

## Setup

On first run the flyout opens with **Choose source folder**. Pick your folder and the list appears. Change it any time from the gear button. There are no accounts or keys.

## Privacy and footprint

- Tray Git makes **no network calls of its own**. The only things that leave your machine are the browser you open with the Remote button and the `git fetch`, `pull`, `push` or `sync` you start, which use your normal git remotes and credentials.
- The list and scan read only each repo's `.git/HEAD` and `.git/config`. While the flyout is open, the status segment runs read-only `git diff HEAD --numstat` and `git status` in each repo (no locks taken, no files changed), at most every 30 seconds per repo.
- Repository details run local read-only commands only while that page is visible. The diff page reads `git diff HEAD`; untracked files are shown in the changes list but Git does not include their contents in the patch until they are staged.
- The scan runs only while the flyout is open: when it opens and the last scan is over five minutes old, when you change the source folder, or when you press Rescan. Results are cached in the app's local folder (`repos.json`) so the list appears instantly.
- Settings (the source folder, recent-use times, favorites and list-filter toggles) are stored in the app's local settings. Nothing is stored outside the app's package data.
- When hidden for a minute, the flyout closes and returns its memory to Windows.

## Build from source

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download), the winapp CLI (`winget install Microsoft.WinAppCli`), and Developer Mode turned on (Settings > System > For developers).

```powershell
winapp run              # build, register with package identity, and launch
winapp run --clean      # same, starting from first-run state
dotnet test tests\TrayGit.Tests
```

Look in the taskbar overflow (^) if the tray icon is hidden.

Releases are built with `build-msix-for-gh.ps1`, which builds x64 and ARM64 packages, bundles them into one signed `.msixbundle`, writes the `.appinstaller` feed, checks the bundle, and creates a draft GitHub release. The feed's `latest/download` link starts working when you publish the draft as the latest (non-pre-release) release.

Publish the draft without changing its tag, then run `.\build-msix-for-gh.ps1 -VerifyPublished` to check the live feed and its download links.

## Project layout

| Path | What it holds |
|---|---|
| `App.xaml.cs` | Tray icon, context menu, single instance, the only exit path |
| `Views/TrayFlyoutWindow` | Borderless popup: placement, light dismiss, animation, idle close, page swap, and the folder picker (light dismiss pauses while it is open) |
| `Views/FlyoutPage` | The repo list, filter, and actions |
| `Views/RepoDetailPage`, `Views/RepoDiffPage` | Repository metadata, Git actions, changes, commits, branches, and full diff |
| `Views/SettingsPage` | Settings page shown in the flyout: source folder, Start with Windows, version |
| `Core/` | Platform-free logic: `RepoScanner`, `GitMetadata` (`.git` parsing), `RemoteUrlPolicy` (remote → web URL), `RepoListPolicy` (filter and order), `RepoStatus` and `RepoDetails` (git output parsing) |
| `Services/RepoStore`, `Services/RepoLauncher`, `Services/GitService` | Scan cache, the terminal / folder / remote launchers, and the git status, detail, diff and operation runner |
| `tests/TrayGit.Tests` | MSTest project that links `Core/` |

## License

MIT, see [LICENSE](LICENSE).
