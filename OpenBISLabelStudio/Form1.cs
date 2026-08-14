using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using bpac; // Reference to Brother b-PAC SDK COM Component
using Newtonsoft.Json;

namespace OpenBISLabelStudio
{
    public partial class Form1 : Form
    {
        private const string PLACEHOLDER_TYPE = "Select an object type...";

        private const string TYPES_FILE_PATH = "openbis_types.json";
        private const string CONFIG_FILE_PATH = "openbis_config.json";
        private const string PRINTERS_CONFIG_FILE_PATH = "printers_config.json";
        private const string RESULTS_FILE_PATH = "openbis_results.json";
        private const string SCRIPT_PATH = "openbis_fetch.py";

        // Template file names
        private const string TEMPLATE_SMALL_BOX_12MM_FILE_PATH = "template_small_box_12mm.lbx";
        private const string TEMPLATE_MOLECULE_BOX_12MM_FILE_PATH = "template_molecule_box_12mm.lbx";
        private const string TEMPLATE_MAGNET_12MM_FILE_PATH = "template_magnet_12mm.lbx";
        private const string TEMPLATE_MOLECULE_MAGNET_12MM_FILE_PATH = "template_molecule_magnet_12mm.lbx";
        private const string TEMPLATE_FLASK_24MM_FILE_PATH = "template_flask_24mm.lbx";
        private const string TEMPLATE_MOLECULE_FLASK_24MM_FILE_PATH = "template_molecule_flask_24mm.lbx";

        private const string PYTHON_PATH = "python";

        private AppConfig _config = new AppConfig();
        private PrinterConfig _printerConfig = new PrinterConfig();
        private List<SampleDto> _loadedSamples = new List<SampleDto>();
        private SampleDetailsDto? _currentSampleDetails = null;
        private string _currentSelectedTypeCode = "";
        private string _transparentPngPath = "";

        private enum LabelTemplateType
        {
            SmallBox12mm,
            Magnet12mm,
            Flask24mm
        }

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            _transparentPngPath = GetOrCreateTransparentPng();
            LoadAppConfig();
            LoadPrinterConfig();
            PopulateInstalledPrinters();
            await LoadOpenBisTypesAsync();
            UpdateFreeTextControlsState();
        }

        private void LoadPrinterConfig()
        {
            try
            {
                string configFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PRINTERS_CONFIG_FILE_PATH);

                if (!File.Exists(configFullPath))
                {
                    PrinterConfig defaultConfig = new PrinterConfig
                    {
                        SmallBoxPrinter = "Brother PT-P700",
                        MagnetPrinter = "Brother PT-P700 (Kopie 1)",
                        FlaskPrinter = "Brother PT-P700 (Kopie 2)"
                    };

                    string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(configFullPath, json);
                }

                string configJson = File.ReadAllText(configFullPath);
                _printerConfig = JsonConvert.DeserializeObject<PrinterConfig>(configJson) ?? new PrinterConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading printer configuration file: {ex.Message}", "Printer Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateInstalledPrinters()
        {
            try
            {
                smallBoxPrinterComboBox.Items.Clear();
                magnetPrinterComboBox.Items.Clear();
                flaskPrinterComboBox.Items.Clear();

                List<string> printers = new List<string>();
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    printers.Add(printer);
                }

                if (printers.Count == 0)
                {
                    printers.Add(_printerConfig.SmallBoxPrinter);
                    printers.Add(_printerConfig.MagnetPrinter);
                    printers.Add(_printerConfig.FlaskPrinter);
                }

                foreach (string p in printers)
                {
                    smallBoxPrinterComboBox.Items.Add(p);
                    magnetPrinterComboBox.Items.Add(p);
                    flaskPrinterComboBox.Items.Add(p);
                }

                // Set default match from config file for Small Box
                SelectBestPrinterMatch(smallBoxPrinterComboBox, _printerConfig.SmallBoxPrinter);

                // Set default match from config file for Magnet
                SelectBestPrinterMatch(magnetPrinterComboBox, _printerConfig.MagnetPrinter);

                // Set default match from config file for Flask
                SelectBestPrinterMatch(flaskPrinterComboBox, _printerConfig.FlaskPrinter);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enumerating printers: {ex.Message}");
            }
        }

