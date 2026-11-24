# openBIS Label Studio

A Windows desktop app for lab members to look up samples and substances in **openBIS** and print formatted **12 mm** and **24 mm** Brother P-touch labels—complete with sample names, PermID QR codes, evaporation temperatures, storage hazard icons, and chemical structure sketches.

---

## Requirements

Before running the app, make sure your lab PC has:

1. **Windows 10 or Windows 11 (64-bit / x64)**
2. **.NET 10 Desktop Runtime (x64)** — required to launch `OpenBISLabelStudio.exe`
3. **Python 3.9 or newer** installed and added to Windows `PATH`, with the `pybis` package installed:
   ```powershell
   pip install pybis
   ```
4. **Brother b-PAC SDK (64-bit / x64, version 3.1 or later)** — Brother's runtime library used to populate and print `.lbx` label templates
5. **Brother PT-P700 printer driver(s)** — with your 12 mm and/or 24 mm tape printer queues installed in Windows
6. **An openBIS account & Personal Access Token** — plus your lab's openBIS server URL (for example, `https://openbis.your-institution.ch`)

---

## Installation

### Option A — Ready-to-Run Folder (Recommended for Lab PCs)

1. Copy or extract the published **openBIS Label Studio** application folder into a permanent location on your PC (for example, `C:\Apps\openBISLabelStudio\`).
2. Keep all files in that folder together—especially `OpenBISLabelStudio.exe`, `openbis_fetch.py`, and the `templates\` subfolder.
3. Double-click **`OpenBISLabelStudio.exe`** (you can also right-click it and choose **Send to → Desktop (create shortcut)**).

> Settings are saved automatically inside a `config\` subfolder next to `OpenBISLabelStudio.exe`. You do not need to create or edit any configuration files manually.

### Option B — Build from Source (Developers)

See [Building & Testing from Source](#building--testing-from-source) below.

---

## First-Time Setup

When you open the app for the first time, a 3-step setup wizard walks you through configuration:

1. **Connection**
   - Enter your **openBIS server URL** and paste your **Personal Access Token** (use **Show token** if you want to verify what you pasted).
   - Click **Test Connection** to confirm the app can reach openBIS and verify your token.

2. **Object Types & Templates**
   - Your openBIS object types will load automatically. Check **Use** next to every object type you want in the main app (use the **Filter** box to find types quickly).
   - Choose a **Template Style** for each enabled type and preview how the labels look below:
     - **`Molecule / Substance`**
       - **Small Box (12 mm):** Sample name, PermID QR code, user handle, and evaporation temperature.
       - **Magnet (12 mm):** Sample name, PermID QR code, researcher full name, evaporation temperature, and special storage icons (poisonous, flammable, dark, dry, freezer, fridge, no oxygen, tightly closed).
       - **Flask (24 mm):** Chemical structure image (from openBIS `ELN_PREVIEW`), sample name, storage location, supplier name, and PermID QR code.
     - **`Generic / Free-text`**
       - **Small Box (12 mm):** Sample name and PermID QR code.
       - **Magnet (12 mm) & Flask (24 mm):** Sample name, PermID QR code, and an editable **Free Text** note field.

3. **Printer Routing**
   - Assign which installed Brother PT-P700 Windows printer queue corresponds to each label size:
     - **Small Box (12mm)**
     - **Magnet (12mm)**
     - **Flask (24mm)**

Click **Finish** to open the main window. You can revisit any of these tabs anytime via the **Settings...** button.

---

## How to Use

1. **Choose the OpenBIS Type** in the top dropdown (for example, *Substance* or *Substrate*).
2. **Select your item** in the second dropdown.
   - Use **Sort** (**Modification Date**, **Name**, or **PermID**) and the **`↓` / `↑`** button on the right to find recent or specific entries quickly.
3. **Inspect the label previews**
   - Selecting an item automatically renders previews for all three label sizes (you can also click **Load Templates** to refresh).
   - You can drag the window borders or maximize the window to make the previews and chemical structure sketches larger and easier to read.
4. **Edit optional label fields (when needed)**
   - **Evaporation Temp** *(Molecule / Substance)*: Automatically filled from openBIS if present (the *Small Box* and *Magnet* fields stay synced). To override the text on the printout, edit the box and press **Enter** or click outside the text box to update the preview.
   - **Free Text** *(Generic / Free-text)*: Enter custom text on the *Magnet* or *Flask* card and press **Enter** or click outside the box to re-render that preview.
5. **Select printers & print**
   - Use the checkbox in the top-right corner of each card to pick which label(s) to print.
   - Check the **Printer** dropdown at the bottom of each card (any change is saved automatically).
   - Click **Print Labels**.

---

## Troubleshooting & Tips

- **Your openBIS token expired:** A prompt automatically appears asking for a new Personal Access Token, saves it, and retries what you were doing.
- **No connection to openBIS at startup:** Previously configured object types still appear with a status notice so you can check settings or inspect templates while offline.
- **Text or chemical structure looks different from what you expected:** Click **Print Labels** only after checking the updated preview; remember to press **Enter** or click outside an edited *Evaporation Temp* or *Free Text* box so the preview refreshes first.
- **Python or `pybis` errors:** Open Windows Command Prompt / PowerShell and run `python --version` and `python -c "import pybis"` to confirm Python and `pybis` are accessible on `PATH`.
- **Label printing fails or templates fail to load:** Confirm the **64-bit Brother b-PAC SDK** is installed and that your Brother PT-P700 printer queues are visible in Windows **Printers & scanners**.
- **Customizing label designs (advanced):** Label templates live in the `templates\` folder as `.lbx` files and can be adjusted in **Brother P-touch Editor** as long as you keep the existing template object names (such as `name`, `PermidQRCode`, `evaporationTemperature`, `studiedBy`, `storageLocation`, `supplierOwnName`, `elnPreview`, `freeText`, and icon names).

---

<details>
<summary><strong>Building &amp; Testing from Source (For Developers)</strong></summary>

### Additional Build Prerequisites

- **.NET 10 SDK**
- **Visual Studio 2022/2026 (or MSBuild Build Tools)** — use `MSBuild.exe` rather than `dotnet build`, because `dotnet build` does not resolve the Brother b-PAC COM reference (`MSB4803`).
- **`pytest`** for Python tests (`pip install pybis pytest`).

### Build, Test &amp; Publish

```powershell
# Build Debug & Release (pass /t:Restore,Build after cleaning obj/)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal

# Run C# unit & WinForms integration tests
& ".\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
& ".\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe"

# Run Python openbis_fetch.py tests
pytest tests/test_openbis_fetch.py -v

# Publish a clean distributable Release folder (contains no config/ secrets)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio\OpenBISLabelStudio.csproj /t:Publish /p:Configuration=Release /p:PublishDir="C:\path\to\deploy-folder\" /v:minimal
```

</details>
