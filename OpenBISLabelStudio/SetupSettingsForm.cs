using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenBISLabelStudio.Backend.Contracts;
using OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio
{
    /// <summary>
    /// Selects the operating mode of <see cref="SetupSettingsForm"/>.
    /// </summary>
    public enum SetupDialogMode
    {
        Wizard,
        ExpiredTokenRecovery,
        Settings
    }

    /// <summary>
    /// Unified modal dialog for first-run configuration wizard, expired-token recovery,
    /// and ongoing Settings management (openBIS connection, object type mapping, and printer routing).
    /// </summary>
    public partial class SetupSettingsForm : Form
    {
        private const string StyleMoleculeDisplay = "Molecule / Substance";
        private const string StyleGenericDisplay = "Generic / Free-text";

        private readonly ILabelStudioBackend _backend;
        private readonly string? _initialWarning;

        private readonly List<ObjectTypeRowDto> _masterTypeRows = new List<ObjectTypeRowDto>();
        private readonly Dictionary<(LabelTemplateProfile Profile, LabelTemplateType Slot), byte[]> _previewPngCache
            = new Dictionary<(LabelTemplateProfile Profile, LabelTemplateType Slot), byte[]>();
        private readonly Font _tabRegularFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _tabBoldFont = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _previewRegularFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _previewBoldFont = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);

        private SetupWizardStep _currentStep;
        private LabelTemplateProfile _activePreviewProfile = LabelTemplateProfile.Molecule;
        private CancellationTokenSource? _previewRenderCts;
        private long _previewRenderVersion;
        private long _typesFetchVersion;
        private bool _isInitializingInputs;
        private bool _isPopulatingGrid;
        private bool _isBusy;
        private bool _isLoadingTypes;
        private bool _hasVisitedTypesPage;
        private bool _hasStartedPage2Previews;
        private bool _cameFromPrintersStep;
        private bool _autoFetchAttemptedForCurrentInputs;
        private bool _hasValidConfiguredTypes;
        private bool _hasPrinterConfig;
        private bool _resourcesDisposed;
        private bool _fontsDisposed;
        private string _validatedUrl = "";
        private string _validatedToken = "";
        private string _fallbackSmallBoxPrinter = "Brother PT-P700";
        private string _fallbackMagnetPrinter = "Brother PT-P700 (Kopie 1)";
        private string _fallbackFlaskPrinter = "Brother PT-P700 (Kopie 2)";
        private Icon? _windowIcon;

        /// <summary>
        /// Initializes the unified setup and settings dialog for the specified <paramref name="mode"/>.
        /// </summary>
        public SetupSettingsForm(
            ILabelStudioBackend backend,
            SetupDialogMode mode,
            SetupWizardStep startStep = SetupWizardStep.Connection,
            string? initialWarning = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            Mode = mode;
            _initialWarning = initialWarning;

            InitializeComponent();
            AppIconProvider.AttachWindowIcon(this, ref _windowIcon);
            InitializeFromConfig(startStep);
        }

        /// <summary>
        /// Gets the active dialog mode.
        /// </summary>
        public SetupDialogMode Mode { get; }

        /// <summary>
        /// Gets whether server object types were synced or pre-seeded during this dialog session.
        /// </summary>
        public bool TypesSyncedFromServer { get; private set; }

        /// <summary>
        /// Replaces the Object Types grid with <paramref name="rows"/> while preserving selection and scroll state.
        /// </summary>
        public void ApplyTypeRows(IReadOnlyList<ObjectTypeRowDto> rows, bool syncedFromServer = true)
        {
            ArgumentNullException.ThrowIfNull(rows);

            CommitGridEdits();
            SyncVisibleGridToMaster();

            TypesSyncedFromServer = syncedFromServer;

            string? selectedRowCode = (dgvObjectTypes.CurrentRow?.Tag as ObjectTypeRowDto)?.Code;
            int firstDisplayedRow = dgvObjectTypes.FirstDisplayedScrollingRowIndex;

            _masterTypeRows.Clear();
            foreach (ObjectTypeRowDto row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Code))
                {
                    continue;
                }

                string cleanCode = row.Code.Trim();
                _masterTypeRows.Add(new ObjectTypeRowDto
                {
                    IsSelected = row.IsSelected,
                    Code = cleanCode,
                    DisplayName = string.IsNullOrWhiteSpace(row.DisplayName) ? cleanCode : row.DisplayName.Trim(),
                    TemplateProfile = Enum.IsDefined(typeof(LabelTemplateProfile), row.TemplateProfile)
                        ? row.TemplateProfile
                        : GetDefaultProfileForCode(cleanCode)
                });
            }

            RefreshObjectTypeGrid(selectedRowCode, firstDisplayedRow);
            if (_currentStep == SetupWizardStep.ObjectTypes && IsHandleCreated)
            {
                if (!_hasStartedPage2Previews)
                {
                    ShowExamplePreviews(GetSelectedOrDefaultPreviewProfile());
                }
            }
            else if (!_hasStartedPage2Previews)
            {
                _activePreviewProfile = GetSelectedOrDefaultPreviewProfile();
                UpdatePreviewToggleVisuals();
            }
        }

        /// <summary>
        /// Validates local settings constraints and atomically writes all configuration
        /// files via the backend without requiring openBIS network access.
        /// </summary>
        public bool TrySaveSettingsOffline(out string validationError)
        {
            CommitGridEdits();
            SyncVisibleGridToMaster();

            OperationResult result = _backend.SaveSettingsOfflineAsync(new SettingsSaveRequest
            {
                OpenBisUrl = (txtInstanceUrl.Text ?? "").Trim(),
                Token = (txtToken.Text ?? "").Trim(),
                ObjectTypes = GetMasterTypeRowsSnapshot(),
                SmallBoxPrinter = GetSelectedPrinterName(cmbSmallBoxPrinter, _fallbackSmallBoxPrinter),
                MagnetPrinter = GetSelectedPrinterName(cmbMagnetPrinter, _fallbackMagnetPrinter),
                FlaskPrinter = GetSelectedPrinterName(cmbFlaskPrinter, _fallbackFlaskPrinter)
            }).GetAwaiter().GetResult();

            validationError = result.Success
                ? ""
                : (string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Could not save configuration files." : result.ErrorMessage);
            return result.Success;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _backend.WarmUpInBackground();

            if (_currentStep == SetupWizardStep.ObjectTypes && !_hasStartedPage2Previews)
            {
                ShowExamplePreviews(_activePreviewProfile);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                txtToken.Focus();
                txtToken.SelectAll();
                return;
            }

            if (_currentStep == SetupWizardStep.ObjectTypes)
            {
                if (!_hasStartedPage2Previews)
                {
                    ShowExamplePreviews(_activePreviewProfile);
                }

                if (!TypesSyncedFromServer && !_autoFetchAttemptedForCurrentInputs)
                {
                    _ = LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
                }
            }
        }

        partial void DisposePreviewCache()
        {
            if (_resourcesDisposed)
            {
                return;
            }

            _resourcesDisposed = true;
            CancelPendingPreviewRender();

            AssignPreviewPictureBoxImage(picPreviewSmallBox, null);
            AssignPreviewPictureBoxImage(picPreviewMagnet, null);
            AssignPreviewPictureBoxImage(picPreviewFlask, null);

            _previewPngCache.Clear();
            AppIconProvider.DetachWindowIcon(this, ref _windowIcon);
        }

        partial void DisposeOwnedFonts()
        {
            if (_fontsDisposed)
            {
                return;
            }

            _fontsDisposed = true;
            _tabRegularFont.Dispose();
            _tabBoldFont.Dispose();
            _previewRegularFont.Dispose();
            _previewBoldFont.Dispose();
        }

        private void InitializeFromConfig(SetupWizardStep requestedStartStep)
        {
            SettingsSnapshotDto snapshot = _backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
            _hasValidConfiguredTypes = snapshot.HasValidConfiguredTypes;
            _hasPrinterConfig = snapshot.HasPrinterConfig;

            _isInitializingInputs = true;
            try
            {
                txtInstanceUrl.Text = snapshot.OpenBisUrl ?? "";
                txtToken.Text = Mode == SetupDialogMode.ExpiredTokenRecovery
                    ? ""
                    : (snapshot.Token ?? "");

                _masterTypeRows.Clear();
                foreach (ObjectTypeRowDto savedType in snapshot.ConfiguredTypes)
                {
                    if (savedType == null || string.IsNullOrWhiteSpace(savedType.Code))
                    {
                        continue;
                    }

                    string cleanCode = savedType.Code.Trim();
                    _masterTypeRows.Add(new ObjectTypeRowDto
                    {
                        IsSelected = savedType.IsSelected,
                        Code = cleanCode,
                        DisplayName = string.IsNullOrWhiteSpace(savedType.DisplayName) ? cleanCode : savedType.DisplayName.Trim(),
                        TemplateProfile = Enum.IsDefined(typeof(LabelTemplateProfile), savedType.TemplateProfile)
                            ? savedType.TemplateProfile
                            : GetDefaultProfileForCode(cleanCode)
                    });
                }

                RefreshObjectTypeGrid();
                PopulatePrinterCombos(snapshot.Printers);
            }
            finally
            {
                _isInitializingInputs = false;
            }

            SetupWizardStep initialStep = Mode == SetupDialogMode.ExpiredTokenRecovery
                ? SetupWizardStep.Connection
                : (requestedStartStep == SetupWizardStep.None
                    ? SetupWizardStep.Connection
                    : requestedStartStep);

            ApplyModeChrome();
            ShowStep(initialStep);

            if (Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                string warning = !string.IsNullOrWhiteSpace(_initialWarning)
                    ? _initialWarning.Trim()
                    : "Your openBIS personal access token has expired or is invalid. Please enter a new token.";
                SetConnectionStatus(warning, StatusSeverity.Warning);
                ActiveControl = txtToken;
            }
            else if (!string.IsNullOrWhiteSpace(_initialWarning))
            {
                SetConnectionStatus(_initialWarning.Trim(), StatusSeverity.Warning);
            }
        }

        private void ApplyModeChrome()
        {
            switch (Mode)
            {
                case SetupDialogMode.Wizard:
                    Text = "First-Run Setup — openBIS Label Studio";
                    pnlWizardHeader.Visible = true;
                    pnlSettingsTabs.Visible = false;
                    btnBack.Visible = true;
                    btnCancel.Text = "Cancel";
                    break;

                case SetupDialogMode.ExpiredTokenRecovery:
                    Text = "openBIS Token Expired — Update Credentials";
                    pnlWizardHeader.Visible = true;
                    pnlSettingsTabs.Visible = false;
                    lblWizardTitle.Text = "openBIS Session Expired";
                    lblWizardSubtitle.Text = "Your saved personal access token is no longer valid. Enter a new token to continue.";
                    btnBack.Visible = false;
                    btnNextFinish.Text = "Save Token";
                    btnCancel.Text = "Cancel";
                    break;

                case SetupDialogMode.Settings:
                    Text = "Settings — openBIS Label Studio";
                    pnlWizardHeader.Visible = false;
                    pnlSettingsTabs.Visible = true;
                    btnBack.Visible = false;
                    btnNextFinish.Text = "Save";
                    btnCancel.Text = "Cancel";
                    break;
            }
        }

        private void ShowStep(SetupWizardStep step)
        {
            if (Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                step = SetupWizardStep.Connection;
            }
            else if (step == SetupWizardStep.None)
            {
                step = SetupWizardStep.Connection;
            }

            if (_currentStep == SetupWizardStep.ObjectTypes && step != SetupWizardStep.ObjectTypes)
            {
                _hasStartedPage2Previews = false;
                CancelPendingPreviewRender();
                CommitGridEdits();
                SyncVisibleGridToMaster();
            }

            _currentStep = step;
            ClearFooterError();

            pnlConnection.Visible = step == SetupWizardStep.Connection;
            pnlObjectTypes.Visible = step == SetupWizardStep.ObjectTypes;
            pnlPrinters.Visible = step == SetupWizardStep.Printers;

            if (Mode == SetupDialogMode.Wizard)
            {
                UpdateWizardStepChrome(step);
            }
            else if (Mode == SetupDialogMode.Settings)
            {
                UpdateSettingsTabSelection(step);
            }

            if (step == SetupWizardStep.ObjectTypes)
            {
                _hasVisitedTypesPage = true;
                LabelTemplateProfile initialProfile = GetSelectedOrDefaultPreviewProfile();
                if (IsHandleCreated)
                {
                    ShowExamplePreviews(initialProfile);
                }
                else
                {
                    _activePreviewProfile = initialProfile;
                    UpdatePreviewToggleVisuals();
                }
            }
            else if (step == SetupWizardStep.Connection && Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                ActiveControl = txtToken;
            }
        }

        private void UpdateWizardStepChrome(SetupWizardStep step)
        {
            switch (step)
            {
                case SetupWizardStep.Connection:
                    lblWizardTitle.Text = "Step 1 of 3 — Connect to openBIS";
                    lblWizardSubtitle.Text = "Enter your openBIS server URL and personal access token.";
                    btnBack.Enabled = false;
                    btnNextFinish.Text = (_hasValidConfiguredTypes && _hasPrinterConfig)
                        ? "Finish"
                        : "Next >";
                    break;

                case SetupWizardStep.ObjectTypes:
                    lblWizardTitle.Text = "Step 2 of 3 — Select Object Types & Label Templates";
                    lblWizardSubtitle.Text = "Choose which object types to enable and preview their label layouts.";
                    btnBack.Enabled = true;
                    btnNextFinish.Text = (!_hasPrinterConfig || _cameFromPrintersStep)
                        ? "Next >"
                        : "Finish";
                    break;

                case SetupWizardStep.Printers:
                    lblWizardTitle.Text = "Step 3 of 3 — Configure Printer Routing";
                    lblWizardSubtitle.Text = "Select the Windows printer queue for each label slot.";
                    btnBack.Enabled = true;
                    btnNextFinish.Text = "Finish";
                    break;
            }
        }

        private void UpdateSettingsTabSelection(SetupWizardStep activeStep)
        {
            ApplyTabVisual(btnTabConnection, activeStep == SetupWizardStep.Connection);
            ApplyTabVisual(btnTabObjectTypes, activeStep == SetupWizardStep.ObjectTypes);
            ApplyTabVisual(btnTabPrinters, activeStep == SetupWizardStep.Printers);
        }

        private void ApplyTabVisual(Button tabButton, bool isActive)
        {
            tabButton.BackColor = isActive ? Color.White : Color.FromArgb(232, 236, 242);
            tabButton.ForeColor = isActive ? Color.FromArgb(24, 34, 48) : Color.FromArgb(80, 90, 105);
            tabButton.Font = isActive ? _tabBoldFont : _tabRegularFont;
            tabButton.FlatAppearance.BorderColor = isActive ? Color.FromArgb(37, 99, 235) : Color.FromArgb(203, 213, 225);
            tabButton.FlatAppearance.BorderSize = isActive ? 2 : 1;
        }

        private void PopulatePrinterCombos(PrinterDropdownState? printersState)
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

            if (!printers.HasInstalledBrotherPrinters)
            {
                lblNoPrintersWarning.Visible = true;
                lblNoPrintersWarning.Text = "No Brother PT-P700 printers detected — saved/default Brother PT-P700 printer names will be kept.";
            }
            else
            {
                lblNoPrintersWarning.Visible = false;
            }

            PopulateSinglePrinterCombo(cmbSmallBoxPrinter, printers.AvailablePrinters, printers.SmallBoxSelectedIndex, _fallbackSmallBoxPrinter);
            PopulateSinglePrinterCombo(cmbMagnetPrinter, printers.AvailablePrinters, printers.MagnetSelectedIndex, _fallbackMagnetPrinter);
            PopulateSinglePrinterCombo(cmbFlaskPrinter, printers.AvailablePrinters, printers.FlaskSelectedIndex, _fallbackFlaskPrinter);
        }

        private static void PopulateSinglePrinterCombo(
            ComboBox comboBox,
            IReadOnlyList<string>? items,
            int preferredIndex,
            string preferredName)
        {
            comboBox.Items.Clear();
            if (items != null)
            {
                foreach (string item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        comboBox.Items.Add(item);
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

        private static string GetSelectedPrinterName(ComboBox comboBox, string fallbackDefault)
        {
            string selected = (comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? "").Trim();
            return string.IsNullOrWhiteSpace(selected) ? fallbackDefault : selected;
        }

        private void RefreshObjectTypeGrid(string? preferredRowCode = null, int preferredFirstDisplayedRow = -1)
        {
            _isPopulatingGrid = true;
            try
            {
                dgvObjectTypes.Rows.Clear();
                string filter = (txtTypeFilter.Text ?? "").Trim();
                int targetRowIndex = -1;

                foreach (ObjectTypeRowDto entry in _masterTypeRows)
                {
                    if (!string.IsNullOrEmpty(filter)
                        && !entry.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        && !entry.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int rowIndex = dgvObjectTypes.Rows.Add(
                        entry.IsSelected,
                        entry.DisplayLabel,
                        ProfileToDisplayString(entry.TemplateProfile));
                    dgvObjectTypes.Rows[rowIndex].Tag = entry;

                    if (targetRowIndex == -1
                        && !string.IsNullOrWhiteSpace(preferredRowCode)
                        && string.Equals(entry.Code, preferredRowCode, StringComparison.OrdinalIgnoreCase))
                    {
                        targetRowIndex = rowIndex;
                    }
                }

                if (targetRowIndex >= 0 && targetRowIndex < dgvObjectTypes.Rows.Count)
                {
                    dgvObjectTypes.CurrentCell = dgvObjectTypes.Rows[targetRowIndex].Cells[colType.Index];
                }

                if (preferredFirstDisplayedRow >= 0 && preferredFirstDisplayedRow < dgvObjectTypes.Rows.Count)
                {
                    dgvObjectTypes.FirstDisplayedScrollingRowIndex = preferredFirstDisplayedRow;
                }
            }
            finally
            {
                _isPopulatingGrid = false;
            }
        }

        private void CommitGridEdits()
        {
            if (dgvObjectTypes.IsCurrentCellInEditMode)
            {
                dgvObjectTypes.EndEdit();
            }
        }

        private void SyncVisibleGridToMaster()
        {
            foreach (DataGridViewRow row in dgvObjectTypes.Rows)
            {
                if (row.Tag is not ObjectTypeRowDto entry)
                {
                    continue;
                }

                entry.IsSelected = Convert.ToBoolean(row.Cells[colUse.Index].Value ?? false);
                entry.TemplateProfile = DisplayStringToProfile(row.Cells[colStyle.Index].Value?.ToString());
            }
        }

        private IReadOnlyList<ObjectTypeRowDto> GetMasterTypeRowsSnapshot()
        {
            return _masterTypeRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Code))
                .Select(r => new ObjectTypeRowDto
                {
                    IsSelected = r.IsSelected,
                    Code = r.Code.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Code.Trim() : r.DisplayName.Trim(),
                    TemplateProfile = r.TemplateProfile
                })
                .ToList()
                .AsReadOnly();
        }

        private LabelTemplateProfile GetSelectedOrDefaultPreviewProfile()
        {
            if (dgvObjectTypes.CurrentRow?.Tag is ObjectTypeRowDto currentEntry)
            {
                return currentEntry.TemplateProfile;
            }

            ObjectTypeRowDto? firstSelected = _masterTypeRows.FirstOrDefault(r => r.IsSelected)
                                              ?? _masterTypeRows.FirstOrDefault();
            return firstSelected?.TemplateProfile ?? LabelTemplateProfile.Molecule;
        }

        private void ShowExamplePreviews(LabelTemplateProfile profile)
        {
            if (_resourcesDisposed || IsDisposed || Disposing)
            {
                return;
            }

            if (profile == _activePreviewProfile && picPreviewSmallBox.Image != null)
            {
                UpdatePreviewToggleVisuals();
                return;
            }

            _hasStartedPage2Previews = true;
            long requestVersion = ++_previewRenderVersion;
            CancelPendingPreviewRender();

            _activePreviewProfile = profile;
            UpdatePreviewToggleVisuals();

            SetSlotPreviewOrPlaceholder(picPreviewSmallBox, LabelTemplateType.SmallBox12mm, profile);
            SetSlotPreviewOrPlaceholder(picPreviewMagnet, LabelTemplateType.Magnet12mm, profile);
            SetSlotPreviewOrPlaceholder(picPreviewFlask, LabelTemplateType.Flask24mm, profile);

            bool needsPrimaryRender = AnySlotUncached(profile);
            LabelTemplateProfile otherProfile = profile == LabelTemplateProfile.Molecule
                ? LabelTemplateProfile.Generic
                : LabelTemplateProfile.Molecule;
            bool needsPrewarm = AnySlotUncached(otherProfile);

            if (!needsPrimaryRender && !needsPrewarm)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _previewRenderCts = cts;
            _ = LoadAndPrewarmExamplePreviewsAsync(profile, otherProfile, requestVersion, cts);
        }

        private bool AnySlotUncached(LabelTemplateProfile profile)
        {
            foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
            {
                if (!_previewPngCache.ContainsKey((profile, slot)))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetSlotPreviewOrPlaceholder(
            PictureBox picBox,
            LabelTemplateType slot,
            LabelTemplateProfile profile)
        {
            byte[] pngBytes = _previewPngCache.TryGetValue((profile, slot), out byte[]? cachedBytes)
                ? cachedBytes
                : _backend.GetLoadingPreviewPng(slot, profile);

            Bitmap bmp = Form1.DecodePngBitmap(
                pngBytes,
                () => _backend.GetFallbackPreviewPng(slot, profile, "Preview unavailable"));
            AssignPreviewPictureBoxImage(picBox, bmp);
        }

        private async Task LoadAndPrewarmExamplePreviewsAsync(
            LabelTemplateProfile requestedProfile,
            LabelTemplateProfile secondaryProfile,
            long requestVersion,
            CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            try
            {
                if (AnySlotUncached(requestedProfile))
                {
                    var updatedSlots = new HashSet<LabelTemplateType>();
                    Dictionary<LabelTemplateType, byte[]> primaryPreviews = await _backend.GetExamplePreviewsAsync(
                        requestedProfile,
                        onSlotReady: (slot, pngBytes) => MarshalSlotExampleReady(
                            requestedProfile,
                            slot,
                            pngBytes,
                            requestVersion,
                            updatedSlots,
                            cancellationToken),
                        cancellationToken);

                    if (cancellationToken.IsCancellationRequested
                        || IsDisposed
                        || Disposing
                        || _resourcesDisposed
                        || requestVersion != _previewRenderVersion
                        || _currentStep != SetupWizardStep.ObjectTypes)
                    {
                        return;
                    }

                    foreach (KeyValuePair<LabelTemplateType, byte[]> kvp in primaryPreviews)
                    {
                        byte[] safeBytes = kvp.Value ?? Array.Empty<byte>();
                        _previewPngCache[(requestedProfile, kvp.Key)] = safeBytes;

                        if (!updatedSlots.Contains(kvp.Key) && _activePreviewProfile == requestedProfile)
                        {
                            updatedSlots.Add(kvp.Key);
                            Bitmap bmp = Form1.DecodePngBitmap(
                                safeBytes,
                                () => _backend.GetFallbackPreviewPng(kvp.Key, requestedProfile, "Preview unavailable"));
                            AssignPreviewPictureBoxImage(GetPreviewPictureBox(kvp.Key), bmp);
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested
                    || IsDisposed
                    || Disposing
                    || _resourcesDisposed
                    || requestVersion != _previewRenderVersion
                    || _currentStep != SetupWizardStep.ObjectTypes)
                {
                    return;
                }

                if (AnySlotUncached(secondaryProfile))
                {
                    Dictionary<LabelTemplateType, byte[]> secondaryPreviews = await _backend.GetExamplePreviewsAsync(
                        secondaryProfile,
                        onSlotReady: null,
                        cancellationToken);

                    if (cancellationToken.IsCancellationRequested
                        || IsDisposed
                        || Disposing
                        || _resourcesDisposed
                        || requestVersion != _previewRenderVersion
                        || _currentStep != SetupWizardStep.ObjectTypes)
                    {
                        return;
                    }

                    foreach (KeyValuePair<LabelTemplateType, byte[]> kvp in secondaryPreviews)
                    {
                        _previewPngCache[(secondaryProfile, kvp.Key)] = kvp.Value ?? Array.Empty<byte>();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored when user switches profile, leaves ObjectTypes step, or closes the dialog.
            }
            catch (Exception ex)
            {
                // Guard fire-and-forget preview task against unobserved backend exceptions.
                if (!_resourcesDisposed
                    && !IsDisposed
                    && !Disposing
                    && !cancellationToken.IsCancellationRequested
                    && requestVersion == _previewRenderVersion
                    && _currentStep == SetupWizardStep.ObjectTypes
                    && _activePreviewProfile == requestedProfile)
                {
                    foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
                    {
                        if (!_previewPngCache.ContainsKey((requestedProfile, slot)))
                        {
                            Bitmap bmp = Form1.DecodePngBitmap(
                                null,
                                () => _backend.GetFallbackPreviewPng(slot, requestedProfile, $"Preview unavailable: {ex.Message}"));
                            AssignPreviewPictureBoxImage(GetPreviewPictureBox(slot), bmp);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _previewRenderCts, null, cts);
                cts.Dispose();
            }
        }

        private void MarshalSlotExampleReady(
            LabelTemplateProfile profile,
            LabelTemplateType slot,
            byte[] pngBytes,
            long requestVersion,
            HashSet<LabelTemplateType> updatedSlots,
            CancellationToken cancellationToken)
        {
            if (_resourcesDisposed || IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                        MarshalSlotExampleReady(profile, slot, pngBytes, requestVersion, updatedSlots, cancellationToken)));
                }
                catch (ObjectDisposedException)
                {
                    // Ignore callbacks arriving as form disposes.
                }
                catch (InvalidOperationException)
                {
                    // Ignore callbacks arriving before handle creation or after handle destruction.
                }

                return;
            }

            if (_resourcesDisposed
                || IsDisposed
                || Disposing
                || cancellationToken.IsCancellationRequested
                || requestVersion != _previewRenderVersion
                || _currentStep != SetupWizardStep.ObjectTypes)
            {
                return;
            }

            byte[] safeBytes = pngBytes ?? Array.Empty<byte>();
            _previewPngCache[(profile, slot)] = safeBytes;

            if (_activePreviewProfile == profile && updatedSlots.Add(slot))
            {
                Bitmap bmp = Form1.DecodePngBitmap(
                    safeBytes,
                    () => _backend.GetFallbackPreviewPng(slot, profile, "Preview unavailable"));
                AssignPreviewPictureBoxImage(GetPreviewPictureBox(slot), bmp);
            }
        }

        private PictureBox GetPreviewPictureBox(LabelTemplateType slot)
        {
            return slot switch
            {
                LabelTemplateType.SmallBox12mm => picPreviewSmallBox,
                LabelTemplateType.Magnet12mm => picPreviewMagnet,
                LabelTemplateType.Flask24mm => picPreviewFlask,
                _ => picPreviewSmallBox
            };
        }

        private static void AssignPreviewPictureBoxImage(PictureBox? picBox, Image? nextImage)
        {
            if (picBox == null || picBox.IsDisposed)
            {
                nextImage?.Dispose();
                return;
            }

            Image? previous = picBox.Image;
            if (ReferenceEquals(previous, nextImage))
            {
                return;
            }

            picBox.Image = nextImage;
            previous?.Dispose();
        }

        private void CancelPendingPreviewRender()
        {
            CancellationTokenSource? existing = Interlocked.Exchange(ref _previewRenderCts, null);
            if (existing == null)
            {
                return;
            }

            try
            {
                existing.Cancel();
            }
            catch
            {
                // Ignore cancellation failure during disposal.
            }
        }

        private void UpdatePreviewToggleVisuals()
        {
            bool isMolecule = _activePreviewProfile == LabelTemplateProfile.Molecule;

            StylePreviewToggleButton(btnPreviewMolecule, isMolecule);
            StylePreviewToggleButton(btnPreviewGeneric, !isMolecule);

            lblPreviewActiveHint.Text = $"Showing example previews for: {ProfileToDisplayString(_activePreviewProfile)}";
        }

        private void StylePreviewToggleButton(Button button, bool isActive)
        {
            button.BackColor = isActive ? Color.FromArgb(239, 246, 255) : SystemColors.Control;
            button.ForeColor = isActive ? Color.FromArgb(29, 78, 216) : Color.FromArgb(55, 65, 81);
            button.Font = isActive ? _previewBoldFont : _previewRegularFont;
            button.FlatAppearance.BorderColor = isActive ? Color.FromArgb(37, 99, 235) : Color.Silver;
            button.FlatAppearance.BorderSize = isActive ? 2 : 1;
        }

        private static LabelTemplateProfile GetDefaultProfileForCode(string code)
        {
            return code.Trim().Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase)
                ? LabelTemplateProfile.Molecule
                : LabelTemplateProfile.Generic;
        }

        private static string ProfileToDisplayString(LabelTemplateProfile profile)
        {
            return profile == LabelTemplateProfile.Molecule
                ? StyleMoleculeDisplay
                : StyleGenericDisplay;
        }

        private static LabelTemplateProfile DisplayStringToProfile(string? display)
        {
            return string.Equals(display?.Trim(), StyleMoleculeDisplay, StringComparison.OrdinalIgnoreCase)
                ? LabelTemplateProfile.Molecule
                : LabelTemplateProfile.Generic;
        }

        private bool IsValidatedForCurrentInputs()
        {
            string url = (txtInstanceUrl.Text ?? "").Trim();
            string token = (txtToken.Text ?? "").Trim();
            return string.Equals(_validatedUrl, url, StringComparison.Ordinal)
                   && string.Equals(_validatedToken, token, StringComparison.Ordinal);
        }

        private async Task<bool> RunLiveConnectionCheckAsync(bool saveCredentialsOnSuccess)
        {
            if (_isBusy)
            {
                return false;
            }

            string url = (txtInstanceUrl.Text ?? "").Trim();
            string token = (txtToken.Text ?? "").Trim();

            SetBusy(true);
            SetConnectionStatus("Checking openBIS session...", StatusSeverity.Info);

            try
            {
                ConnectionCheckResult result = await _backend.TestConnectionAsync(
                    url,
                    token,
                    persistOnSuccess: saveCredentialsOnSuccess);

                if (IsDisposed || Disposing)
                {
                    return result.Success;
                }

                SetBusy(false);

                if (!result.Success)
                {
                    _validatedUrl = "";
                    _validatedToken = "";
                    SetConnectionStatus(result.Message, StatusSeverity.Error);
                    if (result.ShouldFocusToken)
                    {
                        txtToken.Focus();
                        txtToken.SelectAll();
                    }

                    return false;
                }

                _validatedUrl = string.IsNullOrWhiteSpace(result.NormalizedUrl) ? url : result.NormalizedUrl;
                _validatedToken = token;
                SetConnectionStatus(result.Message, StatusSeverity.Success);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    SetConnectionStatus($"Unexpected error while checking openBIS connection: {ex.Message}", StatusSeverity.Error);
                }

                return false;
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
            }
        }

        private async Task LoadServerTypesForCurrentPageAsync(bool redirectToConnectionOnExpiredToken)
        {
            if (_isBusy || _isLoadingTypes)
            {
                return;
            }

            _autoFetchAttemptedForCurrentInputs = true;
            long fetchVersion = ++_typesFetchVersion;
            string url = (txtInstanceUrl.Text ?? "").Trim();
            string token = (txtToken.Text ?? "").Trim();

            // Keep the Object Types grid, filter, Save, and navigation unlocked so user selection
            // is never blocked while openBIS types load in the background.
            SetTypesLoadingState(true);
            SetTypesStatus("Syncing object types from openBIS...", StatusSeverity.Info);

            try
            {
                CommitGridEdits();
                SyncVisibleGridToMaster();
                IReadOnlyList<ObjectTypeRowDto> preFetchRows = GetMasterTypeRowsSnapshot();

                ServerTypeSyncResult result = await _backend.FetchAndMergeServerTypesAsync(
                    url,
                    token,
                    preFetchRows);

                if (IsDisposed || Disposing || fetchVersion != _typesFetchVersion)
                {
                    return;
                }

                if (!string.Equals(url, (txtInstanceUrl.Text ?? "").Trim(), StringComparison.Ordinal)
                    || !string.Equals(token, (txtToken.Text ?? "").Trim(), StringComparison.Ordinal))
                {
                    return;
                }

                if (result.ValidationFailed)
                {
                    if (redirectToConnectionOnExpiredToken)
                    {
                        ShowStep(SetupWizardStep.Connection);
                        SetConnectionStatus(result.StatusMessage, StatusSeverity.Error);
                    }
                    else
                    {
                        SetTypesStatus($"Offline mode: showing saved object types ({result.StatusMessage})", StatusSeverity.Warning);
                    }

                    return;
                }

                if (!result.Success)
                {
                    if (redirectToConnectionOnExpiredToken && result.IsTokenExpired)
                    {
                        ShowStep(SetupWizardStep.Connection);
                        SetConnectionStatus(result.StatusMessage, StatusSeverity.Error);
                        txtToken.Focus();
                        txtToken.SelectAll();
                        return;
                    }

                    SetTypesStatus(
                        $"Offline mode: showing saved object types from local config ({result.StatusMessage})",
                        StatusSeverity.Warning);
                    return;
                }

                _validatedUrl = url;
                _validatedToken = token;

                CommitGridEdits();
                SyncVisibleGridToMaster();
                IReadOnlyList<ObjectTypeRowDto> mergedWithLiveEdits = MergeInFlightTypeEdits(
                    result.MergedRows,
                    preFetchRows,
                    GetMasterTypeRowsSnapshot());

                ApplyTypeRows(mergedWithLiveEdits, syncedFromServer: true);
                SetTypesStatus(result.StatusMessage, StatusSeverity.Success);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing && fetchVersion == _typesFetchVersion)
                {
                    string message = $"Offline mode: showing saved object types ({ex.Message})";
                    SetTypesStatus(message, StatusSeverity.Warning);
                    if (_currentStep == SetupWizardStep.Connection)
                    {
                        SetConnectionStatus(message, StatusSeverity.Warning);
                    }
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetTypesLoadingState(false);
                }
            }
        }

        private static IReadOnlyList<ObjectTypeRowDto> MergeInFlightTypeEdits(
            IReadOnlyList<ObjectTypeRowDto> mergedRows,
            IReadOnlyList<ObjectTypeRowDto> preFetchRows,
            IReadOnlyList<ObjectTypeRowDto> liveRows)
        {
            static Dictionary<string, ObjectTypeRowDto> IndexByCode(IReadOnlyList<ObjectTypeRowDto> rows)
            {
                var map = new Dictionary<string, ObjectTypeRowDto>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectTypeRowDto row in rows)
                {
                    if (row != null && !string.IsNullOrWhiteSpace(row.Code))
                    {
                        string code = row.Code.Trim();
                        map.TryAdd(code, row);
                    }
                }

                return map;
            }

            Dictionary<string, ObjectTypeRowDto> preByCode = IndexByCode(preFetchRows);
            Dictionary<string, ObjectTypeRowDto> liveByCode = IndexByCode(liveRows);
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var combined = new List<ObjectTypeRowDto>(mergedRows.Count + liveRows.Count);

            foreach (ObjectTypeRowDto mergedRow in mergedRows)
            {
                if (mergedRow == null || string.IsNullOrWhiteSpace(mergedRow.Code))
                {
                    continue;
                }

                string code = mergedRow.Code.Trim();
                if (!seenCodes.Add(code))
                {
                    continue;
                }

                bool isSelected = mergedRow.IsSelected;
                LabelTemplateProfile templateProfile = mergedRow.TemplateProfile;

                if (liveByCode.TryGetValue(code, out ObjectTypeRowDto? liveRow))
                {
                    if (!preByCode.TryGetValue(code, out ObjectTypeRowDto? preRow))
                    {
                        isSelected = liveRow.IsSelected;
                        templateProfile = liveRow.TemplateProfile;
                    }
                    else
                    {
                        if (liveRow.IsSelected != preRow.IsSelected)
                        {
                            isSelected = liveRow.IsSelected;
                        }

                        if (liveRow.TemplateProfile != preRow.TemplateProfile)
                        {
                            templateProfile = liveRow.TemplateProfile;
                        }
                    }
                }

                combined.Add(new ObjectTypeRowDto
                {
                    IsSelected = isSelected,
                    Code = code,
                    DisplayName = mergedRow.DisplayName,
                    TemplateProfile = templateProfile
                });
            }

            foreach (ObjectTypeRowDto liveRow in liveRows)
            {
                if (liveRow == null || string.IsNullOrWhiteSpace(liveRow.Code))
                {
                    continue;
                }

                string code = liveRow.Code.Trim();
                if (!seenCodes.Add(code))
                {
                    continue;
                }

                combined.Add(new ObjectTypeRowDto
                {
                    IsSelected = liveRow.IsSelected,
                    Code = code,
                    DisplayName = liveRow.DisplayName,
                    TemplateProfile = liveRow.TemplateProfile
                });
            }

            return combined.AsReadOnly();
        }

        private void SetTypesLoadingState(bool loading)
        {
            _isLoadingTypes = loading;
            btnRefreshTypes.Enabled = !loading && !_isBusy;
            prgTypesLoading.Visible = loading;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            UseWaitCursor = busy;
            txtInstanceUrl.ReadOnly = busy;
            txtToken.ReadOnly = busy;
            btnTestConnection.Enabled = !busy;
            prgConnectionLoading.Visible = busy && _currentStep == SetupWizardStep.Connection;
            btnNextFinish.Enabled = !busy;
            btnRefreshTypes.Enabled = !busy && !_isLoadingTypes;
            btnTabConnection.Enabled = !busy;
            btnTabObjectTypes.Enabled = !busy;
            btnTabPrinters.Enabled = !busy;
            btnBack.Enabled = !busy && Mode == SetupDialogMode.Wizard && _currentStep != SetupWizardStep.Connection;
        }

        private enum StatusSeverity
        {
            Info,
            Success,
            Warning,
            Error
        }

        private void SetConnectionStatus(string message, StatusSeverity severity)
        {
            lblConnectionStatus.Text = message;
            lblConnectionStatus.ForeColor = SeverityToColor(severity);
        }

        private void SetTypesStatus(string message, StatusSeverity severity)
        {
            lblTypesStatus.Text = message;
            lblTypesStatus.ForeColor = SeverityToColor(severity);
        }

        private void ShowFooterError(string message)
        {
            lblFooterError.Text = message;
            lblFooterError.Visible = true;
        }

        private void ClearFooterError()
        {
            lblFooterError.Text = "";
        }

        private static Color SeverityToColor(StatusSeverity severity)
        {
            return severity switch
            {
                StatusSeverity.Success => Color.FromArgb(21, 128, 61),
                StatusSeverity.Warning => Color.FromArgb(180, 83, 9),
                StatusSeverity.Error => Color.Firebrick,
                _ => Color.DimGray
            };
        }

        private void ConnectionInput_TextChanged(object? sender, EventArgs e)
        {
            if (_isInitializingInputs)
            {
                return;
            }

            ClearFooterError();
            ++_typesFetchVersion;
            SetTypesLoadingState(false);
            _validatedUrl = "";
            _validatedToken = "";
            _autoFetchAttemptedForCurrentInputs = false;
            TypesSyncedFromServer = false;
        }

        private void chkShowToken_CheckedChanged(object? sender, EventArgs e)
        {
            txtToken.UseSystemPasswordChar = !chkShowToken.Checked;
        }

        private async void btnTestConnection_Click(object? sender, EventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            bool saveOnSuccess = Mode == SetupDialogMode.Wizard || Mode == SetupDialogMode.ExpiredTokenRecovery;
            await RunLiveConnectionCheckAsync(saveCredentialsOnSuccess: saveOnSuccess);
        }

        private async void btnNextFinish_Click(object? sender, EventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (Mode == SetupDialogMode.Settings)
            {
                if (TrySaveSettingsOffline(out string validationError))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    if (validationError.Contains("object type", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowStep(SetupWizardStep.ObjectTypes);
                        SetTypesStatus(validationError, StatusSeverity.Error);
                    }
                    else if (validationError.StartsWith("Could not save configuration files:", StringComparison.Ordinal))
                    {
                        if (_currentStep == SetupWizardStep.Connection)
                        {
                            SetConnectionStatus(validationError, StatusSeverity.Error);
                        }
                        else if (_currentStep == SetupWizardStep.ObjectTypes)
                        {
                            SetTypesStatus(validationError, StatusSeverity.Error);
                        }
                    }
                    else
                    {
                        ShowStep(SetupWizardStep.Connection);
                        SetConnectionStatus(validationError, StatusSeverity.Error);
                    }

                    ShowFooterError(validationError);
                }

                return;
            }

            if (Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                await HandleExpiredTokenSaveAsync();
                return;
            }

            await HandleWizardNextOrFinishAsync();
        }

        private async Task HandleExpiredTokenSaveAsync()
        {
            if (IsValidatedForCurrentInputs())
            {
                OperationResult saveRes = await _backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.Connection,
                    OpenBisUrl = (txtInstanceUrl.Text ?? "").Trim(),
                    Token = (txtToken.Text ?? "").Trim()
                });

                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (!saveRes.Success)
                {
                    SetConnectionStatus(saveRes.ErrorMessage, StatusSeverity.Error);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            bool connected = await RunLiveConnectionCheckAsync(saveCredentialsOnSuccess: true);
            if (IsDisposed || Disposing || !connected)
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private async Task HandleWizardNextOrFinishAsync()
        {
            switch (_currentStep)
            {
                case SetupWizardStep.Connection:
                {
                    if (!IsValidatedForCurrentInputs())
                    {
                        bool connected = await RunLiveConnectionCheckAsync(saveCredentialsOnSuccess: true);
                        if (IsDisposed || Disposing || !connected)
                        {
                            return;
                        }
                    }
                    else
                    {
                        OperationResult saveRes = await _backend.SaveWizardStepAsync(new WizardStepSaveRequest
                        {
                            Step = SetupWizardStep.Connection,
                            OpenBisUrl = (txtInstanceUrl.Text ?? "").Trim(),
                            Token = (txtToken.Text ?? "").Trim()
                        });

                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        if (!saveRes.Success)
                        {
                            SetConnectionStatus(saveRes.ErrorMessage, StatusSeverity.Error);
                            return;
                        }
                    }

                    SettingsSnapshotDto snapshot = await _backend.GetSettingsSnapshotAsync();
                    if (IsDisposed || Disposing)
                    {
                        return;
                    }

                    _hasValidConfiguredTypes = snapshot.HasValidConfiguredTypes;
                    _hasPrinterConfig = snapshot.HasPrinterConfig;

                    if (_hasValidConfiguredTypes && _hasPrinterConfig)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    ShowStep(!_hasValidConfiguredTypes ? SetupWizardStep.ObjectTypes : SetupWizardStep.Printers);
                    if (_currentStep == SetupWizardStep.ObjectTypes && !TypesSyncedFromServer)
                    {
                        await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
                    }
                    break;
                }

                case SetupWizardStep.ObjectTypes:
                {
                    CommitGridEdits();
                    SyncVisibleGridToMaster();
                    IReadOnlyList<ObjectTypeRowDto> rowsSnapshot = GetMasterTypeRowsSnapshot();
                    if (!rowsSnapshot.Any(r => r.IsSelected))
                    {
                        SetTypesStatus("Select at least one object type.", StatusSeverity.Error);
                        return;
                    }

                    if (_hasPrinterConfig && !_cameFromPrintersStep)
                    {
                        OperationResult saveRes = await _backend.SaveWizardStepAsync(new WizardStepSaveRequest
                        {
                            Step = SetupWizardStep.ObjectTypes,
                            ObjectTypes = rowsSnapshot
                        });

                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        if (!saveRes.Success)
                        {
                            SetTypesStatus(saveRes.ErrorMessage, StatusSeverity.Error);
                            return;
                        }

                        _hasValidConfiguredTypes = true;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    ShowStep(SetupWizardStep.Printers);
                    break;
                }

                case SetupWizardStep.Printers:
                {
                    IReadOnlyList<ObjectTypeRowDto>? objectTypes = null;
                    if (_hasVisitedTypesPage)
                    {
                        CommitGridEdits();
                        SyncVisibleGridToMaster();
                        IReadOnlyList<ObjectTypeRowDto> rowsSnapshot = GetMasterTypeRowsSnapshot();
                        if (!rowsSnapshot.Any(r => r.IsSelected))
                        {
                            ShowStep(SetupWizardStep.ObjectTypes);
                            SetTypesStatus("Select at least one object type.", StatusSeverity.Error);
                            return;
                        }

                        objectTypes = rowsSnapshot;
                    }

                    OperationResult saveRes = await _backend.SaveWizardStepAsync(new WizardStepSaveRequest
                    {
                        Step = SetupWizardStep.Printers,
                        ObjectTypes = objectTypes,
                        SmallBoxPrinter = GetSelectedPrinterName(cmbSmallBoxPrinter, _fallbackSmallBoxPrinter),
                        MagnetPrinter = GetSelectedPrinterName(cmbMagnetPrinter, _fallbackMagnetPrinter),
                        FlaskPrinter = GetSelectedPrinterName(cmbFlaskPrinter, _fallbackFlaskPrinter)
                    });

                    if (IsDisposed || Disposing)
                    {
                        return;
                    }

                    if (!saveRes.Success)
                    {
                        ShowFooterError(saveRes.ErrorMessage);
                        return;
                    }

                    _hasPrinterConfig = true;
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
                }
            }
        }

        private async void btnBack_Click(object? sender, EventArgs e)
        {
            if (_isBusy || Mode != SetupDialogMode.Wizard)
            {
                return;
            }

            if (_currentStep == SetupWizardStep.Printers)
            {
                _cameFromPrintersStep = true;
                ShowStep(SetupWizardStep.ObjectTypes);
                if (!TypesSyncedFromServer && !_autoFetchAttemptedForCurrentInputs)
                {
                    await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
                }
            }
            else if (_currentStep == SetupWizardStep.ObjectTypes)
            {
                ShowStep(SetupWizardStep.Connection);
            }
        }

        private void btnCancel_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnTabConnection_Click(object? sender, EventArgs e)
        {
            if (!_isBusy && Mode == SetupDialogMode.Settings)
            {
                ShowStep(SetupWizardStep.Connection);
            }
        }

        private async void btnTabObjectTypes_Click(object? sender, EventArgs e)
        {
            if (_isBusy || Mode != SetupDialogMode.Settings)
            {
                return;
            }

            ShowStep(SetupWizardStep.ObjectTypes);
            if (!TypesSyncedFromServer && !_autoFetchAttemptedForCurrentInputs)
            {
                await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
            }
        }

        private void btnTabPrinters_Click(object? sender, EventArgs e)
        {
            if (!_isBusy && Mode == SetupDialogMode.Settings)
            {
                ShowStep(SetupWizardStep.Printers);
            }
        }

        private async void btnRefreshTypes_Click(object? sender, EventArgs e)
        {
            if (_isBusy || _isLoadingTypes)
            {
                return;
            }

            await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
        }

        private void PrinterCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isInitializingInputs || Mode != SetupDialogMode.Settings)
            {
                return;
            }

            ClearFooterError();
        }

        private void txtTypeFilter_TextChanged(object? sender, EventArgs e)
        {
            string? selectedRowCode = (dgvObjectTypes.CurrentRow?.Tag as ObjectTypeRowDto)?.Code;
            CommitGridEdits();
            SyncVisibleGridToMaster();
            RefreshObjectTypeGrid(selectedRowCode);
        }

        private void dgvObjectTypes_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (dgvObjectTypes.IsCurrentCellDirty)
            {
                dgvObjectTypes.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void dgvObjectTypes_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_isPopulatingGrid || e.RowIndex < 0 || e.RowIndex >= dgvObjectTypes.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = dgvObjectTypes.Rows[e.RowIndex];
            if (row.Tag is not ObjectTypeRowDto entry)
            {
                return;
            }

            ClearFooterError();

            if (e.ColumnIndex == colUse.Index)
            {
                entry.IsSelected = Convert.ToBoolean(row.Cells[colUse.Index].Value ?? false);
                if (entry.IsSelected && lblTypesStatus.Text == "Select at least one object type.")
                {
                    lblTypesStatus.Text = "";
                }
            }
            else if (e.ColumnIndex == colStyle.Index)
            {
                entry.TemplateProfile = DisplayStringToProfile(row.Cells[colStyle.Index].Value?.ToString());
                if (IsHandleCreated && entry.TemplateProfile != _activePreviewProfile)
                {
                    ShowExamplePreviews(entry.TemplateProfile);
                }
            }
        }

        private void dgvObjectTypes_SelectionChanged(object? sender, EventArgs e)
        {
            if (_isPopulatingGrid)
            {
                return;
            }

            if (dgvObjectTypes.CurrentRow?.Tag is ObjectTypeRowDto entry
                && IsHandleCreated
                && _currentStep == SetupWizardStep.ObjectTypes
                && entry.TemplateProfile != _activePreviewProfile)
            {
                ShowExamplePreviews(entry.TemplateProfile);
            }
        }

        private void dgvObjectTypes_DataError(object? sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void btnPreviewMolecule_Click(object? sender, EventArgs e)
        {
            ShowExamplePreviews(LabelTemplateProfile.Molecule);
        }

        private void btnPreviewGeneric_Click(object? sender, EventArgs e)
        {
            ShowExamplePreviews(LabelTemplateProfile.Generic);
        }
    }
}
