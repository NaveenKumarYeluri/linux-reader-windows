# 🐧 Linux File Reader for Windows (Strict Read-Only)

[![Continuous Integration](https://github.com/NaveenKumarYeluri/linux-reader-windows/actions/workflows/ci.yml/badge.svg)](https://github.com/NaveenKumarYeluri/linux-reader-windows/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)

A fast, lightweight, and strictly **read-only** application for accessing Linux partitions and disk image files directly from Windows.

Designed from the ground up for dual-boot users, virtualization workflows, forensics, and data recovery without any risk of filesystem corruption or system instability.

<p align="center">
  <img src="src/LinuxReader.Gui/Assets/app.png" width="128" height="128" alt="Linux Reader Icon"/>
</p>

---

## 🛡️ Safety & Read-Only Guarantees

Many traditional Linux drivers for Windows (such as old kernel-mode Ext2Fsd drivers) are notorious for causing **Blue Screen of Death (BSOD)** crashes or corrupting journaled modern Linux filesystems (`ext4`) during unexpected writes, journal replays, or fast-startup shutdowns.

**Linux File Reader guarantees 100% non-destructive operation:**
1. **Handle-Level Immutability**: All physical drives (`\\.\PhysicalDriveN`) and image files are opened exclusively with Win32 `GENERIC_READ` (`FILE_SHARE_READ | FILE_SHARE_WRITE`). Write access flags are omitted completely.
2. **User-Space Engine**: Zero kernel-mode drivers installed. Your Windows kernel remains untouched and safe.
3. **Sector-Buffered I/O**: Direct Win32 sector-aligned caching protects against invalid alignment faults and supports unaligned, small reads (inodes, directories) seamlessly.
4. **No Journal Modification**: Operates without journal replays or dirty flag modifications on the Linux partition.
5. **Isolated Extraction**: Files and directories can be extracted safely into any destination Windows folder with original file modification timestamps preserved.

---

## ✨ Features

- **Automatic Physical Drive & Partition Discovery**:
  - Automatically scans physical drives on startup when launched with Administrator access.
  - Automatically probes GPT and MBR partitions for Ext4 superblocks and mounts the primary Linux partition immediately.
  - In-app elevation badge with one-click UAC restart if launched without Administrator access.
- **Multi-Partition Support**:
  - GUID Partition Table (**GPT**) with Linux partition GUID detection (`0FC63DAF-8483-4772-8E79-3D69D8477DE4`, `4F68BCE3-E8CD-4DB1-96E7-FBCAF984B709`, `/home`, etc.)
  - Master Boot Record (**MBR**) with Linux type `0x83` detection.
  - Raw filesystem images (without partition tables).
- **Ext2 / Ext3 / Ext4 Filesystem Parser**:
  - Full Ext4 Extents Tree parsing (`ext4_extent_header`, `ext4_extent_idx`, `ext4_extent`).
  - Legacy Ext2/3 direct and indirect block pointer parsing.
  - Flexible Block Group (`flex_bg`) and 64-bit block numbers (`INCOMPAT_64BIT`).
  - Linear Directory parsing (`ext4_dir_entry_2`).
  - Fast symlinks (embedded in inode) and standard symlinks.
- **Physical Hard Drives & Disk Images**:
  - Dual-boot physical hard disks (`\\.\PhysicalDrive0`, `\\.\PhysicalDrive1`).
  - Raw disk images: `.img`, `.raw`, `.iso`, `.ext4`, `.vhd`, `.bin`.
- **Modern Desktop GUI (WPF)**:
  - Custom application icon and clean modern layout.
  - Visual partition selector with Linux badges and volume labels.
  - Tree-view directory navigation with lazy-loading.
  - File table with size, permissions (`drwxr-xr-x`), and timestamps.
  - Built-in text and code preview pane (first 64KB preview).
  - Search filter bar.
  - One-click file and folder extraction.
- **Scriptable CLI**:
  - Headless inspection, directory listing (`ls`), file viewing (`cat`), and automated extraction (`extract`).
  - Built-in `create-sample` tool to generate test GPT/Ext4 disk images on Windows without needing Linux or root rights.
- **Automated CI/CD via GitHub Actions**:
  - Unit and integration tests run on every push and pull request.
  - Automated Release workflow builds standalone, single-file `.exe` packages and generates SHA-256 checksums.

---

## 🖥️ User Interface Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ 🐧 Linux File Reader for Windows   [🛡️ ADMIN MODE] [🔒 READ-ONLY]     │
├────────────────────────────────────────────────────────────────────────┤
│ [📂 Open Disk Image...]  [🔍 Scan Physical Drives]  [💾 Export Selected]│
├─────────────────┬──────────────────────────────────┬───────────────────┤
│ Disks/Partitions│  /etc/nginx/                     │ Item Inspector    │
│ [PhysicalDrive0]│  Name         Size    Perms      │ nginx.conf        │
│ 🐧 [Linux Ext4] │  📁 conf.d/           drwxr-xr-x │ Size: 2.1 KB      │
│                 │  📄 nginx.conf 2.1 KB -rw-r--r-- │ Mode: -rw-r--r--  │
│ Directory Tree  │  📄 mime.types 5.4 KB -rw-r--r-- │ Inode: 14209      │
│ 📁 /            │                                  ├───────────────────┤
│  ├── etc        │                                  │ File Preview      │
│  │   └── nginx  │                                  │ worker_processes; │
│  ├── home       │                                  │ events { ... }    │
│  └── var        │                                  │                   │
└─────────────────┴──────────────────────────────────┴───────────────────┘
```

---

## 💻 CLI Usage

The command-line interface (`linux-reader-cli.exe`) can be used for automation, scripts, and quick inspection:

```powershell
# 1. List physical drives on the system
linux-reader-cli list-drives

# 2. List partitions on a physical drive or disk image
linux-reader-cli list-partitions \\.\PhysicalDrive0
linux-reader-cli list-partitions ubuntu-disk.img

# 3. View Ext4 superblock and filesystem metadata
linux-reader-cli info ubuntu-disk.img 1

# 4. List directory contents
linux-reader-cli ls ubuntu-disk.img 1 /etc

# 5. Print file contents to console
linux-reader-cli cat ubuntu-disk.img 1 /etc/hosts

# 6. Extract a file or directory safely to Windows
linux-reader-cli extract ubuntu-disk.img 1 /etc/nginx C:\Users\Naveen\Documents\nginx

# 7. Generate a synthetic sample disk image to test without physical drives
linux-reader-cli create-sample sample.img
```

---

## 🏗️ Project Architecture

The solution is divided into clean, decoupled layers:

```
LinuxReader.sln
├── src/
│   ├── LinuxReader.Core/          # Pure filesystem & low-level I/O engine
│   │   ├── IO/                    # SafeDiskStream, SubStream, DeviceManager
│   │   ├── Partitions/            # GPT, MBR, and direct partition scanner
│   │   └── Ext4/                  # Superblock, Inode, Extents, Directories, Ext4FileSystem
│   │
│   ├── LinuxReader.Cli/           # Command-line interface
│   └── LinuxReader.Gui/           # WPF Desktop File Explorer Application
│       └── Assets/                # Multi-resolution application icons (app.ico, app.png)
│
├── tests/
│   └── LinuxReader.Tests/         # Unit and end-to-end integration tests
│
└── .github/
    └── workflows/
        ├── ci.yml                 # Build and unit test automation on push/PR
        └── release.yml            # Single-file binary packaging and release automation
```

---

## 🚀 Building & Testing Locally

### Prerequisites
- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Commands
```powershell
# Clone the repository
git clone https://github.com/NaveenKumarYeluri/linux-reader-windows.git
cd linux-reader-windows

# Restore and build the solution
dotnet build LinuxReader.sln

# Run all unit and integration tests
dotnet test LinuxReader.sln

# Publish single-file standalone GUI
dotnet publish src/LinuxReader.Gui/LinuxReader.Gui.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o bin/publish/gui

# Publish single-file standalone CLI
dotnet publish src/LinuxReader.Cli/LinuxReader.Cli.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o bin/publish/cli
```

---

## ⚙️ GitHub Actions CI/CD

- **`ci.yml`**: Automatically verifies builds, restores packages, and executes the complete test suite on `windows-latest` for every pull request and push to `main`/`master`.
- **`release.yml`**: Triggers whenever a git tag formatted as `v*` (e.g., `v1.0.0`) is pushed. It builds standalone self-contained executables for Windows x64, compresses them into `.zip` archives, calculates SHA-256 checksums, and publishes a new GitHub Release automatically.

---

## 🌟 Credits & Acknowledgments

- **Lead Developer & Maintainer**: [Naveen Kumar Yeluri](https://github.com/NaveenKumarYeluri)
- **Technical Standards**:
  - The [Linux Kernel Documentation on Ext4](https://docs.kernel.org/filesystems/ext4/index.html)
  - Unified Extensible Firmware Interface (UEFI) GPT specifications
  - Microsoft Windows Storage and Win32 Device Management APIs
- **Inspiration**:
  - Developed to provide a modern, completely driverless, strictly read-only, non-destructive utility for accessing Linux files on Windows machines.

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).
