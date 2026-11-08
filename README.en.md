<p align="center"><img src="assets/moa-logo.png" width="96" alt="Moa logo"></p>

# Moa

[한국어](README.md) · **English**

Moa (모아) is a personal canvas app for Windows. Drop text, shapes, lines, handwriting, images, and videos straight onto the board, with no cards required.

## Download

Get `Moa-<version>-windows-x86_64-setup.exe` from [Releases](../../releases/latest) and run it. No administrator rights are needed.

- Requires 64-bit Windows 10/11 and the Microsoft Edge WebView2 Runtime, which is usually included with Windows 11.
- The installer is not code-signed, so SmartScreen may show a warning or antivirus software may block it.
- The app's interface is in Korean.

## Features

- **Free editing:** place, move, and resize text, shapes, lines, arrows, pen strokes, and images.
- **Highlighter:** press `H` and drag to paint a ruler-straight highlight. Nearly horizontal strokes snap level.
- **Video:** play MP4 and WebM files inside the board. They play only when clicked.
- **Cards:** cards with blocks, to-dos, links, and PDFs, plus groups and connectors.
- **Board management:** organize boards in folders, rename them, move them to the trash, or delete them permanently.
- **Themes:** default, lavender, mint, peach, sky, white, and black.
- **Undo:** `Ctrl+Z` and `Ctrl+Shift+Z`.
- **Automatic backups:** a backup is saved on start and exit when something changed, and the latest 30 are kept.
- **Auto-update:** when a new version is released, Moa downloads the new interface file and shows a card in the sidebar. Press "지금 적용" (Apply now) to switch without running an installer. You can turn this off in the storage menu.

## Where your data lives

Your work is saved in the `MoaData` folder next to the program and is never sent anywhere. The default install location is `%LOCALAPPDATA%\Programs\Moa`.

| Folder | Contents |
|---|---|
| `MoaData\App` | Boards, items, and the app log |
| `MoaData\Originals` | Original attached files |
| `MoaData\Backups` | Automatic backups |

Uninstalling keeps your data unless you choose to delete it.

## Building from source

You need Windows and .NET Framework 4.8. The first build downloads the WebView2 SDK from NuGet.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

Output goes to the `dist` folder.

| Path | Description |
|---|---|
| `app/moa-board.html` | The interface and features (HTML, CSS, JavaScript) |
| `src/MoaApp.cs` | The app window and file storage (C#) |
| `src/MoaSetup.cs` | The installer and uninstaller (C#) |
| `scripts/build.ps1` | Build script |

## Releasing a new version

1. Bump `Version` in `src/MoaSetup.cs`: `1.1.1` for bug fixes, `1.2.0` for new features.
2. Build and commit.
3. Create a GitHub release tagged `v<version>` and attach both the installer and `moa-<version>.html` from `dist`. Without the interface file, installed copies cannot auto-update and will only offer the installer.
4. Write the release notes in both Korean and English.

## License

No project license has been chosen yet. For bundled third-party components, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
