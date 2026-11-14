<div align="center">

# 🔴 AmdPanel

**Lightweight, Native Control Panel for AMD GPUs — Inspired by Classic NVIDIA Control Panel**

[English](README.md) • [Русский](README.ru.md)

[![Status](https://shields.io)](#)
[![Release](https://shields.io)](https://github.com)
[![License](https://shields.io)](LICENSE)
[![Platform](https://shields.io)](#)
[![.NET](https://shields.io)](#)

[**Download Latest Release**](https://github.com) • [**Report a Bug**](https://github.com) • [**Build from Source**](#-building-from-source)

</div>

---

> [!WARNING]
> **Beta Testing & Active Development Notice**  
> **AmdPanel** is currently in **beta testing** and active development. Because our team does not have physical access to every AMD GPU generation and model for hardware verification, some features may behave unexpectedly on untested architectures. Bug reports, logs, and feedback from the community are highly appreciated!

## 💡 Why AmdPanel?

**AmdPanel** is built for gamers and enthusiasts who want to install clean **Driver-Only** AMD packages without the bloated *AMD Software: Adrenalin Edition*, embedded web browsers, or background telemetry—while retaining instant, native access to all 3D, display, color, and GPU tuning controls.

* ⚡ **Instant & Lightweight:** Native C++/WPF architecture using less than **30 MB of RAM** with zero persistent background bloat.
* 🎛️ **Classic NVIDIA Control Panel UX:** Familiar two-pane layout with a clean navigation tree and a dense parameter table.
* 🧠 **Smart Hardware Adaptation:** Queries your GPU and monitor via **AMD ADLX SDK**—unsupported features are hidden automatically, and display modes are validated against cable bandwidth (HDMI / DisplayPort) to prevent black screens.

---

## 📸 Screenshots

<div align="center">

| 3D & Game Profiles | Display & Color Settings |
| :---: | :---: |
| ![3D Settings](screenshots/main-3d.png) | ![Display Settings](screenshots/display.png) |

| Performance & Telemetry | Clean Driver Manager |
| :---: | :---: |
| ![Performance](screenshots/performance.png) | ![Driver Manager](screenshots/drivers.png) |

</div>

*(Replace placeholder files in `screenshots/` with your actual captures)*

---

## ✨ Key Features

* **3D Settings & Per-Game Profiles:** Configure *Radeon Anti-Lag*, *Radeon Chill*, *Image Sharpening*, V-Sync, and texture filtering. Bind custom profiles to specific `.exe` games with automatic setting restoration upon exit.
* **Display, Color & FreeSync:** Adjust resolution, refresh rate, color depth (8/10 bpc), pixel format (RGB / YCbCr), *AMD FreeSync*, scaling modes, and digital saturation (*Digital Vibrance* equivalent). Includes a **15-second safety rollback timer** to prevent black screens.
* **Built-in Driver Manager (Driver-Only):** Download and clean-install official AMD drivers without Adrenalin, including a **Clean Install** option with automatic profile backup.
* **Multi-Language & Multi-GPU:** Instant switching between **English** and **Russian**, plus an active GPU selector for laptops and hybrid graphics setups.

---

## 🚀 Download & Quick Start

1. Open the [**Releases**](https://github.com) page.
2. Download **`AmdPanel-v1.0.1-win-x64.zip`**.
3. Extract the archive to any folder and launch **`AmdPanel.exe`**.

**System Requirements:**
* Windows 10 (1903 or newer) or Windows 11 (x64).
* Installed AMD graphics driver (*Driver-Only* mode is sufficient).

---

## 🎮 Supported GPUs & Testing Status

| Architecture | Radeon Series | Support Status |
| :--- | :--- | :--- |
| **Polaris** (GCN 4) | Radeon RX 400 / RX 500 | 🟡 In testing (verified on RX 580) |
| **Vega** (GCN 5) | Radeon RX Vega 56 / 64, Radeon VII | ⚪ Implemented (not tested on hardware yet) |
| **RDNA 1** | Radeon RX 5000 | ⚪ Implemented (not tested on hardware yet) |
| **RDNA 2** | Radeon RX 6000 | ⚪ Implemented (not tested on hardware yet) |
| **RDNA 3** | Radeon RX 7000 | ⚪ Implemented (not tested on hardware yet) |
| **RDNA 4** | Radeon RX 8000 | ⚪ Implemented (not tested on hardware yet) |

> [!TIP]
> **Help Us Test!** If you own a Vega, RX 5000, RX 6000, RX 7000, or RX 8000 GPU, please open an [Issue](https://github.com) and let us know how AmdPanel performs on your system!

---

## 🛠️ Building from Source

**Prerequisites:**
* Visual Studio 2022 (17.8+) or Build Tools with **Desktop development with C++** and **.NET 8 SDK**.

**One-click build:**
```cmd
git clone https://github.com/Kanzy-dev/AmdPanel.git
cd AmdPanel
build.bat
```
Compiled binaries and the release archive will be generated in `dist/`.

---

## 📄 License & Acknowledgments

**AmdPanel** is licensed under the **GNU General Public License v3.0 (GPL-3.0)** — see [LICENSE](LICENSE) for details. Attribution and a link to the original repository are required when redistributing or modifying the code.

Third-party components used in this project:
* **AMD ADLX SDK** — Official AMD Device Library eXtension (licensed under AMD ADLX SDK terms).
* **SharpCompress** & **CommunityToolkit.Mvvm** — Open-source libraries licensed under MIT.