        private void SelectBestPrinterMatch(ComboBox comboBox, string preferredName)
        {
            if (comboBox.Items.Count == 0) return;

            if (string.IsNullOrWhiteSpace(preferredName))
            {
                comboBox.SelectedIndex = 0;
                return;
            }

            // 1. Exact match
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                string itemText = comboBox.Items[i]?.ToString() ?? "";
                if (string.Equals(itemText, preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            // 2. Contains match
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                string itemText = comboBox.Items[i]?.ToString() ?? "";
                if (itemText.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            // 3. Fallback to first item
            comboBox.SelectedIndex = 0;
        }

        private string GetOrCreateTransparentPng()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "openbis_transparent_1x1.png");
                if (!File.Exists(path))
                {
                    using (Bitmap bmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        bmp.SetPixel(0, 0, Color.FromArgb(0, 255, 255, 255));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return path;
            }
            catch
            {
                return "";
            }
        }

        private void LoadAppConfig()
        {
            try
            {
                string configFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_PATH);

                if (!File.Exists(configFullPath))
                {
                    AppConfig defaultConfig = new AppConfig
                    {
                        OpenBisUrl = "https://openbis.your-institution.ch",
                        Token = ""
                    };

                    string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(configFullPath, json);
                }

                string configJson = File.ReadAllText(configFullPath);
                _config = JsonConvert.DeserializeObject<AppConfig>(configJson) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration file: {ex.Message}", "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadOpenBisTypesAsync()
        {
            try
            {
                openbisTypeComboBox.Items.Clear();
                openbisTypeComboBox.Items.Add(new OpenBisTypeItem { Code = "", DisplayText = PLACEHOLDER_TYPE });

                string typesFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TYPES_FILE_PATH);

                if (!File.Exists(typesFullPath))
                {
                    List<string> defaultTypes = new List<string>
                    {
                        "SUBSTANCE",
                        "CRYSTAL",
                        "2D_LAYER_MATERIAL"
                    };

                    string defaultJson = JsonConvert.SerializeObject(defaultTypes, Formatting.Indented);
                    File.WriteAllText(typesFullPath, defaultJson);
                }

                string jsonContent = File.ReadAllText(typesFullPath);
                List<string>? requestedTypes = JsonConvert.DeserializeObject<List<string>>(jsonContent) ?? new List<string>();

                Dictionary<string, string> openBisTypeDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(_config.OpenBisUrl) && !string.IsNullOrEmpty(_config.Token))
                {
                    OpenBisTypesResult typesResult = await Task.Run(() => QueryOpenBisTypes(_config.OpenBisUrl, _config.Token));
                    if (typesResult != null && typesResult.Success && typesResult.Types != null)
                    {
                        foreach (var t in typesResult.Types)
                        {
                            if (!string.IsNullOrEmpty(t.Code))
                            {
                                openBisTypeDescriptions[t.Code] = t.Description;
                            }
                        }
                    }
                }

                List<OpenBisTypeItem> typeItems = new List<OpenBisTypeItem>();

                foreach (string typeCode in requestedTypes)
                {
                    if (string.IsNullOrWhiteSpace(typeCode))
                        continue;

                    string cleanCode = typeCode.Trim();
                    string friendlyName;

                    if (openBisTypeDescriptions.TryGetValue(cleanCode, out string? desc) && !string.IsNullOrWhiteSpace(desc))
                    {
                        friendlyName = desc;
                    }
                    else
                    {
                        friendlyName = FormatFriendlyName(cleanCode);
                    }

                    typeItems.Add(new OpenBisTypeItem
                    {
                        Code = cleanCode,
                        DisplayText = friendlyName
                    });
                }

                // Sort alphabetically A to Z
                typeItems = typeItems.OrderBy(t => t.DisplayText, StringComparer.CurrentCultureIgnoreCase).ToList();

                openbisTypeComboBox.Items.Clear();
                openbisTypeComboBox.Items.Add(new OpenBisTypeItem { Code = "", DisplayText = PLACEHOLDER_TYPE });

                foreach (var item in typeItems)
                {
                    openbisTypeComboBox.Items.Add(item);
                }

                openbisTypeComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading object types: {ex.Message}", "Type Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatFriendlyName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            string[] parts = code.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                }
            }
            return string.Join(" ", parts);
        }

