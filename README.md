# LiveDesk

> Modern, native Windows 11 & 10 live wallpaper engine with fluid video playback, photos, animated GIFs, and YouTube streaming support.

![Windows 11](https://img.shields.io/badge/Windows-11%20%2F%2010-0078d4)
![Architecture](https://img.shields.io/badge/Architecture-x64-black)
![License](https://img.shields.io/badge/License-MIT-green)

---

## ✨ Features

- **Direct Hardware Acceleration**: Smooth 60 FPS video playback powered by embedded LibVLC backend.
- **Master Collection & Playlists**: Centralized library with two-way playlist syncing, multi-playlist loops, and custom transition timers.
- **Spotify-Style Playing Queue**: Reorder upcoming wallpapers with drag-and-drop or move buttons, inspect active queues, and jump straight to any track.
- **Play Next & Add to Queue**: Right-click any card or use the inspector to queue items on the fly without stopping your loop.
- **YouTube Streaming & Downloads**: Paste any YouTube link to stream directly without downloading, or download up to 4K/8K MP4s.
- **Photos & Animated GIFs**: Full support for `.jpg`, `.png`, `.webp`, and animated `.gif` wallpapers.
- **Smart Eco & Focus Rules**: Automatically pauses or mutes when games, fullscreen windows, or work apps cover your desktop.
- **Desktop Mini Controls HUD**: Prev / Play-Pause / Next & seekbar overlay placed directly on your desktop (double-click to reopen app).
- **Inbuilt Transparent Taskbar**: Windows 11 transparent taskbar hook without third-party tools.

---

## 🚀 Installation

Download the single-file installer from the [Releases](https://github.com/Prathamesh0612/LiveDesk/releases) section:

👉 **`LiveDesk_Setup.exe`**

Double-click to install. It packages all VLC runtimes, taskbar hooks, and tools into one self-contained setup.

---

## 🛠️ Building from Source

Requirements:
- Windows 10 or Windows 11 (x64)
- .NET Framework 4.8 / 4.7.2 runtime & `csc.exe` (included with Windows)
- Visual Studio Build Tools (optional, only needed if modifying `TaskbarTap.cpp`)

To build:
```cmd
cd Desktop
build.bat
```
Output: `LiveDesk.exe` + `TaskbarTap.dll`.
