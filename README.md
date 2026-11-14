# AmdPanel — Classic Control Panel for AMD GPUs

[English](README.md) | [Русский](README.ru.md)

**AmdPanel** is a lightweight, fast, open-source control panel for AMD graphics cards, inspired by the classic **NVIDIA Control Panel**.

Built for users who prefer installing clean **Driver-Only** packages without the bloated *AMD Software: Adrenalin Edition*, embedded web engines, or background telemetry—while keeping instant, native access to all 3D, display, color, and tuning settings.

[![Release](https://shields.io)](https://github.com)
[![License: GPL v3](https://shields.io)](LICENSE)
[![Platform](https://shields.io)](#)
[![.NET](https://shields.io)](#)

---

## Features

* **Classic Interface (NVIDIA Control Panel UX):** Familiar two-pane layout with a navigation tree on the left and a dense parameter table on the right. Instant startup, low memory footprint (< 30 MB RAM), and zero persistent background bloat.
* **Smart Hardware Adaptation:** Automatically detects your GPU and monitor capabilities—unsupported features are hidden, and resolution/refresh rate combinations are checked against your cable bandwidth (HDMI / DisplayPort) to prevent black screens.
* **3D Settings & Per-Game Profiles:** Configure *Radeon Anti-Lag*, *Radeon Chill*, *Image Sharpening*, V-Sync, and texture filtering. Supports per-application profiles (`.exe`) with automatic setting restoration when closing a game.
* **Display & Color Management:** Adjust resolution, refresh rate, color depth (8/10 bpc), pixel format (RGB / YCbCr), *AMD FreeSync*, scaling modes, and digital saturation (*Digital Vibrance* equivalent). Includes a 15-second safety rollback timer on display changes.
* **Built-in Driver Manager (Driver-Only):** Download and clean-install official AMD drivers without Adrenalin, including a *Clean Install* option that backs up your profiles automatically.
* **Multi-Language & Multi-GPU Support:** Built-in English and Russian UI localization, plus an active GPU selector for laptops and hybrid graphics setups.

---

## Download & Run

1. Go to [**Releases**](https://github.com).
2. Download **`AmdPanel-v1.0.1-win-x64.zip`**.
3. Extract the archive to any folder and run **`AmdPanel.exe`**.

**System Requirements:**
* Windows 10 (1903 or newer) or Windows 11 (x64).
* Installed AMD graphics driver (*Driver-Only* installation is sufficient).

---

## Supported GPUs

| Architecture | Radeon Series | Support Status |
| :--- | :--- | :--- |
| **Polaris** (GCN 4) | Radeon RX 400 / RX 500 | Full support (partially tested) |
| **Vega** (GCN 5) | Radeon RX Vega 56 / 64, Radeon VII | Full support (not tested yet) |
| **RDNA 1** | Radeon RX 5000 | Full support (not tested yet) |
| **RDNA 2** | Radeon RX 6000 | Full support (not tested yet) |
| **RDNA 3** | Radeon RX 7000 | Full support (not tested yet) |
| **RDNA 4** | Radeon RX 8000 | Full support (not tested yet) |

> *Looking for testers with RX 5000 / 6000 / 7000 / 8000 cards! Feel free to open an [Issue](https://github.com) with feedback.*

---

## Building from Source

**Prerequisites:**
* Visual Studio 2022 (17.8+) or Build Tools with **Desktop development with C++** and **.NET 8 SDK**.

**One-click build:**
```cmd
git clone https://github.com
cd AmdPanel
build.bat
```
The compiled binaries and release archive will be placed in `dist/`.

---

## License & Acknowledgments

**AmdPanel** is licensed under the **GNU General Public License v3.0 (GPL-3.0)** — see [LICENSE](LICENSE) for details. Attribution and a link to the original repository are required when redistributing or modifying the code.

Third-party components used in this project:
* **AMD ADLX SDK** — Official AMD Device Library eXtension (licensed under AMD ADLX SDK terms).
* **SharpCompress** & **CommunityToolkit.Mvvm** — Open-source libraries licensed under MIT.