        private bool IsSubstance()
        {
            if (_currentSampleDetails != null && !string.IsNullOrEmpty(_currentSampleDetails.SampleType))
            {
                return _currentSampleDetails.SampleType.Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase);
            }
            return _currentSelectedTypeCode.Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFreeTextControlsState()
        {
            bool isSubstance = IsSubstance();
            bool hasSelection = !string.IsNullOrEmpty(_currentSelectedTypeCode);

            // FreeText is applicable for non-substance objects
            magnetFreeTextBox.Enabled = hasSelection && !isSubstance;
            flaskFreeTextBox.Enabled = hasSelection && !isSubstance;

            if (isSubstance)
            {
                magnetFreeTextBox.PlaceholderText = "(N/A for Substances)";
                flaskFreeTextBox.PlaceholderText = "(N/A for Substances)";
            }
            else
            {
                magnetFreeTextBox.PlaceholderText = "Free text placeholder";
                flaskFreeTextBox.PlaceholderText = "Free text placeholder";
            }
        }

        private async void openbisTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (openbisTypeComboBox.SelectedItem is OpenBisTypeItem selectedItem)
            {
                // Guard clause: If placeholder is selected, clear items and stop execution
                if (string.IsNullOrEmpty(selectedItem.Code) || selectedItem.DisplayText == PLACEHOLDER_TYPE)
                {
                    selectedOpenbisTypeLabel.Text = "Type:";
                    openbisObjectsComboBox.Items.Clear();
                    _loadedSamples.Clear();
                    _currentSelectedTypeCode = "";
                    ClearAllPreviews();
                    UpdateFreeTextControlsState();
                    return;
                }

                _currentSelectedTypeCode = selectedItem.Code;
                selectedOpenbisTypeLabel.Text = $"{selectedItem.DisplayText}:";
                UpdateFreeTextControlsState();

                if (string.IsNullOrEmpty(_config.OpenBisUrl))
                {
                    MessageBox.Show("openBIS URL is missing in openbis_config.json.", "Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                openbisObjectsComboBox.Items.Clear();
                _loadedSamples.Clear();
                ClearAllPreviews();

                SetControlsEnabled(false);
                Cursor = Cursors.WaitCursor;

                try
                {
                    OpenBisListResult result = await Task.Run(() => QueryOpenBisByType(
                        _config.OpenBisUrl,
                        _config.Token,
                        selectedItem.Code
                    ));

                    if (result != null && result.Success && result.Data != null)
                    {
                        _loadedSamples = result.Data;

                        foreach (var sample in result.Data)
                        {
                            openbisObjectsComboBox.Items.Add($"{sample.Name} ({sample.PermId})");
                        }

                        if (openbisObjectsComboBox.Items.Count > 0)
                        {
                            openbisObjectsComboBox.SelectedIndex = 0;
                        }
                    }
                    else
                    {
                        MessageBox.Show(result?.Error ?? "Failed to fetch items from Python script.", "openBIS Fetch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                finally
                {
                    SetControlsEnabled(true);
                    Cursor = Cursors.Default;
                }
            }
        }

        // =========================================================================
        // LOAD PRINT TEMPLATES BUTTON CLICK
        // =========================================================================
        private async void loadPrintTemplates_Click(object sender, EventArgs e)
        {
            if (openbisObjectsComboBox.SelectedIndex < 0 || openbisObjectsComboBox.SelectedIndex >= _loadedSamples.Count)
            {
                MessageBox.Show("Please select an openBIS object first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SampleDto selectedSample = _loadedSamples[openbisObjectsComboBox.SelectedIndex];

            SetControlsEnabled(false);
            Cursor = Cursors.WaitCursor;

            try
            {
                OpenBisDetailsResult result = await Task.Run(() => QueryOpenBisSampleDetails(
                    _config.OpenBisUrl,
                    _config.Token,
                    selectedSample.PermId
                ));

                if (result != null && result.Success && result.Details != null)
                {
                    _currentSampleDetails = result.Details;
                    bool isSubstance = IsSubstance();
                    UpdateFreeTextControlsState();

                    // Render all 3 previews simultaneously
                    RenderPreview(smallBoxLabelPreview, LabelTemplateType.SmallBox12mm, _currentSampleDetails, isSubstance);
                    RenderPreview(magnetLabelPreview, LabelTemplateType.Magnet12mm, _currentSampleDetails, isSubstance);
                    RenderPreview(flaskLabelPreview, LabelTemplateType.Flask24mm, _currentSampleDetails, isSubstance);

                    // Automatically check all preview checkboxes for convenience
                    smallBoxLabelCheckBox.Checked = true;
                    magnetLabelCheckBox.Checked = true;
                    flaskLabelCheckBox.Checked = true;
                }
                else
                {
                    MessageBox.Show(result?.Error ?? "Could not fetch sample details.", "openBIS Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                SetControlsEnabled(true);
                Cursor = Cursors.Default;
            }
        }

        // =========================================================================
        // DYNAMIC FREE TEXT COMMIT HANDLERS (ON LEAVE OR ENTER)
        // =========================================================================
        private void magnetFreeTextBox_Leave(object sender, EventArgs e)
        {
            if (_currentSampleDetails != null && !IsSubstance())
            {
                RenderPreview(magnetLabelPreview, LabelTemplateType.Magnet12mm, _currentSampleDetails, false);
            }
        }

        private void magnetFreeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                this.ActiveControl = null; // Shift focus to trigger commit
                magnetFreeTextBox_Leave(sender, EventArgs.Empty);
            }
        }

        private void flaskFreeTextBox_Leave(object sender, EventArgs e)
        {
            if (_currentSampleDetails != null && !IsSubstance())
            {
                RenderPreview(flaskLabelPreview, LabelTemplateType.Flask24mm, _currentSampleDetails, false);
            }
        }

        private void flaskFreeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                this.ActiveControl = null; // Shift focus to trigger commit
                flaskFreeTextBox_Leave(sender, EventArgs.Empty);
            }
        }

        // =========================================================================
        // TEMPLATE PATH RESOLUTION & DOCUMENT POPULATION
        // =========================================================================
        private string GetTemplateFileName(LabelTemplateType type, bool isSubstance)
        {
            switch (type)
            {
                case LabelTemplateType.SmallBox12mm:
                    return isSubstance ? TEMPLATE_MOLECULE_BOX_12MM_FILE_PATH : TEMPLATE_SMALL_BOX_12MM_FILE_PATH;
                case LabelTemplateType.Magnet12mm:
                    return isSubstance ? TEMPLATE_MOLECULE_MAGNET_12MM_FILE_PATH : TEMPLATE_MAGNET_12MM_FILE_PATH;
                case LabelTemplateType.Flask24mm:
                    return isSubstance ? TEMPLATE_MOLECULE_FLASK_24MM_FILE_PATH : TEMPLATE_FLASK_24MM_FILE_PATH;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private void PopulateDocument(Document doc, LabelTemplateType type, SampleDetailsDto details, bool isSubstance)
        {
            // 1. Common Name object
            bpac.Object nameObj = doc.GetObject("name");
            if (nameObj != null)
            {
                nameObj.Text = details.Name;
            }

            // 2. Common Permid QR Code object
            bpac.Object qrObj = doc.GetObject("PermidQRCode") ?? doc.GetObject("PermidCode");
            if (qrObj != null)
            {
                qrObj.Text = details.PermId;
            }

            if (isSubstance)
            {
                // 3. Studied By (Small Box uses Username, Magnet uses Full Name)
                bpac.Object studiedByObj = doc.GetObject("studiedBy");
                if (studiedByObj != null)
                {
                    if (type == LabelTemplateType.SmallBox12mm)
                    {
                        studiedByObj.Text = !string.IsNullOrEmpty(details.StudiedByUsername)
                            ? details.StudiedByUsername
                            : details.StudiedBy;
                    }
                    else if (type == LabelTemplateType.Magnet12mm)
                    {
                        studiedByObj.Text = !string.IsNullOrEmpty(details.StudiedByName)
                            ? details.StudiedByName
                            : details.StudiedBy;
                    }
                    else
                    {
                        studiedByObj.Text = details.StudiedBy;
                    }
                }

                // 4. Evaporation Temperature (populated for all substance templates that support it)
                bpac.Object evapTempObj = doc.GetObject("evaporationTemperature");
                if (evapTempObj != null)
                {
                    evapTempObj.Text = details.EvaporationTemperature;
                }

                // 5. Magnet Icons for special storage conditions
                if (type == LabelTemplateType.Magnet12mm)
                {
                    SetIconVisibility(doc, "poisonousIcon", details.IconFlags.Poisonous);
                    SetIconVisibility(doc, "flammableIcon", details.IconFlags.Flammable);
                    SetIconVisibility(doc, "darkIcon", details.IconFlags.Dark);
                    SetIconVisibility(doc, "dryIcon", details.IconFlags.Dry);
                    SetIconVisibility(doc, "freezerIcon", details.IconFlags.Freezer);
                    SetIconVisibility(doc, "fridgeIcon", details.IconFlags.Fridge);
                    SetIconVisibility(doc, "noOxygenIcon", details.IconFlags.NoOxygen);
                    
                    // Support both XML naming variations
                    SetIconVisibility(doc, "tightlyCloseIcon", details.IconFlags.TightlyClosed);
                    SetIconVisibility(doc, "tightlyClosedIcon", details.IconFlags.TightlyClosed);
                }

                // 6. Flask 24mm Specific Fields
                if (type == LabelTemplateType.Flask24mm)
                {
                    bpac.Object storageLocObj = doc.GetObject("storageLocation");
                    if (storageLocObj != null)
                    {
                        storageLocObj.Text = details.StorageLocation;
                    }

                    bpac.Object supplierNameObj = doc.GetObject("supplierOwnName");
                    if (supplierNameObj != null)
                    {
                        supplierNameObj.Text = details.SupplierOwnName;
                    }

                    bpac.Object elnObj = doc.GetObject("elnPreview");
                    if (elnObj != null)
                    {
                        if (!string.IsNullOrEmpty(details.ElnPreviewPath) && File.Exists(details.ElnPreviewPath))
                        {
                            elnObj.SetData(0, details.ElnPreviewPath, 4);
                        }
                        else if (!string.IsNullOrEmpty(_transparentPngPath) && File.Exists(_transparentPngPath))
                        {
                            elnObj.SetData(0, _transparentPngPath, 4);
                        }
                    }
                }
            }
            else
            {
                // Non-substance specific handling (FreeText support)
                const string DEFAULT_FREE_TEXT_PLACEHOLDER = "Free text placeholder";

                if (type == LabelTemplateType.Magnet12mm)
                {
                    bpac.Object freeTextObj = doc.GetObject("freeText") ?? doc.GetObject("samplePlate");
                    if (freeTextObj != null)
                    {
                        string text = string.IsNullOrWhiteSpace(magnetFreeTextBox.Text)
                            ? DEFAULT_FREE_TEXT_PLACEHOLDER
                            : magnetFreeTextBox.Text;
                        freeTextObj.Text = text;
                    }
                }
                else if (type == LabelTemplateType.Flask24mm)
                {
                    bpac.Object freeTextObj = doc.GetObject("freeText");
                    if (freeTextObj != null)
                    {
                        string text = string.IsNullOrWhiteSpace(flaskFreeTextBox.Text)
                            ? DEFAULT_FREE_TEXT_PLACEHOLDER
                            : flaskFreeTextBox.Text;
                        freeTextObj.Text = text;
                    }
                }
            }
        }

        private void SetIconVisibility(Document doc, string objectName, bool isVisible)
        {
            bpac.Object iconObj = doc.GetObject(objectName);
            if (iconObj != null)
            {
                if (!isVisible && !string.IsNullOrEmpty(_transparentPngPath) && File.Exists(_transparentPngPath))
                {
                    iconObj.SetData(0, _transparentPngPath, 4);
                }
            }
        }

        // =========================================================================
        // RENDER PREVIEW TO PICTURE BOX
        // =========================================================================
        private void RenderPreview(PictureBox picBox, LabelTemplateType type, SampleDetailsDto details, bool isSubstance)
        {
            string templateFileName = GetTemplateFileName(type, isSubstance);
            string templateFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateFileName);

            if (!File.Exists(templateFullPath))
            {
                if (picBox.Image != null)
                {
                    picBox.Image.Dispose();
                    picBox.Image = null;
                }
                return;
            }

            Document doc = new Document();
            try
            {
                if (doc.Open(templateFullPath))
                {
                    PopulateDocument(doc, type, details, isSubstance);

                    string tempPreviewFile = Path.Combine(Path.GetTempPath(), $"openbis_preview_{type}_{Guid.NewGuid():N}.png");
                    
                    if (doc.Export((ExportType)4, tempPreviewFile, 360))
                    {
                        if (picBox.Image != null)
                        {
                            picBox.Image.Dispose();
                            picBox.Image = null;
                        }

                        using (FileStream fs = new FileStream(tempPreviewFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            using (Image original = Image.FromStream(fs))
                            {
                                picBox.Image = new Bitmap(original);
                            }
                        }

                        try { File.Delete(tempPreviewFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error rendering preview for {templateFileName}: {ex.Message}");
            }
            finally
            {
                doc.Close();
            }
        }

        private void ClearAllPreviews()
        {
            _currentSampleDetails = null;

            if (smallBoxLabelPreview.Image != null)
            {
                smallBoxLabelPreview.Image.Dispose();
                smallBoxLabelPreview.Image = null;
            }

            if (magnetLabelPreview.Image != null)
            {
                magnetLabelPreview.Image.Dispose();
                magnetLabelPreview.Image = null;
            }

            if (flaskLabelPreview.Image != null)
            {
                flaskLabelPreview.Image.Dispose();
                flaskLabelPreview.Image = null;
            }

            magnetFreeTextBox.Text = "";
            flaskFreeTextBox.Text = "";

            smallBoxLabelCheckBox.Checked = false;
            magnetLabelCheckBox.Checked = false;
            flaskLabelCheckBox.Checked = false;
        }

        private void SetControlsEnabled(bool enabled)
        {
            openbisTypeComboBox.Enabled = enabled;
            openbisObjectsComboBox.Enabled = enabled;
            loadPrintTemplates.Enabled = enabled;
            btnPrintLabel.Enabled = enabled;
        }

        // =========================================================================
        // PRINT LABELS BUTTON CLICK (SELECTED PRINTER ROUTING INFO)
        // =========================================================================
        private void btnPrintLabel_Click_1(object sender, EventArgs e)
        {
            if (_currentSampleDetails == null)
            {
                MessageBox.Show("Please click 'Load Templates' first to load an openBIS object.", "Print Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<(LabelTemplateType Type, string LabelName, string AssignedPrinter)> queuedJobs = new List<(LabelTemplateType, string, string)>();

            if (smallBoxLabelCheckBox.Checked)
            {
                string printer = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.SmallBoxPrinter;
                queuedJobs.Add((LabelTemplateType.SmallBox12mm, "Small Box 12mm Label", printer));
            }

            if (magnetLabelCheckBox.Checked)
            {
                string printer = magnetPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.MagnetPrinter;
                queuedJobs.Add((LabelTemplateType.Magnet12mm, "Magnet 12mm Label", printer));
            }

            if (flaskLabelCheckBox.Checked)
            {
                string printer = flaskPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.FlaskPrinter;
                queuedJobs.Add((LabelTemplateType.Flask24mm, "Flask 24mm Label", printer));
            }

            if (queuedJobs.Count == 0)
            {
                MessageBox.Show("Please select at least one label template to print (check the box at the top-right of the label).", "No Label Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Sample: {_currentSampleDetails.Name} ({_currentSampleDetails.PermId})");
            sb.AppendLine();
            sb.AppendLine("The selected templates are configured to be sent to the following user-selected printers:");
            sb.AppendLine();

            foreach (var job in queuedJobs)
            {
                sb.AppendLine($"  • {job.LabelName}  ➔  [{job.AssignedPrinter}]");
            }

            sb.AppendLine();
            sb.AppendLine("(Note: Direct printing is paused as requested.)");

            MessageBox.Show(sb.ToString(), "Printer Routing & Queue", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {
        }

        // =========================================================================
        // OPENBIS PYTHON PROCESS RUNNERS
        // =========================================================================
        private OpenBisTypesResult QueryOpenBisTypes(string url, string token)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string scriptFullPath = Path.Combine(baseDir, SCRIPT_PATH);
                string resultsFullPath = Path.Combine(baseDir, RESULTS_FILE_PATH);

                if (!File.Exists(scriptFullPath))
                {
                    return new OpenBisTypesResult { Success = false, Error = $"Script missing at '{scriptFullPath}'" };
                }

                if (File.Exists(resultsFullPath))
                {
                    try { File.Delete(resultsFullPath); } catch { }
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PYTHON_PATH,
                    Arguments = $"\"{scriptFullPath}\" \"{url}\" \"{token}\" \"--types\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    process.WaitForExit();
                }

                if (!File.Exists(resultsFullPath))
                {
                    return new OpenBisTypesResult { Success = false, Error = "openbis_results.json was not created." };
                }

                string jsonResult = File.ReadAllText(resultsFullPath);
                return JsonConvert.DeserializeObject<OpenBisTypesResult>(jsonResult)
                       ?? new OpenBisTypesResult { Success = false, Error = "Failed to deserialize types JSON." };
            }
            catch (Exception ex)
            {
                return new OpenBisTypesResult { Success = false, Error = ex.Message };
            }
        }

        private OpenBisListResult QueryOpenBisByType(string url, string token, string sampleType)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string scriptFullPath = Path.Combine(baseDir, SCRIPT_PATH);
                string resultsFullPath = Path.Combine(baseDir, RESULTS_FILE_PATH);

                if (!File.Exists(scriptFullPath))
                {
                    return new OpenBisListResult { Success = false, Error = $"Script missing at '{scriptFullPath}'" };
                }

                if (File.Exists(resultsFullPath))
                {
                    try { File.Delete(resultsFullPath); } catch { }
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PYTHON_PATH,
                    Arguments = $"\"{scriptFullPath}\" \"{url}\" \"{token}\" \"{sampleType}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    process.WaitForExit();
                }

                if (!File.Exists(resultsFullPath))
                {
                    return new OpenBisListResult { Success = false, Error = "Python script completed, but openbis_results.json was not created." };
                }

                string jsonResult = File.ReadAllText(resultsFullPath);

                if (string.IsNullOrWhiteSpace(jsonResult))
                {
                    return new OpenBisListResult { Success = false, Error = "Results JSON file is empty." };
                }

                return JsonConvert.DeserializeObject<OpenBisListResult>(jsonResult)
                       ?? new OpenBisListResult { Success = false, Error = "Failed to deserialize JSON output." };
            }
            catch (Exception ex)
            {
                return new OpenBisListResult { Success = false, Error = ex.Message };
            }
        }

        private OpenBisDetailsResult QueryOpenBisSampleDetails(string url, string token, string permId)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string scriptFullPath = Path.Combine(baseDir, SCRIPT_PATH);
                string resultsFullPath = Path.Combine(baseDir, RESULTS_FILE_PATH);

                if (!File.Exists(scriptFullPath))
                {
                    return new OpenBisDetailsResult { Success = false, Error = $"Script missing at '{scriptFullPath}'" };
                }

                if (File.Exists(resultsFullPath))
                {
                    try { File.Delete(resultsFullPath); } catch { }
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PYTHON_PATH,
                    Arguments = $"\"{scriptFullPath}\" \"{url}\" \"{token}\" \"--details\" \"{permId}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    process.WaitForExit();
                }

                if (!File.Exists(resultsFullPath))
                {
                    return new OpenBisDetailsResult { Success = false, Error = "Python script completed, but openbis_results.json was not created." };
                }

                string jsonResult = File.ReadAllText(resultsFullPath);

                if (string.IsNullOrWhiteSpace(jsonResult))
                {
                    return new OpenBisDetailsResult { Success = false, Error = "Results JSON file is empty." };
                }

                return JsonConvert.DeserializeObject<OpenBisDetailsResult>(jsonResult)
                       ?? new OpenBisDetailsResult { Success = false, Error = "Failed to deserialize details JSON." };
            }
            catch (Exception ex)
            {
                return new OpenBisDetailsResult { Success = false, Error = ex.Message };
            }
        }
    }

    // ComboBox Item Wrapper
    public class OpenBisTypeItem
    {
        public string Code { get; set; } = "";
        public string DisplayText { get; set; } = "";

        public override string ToString() => DisplayText;
    }

    // Config DTO
    public class AppConfig
    {
        [JsonProperty("openbisUrl")]
        public string OpenBisUrl { get; set; } = "https://openbis.your-institution.ch";

        [JsonProperty("token")]
        public string Token { get; set; } = "";
    }

    // Printer Config DTO
    public class PrinterConfig
    {
        [JsonProperty("smallBoxPrinter")]
        public string SmallBoxPrinter { get; set; } = "Brother PT-P700";

        [JsonProperty("magnetPrinter")]
        public string MagnetPrinter { get; set; } = "Brother PT-P700 (Kopie 1)";

        [JsonProperty("flaskPrinter")]
        public string FlaskPrinter { get; set; } = "Brother PT-P700 (Kopie 2)";
    }

    // Query Types Results DTOs
    public class OpenBisTypesResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("types")]
        public List<OpenBisTypeInfoDto> Types { get; set; } = new List<OpenBisTypeInfoDto>();
    }

    public class OpenBisTypeInfoDto
    {
        [JsonProperty("code")]
        public string Code { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";
    }

    // Query List Results DTOs
    public class OpenBisListResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("data")]
        public List<SampleDto> Data { get; set; } = new List<SampleDto>();
    }

    public class SampleDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("permId")]
        public string PermId { get; set; } = "";

        [JsonProperty("modificationDate")]
        public string ModificationDate { get; set; } = "";
    }

    // Query Sample Details DTOs
    public class OpenBisDetailsResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("details")]
        public SampleDetailsDto? Details { get; set; }
    }

    public class SampleDetailsDto
    {
        [JsonProperty("permId")]
        public string PermId { get; set; } = "";

        [JsonProperty("sampleType")]
        public string SampleType { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("studiedBy")]
        public string StudiedBy { get; set; } = "";

        [JsonProperty("studiedByName")]
        public string StudiedByName { get; set; } = "";

        [JsonProperty("studiedByUsername")]
        public string StudiedByUsername { get; set; } = "";

        [JsonProperty("storageLocation")]
        public string StorageLocation { get; set; } = "";

        [JsonProperty("supplierOwnName")]
        public string SupplierOwnName { get; set; } = "";

        [JsonProperty("evaporationTemperature")]
        public string EvaporationTemperature { get; set; } = "";

        [JsonProperty("specialStorageConditions")]
        public List<string> SpecialStorageConditions { get; set; } = new List<string>();

        [JsonProperty("iconFlags")]
        public IconFlagsDto IconFlags { get; set; } = new IconFlagsDto();

        [JsonProperty("elnPreviewPath")]
        public string ElnPreviewPath { get; set; } = "";

        [JsonProperty("properties")]
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public class IconFlagsDto
    {
        [JsonProperty("poisonous")]
        public bool Poisonous { get; set; }

        [JsonProperty("flammable")]
        public bool Flammable { get; set; }

        [JsonProperty("dark")]
        public bool Dark { get; set; }

        [JsonProperty("dry")]
        public bool Dry { get; set; }

        [JsonProperty("freezer")]
        public bool Freezer { get; set; }

        [JsonProperty("fridge")]
        public bool Fridge { get; set; }

        [JsonProperty("noOxygen")]
        public bool NoOxygen { get; set; }

        [JsonProperty("tightlyClosed")]
        public bool TightlyClosed { get; set; }
    }
}