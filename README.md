# WinKey_CommandPallette_Replacement
# WinKey → PowerToys Command Palette

*A small C# utility that makes a **Win** key tap open **PowerToys Command Palette** and prevents the Windows **Start/Search** UI from appearing.*

---

## **What it does**
- Uses a **low-level keyboard hook** (`WH_KEYBOARD_LL`).
- **Win tap →** sends the Command Palette hotkey (default in code: **Ctrl+Space**).
- Blocks Start triggers: **`LWIN`**, **`RWIN`**, and **`Ctrl+Esc`**.
- **No timers** (pure state-based detection).
- **Panic hotkey:** **Ctrl+Alt+F12** quits the app immediately.

---

## **Requirements**
- Microsoft **PowerToys** with **Command Palette** enabled and a hotkey set.
- Run the EXE **as Administrator** (so it applies to elevated apps).

---

## **Quick Start**
1. In **PowerToys → Command Palette**, set a hotkey (for example, **Ctrl+Space**).
2. Run `WinKey_CommandPalette.exe` **as Administrator**.
3. Tap **Win** → Command Palette opens.  
   - `Ctrl+Esc` also opens Command Palette.  
   - Press **Ctrl+Alt+F12** to exit.

> If your download is blocked by the browser or SmartScreen, save it, then:  
> **Right-click → Properties → Unblock → OK**, and run (**More info → Run anyway**).

---

## **Configuration (in `Program.cs`)**
```csharp
// Match these to your Command Palette hotkey:
const int HOTKEY_MOD = VK_CONTROL; // default: Ctrl
const int HOTKEY_KEY = VK_SPACE;   // default: Space

// If true, swallows other keys pressed while Win is held:
const bool STRICT_MODE = true;

// Panic hotkey is registered as Ctrl+Alt+F12.
```

---

## **Build (Visual Studio)**
- Set **Platform target = x64**.
- Set **Output type = Windows Application** (WinExe).
- Add an **Application Manifest** and set:
```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```
- Build **Release | x64**.

---

## **Auto-start (Task Scheduler)**
1. Open **Task Scheduler** → **Create Task…**
2. **General:** check **Run with highest privileges**
3. **Triggers:** **At log on**
4. **Actions:** **Start a program** → select this EXE

---

## **License**
**MIT**
