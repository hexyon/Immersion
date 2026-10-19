# Immersion

A native Windows photo viewer with a dark interface, fullscreen slideshows, and an animated Lava Flow strip colored by the active photo.

## Features

- Photo library with folder import, search, and favorites.
- Cursor-centered zoom from 100% to 400%, drag-to-pan, double-click to reset zoom, and save-copy rotation.
- Click the left or right image side zone to navigate, or use the arrow buttons and keyboard.
- Fullscreen slideshows with shuffle, loop, and adjustable timing.
- Two photo-colored animations: Lava Flow v1 restores the original thin flowing strip; Lava Flow v2 uses flowing liquid and deforming wax blobs. Select either in the slideshow controls, then click it again to turn it off. Selecting one switches off the other; v2 is enabled by default.
- Optional Windows default-app registration.

## Build and run

Build on Windows with PowerShell and the .NET Framework 4.x compiler and WPF assemblies installed. No NuGet packages are required. The build script uses the compiler under `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`.

```powershell
.\build.ps1
.\Immersion.exe
```

The build generates `icons/Immersion.ico` from the source PNG and produces `Immersion.exe`. You can also use `Launch.cmd`, or pass an image path to the executable.

Use `Register-default-app.cmd` to register Immersion and open Windows default-app settings. Use `Unregister-default-app.cmd` to remove its registration. Normal library preferences are stored in `%LOCALAPPDATA%\Immersion`.

## Verification

Run these commands from the project directory after building:

```powershell
Start-Process -FilePath .\Immersion.exe -ArgumentList '--self-test' -Wait
.\tests\verify-lava.ps1
```

The viewer checks create synthetic images and isolated test state in `test-output/`. The fluid checks use those images by default; an optional `-ReferencePhoto <absolute-image-path>` selects another image. Results and screenshots are generated locally and excluded from Git.

See [LAVA-FLOW.md](LAVA-FLOW.md) for the fluid model and research references, and [CHANGELOG.md](CHANGELOG.md) for changes.

## GitHub distribution

Commit the source, scripts, documentation, and source icon. Generated executables, icons, and test output are ignored. Attach the built `Immersion.exe` to a GitHub Release for users; the launcher and registration scripts can be distributed alongside it. Keep the executable and those scripts in the same folder.
