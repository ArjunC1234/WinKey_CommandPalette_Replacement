# WinKey Remapper

A lightweight Windows utility that **replaces the Windows key tap behavior**. Instead of opening the default **Start/Search** menu, this app lets you trigger **any custom keyboard shortcut** (like PowerToys Command Palette, Clipboard History, custom launchers, etc.) when you tap the **Win** key.

It also preserves all your normal **Win+Key** combinations (Win+R, Win+L, Win+V, etc.) by passing them through unchanged.

---

## ✨ Features

- **Win tap → your shortcut**  
  Single tap of the Windows key sends a configurable shortcut (default: **Win+Alt+Space** for PowerToys Command Palette).

- **Normal Win+Key combos still work**  
  Hold **Win** and press another key (e.g., **Win+R**, **Win+L**, **Win+V**) and the app passes the combo through to Windows.

- **Blocks default Start/Search UI**  
  Prevents the stock Windows Start menu / search from popping up on a solo Win tap.

- **Configurable shortcut via installer**  
  A bundled **PowerShell installer** lets you:
  - Install/uninstall the app
  - Choose the **triggered shortcut** (Win/Ctrl/Alt/Shift + any key)
  - Enable/disable **run on startup** (with or without UAC prompts)
  - Start/stop the app and view logs

- **Admin-safe startup**  
  Uses a **Scheduled Task** option so the app runs on logon with **highest privileges** without showing a UAC prompt every boot.

- **Panic hotkey**  
  Global **Ctrl+Alt+F12** immediately quits the remapper if something goes wrong.

- **Pure state-based input logic**  
  No timers. The low-level keyboard hook tracks Win state and decides whether to fire your shortcut or pass the original combo through.

---

## 🧩 How it Works

- Installs a low-level keyboard hook (`WH_KEYBOARD_LL`).
- Tracks when **LWIN** or **RWIN** is pressed and whether any other key is pressed while Win is held.
  - **Solo Win tap** → blocks the real Win key and sends your configured shortcut using `SendInput`/`keybd_event`.
  - **Win+Key combo** → replays a real Win press + your other keys so Windows handles it normally.
- Ignores its **own injected input** using `LLKHF_INJECTED` and a custom `dwExtraInfo` tag so it doesn’t recurse.
- Also supports **Ctrl+Esc → your shortcut**, if you like the classic Start-menu combo.

---

## 📦 Requirements

- Windows 10/11, x64.
- .NET 6/7/8 Desktop Runtime (if you’re running the EXE directly and it isn’t self-contained).
- For PowerToys Command Palette:
  - **PowerToys** installed
  - **Command Palette** enabled
- Best experience when the EXE runs **as Administrator** (so it works in elevated apps too).

---

## 🚀 Installation (Recommended)

Quick Start:

[![Watch the video](https://img.youtube.com/vi/JWPCzTo-_8w/0.jpg)](https://www.youtube.com/watch?v=JWPCzTo-_8w)

1. Download the latest **installer EXE** from the Releases page.
2. **Right-click → Run as administrator.**
3. In the menu:
   - Choose **Install**.
   - Optionally enable **startup** (Scheduled Task is recommended).
   - Optionally create a **desktop shortcut**.
4. After install, choose **Start Win Key Remapper** from the menu (or use the shortcut).

The installer handles:
- Downloading and extracting the latest build
- Creating Start Menu / Desktop shortcuts
- Setting up startup (Task Scheduler or registry)
- Creating an intelligent startup script that waits for PowerToys to be running before launching the remapper

---

## 🛠 Configuring the Triggered Shortcut

You can configure what happens on a Win tap in **two ways**:

### 1. Using the Installer (recommended)

1. Run the installer script/EXE.
2. Choose **Configure triggered shortcut** from the menu.
3. Answer the prompts:
   - Include **Win** in the combo? (Yes/No)
   - Include **Ctrl / Alt / Shift**?
   - Enter the **main key** (examples: `Space`, `V`, `P`, `R`, `Esc`, `F12`, etc.).
4. The installer writes `shortcut.json` next to the EXE and restarts the app so it picks up your new shortcut.

### 2. Editing `shortcut.json` manually

In the install folder (e.g. `C:\Program Files\Win Key Remapper`), there is a file:

```json
{
  "Win": true,
  "Ctrl": false,
  "Alt": true,
  "Shift": false,
  "MainKey": "Space"
}
```

- `Win`, `Ctrl`, `Alt`, `Shift` are booleans.
- `MainKey` is a key name (case-insensitive), e.g. `Space`, `V`, `P`, `R`, `Esc`, `F12`.

On startup, the app reads this file once and maps it to virtual key codes. If the file is missing or invalid, it falls back to **Win+Alt+Space**.

---

## 🔁 Updating

The installer includes a **Check for updates** option:

- Contacts GitHub Releases API to find the latest version.
- Compares with your installed version.
- If a newer version is available, it performs a **fresh install in-place**:
  - Stops the running remapper
  - Downloads the newest ZIP
  - Clears old EXEs/DLLs
  - Extracts the new build into the same install directory
  - Keeps your **`shortcut.json`**, startup script, and logs

You can also always manually download the latest release and re-run **Install**.

---

## ▶️ Running Manually

If you bypass the installer and just have the EXE + `shortcut.json`:

1. Place them in a folder (e.g. `C:\Tools\WinKeyRemapper`).
2. Right-click the EXE → **Run as administrator**.
3. Win tap should now fire your configured shortcut.

To stop it, either:
- Use the **Ctrl+Alt+F12** panic hotkey, or
- Kill the process from **Task Manager**.

---

## 🧪 Building from Source

### C# Remapper

- Project type: **.NET 6/7/8**, x64
- Output type: **Windows Application (WinExe)**
- Recommended: embed a manifest with

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

Build in **Release | x64** and ship the EXE (optionally self-contained).

### PowerShell Installer

The installer is a `.ps1` script that can be packaged into an EXE using tools like `ps2exe`. It provides:

- Menu-based UI in a console
- Install / uninstall
- Start / stop
- Startup configuration (Scheduled Task or registry)
- Shortcut configuration (`shortcut.json`)
- Log viewing and basic diagnostics

---

## 🧯 Safety & Escape Hatch

- **Ctrl+Alt+F12**: global panic hotkey to immediately quit the remapper.
- If you ever get stuck:
  - Press **Ctrl+Alt+F12**
  - Or run the installer and choose **Stop Win Key Remapper**
  - Or reboot into Safe Mode and remove from startup / uninstall

---

## 💡 Example Use Cases

- **PowerToys Command Palette** (Win tap → Win+Alt+Space)
- **Clipboard history** (Win tap → Win+V)
- **Your own launcher** (Win tap → Ctrl+Alt+P, etc.)
- Any workflow where you want the **Win key tap** to trigger a specific action instead of the Start menu.

---

## ❗ Disclaimer

This tool hooks keyboard input at a low level and modifies how the Windows key behaves. While it has a panic hotkey and has been tested in normal environments, use it **at your own risk**. Keep a way to uninstall or disable it if something conflicts with your setup. You can always manage the installation and the background process inside of the installer.

---

## Donate
- This is **NOT** required, but is highly appreciated as it heavily supports me as a young independent software developer.
- [Donate Here](https://buymeacoffee.com/arjuncattamanchi)

## 📜 License

MIT License – feel free to fork, modify, and use in your own setups.

If you find it helpful, sharing the repo or sending feedback is more than enough support.
