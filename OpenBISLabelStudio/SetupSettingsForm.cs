using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenBISLabelStudio
{
    using OpenBISLabelStudio.Services;

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

        private readonly AppConfigService _configService;
        private readonly OpenBisClient _openBisClient;
        private readonly LabelPreviewRenderer _previewRenderer;
        private readonly Func<IReadOnlyList<string>>? _installedPrintersProvider;
        private readonly string? _initialWarning;

        private readonly List<TypeRowEntry> _masterTypeRows = new List<TypeRowEntry>();
        private readonly Dictionary<(Models.LabelTemplateProfile Profile, Models.LabelTemplateType Slot), Image> _previewCache
            = new Dictionary<(Models.LabelTemplateProfile Profile, Models.LabelTemplateType Slot), Image>();
        private readonly Font _tabRegularFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _tabBoldFont = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _previewRegularFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _previewBoldFont = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);

        private Models.SetupWizardStep _currentStep;
        private Models.LabelTemplateProfile _activePreviewProfile = Models.LabelTemplateProfile.Molecule;
        private CancellationTokenSource? _previewRenderCts;
        private long _previewRenderVersion;
        private bool _isInitializingInputs;
        private bool _isPopulatingGrid;
        private bool _isBusy;
        private bool _hasVisitedTypesPage;
        private bool _cameFromPrintersStep;
        private bool _autoFetchAttemptedForCurrentInputs;
        private bool _resourcesDisposed;
        private bool _fontsDisposed;
        private string _validatedUrl = "";
        private string _validatedToken = "";

        private sealed class TypeRowEntry
        {
            public bool IsSelected { get; set; }
            public string Code { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public Models.LabelTemplateProfile TemplateProfile { get; set; } = Models.LabelTemplateProfile.Generic;
            public string DisplayLabel => $"{DisplayName} ({Code})";
        }

        /// <summary>
        /// Initializes the unified setup and settings dialog for the specified <paramref name="mode"/>.
        /// </summary>
        public SetupSettingsForm(
            AppConfigService configService,
            OpenBisClient openBisClient,
            LabelPreviewRenderer previewRenderer,
            SetupDialogMode mode,
            Models.SetupWizardStep startStep = Models.SetupWizardStep.Connection,
            string? initialWarning = null,
            Func<IReadOnlyList<string>>? installedPrintersProvider = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _openBisClient = openBisClient ?? throw new ArgumentNullException(nameof(openBisClient));
            _previewRenderer = previewRenderer ?? throw new ArgumentNullException(nameof(previewRenderer));
            Mode = mode;
            _initialWarning = initialWarning;
            _installedPrintersProvider = installedPrintersProvider;

            InitializeComponent();
            InitializeFromConfig(startStep);
        }

        /// <summary>
        /// Gets the active dialog mode.
        /// </summary>
        public SetupDialogMode Mode { get; }

        /// <summary>
        /// Gets the most recently fetched list of openBIS server object types, if any.
        /// </summary>
        public IReadOnlyList<Models.OpenBisTypeInfoDto>? CachedServerTypes { get; private set; }

        /// <summary>
        /// Merges server-discovered object types with existing saved mappings.
        /// Unconfigured <c>SUBSTANCE</c> defaults to <see cref="Models.LabelTemplateProfile.Molecule"/>;
        /// all other unconfigured types default to <see cref="Models.LabelTemplateProfile.Generic"/>.
        /// </summary>
        public static List<Models.ConfiguredOpenBisType> BuildInitialTypeRows(
            IEnumerable<Models.OpenBisTypeInfoDto> serverTypes,
            IReadOnlyList<Models.ConfiguredOpenBisType> existingConfigured)
        {
            ArgumentNullException.ThrowIfNull(serverTypes);
            ArgumentNullException.ThrowIfNull(existingConfigured);

            var existingByCode = new Dictionary<string, Models.ConfiguredOpenBisType>(StringComparer.OrdinalIgnoreCase);
            foreach (Models.ConfiguredOpenBisType existing in existingConfigured)
            {
                if (existing == null || string.IsNullOrWhiteSpace(existing.Code))
                {
                    continue;
                }

                string code = existing.Code.Trim();
                if (!existingByCode.ContainsKey(code))
                {
                    existingByCode[code] = existing;
                }
            }

            var result = new List<Models.ConfiguredOpenBisType>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Models.OpenBisTypeInfoDto serverType in serverTypes)
            {
                if (serverType == null || string.IsNullOrWhiteSpace(serverType.Code))
                {
                    continue;
                }

                string code = serverType.Code.Trim();
                if (!seenCodes.Add(code))
                {
                    continue;
                }

                existingByCode.TryGetValue(code, out Models.ConfiguredOpenBisType? existing);

                string displayName = !string.IsNullOrWhiteSpace(serverType.Description)
                    ? serverType.Description.Trim()
                    : (!string.IsNullOrWhiteSpace(existing?.DisplayName)
                        ? existing.DisplayName.Trim()
                        : AppConfigService.FormatFriendlyName(code));

                Models.LabelTemplateProfile profile = existing != null && Enum.IsDefined(typeof(Models.LabelTemplateProfile), existing.TemplateProfile)
                    ? existing.TemplateProfile
                    : GetDefaultProfileForCode(code);

                result.Add(new Models.ConfiguredOpenBisType
                {
                    Code = code,
                    DisplayName = displayName,
                    TemplateProfile = profile
                });
            }

            // Preserve any saved/existing entries absent from serverTypes (including offline fallback).
            foreach (Models.ConfiguredOpenBisType existing in existingConfigured)
            {
                if (existing == null || string.IsNullOrWhiteSpace(existing.Code))
                {
                    continue;
                }

                string code = existing.Code.Trim();
                if (!seenCodes.Add(code))
                {
                    continue;
                }

                Models.LabelTemplateProfile profile = Enum.IsDefined(typeof(Models.LabelTemplateProfile), existing.TemplateProfile)
                    ? existing.TemplateProfile
                    : GetDefaultProfileForCode(code);

                result.Add(new Models.ConfiguredOpenBisType
                {
                    Code = code,
                    DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName)
                        ? AppConfigService.FormatFriendlyName(code)
                        : existing.DisplayName.Trim(),
                    TemplateProfile = profile
                });
            }

            return result;
        }

        /// <summary>
        /// Replaces/merges the Object Types grid with <paramref name="serverTypes"/> while
        /// preserving checked state and template profiles for already-known entries.
        /// </summary>
        public void ApplyServerTypes(IEnumerable<Models.OpenBisTypeInfoDto> serverTypes)
        {
            ArgumentNullException.ThrowIfNull(serverTypes);

            CommitGridEdits();
            SyncVisibleGridToMaster();

            var validServerTypes = serverTypes
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Code))
                .Select(t => new Models.OpenBisTypeInfoDto
                {
                    Code = t.Code.Trim(),
                    Description = t.Description?.Trim() ?? ""
                })
                .ToList();

            CachedServerTypes = validServerTypes.AsReadOnly();

            var checkedCodes = new HashSet<string>(
                _masterTypeRows.Where(r => r.IsSelected).Select(r => r.Code),
                StringComparer.OrdinalIgnoreCase);

            List<Models.ConfiguredOpenBisType> existingSnapshot = _masterTypeRows
                .Select(r => new Models.ConfiguredOpenBisType
                {
                    Code = r.Code,
                    DisplayName = r.DisplayName,
                    TemplateProfile = r.TemplateProfile
                })
                .ToList();

            List<Models.ConfiguredOpenBisType> merged = BuildInitialTypeRows(validServerTypes, existingSnapshot);

            _masterTypeRows.Clear();
            foreach (Models.ConfiguredOpenBisType item in merged)
            {
                _masterTypeRows.Add(new TypeRowEntry
                {
                    IsSelected = checkedCodes.Contains(item.Code),
                    Code = item.Code,
                    DisplayName = item.DisplayName,
                    TemplateProfile = item.TemplateProfile
                });
            }

            RefreshObjectTypeGrid();
            Models.LabelTemplateProfile selectedProfile = GetSelectedOrDefaultPreviewProfile();
            if (IsHandleCreated && _currentStep == Models.SetupWizardStep.ObjectTypes)
            {
                ShowExamplePreviews(selectedProfile);
            }
            else
            {
                _activePreviewProfile = selectedProfile;
                UpdatePreviewToggleVisuals();
            }
        }

        /// <summary>
        /// Validates local settings constraints and atomically writes all three configuration
        /// files under <c>config/</c> without invoking openBIS or requiring network access.
        /// </summary>
        public bool TrySaveSettingsOffline(out string validationError)
        {
            CommitGridEdits();
            SyncVisibleGridToMaster();

            if (!ValidateConnectionInputs(out string url, out string token, out validationError))
            {
                return false;
            }

            List<Models.ConfiguredOpenBisType> selectedTypes = GetSelectedConfiguredTypes();
            if (selectedTypes.Count == 0)
            {
                validationError = "Select at least one object type.";
                return false;
            }

            Models.PrinterConfig printerConfig = BuildPrinterConfigFromSelections();

            return TryPersistConfig(() =>
            {
                _configService.SaveAppConfig(new Models.AppConfig
                {
                    OpenBisUrl = url,
                    Token = token
                });
                _configService.SaveConfiguredTypes(selectedTypes);
                _configService.SavePrinterConfig(printerConfig);
            }, out validationError);
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

            if (_currentStep == Models.SetupWizardStep.ObjectTypes)
            {
                ShowExamplePreviews(_activePreviewProfile);
                if (CachedServerTypes == null && !_autoFetchAttemptedForCurrentInputs)
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

            foreach (Image image in _previewCache.Values)
            {
                image.Dispose();
            }

            _previewCache.Clear();
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

        private void InitializeFromConfig(Models.SetupWizardStep requestedStartStep)
        {
            Models.AppConfig? savedApp = _configService.LoadAppConfig();
            List<Models.ConfiguredOpenBisType> savedTypes = _configService.LoadConfiguredTypes();
            Models.PrinterConfig? savedPrinters = _configService.LoadPrinterConfig();

            _isInitializingInputs = true;
            try
            {
                txtInstanceUrl.Text = savedApp?.OpenBisUrl ?? "";
                txtToken.Text = Mode == SetupDialogMode.ExpiredTokenRecovery
                    ? ""
                    : (savedApp?.Token ?? "");

                _masterTypeRows.Clear();
                foreach (Models.ConfiguredOpenBisType savedType in savedTypes)
                {
                    if (string.IsNullOrWhiteSpace(savedType.Code))
                    {
                        continue;
                    }

                    _masterTypeRows.Add(new TypeRowEntry
                    {
                        IsSelected = true,
                        Code = savedType.Code.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(savedType.DisplayName)
                            ? AppConfigService.FormatFriendlyName(savedType.Code)
                            : savedType.DisplayName.Trim(),
                        TemplateProfile = Enum.IsDefined(typeof(Models.LabelTemplateProfile), savedType.TemplateProfile)
                            ? savedType.TemplateProfile
                            : GetDefaultProfileForCode(savedType.Code)
                    });
                }

                RefreshObjectTypeGrid();
                PopulatePrinterCombos(savedPrinters);
            }
            finally
            {
                _isInitializingInputs = false;
            }

            Models.SetupWizardStep initialStep = Mode == SetupDialogMode.ExpiredTokenRecovery
                ? Models.SetupWizardStep.Connection
                : (requestedStartStep == Models.SetupWizardStep.None
                    ? Models.SetupWizardStep.Connection
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

        private void ShowStep(Models.SetupWizardStep step)
        {
            if (Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                step = Models.SetupWizardStep.Connection;
            }
            else if (step == Models.SetupWizardStep.None)
            {
                step = Models.SetupWizardStep.Connection;
            }

            if (_currentStep == Models.SetupWizardStep.ObjectTypes && step != Models.SetupWizardStep.ObjectTypes)
            {
                CancelPendingPreviewRender();
                CommitGridEdits();
                SyncVisibleGridToMaster();
            }

            _currentStep = step;
            ClearFooterError();

            pnlConnection.Visible = step == Models.SetupWizardStep.Connection;
            pnlObjectTypes.Visible = step == Models.SetupWizardStep.ObjectTypes;
            pnlPrinters.Visible = step == Models.SetupWizardStep.Printers;

            if (Mode == SetupDialogMode.Wizard)
            {
                UpdateWizardStepChrome(step);
            }
            else if (Mode == SetupDialogMode.Settings)
            {
                UpdateSettingsTabSelection(step);
            }

            if (step == Models.SetupWizardStep.ObjectTypes)
            {
                _hasVisitedTypesPage = true;
                Models.LabelTemplateProfile initialProfile = GetSelectedOrDefaultPreviewProfile();
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
            else if (step == Models.SetupWizardStep.Connection && Mode == SetupDialogMode.ExpiredTokenRecovery)
            {
                ActiveControl = txtToken;
            }
        }

        private void UpdateWizardStepChrome(Models.SetupWizardStep step)
        {
            switch (step)
            {
                case Models.SetupWizardStep.Connection:
                    lblWizardTitle.Text = "Step 1 of 3 — Connect to openBIS";
                    lblWizardSubtitle.Text = "Enter your openBIS server URL and personal access token.";
                    btnBack.Enabled = false;
                    btnNextFinish.Text = (_configService.HasValidConfiguredTypes() && _configService.HasPrinterConfig())
                        ? "Finish"
                        : "Next >";
                    break;

                case Models.SetupWizardStep.ObjectTypes:
                    lblWizardTitle.Text = "Step 2 of 3 — Select Object Types & Label Templates";
                    lblWizardSubtitle.Text = "Choose which object types to enable and preview their label layouts.";
                    btnBack.Enabled = true;
                    btnNextFinish.Text = (!_configService.HasPrinterConfig() || _cameFromPrintersStep)
                        ? "Next >"
                        : "Finish";
                    break;

                case Models.SetupWizardStep.Printers:
                    lblWizardTitle.Text = "Step 3 of 3 — Configure Printer Routing";
                    lblWizardSubtitle.Text = "Select the Windows printer queue for each label slot.";
                    btnBack.Enabled = true;
                    btnNextFinish.Text = "Finish";
                    break;
            }
        }

        private void UpdateSettingsTabSelection(Models.SetupWizardStep activeStep)
        {
            ApplyTabVisual(btnTabConnection, activeStep == Models.SetupWizardStep.Connection);
            ApplyTabVisual(btnTabObjectTypes, activeStep == Models.SetupWizardStep.ObjectTypes);
            ApplyTabVisual(btnTabPrinters, activeStep == Models.SetupWizardStep.Printers);
        }

        private void ApplyTabVisual(Button tabButton, bool isActive)
        {
            tabButton.BackColor = isActive ? Color.White : Color.FromArgb(232, 236, 242);
            tabButton.ForeColor = isActive ? Color.FromArgb(24, 34, 48) : Color.FromArgb(80, 90, 105);
            tabButton.Font = isActive ? _tabBoldFont : _tabRegularFont;
            tabButton.FlatAppearance.BorderColor = isActive ? Color.FromArgb(37, 99, 235) : Color.FromArgb(203, 213, 225);
            tabButton.FlatAppearance.BorderSize = isActive ? 2 : 1;
        }

        private void PopulatePrinterCombos(Models.PrinterConfig? savedPrinters)
        {
            IReadOnlyList<string>? rawPrinters = _installedPrintersProvider != null
                ? _installedPrintersProvider()
                : GetSystemInstalledPrinters();

            var availablePrinters = (rawPrinters ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Models.PrinterConfig defaults = AppConfigService.CreateDefaultPrinterConfig();
            string preferredBox = !string.IsNullOrWhiteSpace(savedPrinters?.SmallBoxPrinter)
                ? savedPrinters!.SmallBoxPrinter.Trim()
                : defaults.SmallBoxPrinter;
            string preferredMagnet = !string.IsNullOrWhiteSpace(savedPrinters?.MagnetPrinter)
                ? savedPrinters!.MagnetPrinter.Trim()
                : defaults.MagnetPrinter;
            string preferredFlask = !string.IsNullOrWhiteSpace(savedPrinters?.FlaskPrinter)
                ? savedPrinters!.FlaskPrinter.Trim()
                : defaults.FlaskPrinter;

            if (availablePrinters.Count == 0)
            {
                lblNoPrintersWarning.Visible = true;
                lblNoPrintersWarning.Text = "No Windows printers detected — saved/default printer names will be kept.";

                var fallbackItems = new[] { preferredBox, preferredMagnet, preferredFlask }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                PopulateSinglePrinterCombo(cmbSmallBoxPrinter, fallbackItems, preferredBox);
                PopulateSinglePrinterCombo(cmbMagnetPrinter, fallbackItems, preferredMagnet);
                PopulateSinglePrinterCombo(cmbFlaskPrinter, fallbackItems, preferredFlask);
            }
            else
            {
                lblNoPrintersWarning.Visible = false;
                PopulateSinglePrinterCombo(cmbSmallBoxPrinter, availablePrinters, preferredBox);
                PopulateSinglePrinterCombo(cmbMagnetPrinter, availablePrinters, preferredMagnet);
                PopulateSinglePrinterCombo(cmbFlaskPrinter, availablePrinters, preferredFlask);
            }
        }

        private static void PopulateSinglePrinterCombo(ComboBox comboBox, IReadOnlyList<string> items, string preferredName)
        {
            comboBox.Items.Clear();
            foreach (string item in items)
            {
                comboBox.Items.Add(item);
            }

            if (comboBox.Items.Count > 0)
            {
                int index = AppConfigService.FindBestPrinterIndex(items, preferredName);
                comboBox.SelectedIndex = index >= 0 && index < comboBox.Items.Count ? index : 0;
            }
        }

        private static IReadOnlyList<string> GetSystemInstalledPrinters()
        {
            try
            {
                var printers = new List<string>();
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        printers.Add(printer);
                    }
                }

                return printers;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private Models.PrinterConfig BuildPrinterConfigFromSelections()
        {
            Models.PrinterConfig defaults = AppConfigService.CreateDefaultPrinterConfig();
            return new Models.PrinterConfig
            {
                SmallBoxPrinter = GetSelectedPrinterName(cmbSmallBoxPrinter, defaults.SmallBoxPrinter),
                MagnetPrinter = GetSelectedPrinterName(cmbMagnetPrinter, defaults.MagnetPrinter),
                FlaskPrinter = GetSelectedPrinterName(cmbFlaskPrinter, defaults.FlaskPrinter)
            };
        }

        private static string GetSelectedPrinterName(ComboBox comboBox, string fallbackDefault)
        {
            string selected = (comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? "").Trim();
            return string.IsNullOrWhiteSpace(selected) ? fallbackDefault : selected;
        }

        private void RefreshObjectTypeGrid()
        {
            _isPopulatingGrid = true;
            try
            {
                dgvObjectTypes.Rows.Clear();
                string filter = (txtTypeFilter.Text ?? "").Trim();

                foreach (TypeRowEntry entry in _masterTypeRows)
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
                if (row.Tag is not TypeRowEntry entry)
                {
                    continue;
                }

                entry.IsSelected = Convert.ToBoolean(row.Cells[colUse.Index].Value ?? false);
                entry.TemplateProfile = DisplayStringToProfile(row.Cells[colStyle.Index].Value?.ToString());
            }
        }

        private List<Models.ConfiguredOpenBisType> GetSelectedConfiguredTypes()
        {
            return _masterTypeRows
                .Where(r => r.IsSelected && !string.IsNullOrWhiteSpace(r.Code))
                .Select(r => new Models.ConfiguredOpenBisType
                {
                    Code = r.Code.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(r.DisplayName)
                        ? AppConfigService.FormatFriendlyName(r.Code)
                        : r.DisplayName.Trim(),
                    TemplateProfile = r.TemplateProfile
                })
                .ToList();
        }

        private Models.LabelTemplateProfile GetSelectedOrDefaultPreviewProfile()
        {
            if (dgvObjectTypes.CurrentRow?.Tag is TypeRowEntry currentEntry)
            {
                return currentEntry.TemplateProfile;
            }

            TypeRowEntry? firstSelected = _masterTypeRows.FirstOrDefault(r => r.IsSelected)
                                         ?? _masterTypeRows.FirstOrDefault();
            return firstSelected?.TemplateProfile ?? Models.LabelTemplateProfile.Molecule;
        }

        private void ShowExamplePreviews(Models.LabelTemplateProfile profile)
        {
            if (_resourcesDisposed || IsDisposed || Disposing)
            {
                return;
            }

            long requestVersion = ++_previewRenderVersion;
            CancelPendingPreviewRender();

            _activePreviewProfile = profile;
            UpdatePreviewToggleVisuals();

            SetSlotPreviewOrPlaceholder(picPreviewSmallBox, Models.LabelTemplateType.SmallBox12mm, profile);
            SetSlotPreviewOrPlaceholder(picPreviewMagnet, Models.LabelTemplateType.Magnet12mm, profile);
            SetSlotPreviewOrPlaceholder(picPreviewFlask, Models.LabelTemplateType.Flask24mm, profile);

            bool needsPrimaryRender = AnySlotUncached(profile);
            Models.LabelTemplateProfile otherProfile = profile == Models.LabelTemplateProfile.Molecule
                ? Models.LabelTemplateProfile.Generic
                : Models.LabelTemplateProfile.Molecule;
            bool needsPrewarm = AnySlotUncached(otherProfile);

            if (!needsPrimaryRender && !needsPrewarm)
            {
                return;
            }

            _previewRenderCts = new CancellationTokenSource();
            _ = LoadAndPrewarmExamplePreviewsAsync(profile, otherProfile, requestVersion, _previewRenderCts.Token);
        }

        private bool AnySlotUncached(Models.LabelTemplateProfile profile)
        {
            foreach (Models.LabelTemplateType slot in Enum.GetValues<Models.LabelTemplateType>())
            {
                if (!_previewCache.ContainsKey((profile, slot)))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetSlotPreviewOrPlaceholder(
            PictureBox picBox,
            Models.LabelTemplateType slot,
            Models.LabelTemplateProfile profile)
        {
            if (_previewCache.TryGetValue((profile, slot), out Image? cachedImage))
            {
                AssignPreviewPictureBoxImage(picBox, cachedImage);
            }
            else
            {
                AssignPreviewPictureBoxImage(picBox, LabelPreviewRenderer.CreateLoadingPreviewBitmap(slot, profile));
            }
        }

        private async Task LoadAndPrewarmExamplePreviewsAsync(
            Models.LabelTemplateProfile requestedProfile,
            Models.LabelTemplateProfile secondaryProfile,
            long requestVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                await RenderProfileSlotsAsync(requestedProfile, requestVersion, updateUiOnSlotReady: true, cancellationToken);

                if (cancellationToken.IsCancellationRequested
                    || IsDisposed
                    || Disposing
                    || _resourcesDisposed
                    || requestVersion != _previewRenderVersion
                    || _currentStep != Models.SetupWizardStep.ObjectTypes)
                {
                    return;
                }

                await RenderProfileSlotsAsync(secondaryProfile, requestVersion, updateUiOnSlotReady: false, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignored when user switches profile, leaves ObjectTypes step, or closes the dialog.
            }
        }

        private async Task RenderProfileSlotsAsync(
            Models.LabelTemplateProfile profile,
            long requestVersion,
            bool updateUiOnSlotReady,
            CancellationToken cancellationToken)
        {
            Models.SampleDetailsDto example = LabelPreviewRenderer.BuildExampleSampleDetails(profile);
            string exampleFreeText = profile == Models.LabelTemplateProfile.Generic ? "Batch #4 / Substrate" : "";

            foreach (Models.LabelTemplateType slot in Enum.GetValues<Models.LabelTemplateType>())
            {
                if (cancellationToken.IsCancellationRequested
                    || IsDisposed
                    || Disposing
                    || _resourcesDisposed
                    || requestVersion != _previewRenderVersion
                    || _currentStep != Models.SetupWizardStep.ObjectTypes)
                {
                    return;
                }

                if (_previewCache.ContainsKey((profile, slot)))
                {
                    continue;
                }

                Image rendered = await _previewRenderer.RenderTemplatePreviewAsync(
                    slot,
                    profile,
                    example,
                    exampleFreeText,
                    cancellationToken);

                if (IsDisposed || Disposing || _resourcesDisposed)
                {
                    rendered.Dispose();
                    return;
                }

                Image imageToDisplay;
                if (_previewCache.TryGetValue((profile, slot), out Image? existing))
                {
                    rendered.Dispose();
                    imageToDisplay = existing;
                }
                else
                {
                    _previewCache[(profile, slot)] = rendered;
                    imageToDisplay = rendered;
                }

                if (updateUiOnSlotReady
                    && !cancellationToken.IsCancellationRequested
                    && requestVersion == _previewRenderVersion
                    && _activePreviewProfile == profile
                    && _currentStep == Models.SetupWizardStep.ObjectTypes)
                {
                    AssignPreviewPictureBoxImage(GetPreviewPictureBox(slot), imageToDisplay);
                }
            }
        }

        private PictureBox GetPreviewPictureBox(Models.LabelTemplateType slot)
        {
            return slot switch
            {
                Models.LabelTemplateType.SmallBox12mm => picPreviewSmallBox,
                Models.LabelTemplateType.Magnet12mm => picPreviewMagnet,
                Models.LabelTemplateType.Flask24mm => picPreviewFlask,
                _ => picPreviewSmallBox
            };
        }

        private void AssignPreviewPictureBoxImage(PictureBox? picBox, Image? nextImage)
        {
            if (picBox == null || picBox.IsDisposed)
            {
                if (nextImage != null && !IsCachedPreviewImage(nextImage))
                {
                    nextImage.Dispose();
                }
                return;
            }

            Image? previous = picBox.Image;
            if (ReferenceEquals(previous, nextImage))
            {
                return;
            }

            picBox.Image = nextImage;
            if (previous != null && !IsCachedPreviewImage(previous))
            {
                previous.Dispose();
            }
        }

        private bool IsCachedPreviewImage(Image image)
        {
            foreach (Image cached in _previewCache.Values)
            {
                if (ReferenceEquals(cached, image))
                {
                    return true;
                }
            }

            return false;
        }

        private void CancelPendingPreviewRender()
        {
            if (_previewRenderCts == null)
            {
                return;
            }

            try
            {
                _previewRenderCts.Cancel();
            }
            catch
            {
                // Ignore cancellation failure during disposal.
            }

            _previewRenderCts.Dispose();
            _previewRenderCts = null;
        }

        private void UpdatePreviewToggleVisuals()
        {
            bool isMolecule = _activePreviewProfile == Models.LabelTemplateProfile.Molecule;

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

        private static Models.LabelTemplateProfile GetDefaultProfileForCode(string code)
        {
            return code.Trim().Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase)
                ? Models.LabelTemplateProfile.Molecule
                : Models.LabelTemplateProfile.Generic;
        }

        private static string ProfileToDisplayString(Models.LabelTemplateProfile profile)
        {
            return profile == Models.LabelTemplateProfile.Molecule
                ? StyleMoleculeDisplay
                : StyleGenericDisplay;
        }

        private static Models.LabelTemplateProfile DisplayStringToProfile(string? display)
        {
            return string.Equals(display?.Trim(), StyleMoleculeDisplay, StringComparison.OrdinalIgnoreCase)
                ? Models.LabelTemplateProfile.Molecule
                : Models.LabelTemplateProfile.Generic;
        }

        private bool ValidateConnectionInputs(out string url, out string token, out string errorMessage)
        {
            url = (txtInstanceUrl.Text ?? "").Trim();
            token = (txtToken.Text ?? "").Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri)
                || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                errorMessage = "Enter a valid openBIS Instance URL starting with http:// or https://.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                errorMessage = "Enter an openBIS Personal Access Token.";
                return false;
            }

            errorMessage = "";
            return true;
        }

        private bool IsValidatedForCurrentInputs()
        {
            string url = (txtInstanceUrl.Text ?? "").Trim();
            string token = (txtToken.Text ?? "").Trim();
            return CachedServerTypes != null
                   && string.Equals(_validatedUrl, url, StringComparison.Ordinal)
                   && string.Equals(_validatedToken, token, StringComparison.Ordinal);
        }

        private bool TryPersistConfig(Action saveAction, out string errorMessage)
        {
            try
            {
                saveAction();
                errorMessage = "";
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Could not save configuration files: {ex.Message}";
                return false;
            }
        }

        private async Task<Models.OpenBisTypesResult?> RunLiveConnectionCheckAsync(bool saveCredentialsOnSuccess)
        {
            if (_isBusy)
            {
                return null;
            }

            if (!ValidateConnectionInputs(out string url, out string token, out string inputError))
            {
                SetConnectionStatus(inputError, StatusSeverity.Error);
                return null;
            }

            SetBusy(true);
            SetConnectionStatus("Connecting to openBIS and validating token...", StatusSeverity.Info);

            try
            {
                Models.OpenBisTypesResult result = await _openBisClient.QueryTypesAsync(url, token);
                if (IsDisposed || Disposing)
                {
                    return result;
                }

                if (!result.Success)
                {
                    _validatedUrl = "";
                    _validatedToken = "";
                    string message = FormatOpenBisFailureMessage(result);
                    SetConnectionStatus(message, StatusSeverity.Error);
                    if (string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase))
                    {
                        txtToken.Focus();
                        txtToken.SelectAll();
                    }

                    return result;
                }

                _validatedUrl = url;
                _validatedToken = token;
                _autoFetchAttemptedForCurrentInputs = true;

                ApplyServerTypes(result.Types);

                if (saveCredentialsOnSuccess && !TryPersistConfig(
                        () => _configService.SaveAppConfig(new Models.AppConfig
                        {
                            OpenBisUrl = url,
                            Token = token
                        }),
                        out string saveError))
                {
                    SetConnectionStatus(saveError, StatusSeverity.Error);
                    return null;
                }

                string suffix = saveCredentialsOnSuccess ? " Credentials saved." : "";
                SetConnectionStatus(
                    $"Connected successfully ({result.Types.Count} object type(s) found).{suffix}",
                    StatusSeverity.Success);
                return result;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    SetConnectionStatus($"Unexpected error while checking openBIS connection: {ex.Message}", StatusSeverity.Error);
                }

                return null;
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
            if (_isBusy)
            {
                return;
            }

            _autoFetchAttemptedForCurrentInputs = true;

            if (!ValidateConnectionInputs(out string url, out string token, out string inputError))
            {
                if (redirectToConnectionOnExpiredToken)
                {
                    ShowStep(Models.SetupWizardStep.Connection);
                    SetConnectionStatus(inputError, StatusSeverity.Error);
                }
                else
                {
                    SetTypesStatus($"Offline mode: showing saved object types ({inputError})", StatusSeverity.Warning);
                }
                return;
            }

            SetBusy(true);
            SetTypesStatus("Loading object types from openBIS...", StatusSeverity.Info);

            try
            {
                Models.OpenBisTypesResult result = await _openBisClient.QueryTypesAsync(url, token);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (!result.Success)
                {
                    if (redirectToConnectionOnExpiredToken
                        && string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowStep(Models.SetupWizardStep.Connection);
                        SetConnectionStatus(FormatOpenBisFailureMessage(result), StatusSeverity.Error);
                        txtToken.Focus();
                        txtToken.SelectAll();
                        return;
                    }

                    SetTypesStatus(
                        $"Offline mode: showing saved object types from local config ({FormatOpenBisFailureMessage(result)})",
                        StatusSeverity.Warning);
                    return;
                }

                _validatedUrl = url;
                _validatedToken = token;
                ApplyServerTypes(result.Types);
                SetTypesStatus($"Loaded {result.Types.Count} object type(s) from openBIS.", StatusSeverity.Success);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    string message = $"Offline mode: showing saved object types ({ex.Message})";
                    SetTypesStatus(message, StatusSeverity.Warning);
                    if (_currentStep == Models.SetupWizardStep.Connection)
                    {
                        SetConnectionStatus(message, StatusSeverity.Warning);
                    }
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
            }
        }

        private static string FormatOpenBisFailureMessage(Models.OpenBisTypesResult result)
        {
            if (string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(result.Error)
                    ? "Your openBIS personal access token has expired or is invalid. Please enter a new token."
                    : $"Token expired or invalid: {result.Error}";
            }

            if (string.Equals(result.ErrorType, "connection_error", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(result.Error)
                    ? "Could not connect to the openBIS server."
                    : $"Connection error: {result.Error}";
            }

            return string.IsNullOrWhiteSpace(result.Error)
                ? "Failed to query openBIS object types."
                : result.Error;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            UseWaitCursor = busy;
            txtInstanceUrl.ReadOnly = busy;
            txtToken.ReadOnly = busy;
            btnTestConnection.Enabled = !busy;
            btnNextFinish.Enabled = !busy;
            btnRefreshTypes.Enabled = !busy;
            btnTabConnection.Enabled = !busy;
            btnTabObjectTypes.Enabled = !busy;
            btnTabPrinters.Enabled = !busy;
            btnBack.Enabled = !busy && Mode == SetupDialogMode.Wizard && _currentStep != Models.SetupWizardStep.Connection;
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
            _validatedUrl = "";
            _validatedToken = "";
            _autoFetchAttemptedForCurrentInputs = false;
            CachedServerTypes = null;
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
                        ShowStep(Models.SetupWizardStep.ObjectTypes);
                        SetTypesStatus(validationError, StatusSeverity.Error);
                    }
                    else if (validationError.StartsWith("Could not save configuration files:", StringComparison.Ordinal))
                    {
                        if (_currentStep == Models.SetupWizardStep.Connection)
                        {
                            SetConnectionStatus(validationError, StatusSeverity.Error);
                        }
                        else if (_currentStep == Models.SetupWizardStep.ObjectTypes)
                        {
                            SetTypesStatus(validationError, StatusSeverity.Error);
                        }
                    }
                    else
                    {
                        ShowStep(Models.SetupWizardStep.Connection);
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
                if (!TryPersistConfig(
                        () => _configService.SaveAppConfig(new Models.AppConfig
                        {
                            OpenBisUrl = txtInstanceUrl.Text.Trim(),
                            Token = txtToken.Text.Trim()
                        }),
                        out string saveError))
                {
                    SetConnectionStatus(saveError, StatusSeverity.Error);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            Models.OpenBisTypesResult? result = await RunLiveConnectionCheckAsync(saveCredentialsOnSuccess: true);
            if (IsDisposed || Disposing || result == null || !result.Success)
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
                case Models.SetupWizardStep.Connection:
                {
                    if (!IsValidatedForCurrentInputs())
                    {
                        Models.OpenBisTypesResult? result = await RunLiveConnectionCheckAsync(saveCredentialsOnSuccess: true);
                        if (IsDisposed || Disposing || result == null || !result.Success)
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (!TryPersistConfig(
                                () => _configService.SaveAppConfig(new Models.AppConfig
                                {
                                    OpenBisUrl = txtInstanceUrl.Text.Trim(),
                                    Token = txtToken.Text.Trim()
                                }),
                                out string saveError))
                        {
                            SetConnectionStatus(saveError, StatusSeverity.Error);
                            return;
                        }
                    }

                    bool hasTypes = _configService.HasValidConfiguredTypes();
                    bool hasPrinters = _configService.HasPrinterConfig();

                    if (hasTypes && hasPrinters)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    ShowStep(!hasTypes ? Models.SetupWizardStep.ObjectTypes : Models.SetupWizardStep.Printers);
                    if (_currentStep == Models.SetupWizardStep.ObjectTypes && CachedServerTypes == null)
                    {
                        await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
                    }
                    break;
                }

                case Models.SetupWizardStep.ObjectTypes:
                {
                    CommitGridEdits();
                    SyncVisibleGridToMaster();
                    List<Models.ConfiguredOpenBisType> selectedTypes = GetSelectedConfiguredTypes();
                    if (selectedTypes.Count == 0)
                    {
                        SetTypesStatus("Select at least one object type.", StatusSeverity.Error);
                        return;
                    }

                    if (_configService.HasPrinterConfig() && !_cameFromPrintersStep)
                    {
                        if (!TryPersistConfig(() => _configService.SaveConfiguredTypes(selectedTypes), out string saveError))
                        {
                            SetTypesStatus(saveError, StatusSeverity.Error);
                            return;
                        }

                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    ShowStep(Models.SetupWizardStep.Printers);
                    break;
                }

                case Models.SetupWizardStep.Printers:
                {
                    if (_hasVisitedTypesPage)
                    {
                        CommitGridEdits();
                        SyncVisibleGridToMaster();
                        List<Models.ConfiguredOpenBisType> selectedTypes = GetSelectedConfiguredTypes();
                        if (selectedTypes.Count == 0)
                        {
                            ShowStep(Models.SetupWizardStep.ObjectTypes);
                            SetTypesStatus("Select at least one object type.", StatusSeverity.Error);
                            return;
                        }

                        if (!TryPersistConfig(() => _configService.SaveConfiguredTypes(selectedTypes), out string typesSaveError))
                        {
                            ShowFooterError(typesSaveError);
                            return;
                        }
                    }

                    if (!TryPersistConfig(() => _configService.SavePrinterConfig(BuildPrinterConfigFromSelections()), out string printerSaveError))
                    {
                        ShowFooterError(printerSaveError);
                        return;
                    }

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

            if (_currentStep == Models.SetupWizardStep.Printers)
            {
                _cameFromPrintersStep = true;
                ShowStep(Models.SetupWizardStep.ObjectTypes);
                if (CachedServerTypes == null && !_autoFetchAttemptedForCurrentInputs)
                {
                    await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
                }
            }
            else if (_currentStep == Models.SetupWizardStep.ObjectTypes)
            {
                ShowStep(Models.SetupWizardStep.Connection);
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
                ShowStep(Models.SetupWizardStep.Connection);
            }
        }

        private async void btnTabObjectTypes_Click(object? sender, EventArgs e)
        {
            if (_isBusy || Mode != SetupDialogMode.Settings)
            {
                return;
            }

            ShowStep(Models.SetupWizardStep.ObjectTypes);
            if (CachedServerTypes == null && !_autoFetchAttemptedForCurrentInputs)
            {
                await LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken: true);
            }
        }

        private void btnTabPrinters_Click(object? sender, EventArgs e)
        {
            if (!_isBusy && Mode == SetupDialogMode.Settings)
            {
                ShowStep(Models.SetupWizardStep.Printers);
            }
        }

        private async void btnRefreshTypes_Click(object? sender, EventArgs e)
        {
            if (_isBusy)
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
            CommitGridEdits();
            SyncVisibleGridToMaster();
            RefreshObjectTypeGrid();
            if (IsHandleCreated && _currentStep == Models.SetupWizardStep.ObjectTypes)
            {
                ShowExamplePreviews(GetSelectedOrDefaultPreviewProfile());
            }
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
            if (row.Tag is not TypeRowEntry entry)
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
                if (IsHandleCreated)
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

            if (dgvObjectTypes.CurrentRow?.Tag is TypeRowEntry entry && IsHandleCreated)
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
            ShowExamplePreviews(Models.LabelTemplateProfile.Molecule);
        }

        private void btnPreviewGeneric_Click(object? sender, EventArgs e)
        {
            ShowExamplePreviews(Models.LabelTemplateProfile.Generic);
        }
    }
}
