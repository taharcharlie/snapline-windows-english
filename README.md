# Snapline for Windows — English, Ashes edition

A clothesline for the things you are working with. Screenshots, notes, files, folders and links hang from a line under the top of the screen; touch the line and they drop into view, ready to copy or drag into another app.

This is an English fork of [TE1233/snapline-windows](https://github.com/TE1233/snapline-windows) (version 0.5.8), restyled in the Base16 Ashes palette and extended. The interaction idea comes from [Tendedero](https://github.com/alejandrobujan/tendedero) for macOS. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Everything runs on your PC. The app has no network code, uploads nothing, and does not change Windows screenshot settings.

## What is different from upstream

- English interface.
- Dark, square Ashes theme with the Barlow Condensed font (falls back to Segoe UI if it is not installed).
- The line hangs from a [komorebi-bar](https://github.com/LGUG2Z/komorebi) at the top of the screen when one is running, and its tension is adjustable.
- The line rolls: any number of cards, with the focused card enlarged, driven by wheel, drag or keyboard.
- Reorder by dragging, pull a card off to drop it elsewhere, delete on a peg or a bin, with Undo. "Keep" protects a card from every kind of clearing and gives it a yellow edge.
- Files, folders and web addresses hang as reference cards. The original is never moved, changed or deleted.
- Hang the clipboard with one shortcut; copy a card by its number.
- A note can carry one picture: copy an image, open the note and press `Ctrl+V`.
- Optional auto-clear of old cards.
- Start at login is off until you turn it on.
- The cards hang still and skip the falling animation on battery, or when Windows animations are off.

## Shortcuts

| Keys | Action |
|---|---|
| `Ctrl+Alt+S` | Open the line and keep it open; again to close |
| `←` `→` | Roll the line (after `Ctrl+Alt+S`) |
| `Enter` | Copy the focused card and close |
| `1`–`9` | Copy the card with that number and close |
| `K` | Keep or stop keeping the focused card |
| `Delete` | Delete the focused card (Undo appears) |
| `Esc` | Close |
| `Ctrl+Alt+D` | Hang what is on the clipboard |
| `Ctrl+Alt+A` | Capture a region and hang it (default; change it in the tray menu) |

While the line is open from `Ctrl+Alt+S`, these plain keys go to Snapline rather than the app underneath, until you close the line or click somewhere else.

## Mouse

- Touch the line at the top centre of the screen to open it. It hides when you leave.
- Click a card to copy it. Double-click to open it (a note opens for editing).
- A note copies a moment after the click, so double-clicking one to paste into it leaves the clipboard alone.
- Scroll, or swipe sideways, to roll the line.
- Drag a card along the line to reorder.
- Pull a card down to take it off the line: drop it into another app, or on the Tips card to delete it.
- Click a peg to delete the card. Undo shows for a few seconds.
- Right-click a card for more, including Keep.
- Drop a file, folder, link or text onto a card to hang it. Images are copied onto the line; other files and folders are hung as references.
- Hover the Tips card for a reminder of all of this.

## Tray menu

Capture shortcut, line tension, touch line length, auto-clear, start at login, keep visible, import images, clear screenshots, help and quit.

The touch line is the thin strip at the top centre that opens the line. It has three lengths: short, medium (one and a half times that), and long, which matches the width of the date and clock in the middle of a komorebi bar.

## Limits and data

- Up to 60 screenshots and 60 reference cards are held; when a limit is passed, the oldest card that is not kept makes room.
- Only captures made with Snapline's own capture shortcut are collected. `Win+Shift+S` and other screenshot tools are not.
- Data lives in `%LOCALAPPDATA%\Snapline`. `--data-dir <folder>` uses another folder.

## Build

Requires Windows with .NET Framework 4.8, which Windows 10 and 11 include. No SDK or packages are needed; the script uses the compiler that ships with the framework, so the source is limited to C# 5.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The result is `dist\Snapline.exe`. `--self-test` runs the built-in checks.

## Source layout

| File | Contents |
|---|---|
| `src\Theme.cs` | Colours, font, menu styles, calm mode |
| `src\Store.cs`, `Memo.cs`, `Links.cs` | Saved data: screenshots, notes, reference cards |
| `src\Shelf*.cs` | The line window: layout, gestures, motion, capture, tray |
| `src\Remark.cs`, `ShortcutEditor.cs`, `RegionCapture.cs` | Caption editor, shortcut dialog, capture overlay |
| `src\*Tests.cs`, `Integration.cs` | Built-in self-tests |

## License

MIT, see [LICENSE.txt](LICENSE.txt).
