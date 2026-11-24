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
        private bool _isPopulatingObjects;
        private bool _isUpdatingSortControls;
        private bool _isSyncingEvapTemp;
        private string _lastCommittedEvapTemp = "";
        private string _lastCommittedMagnetFreeText = "";
        private string _lastCommittedFlaskFreeText = "";
        private IReadOnlyList<SampleSummaryDto> _loadedTypeSamples = Array.Empty<SampleSummaryDto>();
        private SampleSortField _currentSortField = SampleSortField.ModificationDate;
        private SampleSortDirection _currentSortDirection = SampleSortDirection.Descending;
        private readonly Dictionary<LabelTemplateType, long> _slotRenderVersions = new Dictionary<LabelTemplateType, long>();
        private readonly Dictionary<LabelTemplateType, CancellationTokenSource> _slotPreviewCts = new Dictionary<LabelTemplateType, CancellationTokenSource>();
        private CancellationTokenSource? _moleculeTempRenderCts;
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
        private bool _hasLayoutBaseline;
        private bool _isApplyingResponsiveLayout;
        private int _initialDpi = 96;
        private int _baseClientWidth = 1080;
        private int _baseClientHeight = 530;
        private int _baseCardGap = 25;
        private int _baseCardsToButtonsGap = 18;
        private int _baseButtonsBottomMargin = 22;
        private int _baseButtonGap = 40;
        private int _baseMinCardHeight = 296;

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
            DoubleBuffered = true;
            CaptureBaselineLayoutMetrics();
            UpdateResponsiveLayout();
            InitializeSortControls();
            RegisterClickOutsideFocusClear(this);
            UpdateTemplateInputControlsState();
            AppIconProvider.AttachWindowIcon(this, ref _windowIcon);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UpdateResponsiveLayout();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            MinimumSize = Size.Empty;
            base.OnDpiChanged(e);
            ApplyMinimumWindowSize();
            UpdateResponsiveLayout();
        }

        private void CaptureBaselineLayoutMetrics()
        {
            _initialDpi = Math.Max(96, DeviceDpi);
            _baseClientWidth = Math.Max(1, ClientSize.Width);
            _baseClientHeight = Math.Max(1, ClientSize.Height);
            _baseCardGap = Math.Max(4, magnetLabelGroupBox.Left - smallBoxLabelGroupBox.Right);
            _baseCardsToButtonsGap = Math.Max(4, loadPrintTemplates.Top - smallBoxLabelGroupBox.Bottom);
            _baseButtonsBottomMargin = Math.Max(4, _baseClientHeight - loadPrintTemplates.Bottom);
            _baseButtonGap = Math.Max(4, btnPrintLabel.Left - loadPrintTemplates.Right);
            _baseMinCardHeight = Math.Max(120, (int)Math.Round(smallBoxLabelGroupBox.Height * (296.0 / 326.0)));
            _hasLayoutBaseline = true;
            ApplyMinimumWindowSize();
        }

        private void ApplyMinimumWindowSize()
        {
            if (!_hasLayoutBaseline)
            {
                return;
            }

            double dpiRatio = (double)Math.Max(96, DeviceDpi) / _initialDpi;
            int minClientW = (int)Math.Round(_baseClientWidth * (900.0 / 1080.0) * dpiRatio);
            int minClientH = (int)Math.Round(_baseClientHeight * (500.0 / 530.0) * dpiRatio);
            MinimumSize = SizeFromClientSize(new Size(minClientW, minClientH));
        }

        private void UpdateResponsiveLayout()
        {
            if (!_hasLayoutBaseline || _isApplyingResponsiveLayout || IsDisposed)
            {
                return;
            }

            if (WindowState == FormWindowState.Minimized || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            double dpiRatio = (double)Math.Max(96, DeviceDpi) / _initialDpi;
            int cardGap = Math.Max(4, (int)Math.Round(_baseCardGap * dpiRatio));
            int cardsToButtonsGap = Math.Max(4, (int)Math.Round(_baseCardsToButtonsGap * dpiRatio));
            int buttonsBottomMargin = Math.Max(4, (int)Math.Round(_baseButtonsBottomMargin * dpiRatio));
            int buttonGap = Math.Max(4, (int)Math.Round(_baseButtonGap * dpiRatio));
            int minCardHeight = Math.Max(120, (int)Math.Round(_baseMinCardHeight * dpiRatio));

            _isApplyingResponsiveLayout = true;
            try
            {
                SuspendLayout();
                int buttonTop = Math.Max(0, ClientSize.Height - buttonsBottomMargin - loadPrintTemplates.Height);
                ResponsiveLayoutHelper.CenterHorizontalRow(
                    ClientSize.Width,
                    buttonTop,
                    buttonGap,
                    loadPrintTemplates,
                    btnPrintLabel,
                    btnSettings);

                int cardsLeft = openbisTypeLabel.Left;
                int cardsRight = Math.Max(cardsLeft + 30, openbisTypeComboBox.Right);
                int cardsTop = smallBoxLabelGroupBox.Top;
                int cardsHeight = Math.Max(minCardHeight, buttonTop - cardsToButtonsGap - cardsTop);

                ResponsiveLayoutHelper.LayoutEqualRow(
                    cardsLeft,
                    cardsRight,
                    cardsTop,
                    cardsHeight,
                    cardGap,
                    smallBoxLabelGroupBox,
                    magnetLabelGroupBox,
                    flaskLabelGroupBox);
            }
            finally
            {
                ResumeLayout(performLayout: false);
                _isApplyingResponsiveLayout = false;
            }
        }

        private sealed class SortFieldOption
        {
            public SortFieldOption(SampleSortField field, string displayText)
            {
                Field = field;
                DisplayText = displayText;
            }

            public SampleSortField Field { get; }
            public string DisplayText { get; }

            public override string ToString() => DisplayText;
        }

        private void InitializeSortControls()
        {
            _isUpdatingSortControls = true;
            try
            {
                sortByComboBox.Items.Clear();
                sortByComboBox.Items.Add(new SortFieldOption(SampleSortField.ModificationDate, "Modification Date"));
                sortByComboBox.Items.Add(new SortFieldOption(SampleSortField.Name, "Name"));
                sortByComboBox.Items.Add(new SortFieldOption(SampleSortField.PermId, "PermID"));
                sortByComboBox.SelectedIndex = 0;
                _currentSortField = SampleSortField.ModificationDate;
                _currentSortDirection = SampleSortDirection.Descending;
                UpdateSortDirectionButton();
            }
            finally
            {
                _isUpdatingSortControls = false;
            }
        }

        private void UpdateSortDirectionButton()
        {
            btnSortDirection.Text = _currentSortDirection == SampleSortDirection.Ascending ? "\u2191" : "\u2193";
            string tooltip = (_currentSortField, _currentSortDirection) switch
            {
                (SampleSortField.Name, SampleSortDirection.Ascending) => "Name (A to Z) — click to reverse",
                (SampleSortField.Name, SampleSortDirection.Descending) => "Name (Z to A) — click to reverse",
                (SampleSortField.PermId, SampleSortDirection.Ascending) => "PermID (Oldest first) — click to reverse",
                (SampleSortField.PermId, SampleSortDirection.Descending) => "PermID (Newest first) — click to reverse",
                (_, SampleSortDirection.Ascending) => "Modification Date (Oldest first) — click to reverse",
                _ => "Modification Date (Newest first) — click to reverse"
            };
            _statusToolTip.SetToolTip(btnSortDirection, tooltip);
        }

        private void ApplySortedObjects(bool preserveSelection)
        {
            string? selectedPermId = preserveSelection && openbisObjectsComboBox.SelectedItem is SampleSummaryDto current
                ? current.PermId
                : null;

            IReadOnlyList<SampleSummaryDto> sorted = _backend.SortSamples(
                _loadedTypeSamples,
                _currentSortField,
                _currentSortDirection);

            openbisObjectsComboBox.BeginUpdate();
            _isPopulatingObjects = true;
            try
            {
                openbisObjectsComboBox.Items.Clear();
                int restoredIndex = -1;
                foreach (SampleSummaryDto sample in sorted)
                {
                    if (sample == null)
                    {
                        continue;
                    }

                    int index = openbisObjectsComboBox.Items.Add(sample);
                    if (restoredIndex < 0
                        && !string.IsNullOrEmpty(selectedPermId)
                        && string.Equals(sample.PermId, selectedPermId, StringComparison.OrdinalIgnoreCase))
                    {
                        restoredIndex = index;
                    }
                }

                if (openbisObjectsComboBox.Items.Count > 0)
                {
                    openbisObjectsComboBox.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
                }
            }
            finally
            {
                _isPopulatingObjects = false;
                openbisObjectsComboBox.EndUpdate();
            }

            if (_hasLoadedSample)
            {
                string currentPermId = (openbisObjectsComboBox.SelectedItem as SampleSummaryDto)?.PermId ?? "";
                if (!string.Equals(currentPermId, _loadedSamplePermId, StringComparison.OrdinalIgnoreCase))
                {
                    InvalidateLoadedSampleState();
                }
            }
        }

        private void openbisObjectsComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isPopulatingObjects)
            {
                return;
            }

            string selectedPermId = (openbisObjectsComboBox.SelectedItem as SampleSummaryDto)?.PermId ?? "";
            if (_hasLoadedSample
                && !string.Equals(selectedPermId, _loadedSamplePermId, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateLoadedSampleState();
            }
        }

        private void InvalidateLoadedSampleState()
        {
            ++_sampleDetailsRequestVersion;
            CancelPendingSampleLoad();
            _backend.ClearActiveSample();
            ClearAllPreviews();
        }

        private void sortByComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingSortControls || sortByComboBox.SelectedItem is not SortFieldOption option)
            {
                return;
            }

            _currentSortField = option.Field;
            _currentSortDirection = _currentSortField == SampleSortField.Name
                ? SampleSortDirection.Ascending
                : SampleSortDirection.Descending;
            UpdateSortDirectionButton();
            ApplySortedObjects(preserveSelection: true);
        }

        private void btnSortDirection_Click(object? sender, EventArgs e)
        {
            if (_isUpdatingSortControls)
            {
                return;
            }

            _currentSortDirection = _currentSortDirection == SampleSortDirection.Ascending
                ? SampleSortDirection.Descending
                : SampleSortDirection.Ascending;
            UpdateSortDirectionButton();
            ApplySortedObjects(preserveSelection: true);
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
            UpdateTemplateInputControlsState();

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
                    UpdateTemplateInputControlsState();
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

        private void UpdateTemplateInputControlsState()
        {
            bool hasSelection = !string.IsNullOrEmpty(_currentSelectedTypeCode);
            bool showEvapTemp = hasSelection && _activeProfile == LabelTemplateProfile.Molecule;
            bool showFreeText = hasSelection && _activeProfile == LabelTemplateProfile.Generic;

            smallBoxEvapTempLabel.Visible = showEvapTemp;
            smallBoxEvapTempLabel.Enabled = showEvapTemp;
            smallBoxEvapTempBox.Visible = showEvapTemp;
            smallBoxEvapTempBox.Enabled = showEvapTemp;

            magnetEvapTempLabel.Visible = showEvapTemp;
            magnetEvapTempLabel.Enabled = showEvapTemp;
            magnetEvapTempBox.Visible = showEvapTemp;
            magnetEvapTempBox.Enabled = showEvapTemp;

            magnetFreeTextLabel.Visible = showFreeText;
            magnetFreeTextLabel.Enabled = showFreeText;
            magnetFreeTextBox.Visible = showFreeText;
            magnetFreeTextBox.Enabled = showFreeText;

            flaskFreeTextLabel.Visible = showFreeText;
            flaskFreeTextLabel.Enabled = showFreeText;
            flaskFreeTextBox.Visible = showFreeText;
            flaskFreeTextBox.Enabled = showFreeText;

            smallBoxEvapTempBox.PlaceholderText = showEvapTemp ? "e.g. 250°C" : "";
            magnetEvapTempBox.PlaceholderText = showEvapTemp ? "e.g. 250°C" : "";
            magnetFreeTextBox.PlaceholderText = showFreeText ? "Free text placeholder" : "";
            flaskFreeTextBox.PlaceholderText = showFreeText ? "Free text placeholder" : "";
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
                _loadedTypeSamples = Array.Empty<SampleSummaryDto>();
                openbisObjectsComboBox.Items.Clear();
                _currentSelectedTypeCode = "";
                _activeProfile = LabelTemplateProfile.Generic;
                _hasLoadedSample = false;
                _loadedSampleName = "";
                _loadedSamplePermId = "";
                _loadedSampleLabel = "";
                _backend.ClearActiveSample();
                ClearAllPreviews();
                UpdateTemplateInputControlsState();
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
            _loadedTypeSamples = Array.Empty<SampleSummaryDto>();
            openbisObjectsComboBox.Items.Clear();
            ClearAllPreviews();
            UpdateTemplateInputControlsState();

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
                    _loadedTypeSamples = result.Samples ?? Array.Empty<SampleSummaryDto>();
                    ApplySortedObjects(preserveSelection: false);
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
            ClearEvapTempControls();
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

                UpdateTemplateInputControlsState();
                if (_activeProfile == LabelTemplateProfile.Molecule)
                {
                    ApplyEvapTempBundleValues(
                        bundle.EvaporationTemperature ?? "",
                        bundle.EvaporationTemperatureRaw ?? "");
                    _lastCommittedMagnetFreeText = "";
                    _lastCommittedFlaskFreeText = "";
                }
                else
                {
                    ClearEvapTempControls();
                    _lastCommittedMagnetFreeText = magnetFreeTextBox.Text ?? "";
                    _lastCommittedFlaskFreeText = flaskFreeTextBox.Text ?? "";
                }

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
        // DYNAMIC COMMIT HANDLERS (CLICK OUTSIDE OR ENTER)
        // =========================================================================
        private void RegisterClickOutsideFocusClear(Control root)
        {
            if (root is TextBoxBase)
            {
                return;
            }

            if (root is Form or GroupBox or Label or PictureBox or ProgressBar)
            {
                root.MouseDown += NonInputControl_MouseDown;
            }

            foreach (Control child in root.Controls)
            {
                RegisterClickOutsideFocusClear(child);
            }
        }

        private void NonInputControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (ActiveControl is TextBoxBase activeTextBox)
            {
                ActiveControl = null;
                CommitTextBoxEditIfChanged(activeTextBox);
            }
        }

        private void smallBoxEvapTempBox_TextChanged(object? sender, EventArgs e)
        {
            SyncEvapTempText(smallBoxEvapTempBox, magnetEvapTempBox);
        }

        private void magnetEvapTempBox_TextChanged(object? sender, EventArgs e)
        {
            SyncEvapTempText(magnetEvapTempBox, smallBoxEvapTempBox);
        }

        private void SyncEvapTempText(TextBox source, TextBox target)
        {
            if (_isSyncingEvapTemp || string.Equals(source.Text, target.Text, StringComparison.Ordinal))
            {
                return;
            }

            _isSyncingEvapTemp = true;
            try
            {
                target.Text = source.Text;
            }
            finally
            {
                _isSyncingEvapTemp = false;
            }
        }

        private void smallBoxEvapTempBox_Leave(object? sender, EventArgs e)
        {
            CommitTextBoxEditIfChanged(smallBoxEvapTempBox);
        }

        private void smallBoxEvapTempBox_KeyDown(object? sender, KeyEventArgs e)
        {
            CommitOnEnterKey(sender, e);
        }

        private void magnetEvapTempBox_Leave(object? sender, EventArgs e)
        {
            CommitTextBoxEditIfChanged(magnetEvapTempBox);
        }

        private void magnetEvapTempBox_KeyDown(object? sender, KeyEventArgs e)
        {
            CommitOnEnterKey(sender, e);
        }

        private void magnetFreeTextBox_Leave(object? sender, EventArgs e)
        {
            CommitTextBoxEditIfChanged(magnetFreeTextBox);
        }

        private void magnetFreeTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            CommitOnEnterKey(sender, e);
        }

        private void flaskFreeTextBox_Leave(object? sender, EventArgs e)
        {
            CommitTextBoxEditIfChanged(flaskFreeTextBox);
        }

        private void flaskFreeTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            CommitOnEnterKey(sender, e);
        }

        private void CommitOnEnterKey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            ActiveControl = null;
            if (sender is TextBoxBase box)
            {
                CommitTextBoxEditIfChanged(box);
            }
        }

        private void CommitTextBoxEditIfChanged(TextBoxBase textBox)
        {
            if (!_hasLoadedSample)
            {
                return;
            }

            if ((ReferenceEquals(textBox, smallBoxEvapTempBox) || ReferenceEquals(textBox, magnetEvapTempBox))
                && _activeProfile == LabelTemplateProfile.Molecule)
            {
                string currentTemp = smallBoxEvapTempBox.Text ?? "";
                if (string.Equals(currentTemp, _lastCommittedEvapTemp, StringComparison.Ordinal))
                {
                    return;
                }

                _lastCommittedEvapTemp = currentTemp;
                _ = RenderMoleculeTemperaturePreviewsAsync(currentTemp);
                return;
            }

            if (ReferenceEquals(textBox, magnetFreeTextBox) && _activeProfile == LabelTemplateProfile.Generic)
            {
                string currentFreeText = magnetFreeTextBox.Text ?? "";
                if (string.Equals(currentFreeText, _lastCommittedMagnetFreeText, StringComparison.Ordinal))
                {
                    return;
                }

                _lastCommittedMagnetFreeText = currentFreeText;
                _ = RenderSlotFreeTextPreviewAsync(
                    magnetLabelPreview,
                    LabelTemplateType.Magnet12mm,
                    currentFreeText);
                return;
            }

            if (ReferenceEquals(textBox, flaskFreeTextBox) && _activeProfile == LabelTemplateProfile.Generic)
            {
                string currentFreeText = flaskFreeTextBox.Text ?? "";
                if (string.Equals(currentFreeText, _lastCommittedFlaskFreeText, StringComparison.Ordinal))
                {
                    return;
                }

                _lastCommittedFlaskFreeText = currentFreeText;
                _ = RenderSlotFreeTextPreviewAsync(
                    flaskLabelPreview,
                    LabelTemplateType.Flask24mm,
                    currentFreeText);
            }
        }

        private void ApplyEvapTempBundleValues(string displayedTemp, string rawTemp)
        {
            _isSyncingEvapTemp = true;
            try
            {
                smallBoxEvapTempBox.Text = displayedTemp;
                magnetEvapTempBox.Text = displayedTemp;
            }
            finally
            {
                _isSyncingEvapTemp = false;
            }

            _lastCommittedEvapTemp = displayedTemp;

            string tooltip;
            if (string.IsNullOrWhiteSpace(rawTemp))
            {
                tooltip = "Overrides the printed evaporation temperature locally (does not modify openBIS).";
            }
            else if (rawTemp.Trim().Length > 10 && displayedTemp.Length <= 10 && !string.Equals(rawTemp, displayedTemp, StringComparison.Ordinal))
            {
                tooltip = $"openBIS value: {rawTemp} (trimmed to 10 chars; edit here to override printout)";
            }
            else if (!string.Equals(rawTemp, displayedTemp, StringComparison.Ordinal))
            {
                tooltip = $"openBIS value: {rawTemp} (normalized to {displayedTemp}; edit here to override printout)";
            }
            else
            {
                tooltip = $"openBIS value: {rawTemp} (edit here to override printout)";
            }

            _statusToolTip.SetToolTip(smallBoxEvapTempBox, tooltip);
            _statusToolTip.SetToolTip(magnetEvapTempBox, tooltip);
        }

        private void ClearEvapTempControls()
        {
            _isSyncingEvapTemp = true;
            try
            {
                smallBoxEvapTempBox.Text = "";
                magnetEvapTempBox.Text = "";
            }
            finally
            {
                _isSyncingEvapTemp = false;
            }

            _lastCommittedEvapTemp = "";
            _statusToolTip.SetToolTip(smallBoxEvapTempBox, null);
            _statusToolTip.SetToolTip(magnetEvapTempBox, null);
        }

        private async Task RenderMoleculeTemperaturePreviewsAsync(string evaporationTemperature)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            long boxVersion = NextSlotRenderVersion(LabelTemplateType.SmallBox12mm);
            long magnetVersion = NextSlotRenderVersion(LabelTemplateType.Magnet12mm);
            CancelSlotPreviewRender(LabelTemplateType.SmallBox12mm);
            CancelSlotPreviewRender(LabelTemplateType.Magnet12mm);
            CancelMoleculeTemperatureRender();

            var cts = new CancellationTokenSource();
            _moleculeTempRenderCts = cts;
            CancellationToken cancellationToken = cts.Token;

            SetPictureBoxImage(
                smallBoxLabelPreview,
                DecodeSlotPreview(
                    LabelTemplateType.SmallBox12mm,
                    LabelTemplateProfile.Molecule,
                    _backend.GetLoadingPreviewPng(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule)));
            SetPictureBoxImage(
                magnetLabelPreview,
                DecodeSlotPreview(
                    LabelTemplateType.Magnet12mm,
                    LabelTemplateProfile.Molecule,
                    _backend.GetLoadingPreviewPng(LabelTemplateType.Magnet12mm, LabelTemplateProfile.Molecule)));

            try
            {
                IReadOnlyDictionary<LabelTemplateType, byte[]> previews =
                    await _backend.RenderMoleculeEvaporationTemperatureAsync(evaporationTemperature, cancellationToken);

                if (IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (IsCurrentSlotVersion(LabelTemplateType.SmallBox12mm, boxVersion)
                    && previews.TryGetValue(LabelTemplateType.SmallBox12mm, out byte[]? boxPng))
                {
                    SetPictureBoxImage(
                        smallBoxLabelPreview,
                        DecodeSlotPreview(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule, boxPng));
                }

                if (IsCurrentSlotVersion(LabelTemplateType.Magnet12mm, magnetVersion)
                    && previews.TryGetValue(LabelTemplateType.Magnet12mm, out byte[]? magnetPng))
                {
                    SetPictureBoxImage(
                        magnetLabelPreview,
                        DecodeSlotPreview(LabelTemplateType.Magnet12mm, LabelTemplateProfile.Molecule, magnetPng));
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored when a newer render or ClearAllPreviews supersedes this request.
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    if (IsCurrentSlotVersion(LabelTemplateType.SmallBox12mm, boxVersion))
                    {
                        SetPictureBoxImage(
                            smallBoxLabelPreview,
                            DecodeSlotPreview(
                                LabelTemplateType.SmallBox12mm,
                                LabelTemplateProfile.Molecule,
                                _backend.GetFallbackPreviewPng(
                                    LabelTemplateType.SmallBox12mm,
                                    LabelTemplateProfile.Molecule,
                                    $"Preview unavailable: {ex.Message}")));
                    }

                    if (IsCurrentSlotVersion(LabelTemplateType.Magnet12mm, magnetVersion))
                    {
                        SetPictureBoxImage(
                            magnetLabelPreview,
                            DecodeSlotPreview(
                                LabelTemplateType.Magnet12mm,
                                LabelTemplateProfile.Molecule,
                                _backend.GetFallbackPreviewPng(
                                    LabelTemplateType.Magnet12mm,
                                    LabelTemplateProfile.Molecule,
                                    $"Preview unavailable: {ex.Message}")));
                    }
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _moleculeTempRenderCts, null, cts);
                cts.Dispose();
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

        private bool CancelMoleculeTemperatureRender()
        {
            return CancelTokenSourceSafe(Interlocked.Exchange(ref _moleculeTempRenderCts, null));
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
            CancelMoleculeTemperatureRender();
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

            ClearEvapTempControls();
            magnetFreeTextBox.Text = "";
            flaskFreeTextBox.Text = "";
            _lastCommittedMagnetFreeText = "";
            _lastCommittedFlaskFreeText = "";

            smallBoxLabelCheckBox.Checked = false;
            magnetLabelCheckBox.Checked = false;
            flaskLabelCheckBox.Checked = false;
        }

        private void SetSampleQueryControlsEnabled(bool enabled)
        {
            openbisObjectsComboBox.Enabled = enabled;
            sortByComboBox.Enabled = enabled;
            btnSortDirection.Enabled = enabled;
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
            string? moleculeTempOverride = _activeProfile == LabelTemplateProfile.Molecule
                ? smallBoxEvapTempBox.Text
                : null;

            if (smallBoxLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.SmallBox12mm,
                    PrinterName = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _fallbackSmallBoxPrinter,
                    FreeText = "",
                    EvaporationTemperature = moleculeTempOverride
                });
            }

            if (magnetLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.Magnet12mm,
                    PrinterName = magnetPrinterComboBox.SelectedItem?.ToString() ?? _fallbackMagnetPrinter,
                    FreeText = _activeProfile == LabelTemplateProfile.Generic ? magnetFreeTextBox.Text : "",
                    EvaporationTemperature = moleculeTempOverride
                });
            }

            if (flaskLabelCheckBox.Checked)
            {
                selections.Add(new PrintSlotSelection
                {
                    Slot = LabelTemplateType.Flask24mm,
                    PrinterName = flaskPrinterComboBox.SelectedItem?.ToString() ?? _fallbackFlaskPrinter,
                    FreeText = _activeProfile == LabelTemplateProfile.Generic ? flaskFreeTextBox.Text : "",
                    EvaporationTemperature = moleculeTempOverride
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
                ClearEvapTempControls();
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
                _loadedTypeSamples = Array.Empty<SampleSummaryDto>();
                openbisObjectsComboBox.Items.Clear();
                _backend.ClearActiveSample();
                ClearAllPreviews();
                UpdateTemplateInputControlsState();
            }
            else
            {
                PopulateOpenBisTypeComboBox(startup.ConfiguredTypes, preferredTypeCode: previousTypeCode);
                LabelTemplateProfile updatedProfile = _activeProfile;
                UpdateTemplateInputControlsState();

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
