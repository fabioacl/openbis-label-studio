using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenBISLabelStudio.Backend.Contracts;
using OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio
{
    public partial class Form1 : Form
    {
        private const string PLACEHOLDER_TYPE = "Select an object type...";

        private readonly ILabelStudioBackend _backend;
        private readonly bool _ownsBackend;
        private readonly FormTokenRecoveryPrompter _recoveryPrompter;
        private readonly ToolTip _statusToolTip = new ToolTip();

        private string _credentialFingerprint = "";
        private LabelTemplateProfile _activeProfile = LabelTemplateProfile.Generic;
        private string _currentSelectedTypeCode = "";
        private bool _hasLoadedSample;
        private string _loadedSampleName = "";
        private string _loadedSamplePermId = "";
        private string _loadedSampleLabel = "";
        private string _fallbackSmallBoxPrinter = "Brother PT-P700";
        private string _fallbackMagnetPrinter = "Brother PT-P700 (Kopie 1)";
        private string _fallbackFlaskPrinter = "Brother PT-P700 (Kopie 2)";
        private bool _isPopulatingPrinters;
        private bool _isPopulatingTypes;
        private readonly Dictionary<LabelTemplateType, long> _slotRenderVersions = new Dictionary<LabelTemplateType, long>();
        private readonly Dictionary<LabelTemplateType, CancellationTokenSource> _slotPreviewCts = new Dictionary<LabelTemplateType, CancellationTokenSource>();
        private CancellationTokenSource? _typeRefreshCts;
        private CancellationTokenSource? _typeSelectionCts;
        private CancellationTokenSource? _sampleLoadPreviewCts;
        private long _typeRefreshRequestVersion;
        private long _typeSelectionRequestVersion;
        private long _sampleDetailsRequestVersion;
        private string? _loadingStatusMessage;
        private string _persistentNoticeMessage = "";
        private string? _persistentNoticeTooltip;
        private Icon? _windowIcon;

        public Form1()
            : this(new OpenBISLabelStudio.Backend.Services.LabelStudioBackend(), ownsBackend: true)
        {
        }

        public Form1(ILabelStudioBackend backend)
            : this(backend, ownsBackend: false)
        {
        }

        private Form1(ILabelStudioBackend backend, bool ownsBackend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _ownsBackend = ownsBackend;
            _recoveryPrompter = new FormTokenRecoveryPrompter(this, _backend);

            InitializeComponent();
            AppIconProvider.AttachWindowIcon(this, ref _windowIcon);
        }

        /// <summary>
        /// Safely decodes PNG bytes into an independent <see cref="Bitmap"/>, falling back to
        /// <paramref name="fallbackFactory"/> or a blank placeholder bitmap if the payload is empty or corrupt.
        /// </summary>
        public static Bitmap DecodePngBitmap(byte[]? pngBytes, Func<byte[]>? fallbackFactory = null)
        {
            if (TryDecodeBitmapFromBytes(pngBytes, out Bitmap? bitmap))
            {
                return bitmap!;
            }

            if (fallbackFactory != null)
            {
                try
                {
                    byte[]? fallbackBytes = fallbackFactory();
                    if (TryDecodeBitmapFromBytes(fallbackBytes, out Bitmap? fallbackBitmap))
                    {
                        return fallbackBitmap!;
                    }
                }
                catch
                {
                    // Fall through to blank safe bitmap.
                }
            }

            return CreateSafeBlankBitmap();
        }

        private static bool TryDecodeBitmapFromBytes(byte[]? pngBytes, out Bitmap? bitmap)
        {
            bitmap = null;
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return false;
            }

            try
            {
                using var ms = new MemoryStream(pngBytes);
                using var decoded = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);
                bitmap = new Bitmap(decoded);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Bitmap CreateSafeBlankBitmap()
        {
            var bmp = new Bitmap(240, 80, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            return bmp;
        }

        partial void DisposeManagedResources()
        {
            CancelPendingTypeRefresh();
            CancelPendingTypeSelection();
            CancelPendingSampleLoad();
            CancelAllSlotPreviewRenders();
            SetPictureBoxImage(smallBoxLabelPreview, null);
            SetPictureBoxImage(magnetLabelPreview, null);
            SetPictureBoxImage(flaskLabelPreview, null);
            _statusToolTip.Dispose();
            AppIconProvider.DetachWindowIcon(this, ref _windowIcon);

            if (_ownsBackend)
            {
                _backend.Dispose();
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            bool wizardSyncedTypes = false;
            WorkspaceStartupState startup = await _backend.InitializeWorkspaceAsync(_recoveryPrompter);
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (startup.MissingWizardStep != SetupWizardStep.None)
            {
                using var wizard = new SetupSettingsForm(
                    _backend,
                    SetupDialogMode.Wizard,
                    startup.MissingWizardStep);

                DialogResult wizardResult = wizard.ShowDialog(this);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                wizardSyncedTypes = wizard.TypesSyncedFromServer;
                startup = await _backend.InitializeWorkspaceAsync(_recoveryPrompter);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (wizardResult != DialogResult.OK && startup.MissingWizardStep != SetupWizardStep.None)
                {
                    BeginInvoke(new Action(Close));
                    return;
                }
            }

            ApplyWorkspaceStartupState(startup, preferredTypeCode: null);
            _backend.WarmUpInBackground();

            if (wizardSyncedTypes)
            {
                ClearStatusNotice();
                return;
            }

            await RefreshWorkspaceTypesAsync();
        }

        private void ApplyWorkspaceStartupState(WorkspaceStartupState startup, string? preferredTypeCode)
        {
            _credentialFingerprint = startup.CredentialFingerprint ?? "";
            PopulateInstalledPrinters(startup.Printers);
            PopulateOpenBisTypeComboBox(startup.ConfiguredTypes, preferredTypeCode);
            UpdateFreeTextControlsState();

            if (!string.IsNullOrWhiteSpace(startup.NoticeMessage))
            {
                ShowStatusNotice(startup.NoticeMessage);
            }
        }

        private void PopulateInstalledPrinters(PrinterDropdownState? printersState)
        {
            _isPopulatingPrinters = true;
            try
            {
                PrinterDropdownState printers = printersState ?? new PrinterDropdownState();

                _fallbackSmallBoxPrinter = string.IsNullOrWhiteSpace(printers.SmallBoxPrinter)
                    ? "Brother PT-P700"
                    : printers.SmallBoxPrinter.Trim();
                _fallbackMagnetPrinter = string.IsNullOrWhiteSpace(printers.MagnetPrinter)
                    ? "Brother PT-P700 (Kopie 1)"
                    : printers.MagnetPrinter.Trim();
                _fallbackFlaskPrinter = string.IsNullOrWhiteSpace(printers.FlaskPrinter)
                    ? "Brother PT-P700 (Kopie 2)"
                    : printers.FlaskPrinter.Trim();

                PopulatePrinterCombo(smallBoxPrinterComboBox, printers.AvailablePrinters, printers.SmallBoxSelectedIndex, _fallbackSmallBoxPrinter);
                PopulatePrinterCombo(magnetPrinterComboBox, printers.AvailablePrinters, printers.MagnetSelectedIndex, _fallbackMagnetPrinter);
                PopulatePrinterCombo(flaskPrinterComboBox, printers.AvailablePrinters, printers.FlaskSelectedIndex, _fallbackFlaskPrinter);

                _fallbackSmallBoxPrinter = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _fallbackSmallBoxPrinter;
                _fallbackMagnetPrinter = magnetPrinterComboBox.SelectedItem?.ToString() ?? _fallbackMagnetPrinter;
                _fallbackFlaskPrinter = flaskPrinterComboBox.SelectedItem?.ToString() ?? _fallbackFlaskPrinter;
            }
            finally
            {
                _isPopulatingPrinters = false;
            }
        }

        private static void PopulatePrinterCombo(
            ComboBox comboBox,
            IReadOnlyList<string>? printers,
            int preferredIndex,
            string preferredName)
        {
            comboBox.Items.Clear();
            if (printers != null)
            {
                foreach (string printer in printers)
                {
                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        comboBox.Items.Add(printer);
                    }
                }
            }

            if (comboBox.Items.Count == 0)
            {
                return;
            }

            if (preferredIndex >= 0 && preferredIndex < comboBox.Items.Count)
            {
                comboBox.SelectedIndex = preferredIndex;
                return;
            }

            int matchedIndex = 0;
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (string.Equals(comboBox.Items[i]?.ToString(), preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndex = i;
                    break;
                }
            }

            comboBox.SelectedIndex = matchedIndex;
        }

        private async void PrinterComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isPopulatingPrinters)
            {
                return;
            }

            string smallBox = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _fallbackSmallBoxPrinter;
            string magnet = magnetPrinterComboBox.SelectedItem?.ToString() ?? _fallbackMagnetPrinter;
            string flask = flaskPrinterComboBox.SelectedItem?.ToString() ?? _fallbackFlaskPrinter;

            if (string.Equals(_fallbackSmallBoxPrinter, smallBox, StringComparison.Ordinal)
                && string.Equals(_fallbackMagnetPrinter, magnet, StringComparison.Ordinal)
                && string.Equals(_fallbackFlaskPrinter, flask, StringComparison.Ordinal))
            {
                return;
            }

            _fallbackSmallBoxPrinter = smallBox;
            _fallbackMagnetPrinter = magnet;
            _fallbackFlaskPrinter = flask;

            try
            {
                OperationResult result = await _backend.UpdatePrinterSelectionsAsync(smallBox, magnet, flask);
                if (!IsDisposed && !Disposing && !result.Success)
                {
                    ShowStatusNotice(result.ErrorMessage, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    ShowStatusNotice($"Could not save printer selection: {ex.Message}", ex.Message);
                }
            }
        }

        private void PopulateOpenBisTypeComboBox(IReadOnlyList<TypeOptionDto>? configuredTypes, string? preferredTypeCode)
        {
            _isPopulatingTypes = true;
            try
            {
                List<TypeOptionDto> typeItems = (configuredTypes ?? Array.Empty<TypeOptionDto>())
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Code))
                    .Select(t => new TypeOptionDto
                    {
                        Code = t.Code.Trim(),
                        DisplayText = string.IsNullOrWhiteSpace(t.DisplayText) ? t.Code.Trim() : t.DisplayText.Trim(),
                        TemplateProfile = t.TemplateProfile
                    })
                    .OrderBy(t => t.DisplayText, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.Code, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                openbisTypeComboBox.Items.Clear();
                openbisTypeComboBox.Items.Add(new TypeOptionDto
                {
                    Code = "",
                    DisplayText = PLACEHOLDER_TYPE,
                    TemplateProfile = LabelTemplateProfile.Generic
                });

                int matchedIndex = 0;
                for (int i = 0; i < typeItems.Count; i++)
                {
                    openbisTypeComboBox.Items.Add(typeItems[i]);
                    if (!string.IsNullOrWhiteSpace(preferredTypeCode)
                        && string.Equals(typeItems[i].Code, preferredTypeCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedIndex = i + 1;
                    }
                }

                openbisTypeComboBox.SelectedIndex = matchedIndex;
                if (matchedIndex > 0 && openbisTypeComboBox.SelectedItem is TypeOptionDto activeItem)
                {
                    _currentSelectedTypeCode = activeItem.Code;
                    _activeProfile = activeItem.TemplateProfile;
                    selectedOpenbisTypeLabel.Text = $"{activeItem.DisplayText}:";
                }
                else
                {
                    _currentSelectedTypeCode = "";
                    _activeProfile = LabelTemplateProfile.Generic;
                    selectedOpenbisTypeLabel.Text = "Type:";
                }
            }
            finally
            {
                _isPopulatingTypes = false;
            }
        }

        private async Task RefreshWorkspaceTypesAsync()
        {
            long requestVersion = ++_typeRefreshRequestVersion;
            CancelPendingTypeRefresh();

            var cts = new CancellationTokenSource();
            _typeRefreshCts = cts;
            CancellationToken ct = cts.Token;

            try
            {
                BackgroundTypeRefreshResult refresh = await _backend.RefreshWorkspaceTypesAsync(_recoveryPrompter, ct);
                if (IsDisposed || Disposing || ct.IsCancellationRequested || requestVersion != _typeRefreshRequestVersion)
                {
                    return;
                }

                if (refresh.Skipped)
                {
                    return;
                }

                if (!refresh.Success)
                {
                    if (!string.IsNullOrWhiteSpace(refresh.NoticeMessage))
                    {
                        ShowStatusNotice(refresh.NoticeMessage, refresh.NoticeTooltip);
                    }

                    return;
                }

                ClearStatusNotice();
                if (refresh.TypesUpdated)
                {
                    PopulateOpenBisTypeComboBox(refresh.ConfiguredTypes, _currentSelectedTypeCode);
                    UpdateFreeTextControlsState();
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored when a newer refresh, Settings dialog, or form disposal cancels this request.
            }
            finally
            {
                Interlocked.CompareExchange(ref _typeRefreshCts, null, cts);
                cts.Dispose();
            }
        }

        private void UpdateFreeTextControlsState()
        {
            bool isMoleculeProfile = _activeProfile == LabelTemplateProfile.Molecule;
            bool hasSelection = !string.IsNullOrEmpty(_currentSelectedTypeCode);

            // FreeText is applicable only for Generic / Free-text template profile
            magnetFreeTextBox.Enabled = hasSelection && !isMoleculeProfile;
            flaskFreeTextBox.Enabled = hasSelection && !isMoleculeProfile;

            if (isMoleculeProfile)
            {
                magnetFreeTextBox.PlaceholderText = "(N/A for Molecule templates)";
                flaskFreeTextBox.PlaceholderText = "(N/A for Molecule templates)";
            }
            else
            {
                magnetFreeTextBox.PlaceholderText = "Free text placeholder";
                flaskFreeTextBox.PlaceholderText = "Free text placeholder";
            }
        }

        private async void openbisTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isPopulatingTypes || openbisTypeComboBox.SelectedItem is not TypeOptionDto selectedItem)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedItem.Code) || selectedItem.DisplayText == PLACEHOLDER_TYPE)
            {
                ++_typeSelectionRequestVersion;
                ++_sampleDetailsRequestVersion;
                CancelPendingTypeSelection();
                CancelPendingSampleLoad();
                SetSampleQueryControlsEnabled(true);
                selectedOpenbisTypeLabel.Text = "Type:";
                openbisObjectsComboBox.Items.Clear();
                _currentSelectedTypeCode = "";
                _activeProfile = LabelTemplateProfile.Generic;
                _hasLoadedSample = false;
                _loadedSampleName = "";
                _loadedSamplePermId = "";
                _loadedSampleLabel = "";
                _backend.ClearActiveSample();
                ClearAllPreviews();
                UpdateFreeTextControlsState();
                ClearLoadingFeedback();
                return;
            }

            long requestVersion = ++_typeSelectionRequestVersion;
            ++_sampleDetailsRequestVersion;
            CancelPendingTypeSelection();
            CancelPendingSampleLoad();

            var cts = new CancellationTokenSource();
            _typeSelectionCts = cts;
            CancellationToken ct = cts.Token;

            _currentSelectedTypeCode = selectedItem.Code;
            _activeProfile = selectedItem.TemplateProfile;
            _hasLoadedSample = false;
            _loadedSampleName = "";
            _loadedSamplePermId = "";
            _loadedSampleLabel = "";
            _backend.ClearActiveSample();

            selectedOpenbisTypeLabel.Text = $"{selectedItem.DisplayText}:";
            openbisObjectsComboBox.Items.Clear();
            ClearAllPreviews();
            UpdateFreeTextControlsState();

            // Allow switching object types or opening Settings even while items for a previous selection load.
            SetSampleQueryControlsEnabled(false);
            SetLoadingFeedback($"Loading {selectedItem.DisplayText} objects from openBIS...");

            try
            {
                SampleListResultDto result = await _backend.LoadObjectsForTypeAsync(selectedItem.Code, _recoveryPrompter, ct);
                if (IsDisposed || Disposing || ct.IsCancellationRequested || requestVersion != _typeSelectionRequestVersion)
                {
                    return;
                }

                if (result.WasCancelled)
                {
                    if (!string.IsNullOrWhiteSpace(result.NoticeMessage))
                    {
                        ShowStatusNotice(result.NoticeMessage, result.NoticeTooltip);
                    }

                    return;
                }

                if (result.IsMissingConfiguration)
                {
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "openBIS URL is missing. Please configure it in Settings..."
                            : result.ErrorMessage,
                        "Configuration Missing",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (result.Success)
                {
                    ClearStatusNotice();
                    foreach (SampleSummaryDto sample in result.Samples)
                    {
                        if (sample != null)
                        {
                            openbisObjectsComboBox.Items.Add(sample);
                        }
                    }

                    if (openbisObjectsComboBox.Items.Count > 0)
                    {
                        openbisObjectsComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to fetch items from openBIS."
                            : result.ErrorMessage,
                        "openBIS Fetch Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored when a newer type selection, placeholder reset, Settings dialog, or form disposal cancels this request.
            }
            finally
            {
                Interlocked.CompareExchange(ref _typeSelectionCts, null, cts);
                cts.Dispose();

                if (!IsDisposed && !Disposing && requestVersion == _typeSelectionRequestVersion)
                {
                    ClearLoadingFeedback();
                    SetSampleQueryControlsEnabled(true);
                }
            }
        }

        // =========================================================================
        // LOAD PRINT TEMPLATES BUTTON CLICK
        // =========================================================================
        private async void loadPrintTemplates_Click(object sender, EventArgs e)
        {
            if (openbisObjectsComboBox.SelectedItem is not SampleSummaryDto selectedSample)
            {
                MessageBox.Show("Please select an openBIS object first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await LoadAndStreamSamplePreviewsAsync(
                _currentSelectedTypeCode,
                selectedSample.PermId,
                selectedSample.Name);
        }

        private async Task LoadAndStreamSamplePreviewsAsync(string typeCode, string permId, string sampleName)
        {
            long requestVersion = ++_sampleDetailsRequestVersion;
            CancelPendingSampleLoad();
            CancelAllSlotPreviewRenders();

            _hasLoadedSample = false;
            _loadedSampleName = "";
            _loadedSamplePermId = "";
            _loadedSampleLabel = "";
            _backend.ClearActiveSample();

            var cts = new CancellationTokenSource();
            _sampleLoadPreviewCts = cts;
            CancellationToken ct = cts.Token;

            LabelTemplateProfile initialProfile = _activeProfile;
            string magnetFreeText = initialProfile == LabelTemplateProfile.Generic ? magnetFreeTextBox.Text : "";
            string flaskFreeText = initialProfile == LabelTemplateProfile.Generic ? flaskFreeTextBox.Text : "";

            var slotVersions = new Dictionary<LabelTemplateType, long>();
            foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
            {
                slotVersions[slot] = NextSlotRenderVersion(slot);
                SetPictureBoxImage(
                    GetSlotPictureBox(slot),
                    DecodeSlotPreview(slot, initialProfile, _backend.GetLoadingPreviewPng(slot, initialProfile)));
            }

            // Allow switching object types or opening Settings even while items for a previous selection load.
            SetSampleQueryControlsEnabled(false);
            SetLoadingFeedback($"Loading details and previews for {sampleName}...");

            var appliedSlots = new HashSet<LabelTemplateType>();

            try
            {
                SamplePreviewBundleDto bundle = await _backend.LoadSamplePreviewsAsync(
                    typeCode,
                    permId,
                    magnetFreeText,
                    flaskFreeText,
                    onSlotReady: (slot, pngBytes) => MarshalLoadedSlotReady(
                        slot,
                        pngBytes,
                        requestVersion,
                        slotVersions,
                        appliedSlots,
                        initialProfile,
                        ct),
                    recoveryPrompter: _recoveryPrompter,
                    ct: ct);

                if (IsDisposed || Disposing || ct.IsCancellationRequested || requestVersion != _sampleDetailsRequestVersion)
                {
                    return;
                }

                if (bundle.WasCancelled)
                {
                    ClearSlotPreviewImages();
                    if (!string.IsNullOrWhiteSpace(bundle.NoticeMessage))
                    {
                        ShowStatusNotice(bundle.NoticeMessage, bundle.NoticeTooltip);
                    }

                    return;
                }

                if (!bundle.Success)
                {
                    ClearSlotPreviewImages();
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(bundle.ErrorMessage)
                            ? "Could not fetch sample details."
                            : bundle.ErrorMessage,
                        "openBIS Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                ClearStatusNotice();
                _hasLoadedSample = true;
                _activeProfile = bundle.Profile;
                _loadedSampleName = string.IsNullOrWhiteSpace(bundle.SampleName) ? sampleName : bundle.SampleName;
                _loadedSamplePermId = string.IsNullOrWhiteSpace(bundle.PermId) ? permId : bundle.PermId;
                _loadedSampleLabel = string.IsNullOrWhiteSpace(_loadedSampleName)
                    ? _loadedSamplePermId
                    : $"{_loadedSampleName} ({_loadedSamplePermId})";

                UpdateFreeTextControlsState();

                // Automatically check all preview checkboxes for convenience
                smallBoxLabelCheckBox.Checked = true;
                magnetLabelCheckBox.Checked = true;
                flaskLabelCheckBox.Checked = true;

                foreach (KeyValuePair<LabelTemplateType, byte[]> kvp in bundle.SlotPreviews)
                {
                    if (appliedSlots.Add(kvp.Key))
                    {
                        SetPictureBoxImage(
                            GetSlotPictureBox(kvp.Key),
                            DecodeSlotPreview(kvp.Key, _activeProfile, kvp.Value));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored when a newer type/sample selection supersedes this request.
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing && requestVersion == _sampleDetailsRequestVersion)
                {
                    ClearSlotPreviewImages();
                    MessageBox.Show($"Could not fetch sample details: {ex.Message}", "openBIS Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _sampleLoadPreviewCts, null, cts);
                cts.Dispose();

                if (!IsDisposed && !Disposing && requestVersion == _sampleDetailsRequestVersion)
                {
                    ClearLoadingFeedback();
                    SetSampleQueryControlsEnabled(true);
                }
            }
        }

        private void MarshalLoadedSlotReady(
            LabelTemplateType slot,
            byte[] pngBytes,
            long requestVersion,
            Dictionary<LabelTemplateType, long> slotVersions,
            HashSet<LabelTemplateType> appliedSlots,
            LabelTemplateProfile fallbackProfile,
            CancellationToken ct)
        {
            if (IsDisposed || Disposing || ct.IsCancellationRequested)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                        MarshalLoadedSlotReady(slot, pngBytes, requestVersion, slotVersions, appliedSlots, fallbackProfile, ct)));
                }
                catch (ObjectDisposedException)
                {
                    // Ignore callbacks arriving as form disposes.
                }
                catch (InvalidOperationException)
                {
                    // Ignore callbacks arriving after handle destruction.
                }

                return;
            }

            if (IsDisposed
                || Disposing
                || ct.IsCancellationRequested
                || requestVersion != _sampleDetailsRequestVersion)
            {
                return;
            }

            if (!slotVersions.TryGetValue(slot, out long expectedSlotVersion)
                || !IsCurrentSlotVersion(slot, expectedSlotVersion))
            {
                return;
            }

            if (appliedSlots.Add(slot))
            {
                SetPictureBoxImage(
                    GetSlotPictureBox(slot),
                    DecodeSlotPreview(slot, fallbackProfile, pngBytes));
            }
        }

        // =========================================================================
        // DYNAMIC FREE TEXT COMMIT HANDLERS (ON LEAVE OR ENTER)
        // =========================================================================
        private void magnetFreeTextBox_Leave(object sender, EventArgs e)
        {
            if (_hasLoadedSample && _activeProfile == LabelTemplateProfile.Generic)
            {
                _ = RenderSlotFreeTextPreviewAsync(
                    magnetLabelPreview,
                    LabelTemplateType.Magnet12mm,
                    magnetFreeTextBox.Text);
            }
        }

        private void magnetFreeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ActiveControl = null; // Shift focus to trigger commit
                magnetFreeTextBox_Leave(sender, EventArgs.Empty);
            }
        }

        private void flaskFreeTextBox_Leave(object sender, EventArgs e)
        {
            if (_hasLoadedSample && _activeProfile == LabelTemplateProfile.Generic)
            {
                _ = RenderSlotFreeTextPreviewAsync(
                    flaskLabelPreview,
                    LabelTemplateType.Flask24mm,
                    flaskFreeTextBox.Text);
            }
        }

        private void flaskFreeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ActiveControl = null; // Shift focus to trigger commit
                flaskFreeTextBox_Leave(sender, EventArgs.Empty);
            }
        }

        private async Task RenderSlotFreeTextPreviewAsync(
            PictureBox picBox,
            LabelTemplateType slot,
            string freeText)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            long slotVersion = NextSlotRenderVersion(slot);
            CancelSlotPreviewRender(slot);

            var cts = new CancellationTokenSource();
            lock (_slotPreviewCts)
            {
                _slotPreviewCts[slot] = cts;
            }

            CancellationToken cancellationToken = cts.Token;
            LabelTemplateProfile profile = _activeProfile;

            SetPictureBoxImage(
                picBox,
                DecodeSlotPreview(slot, profile, _backend.GetLoadingPreviewPng(slot, profile)));

            try
            {
                byte[] pngBytes = await _backend.RenderSlotWithFreeTextAsync(slot, freeText, cancellationToken);
                if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested || !IsCurrentSlotVersion(slot, slotVersion))
                {
                    return;
                }

                SetPictureBoxImage(picBox, DecodeSlotPreview(slot, profile, pngBytes));
            }
            catch (OperationCanceledException)
            {
                // Ignored when a newer render or ClearAllPreviews supersedes this slot request.
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing && IsCurrentSlotVersion(slot, slotVersion))
                {
                    SetPictureBoxImage(
                        picBox,
                        DecodeSlotPreview(slot, profile, _backend.GetFallbackPreviewPng(slot, profile, $"Preview unavailable: {ex.Message}")));
                }
            }
            finally
            {
                lock (_slotPreviewCts)
                {
                    if (_slotPreviewCts.TryGetValue(slot, out CancellationTokenSource? tracked) && ReferenceEquals(tracked, cts))
                    {
                        _slotPreviewCts.Remove(slot);
                    }
                }

                cts.Dispose();
            }
        }

        private Bitmap DecodeSlotPreview(LabelTemplateType slot, LabelTemplateProfile profile, byte[]? pngBytes)
        {
            return DecodePngBitmap(
                pngBytes,
                () => _backend.GetFallbackPreviewPng(slot, profile, "Preview unavailable"));
        }

        private PictureBox GetSlotPictureBox(LabelTemplateType slot)
        {
            return slot switch
            {
                LabelTemplateType.SmallBox12mm => smallBoxLabelPreview,
                LabelTemplateType.Magnet12mm => magnetLabelPreview,
                LabelTemplateType.Flask24mm => flaskLabelPreview,
                _ => smallBoxLabelPreview
            };
        }

        private long NextSlotRenderVersion(LabelTemplateType slot)
        {
            long nextVersion = (_slotRenderVersions.TryGetValue(slot, out long current) ? current : 0) + 1;
            _slotRenderVersions[slot] = nextVersion;
            return nextVersion;
        }

        private bool IsCurrentSlotVersion(LabelTemplateType slot, long expectedVersion)
        {
            return _slotRenderVersions.TryGetValue(slot, out long activeVersion) && activeVersion == expectedVersion;
        }

        private bool CancelPendingTypeRefresh()
        {
            return CancelTokenSourceSafe(Interlocked.Exchange(ref _typeRefreshCts, null));
        }

        private bool CancelPendingTypeSelection()
        {
            return CancelTokenSourceSafe(Interlocked.Exchange(ref _typeSelectionCts, null));
        }

        private bool CancelPendingSampleLoad()
        {
            return CancelTokenSourceSafe(Interlocked.Exchange(ref _sampleLoadPreviewCts, null));
        }

        private static bool CancelTokenSourceSafe(CancellationTokenSource? cts)
        {
            if (cts == null)
            {
                return false;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
                // Ignore cancellation failure during disposal.
            }

            return true;
        }

        private void CancelSlotPreviewRender(LabelTemplateType slot)
        {
            CancellationTokenSource? existing;
            lock (_slotPreviewCts)
            {
                if (!_slotPreviewCts.TryGetValue(slot, out existing))
                {
                    return;
                }

                _slotPreviewCts.Remove(slot);
            }

            // Defer Dispose() to RenderSlotFreeTextPreviewAsync's finally block while renderers still hold the token.
            CancelTokenSourceSafe(existing);
        }

        private void CancelAllSlotPreviewRenders()
        {
            foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
            {
                NextSlotRenderVersion(slot);
                CancelSlotPreviewRender(slot);
            }
        }

        private static void SetPictureBoxImage(PictureBox? picBox, Image? newImage)
        {
            if (picBox == null || picBox.IsDisposed)
            {
                newImage?.Dispose();
                return;
            }

            Image? previous = picBox.Image;
            if (ReferenceEquals(previous, newImage))
            {
                return;
            }

            picBox.Image = newImage;
            previous?.Dispose();
        }

        private void ClearSlotPreviewImages()
        {
            CancelAllSlotPreviewRenders();
            SetPictureBoxImage(smallBoxLabelPreview, null);
            SetPictureBoxImage(magnetLabelPreview, null);
            SetPictureBoxImage(flaskLabelPreview, null);
        }

        private void ClearAllPreviews()
        {
            ClearSlotPreviewImages();
            _hasLoadedSample = false;
            _loadedSampleName = "";
            _loadedSamplePermId = "";
            _loadedSampleLabel = "";

            magnetFreeTextBox.Text = "";
            flaskFreeTextBox.Text = "";

            smallBoxLabelCheckBox.Checked = false;
            magnetLabelCheckBox.Checked = false;
            flaskLabelCheckBox.Checked = false;
        }

        private void SetSampleQueryControlsEnabled(bool enabled)
        {
            openbisObjectsComboBox.Enabled = enabled;
            loadPrintTemplates.Enabled = enabled;
            btnPrintLabel.Enabled = enabled;
        }

        private void SetLoadingFeedback(string message)
        {
            _loadingStatusMessage = message;
            prgLoading.Visible = true;
            lblStatusNotice.ForeColor = Color.FromArgb(29, 78, 216);
            lblStatusNotice.Text = message;
            lblStatusNotice.Visible = true;
            _statusToolTip.SetToolTip(lblStatusNotice, message);
        }

        private void ClearLoadingFeedback()
        {
            _loadingStatusMessage = null;
            prgLoading.Visible = false;
            ApplyPersistentNoticeVisuals();
        }

        private void ShowStatusNotice(string message, string? tooltipDetail = null)
        {
            _persistentNoticeMessage = message ?? "";
            _persistentNoticeTooltip = tooltipDetail;
            if (string.IsNullOrEmpty(_loadingStatusMessage))
            {
                ApplyPersistentNoticeVisuals();
            }
        }

        private void ClearStatusNotice()
        {
            _persistentNoticeMessage = "";
            _persistentNoticeTooltip = null;
            if (string.IsNullOrEmpty(_loadingStatusMessage))
            {
                ApplyPersistentNoticeVisuals();
            }
        }

        private void ApplyPersistentNoticeVisuals()
        {
            bool hasNotice = !string.IsNullOrWhiteSpace(_persistentNoticeMessage);
            lblStatusNotice.ForeColor = Color.FromArgb(180, 83, 9);
            lblStatusNotice.Text = _persistentNoticeMessage;
            lblStatusNotice.Visible = hasNotice;
            _statusToolTip.SetToolTip(
                lblStatusNotice,
                !hasNotice ? null : (string.IsNullOrWhiteSpace(_persistentNoticeTooltip) ? _persistentNoticeMessage : _persistentNoticeTooltip));
        }

        // =========================================================================
        // PRINT LABELS BUTTON CLICK (SEND SELECTED LABELS VIA b-PAC)
        // =========================================================================
        private async void btnPrintLabel_Click_1(object sender, EventArgs e)
        {
            if (!_hasLoadedSample)
            {
                MessageBox.Show("Please click 'Load Templates' first to load an openBIS object.", "Print Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selections = new List<PrintSlotSelection>();

            if (smallBoxLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.SmallBox12mm,
                    PrinterName = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _fallbackSmallBoxPrinter,
                    FreeText = ""
                });
            }

            if (magnetLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.Magnet12mm,
                    PrinterName = magnetPrinterComboBox.SelectedItem?.ToString() ?? _fallbackMagnetPrinter,
                    FreeText = _activeProfile == LabelTemplateProfile.Generic ? magnetFreeTextBox.Text : ""
                });
            }

            if (flaskLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.Flask24mm,
                    PrinterName = flaskPrinterComboBox.SelectedItem?.ToString() ?? _fallbackFlaskPrinter,
                    FreeText = _activeProfile == LabelTemplateProfile.Generic ? flaskFreeTextBox.Text : ""
                });
            }

            if (selections.Count == 0)
            {
                MessageBox.Show("Please select at least one label template to print (check the box at the top-right of the label).", "No Label Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string sampleSummary = string.IsNullOrWhiteSpace(_loadedSampleLabel)
                ? (openbisObjectsComboBox.SelectedItem?.ToString() ?? "")
                : _loadedSampleLabel;
            LabelTemplateProfile profileSnapshot = _activeProfile;

            SetSampleQueryControlsEnabled(false);
            SetLoadingFeedback($"Printing {selections.Count} label(s)...");

            try
            {
                LabelPrintBatchResult batchResult = await _backend.PrintSelectedLabelsAsync(selections);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                ClearLoadingFeedback();

                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(sampleSummary))
                {
                    sb.AppendLine($"Sample: {sampleSummary}");
                }
                sb.AppendLine($"Template Profile: {profileSnapshot}");
                sb.AppendLine();

                foreach (LabelPrintJobResult job in batchResult.Jobs)
                {
                    string statusText = job.Success
                        ? $"Sent to [{job.PrinterName}]"
                        : $"FAILED [{job.PrinterName}]: {job.Error}";
                    sb.AppendLine($"  • {job.LabelName}  ➔  {statusText}");
                }

                if (batchResult.Jobs.Count == 0 && !string.IsNullOrWhiteSpace(batchResult.Error))
                {
                    sb.AppendLine(batchResult.Error);
                }

                MessageBox.Show(
                    sb.ToString(),
                    batchResult.AllSucceeded ? "Print Complete" : "Print Error",
                    MessageBoxButtons.OK,
                    batchResult.AllSucceeded ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    ClearLoadingFeedback();
                    MessageBox.Show($"Unexpected error while printing labels: {ex.Message}", "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    ClearLoadingFeedback();
                    SetSampleQueryControlsEnabled(true);
                }
            }
        }

        private async void btnSettings_Click(object? sender, EventArgs e)
        {
            // Cancel any in-flight startup type refresh, type selection, or sample load so none
            // can open recovery UI over Settings or apply stale results.
            ++_typeRefreshRequestVersion;
            ++_typeSelectionRequestVersion;
            ++_sampleDetailsRequestVersion;
            CancelPendingTypeRefresh();
            bool cancelledTypeSelection = CancelPendingTypeSelection();
            bool cancelledSampleLoad = CancelPendingSampleLoad();

            if (cancelledSampleLoad)
            {
                ClearSlotPreviewImages();
            }

            if (cancelledTypeSelection || cancelledSampleLoad)
            {
                ClearLoadingFeedback();
                SetSampleQueryControlsEnabled(true);
            }

            string previousFingerprint = _credentialFingerprint;
            string previousTypeCode = _currentSelectedTypeCode;
            LabelTemplateProfile previousProfile = _activeProfile;

            using var settingsForm = new SetupSettingsForm(
                _backend,
                SetupDialogMode.Settings,
                SetupWizardStep.Connection);

            if (settingsForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ++_typeRefreshRequestVersion;
            WorkspaceStartupState startup = await _backend.InitializeWorkspaceAsync(_recoveryPrompter);
            if (IsDisposed || Disposing)
            {
                return;
            }

            _credentialFingerprint = startup.CredentialFingerprint ?? "";
            PopulateInstalledPrinters(startup.Printers);

            bool credentialsChanged = !string.Equals(previousFingerprint, _credentialFingerprint, StringComparison.Ordinal);

            TypeOptionDto? matchingType = !string.IsNullOrWhiteSpace(previousTypeCode)
                ? startup.ConfiguredTypes.FirstOrDefault(t => string.Equals(t.Code, previousTypeCode, StringComparison.OrdinalIgnoreCase))
                : null;

            if (credentialsChanged || matchingType == null)
            {
                ++_typeSelectionRequestVersion;
                ++_sampleDetailsRequestVersion;
                CancelPendingTypeSelection();
                CancelPendingSampleLoad();
                PopulateOpenBisTypeComboBox(startup.ConfiguredTypes, preferredTypeCode: null);
                openbisObjectsComboBox.Items.Clear();
                _backend.ClearActiveSample();
                ClearAllPreviews();
                UpdateFreeTextControlsState();
            }
            else
            {
                PopulateOpenBisTypeComboBox(startup.ConfiguredTypes, preferredTypeCode: previousTypeCode);
                LabelTemplateProfile updatedProfile = _activeProfile;
                UpdateFreeTextControlsState();

                if (_hasLoadedSample
                    && updatedProfile != previousProfile
                    && !string.IsNullOrWhiteSpace(_loadedSamplePermId))
                {
                    await LoadAndStreamSamplePreviewsAsync(previousTypeCode, _loadedSamplePermId, _loadedSampleName);
                }
            }

            if (settingsForm.TypesSyncedFromServer || credentialsChanged)
            {
                ClearStatusNotice();
            }
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {
        }
    }
}
