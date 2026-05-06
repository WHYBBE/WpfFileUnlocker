# FileUnlocker

A lightweight Windows utility to detect which processes are locking a file or folder, built with .NET 10 / WPF.

![License](https://img.shields.io/badge/license-MIT-blue)

## Features

- **Dual-engine detection** — Restart Manager API + `NtQueryInformationFile` for accurate results
- **Folder support** — Detect locks on folders with configurable scan depth (current only / one level / recursive)
- **File tracking** — Shows exactly which file is locked by which process
- **Process kill** — One-click terminate the locking process
- **Async & parallel** — Non-blocking UI, multi-core parallel scanning
- **Skip .git** — Option to exclude .git directories from folder scans
- **Self-process detection** — Even detects if the current process is locking the target
- **i18n** — English / 中文 switch, persisted across sessions
- **Persistent settings** — Language, scan depth, .git preference saved to `%APPDATA%\FileUnlocker\settings.json`

## Screenshot

![FileUnlocker](https://via.placeholder.com/720x540?text=FileUnlocker+Screenshot)

## Download

Clone and build with .NET 10 SDK:

```bash
git clone https://github.com/0x574859/FileUnlocker.git
cd FileUnlocker
dotnet build -c Release
```

The output is in `bin/Release/net10.0-windows/`.

## Usage

1. Enter a file/folder path, or drag & drop onto the window
2. Click **Detect**
3. View locking processes — hover the ℹ icon to see EXE path
4. Click **Kill** to terminate a locking process

## License

This project is licensed under the [MIT License](LICENSE.txt).

---

[中文版 README](README.zh-CN.md)
