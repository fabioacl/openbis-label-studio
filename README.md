# openBIS Label Studio

A specialized Windows desktop application built with .NET 10 WinForms and Python pyBIS for querying openBIS laboratory inventory and printing dynamic, formatted physical labels using Brother b-PAC SDK and Brother PT-P700 label printers.

---

## Features

- **openBIS Integration via pyBIS**:
  - Automatically loads and formats available openBIS object types (e.g. *Substance*, *Crystal*, *2D Layer Material*).
  - Fetches registered samples sorted by latest modification date.
  - Automatically resolves references (e.g. `studied_by` person full name and username, storage location).
  - Extracts evaporation temperature from pyBIS `Spreadsheet` objects according to column units (e.g. `Temperature [K]`).
  - Downloads parent molecule `ELN_PREVIEW` chemical structures.

- **Dynamic Brother Label Templates (`.lbx`)**:
  - **Small Box 12mm Label**: Sample Name, PermID QR code, and researcher username for substances.
  - **Magnet 12mm Label**: Sample Name, PermID QR code, researcher full name, evaporation temperature, and special storage condition icons (poisonous, flammable, dark, dry, freezer, fridge, no oxygen, tightly closed).
  - **Flask 24mm Label**: Enlarged 24mm tape format featuring chemical ELN preview structure, storage location, supplier own name, and PermID QR code.
  - **Non-substance Support with Free Text**: Interactive free-text field for custom label notes with live preview rendering.

- **Configurable Multi-Printer Routing**:
  - Supports multi-label routing configured via `printers_config.json`:
    - Small Box $\rightarrow$ `Brother PT-P700`
    - Magnet $\rightarrow$ `Brother PT-P700 (Kopie 1)`
    - Flask $\rightarrow$ `Brother PT-P700 (Kopie 2)`
  - Per-groupbox printer selection dropdowns populated with all system printers.

---

## Prerequisites

1. **Windows 10 / 11 (x64)**
2. **.NET 10 SDK & Desktop Runtime**
3. **Python 3.9+** with `pybis`:
   ```bash
   pip install pybis
   ```
4. **Brother b-PAC SDK** (version 3.1+ x64 COM component)
5. **Brother PT-P700 Printer Drivers**

---

## Configuration

1. Copy `OpenBISLabelStudio/openbis_config.example.json` to `OpenBISLabelStudio/openbis_config.json`:
   ```json
   {
     "openbisUrl": "https://openbis.your-institution.ch",
     "token": "YOUR_OPENBIS_PERSONAL_ACCESS_TOKEN"
   }
   ```
2. *(Optional)* Customize `OpenBISLabelStudio/printers_config.json` to match your local printer queue names:
   ```json
   {
     "smallBoxPrinter": "Brother PT-P700",
     "magnetPrinter": "Brother PT-P700 (Kopie 1)",
     "flaskPrinter": "Brother PT-P700 (Kopie 2)"
   }
   ```

---

## Building & Running

Open the solution in Visual Studio or build with MSBuild:

```powershell
MSBuild.exe OpenBISLabelStudio.slnx
```
