using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenBISLabelStudio.Models;
using OpenBISLabelStudio.Services;

namespace OpenBISLabelStudio
{
    public partial class Form1 : Form
    {
        private const string PLACEHOLDER_TYPE = "Select an object type...";

        private readonly AppConfigService _configService;
        private readonly OpenBisClient _openBisClient;
        private readonly LabelPreviewRenderer _previewRenderer;
        private readonly Func<IReadOnlyList<string>>? _installedPrintersProvider;
        private readonly ToolTip _statusToolTip = new ToolTip();

        private AppConfig _config = new AppConfig();
        private List<ConfiguredOpenBisType> _configuredTypes = new List<ConfiguredOpenBisType>();
        private PrinterConfig _printerConfig = new PrinterConfig();
        private List<SampleDto> _loadedSamples = new List<SampleDto>();
        private SampleDetailsDto? _currentSampleDetails;
        private string _currentSelectedTypeCode = "";
        private bool _isPopulatingPrinters;
        private bool _isPopulatingTypes;
        private readonly TokenRecoveryCoordinator _tokenRecoveryCoordinator = new TokenRecoveryCoordinator();
        private long _typeRefreshRequestVersion;
        private long _typeSelectionRequestVersion;
        private long _sampleDetailsRequestVersion;

        public Form1()
            : this(new AppConfigService())
        {
        }

        public Form1(
            AppConfigService configService,
            OpenBisClient? openBisClient = null,
            LabelPreviewRenderer? previewRenderer = null,
            Func<IReadOnlyList<string>>? installedPrintersProvider = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _openBisClient = openBisClient ?? new OpenBisClient();
            _previewRenderer = previewRenderer ?? new LabelPreviewRenderer(_configService);
            _installedPrintersProvider = installedPrintersProvider;

            InitializeComponent();
        }

        /// <summary>
        /// Resolves the <see cref="LabelTemplateProfile"/> for the active openBIS type from
        /// <paramref name="configuredTypes"/>, falling back to <paramref name="sampleDetailsType"/>
        /// and defaulting to <see cref="LabelTemplateProfile.Generic"/>.
        /// </summary>
        public static LabelTemplateProfile ResolveTemplateProfile(
            string? selectedTypeCode,
            IReadOnlyList<ConfiguredOpenBisType>? configuredTypes,
            string? sampleDetailsType)
        {
            if (configuredTypes == null || configuredTypes.Count == 0)
            {
                return LabelTemplateProfile.Generic;
            }

            if (TryFindConfiguredProfile(selectedTypeCode, configuredTypes, out LabelTemplateProfile profile))
            {
                return profile;
            }

            if (TryFindConfiguredProfile(sampleDetailsType, configuredTypes, out profile))
            {
                return profile;
            }

            return LabelTemplateProfile.Generic;
        }

        private static bool TryFindConfiguredProfile(
            string? typeCode,
            IReadOnlyList<ConfiguredOpenBisType> configuredTypes,
            out LabelTemplateProfile profile)
        {
            string normalized = (typeCode ?? "").Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                foreach (ConfiguredOpenBisType entry in configuredTypes)
                {
                    if (entry != null
                        && !string.IsNullOrWhiteSpace(entry.Code)
                        && string.Equals(entry.Code.Trim(), normalized, StringComparison.OrdinalIgnoreCase)
                        && Enum.IsDefined(typeof(LabelTemplateProfile), entry.TemplateProfile))
                    {
                        profile = entry.TemplateProfile;
                        return true;
                    }
                }
            }

            profile = LabelTemplateProfile.Generic;
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="currentConfig"/> holds a non-empty token and either
        /// its URL or token changed compared to the credentials captured for an in-flight request.
        /// </summary>
        public static bool CredentialsChangedSinceRequest(string? requestUrl, string? requestToken, AppConfig? currentConfig)
        {
            return currentConfig != null
                   && !string.IsNullOrWhiteSpace(currentConfig.Token)
                   && (!string.Equals(currentConfig.Token, requestToken, StringComparison.Ordinal)
                       || !string.Equals(currentConfig.OpenBisUrl, requestUrl, StringComparison.Ordinal));
        }

        partial void DisposeManagedResources()
        {
            SetPictureBoxImage(smallBoxLabelPreview, null);
            SetPictureBoxImage(magnetLabelPreview, null);
            SetPictureBoxImage(flaskLabelPreview, null);
            _statusToolTip.Dispose();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            IReadOnlyList<OpenBisTypeInfoDto>? cachedWizardServerTypes = null;
            SetupWizardStep missingStep = _configService.GetFirstMissingWizardStep();

            if (missingStep != SetupWizardStep.None)
            {
                using var wizard = new SetupSettingsForm(
                    _configService,
                    _openBisClient,
                    _previewRenderer,
                    SetupDialogMode.Wizard,
                    missingStep,
                    installedPrintersProvider: _installedPrintersProvider);

                DialogResult wizardResult = wizard.ShowDialog(this);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (wizardResult != DialogResult.OK && _configService.GetFirstMissingWizardStep() != SetupWizardStep.None)
                {
                    BeginInvoke(new Action(Close));
                    return;
                }

                cachedWizardServerTypes = wizard.CachedServerTypes;
            }

            ReloadConfiguredStateFromDisk();
            PopulateInstalledPrinters();
            PopulateOpenBisTypeComboBox(preferredTypeCode: null);
            UpdateFreeTextControlsState();

            if (cachedWizardServerTypes != null)
            {
                ClearStatusNotice();
                ApplyServerTypeDescriptions(cachedWizardServerTypes);
                return;
            }

            await RefreshTypeDescriptionsFromServerAsync();
        }

        private void ReloadConfiguredStateFromDisk()
        {
            _config = _configService.LoadAppConfig() ?? new AppConfig();
            _configuredTypes = _configService.LoadConfiguredTypes();
            _printerConfig = _configService.LoadPrinterConfig() ?? AppConfigService.CreateDefaultPrinterConfig();
        }

        private void PopulateInstalledPrinters()
        {
            _isPopulatingPrinters = true;
            try
            {
                IReadOnlyList<string> rawPrinters = _installedPrintersProvider != null
                    ? _installedPrintersProvider()
                    : GetSystemInstalledPrinters();

                List<string> printers = (rawPrinters ?? Array.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (printers.Count == 0)
                {
                    PrinterConfig defaults = AppConfigService.CreateDefaultPrinterConfig();
                    printers = new[]
                        {
                            _printerConfig.SmallBoxPrinter,
                            _printerConfig.MagnetPrinter,
                            _printerConfig.FlaskPrinter
                        }
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (printers.Count == 0)
                    {
                        printers = new[]
                            {
                                defaults.SmallBoxPrinter,
                                defaults.MagnetPrinter,
                                defaults.FlaskPrinter
                            }
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }

                PopulatePrinterCombo(smallBoxPrinterComboBox, printers, _printerConfig.SmallBoxPrinter);
                PopulatePrinterCombo(magnetPrinterComboBox, printers, _printerConfig.MagnetPrinter);
                PopulatePrinterCombo(flaskPrinterComboBox, printers, _printerConfig.FlaskPrinter);
            }
            finally
            {
                _isPopulatingPrinters = false;
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

        private static void PopulatePrinterCombo(ComboBox comboBox, IReadOnlyList<string> printers, string preferredName)
        {
            comboBox.Items.Clear();
            foreach (string printer in printers)
            {
                comboBox.Items.Add(printer);
            }

            if (comboBox.Items.Count > 0)
            {
                int index = AppConfigService.FindBestPrinterIndex(printers, preferredName);
                comboBox.SelectedIndex = index >= 0 && index < comboBox.Items.Count ? index : 0;
            }
        }

        private void PrinterComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isPopulatingPrinters)
            {
                return;
            }

            string smallBox = smallBoxPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.SmallBoxPrinter;
            string magnet = magnetPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.MagnetPrinter;
            string flask = flaskPrinterComboBox.SelectedItem?.ToString() ?? _printerConfig.FlaskPrinter;

            if (string.Equals(_printerConfig.SmallBoxPrinter, smallBox, StringComparison.Ordinal)
                && string.Equals(_printerConfig.MagnetPrinter, magnet, StringComparison.Ordinal)
                && string.Equals(_printerConfig.FlaskPrinter, flask, StringComparison.Ordinal))
            {
                return;
            }

            _printerConfig.SmallBoxPrinter = smallBox;
            _printerConfig.MagnetPrinter = magnet;
            _printerConfig.FlaskPrinter = flask;

            try
            {
                _configService.SavePrinterConfig(_printerConfig);
            }
            catch (Exception ex)
            {
                ShowStatusNotice($"Could not save printer selection: {ex.Message}", ex.Message);
            }
        }

        private void PopulateOpenBisTypeComboBox(string? preferredTypeCode)
        {
            _isPopulatingTypes = true;
            try
            {
                List<OpenBisTypeItem> typeItems = _configuredTypes
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Code))
                    .Select(t =>
                    {
                        string cleanCode = t.Code.Trim();
                        string displayText = string.IsNullOrWhiteSpace(t.DisplayName)
                            ? AppConfigService.FormatFriendlyName(cleanCode)
                            : t.DisplayName.Trim();
                        return new OpenBisTypeItem
                        {
                            Code = cleanCode,
                            DisplayText = displayText,
                            TemplateProfile = t.TemplateProfile
                        };
                    })
                    .OrderBy(t => t.DisplayText, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                openbisTypeComboBox.Items.Clear();
                openbisTypeComboBox.Items.Add(new OpenBisTypeItem { Code = "", DisplayText = PLACEHOLDER_TYPE });

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
                if (matchedIndex > 0 && openbisTypeComboBox.SelectedItem is OpenBisTypeItem activeItem)
                {
                    _currentSelectedTypeCode = activeItem.Code;
                    selectedOpenbisTypeLabel.Text = $"{activeItem.DisplayText}:";
                }
                else
                {
                    _currentSelectedTypeCode = "";
                    selectedOpenbisTypeLabel.Text = "Type:";
                }
            }
            finally
            {
                _isPopulatingTypes = false;
            }
        }

        private async Task RefreshTypeDescriptionsFromServerAsync()
        {
            long requestVersion = ++_typeRefreshRequestVersion;

            if (string.IsNullOrWhiteSpace(_config.OpenBisUrl) || string.IsNullOrWhiteSpace(_config.Token))
            {
                return;
            }

            (bool shouldProceed, bool attemptedRecovery, OpenBisTypesResult? result) = await ExecuteWithExpiredTokenRecoveryAsync(
                (url, token) => _openBisClient.QueryTypesAsync(url, token),
                r => r.Success,
                r => r.ErrorType,
                r => r.Error,
                () => requestVersion != _typeRefreshRequestVersion,
                "openBIS token expired or invalid — showing cached object types. Click Settings... to update credentials.");

            if (!shouldProceed || result == null)
            {
                return;
            }

            if (!result.Success)
            {
                string errDetail = FormatOpenBisError(result.Error, attemptedRecovery
                    ? "Could not refresh openBIS object types."
                    : "Could not connect to openBIS.");
                string banner = attemptedRecovery
                    ? $"Offline mode: showing cached object types ({errDetail})"
                    : string.Equals(result.ErrorType, "connection_error", StringComparison.OrdinalIgnoreCase)
                        ? $"Offline mode: could not reach openBIS — showing cached object types. ({errDetail})"
                        : $"Notice: could not refresh openBIS object types — showing cached object types. ({errDetail})";
                ShowStatusNotice(banner, errDetail);
                return;
            }

            ClearStatusNotice();
            if (result.Types != null)
            {
                ApplyServerTypeDescriptions(result.Types);
            }
        }

        private async Task<(bool ShouldProceed, bool AttemptedRecovery, TResult? Result)> ExecuteWithExpiredTokenRecoveryAsync<TResult>(
            Func<string, string, Task<TResult>> queryAsync,
            Func<TResult, bool> isSuccess,
            Func<TResult, string?> getErrorType,
            Func<TResult, string?> getError,
            Func<bool> isVersionStale,
            string cancelledStatusNotice)
        {
            bool IsStale() => IsDisposed || Disposing || isVersionStale();

            string requestUrl = _config.OpenBisUrl;
            string requestToken = _config.Token;

            TResult result = await queryAsync(requestUrl, requestToken);
            if (IsStale())
            {
                return (false, false, default);
            }

            if (!isSuccess(result) && IsTokenExpiredError(getErrorType(result))
                && CredentialsChangedSinceRequest(requestUrl, requestToken, _config))
            {
                requestUrl = _config.OpenBisUrl;
                requestToken = _config.Token;
                result = await queryAsync(requestUrl, requestToken);
                if (IsStale())
                {
                    return (false, false, default);
                }
            }

            bool attemptedRecovery = false;
            if (!isSuccess(result) && IsTokenExpiredError(getErrorType(result)))
            {
                attemptedRecovery = true;
                string? expiredError = getError(result);
                if (!await TryRecoverExpiredTokenAsync(expiredError))
                {
                    if (!IsStale())
                    {
                        ShowStatusNotice(cancelledStatusNotice, expiredError);
                    }

                    return (false, true, default);
                }

                if (IsStale())
                {
                    return (false, true, default);
                }

                result = await queryAsync(_config.OpenBisUrl, _config.Token);
                if (IsStale())
                {
                    return (false, true, default);
                }
            }

            return (true, attemptedRecovery, result);
        }

        private static bool IsTokenExpiredError(string? errorType)
        {
            return string.Equals(errorType, "token_expired", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyServerTypeDescriptions(IReadOnlyList<OpenBisTypeInfoDto> serverTypes)
        {
            var descriptionsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenBisTypeInfoDto serverType in serverTypes)
            {
                if (serverType != null
                    && !string.IsNullOrWhiteSpace(serverType.Code)
                    && !string.IsNullOrWhiteSpace(serverType.Description))
                {
                    descriptionsByCode[serverType.Code.Trim()] = serverType.Description.Trim();
                }
            }

            bool anyChanged = false;
            foreach (ConfiguredOpenBisType configured in _configuredTypes)
            {
                if (configured == null || string.IsNullOrWhiteSpace(configured.Code))
                {
                    continue;
                }

                if (descriptionsByCode.TryGetValue(configured.Code.Trim(), out string? desc)
                    && !string.Equals(configured.DisplayName, desc, StringComparison.Ordinal))
                {
                    configured.DisplayName = desc;
                    anyChanged = true;
                }
            }

            if (!anyChanged)
            {
                return;
            }

            try
            {
                _configService.SaveConfiguredTypes(_configuredTypes);
            }
            catch
            {
                // Ignore best-effort displayName cache persistence failure.
            }

            PopulateOpenBisTypeComboBox(_currentSelectedTypeCode);
        }

        private Task<bool> TryRecoverExpiredTokenAsync(string? errorDetail)
        {
            if (IsDisposed || Disposing)
            {
                return Task.FromResult(false);
            }

            return _tokenRecoveryCoordinator.RecoverAsync(() => Task.FromResult(ShowExpiredTokenRecoveryDialog(errorDetail)));
        }

        private bool ShowExpiredTokenRecoveryDialog(string? errorDetail)
        {
            if (IsDisposed || Disposing)
            {
                return false;
            }

            string warning = string.IsNullOrWhiteSpace(errorDetail)
                ? "Your openBIS personal access token has expired or is invalid. Please enter a new token."
                : $"Your openBIS personal access token has expired or is invalid ({errorDetail.Trim()}). Please enter a new token.";

            using var recoveryForm = new SetupSettingsForm(
                _configService,
                _openBisClient,
                _previewRenderer,
                SetupDialogMode.ExpiredTokenRecovery,
                SetupWizardStep.Connection,
                initialWarning: warning,
                installedPrintersProvider: _installedPrintersProvider);

            DialogResult result = recoveryForm.ShowDialog(this);
            if (IsDisposed || Disposing || result != DialogResult.OK)
            {
                return false;
            }

            AppConfig? updatedConfig = _configService.LoadAppConfig();
            if (updatedConfig != null)
            {
                _config = updatedConfig;
            }

            ClearStatusNotice();
            return true;
        }

        private LabelTemplateProfile GetActiveTemplateProfile()
        {
            return ResolveTemplateProfile(_currentSelectedTypeCode, _configuredTypes, _currentSampleDetails?.SampleType);
        }

        private void UpdateFreeTextControlsState()
        {
            bool isMoleculeProfile = GetActiveTemplateProfile() == LabelTemplateProfile.Molecule;
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
            if (_isPopulatingTypes || openbisTypeComboBox.SelectedItem is not OpenBisTypeItem selectedItem)
            {
                return;
            }

            if (string.IsNullOrEmpty(selectedItem.Code) || selectedItem.DisplayText == PLACEHOLDER_TYPE)
            {
                ++_typeSelectionRequestVersion;
                ++_sampleDetailsRequestVersion;
                selectedOpenbisTypeLabel.Text = "Type:";
                openbisObjectsComboBox.Items.Clear();
                _loadedSamples.Clear();
                _currentSelectedTypeCode = "";
                ClearAllPreviews();
                UpdateFreeTextControlsState();
                return;
            }

            long requestVersion = ++_typeSelectionRequestVersion;
            ++_sampleDetailsRequestVersion;
            _currentSelectedTypeCode = selectedItem.Code;
            selectedOpenbisTypeLabel.Text = $"{selectedItem.DisplayText}:";
            openbisObjectsComboBox.Items.Clear();
            _loadedSamples.Clear();
            ClearAllPreviews();
            UpdateFreeTextControlsState();

            if (string.IsNullOrWhiteSpace(_config.OpenBisUrl))
            {
                MessageBox.Show("openBIS URL is missing. Please configure it in Settings...", "Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetControlsEnabled(false);
            Cursor = Cursors.WaitCursor;

            try
            {
                (bool shouldProceed, _, OpenBisListResult? result) = await ExecuteWithExpiredTokenRecoveryAsync(
                    (url, token) => _openBisClient.QuerySamplesByTypeAsync(url, token, selectedItem.Code),
                    r => r.Success,
                    r => r.ErrorType,
                    r => r.Error,
                    () => requestVersion != _typeSelectionRequestVersion,
                    "openBIS token expired or invalid — click Settings... to update credentials.");

                if (!shouldProceed || result == null)
                {
                    return;
                }

                if (result.Success && result.Data != null)
                {
                    ClearStatusNotice();
                    _loadedSamples = result.Data;

                    foreach (SampleDto sample in result.Data)
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
                    MessageBox.Show(
                        FormatOpenBisError(result.Error, "Failed to fetch items from openBIS."),
                        "openBIS Fetch Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing && requestVersion == _typeSelectionRequestVersion)
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
            long requestVersion = ++_sampleDetailsRequestVersion;

            SetControlsEnabled(false);
            Cursor = Cursors.WaitCursor;

            try
            {
                (bool shouldProceed, _, OpenBisDetailsResult? result) = await ExecuteWithExpiredTokenRecoveryAsync(
                    (url, token) => _openBisClient.QuerySampleDetailsAsync(url, token, selectedSample.PermId),
                    r => r.Success,
                    r => r.ErrorType,
                    r => r.Error,
                    () => requestVersion != _sampleDetailsRequestVersion,
                    "openBIS token expired or invalid — click Settings... to update credentials.");

                if (!shouldProceed || result == null)
                {
                    return;
                }

                if (result.Success && result.Details != null)
                {
                    ClearStatusNotice();
                    _currentSampleDetails = result.Details;
                    LabelTemplateProfile activeProfile = GetActiveTemplateProfile();
                    UpdateFreeTextControlsState();

                    RenderAllSlotPreviews(activeProfile, _currentSampleDetails);

                    // Automatically check all preview checkboxes for convenience
                    smallBoxLabelCheckBox.Checked = true;
                    magnetLabelCheckBox.Checked = true;
                    flaskLabelCheckBox.Checked = true;
                }
                else
                {
                    MessageBox.Show(
                        FormatOpenBisError(result.Error, "Could not fetch sample details."),
                        "openBIS Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing && requestVersion == _sampleDetailsRequestVersion)
                {
                    SetControlsEnabled(true);
                    Cursor = Cursors.Default;
                }
            }
        }

        // =========================================================================
        // DYNAMIC FREE TEXT COMMIT HANDLERS (ON LEAVE OR ENTER)
        // =========================================================================
        private void magnetFreeTextBox_Leave(object sender, EventArgs e)
        {
            if (_currentSampleDetails != null && GetActiveTemplateProfile() == LabelTemplateProfile.Generic)
            {
                RenderSlotPreview(
                    magnetLabelPreview,
                    LabelTemplateType.Magnet12mm,
                    LabelTemplateProfile.Generic,
                    _currentSampleDetails,
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
            if (_currentSampleDetails != null && GetActiveTemplateProfile() == LabelTemplateProfile.Generic)
            {
                RenderSlotPreview(
                    flaskLabelPreview,
                    LabelTemplateType.Flask24mm,
                    LabelTemplateProfile.Generic,
                    _currentSampleDetails,
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

        private void RenderAllSlotPreviews(LabelTemplateProfile profile, SampleDetailsDto details)
        {
            string magnetFreeText = profile == LabelTemplateProfile.Generic ? magnetFreeTextBox.Text : "";
            string flaskFreeText = profile == LabelTemplateProfile.Generic ? flaskFreeTextBox.Text : "";

            RenderSlotPreview(smallBoxLabelPreview, LabelTemplateType.SmallBox12mm, profile, details, "");
            RenderSlotPreview(magnetLabelPreview, LabelTemplateType.Magnet12mm, profile, details, magnetFreeText);
            RenderSlotPreview(flaskLabelPreview, LabelTemplateType.Flask24mm, profile, details, flaskFreeText);
        }

        private void RenderSlotPreview(
            PictureBox picBox,
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            SampleDetailsDto details,
            string freeText)
        {
            Image previewImage = _previewRenderer.RenderTemplatePreview(slot, profile, details, freeText);
            SetPictureBoxImage(picBox, previewImage);
        }

        private static void SetPictureBoxImage(PictureBox? picBox, Image? newImage)
        {
            if (picBox == null)
            {
                newImage?.Dispose();
                return;
            }

            Image? previous = picBox.Image;
            picBox.Image = newImage;
            if (previous != null && !ReferenceEquals(previous, newImage))
            {
                previous.Dispose();
            }
        }

        private void ClearAllPreviews()
        {
            _currentSampleDetails = null;

            SetPictureBoxImage(smallBoxLabelPreview, null);
            SetPictureBoxImage(magnetLabelPreview, null);
            SetPictureBoxImage(flaskLabelPreview, null);

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
            btnSettings.Enabled = enabled;
        }

        private void ShowStatusNotice(string message, string? tooltipDetail = null)
        {
            lblStatusNotice.Text = message;
            lblStatusNotice.Visible = !string.IsNullOrWhiteSpace(message);
            _statusToolTip.SetToolTip(lblStatusNotice, string.IsNullOrWhiteSpace(tooltipDetail) ? message : tooltipDetail);
        }

        private void ClearStatusNotice()
        {
            lblStatusNotice.Text = "";
            lblStatusNotice.Visible = false;
            _statusToolTip.SetToolTip(lblStatusNotice, null);
        }

        private static string FormatOpenBisError(string? error, string fallback)
        {
            return string.IsNullOrWhiteSpace(error) ? fallback : error;
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

            LabelTemplateProfile activeProfile = GetActiveTemplateProfile();
            var queuedJobs = new List<(LabelTemplateType Slot, string LabelName, string AssignedPrinter)>();

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

            var sb = new StringBuilder();
            sb.AppendLine($"Sample: {_currentSampleDetails.Name} ({_currentSampleDetails.PermId})");
            sb.AppendLine($"Template Profile: {activeProfile}");
            sb.AppendLine();
            sb.AppendLine("The selected templates are configured to be sent to the following user-selected printers:");
            sb.AppendLine();

            foreach (var job in queuedJobs)
            {
                string templateFileName = Path.GetFileName(_configService.GetTemplateFilePath(job.Slot, activeProfile));
                sb.AppendLine($"  • {job.LabelName} ({templateFileName})  ➔  [{job.AssignedPrinter}]");
            }

            sb.AppendLine();
            sb.AppendLine("(Note: Direct printing is paused as requested.)");

            MessageBox.Show(sb.ToString(), "Printer Routing & Queue", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnSettings_Click(object? sender, EventArgs e)
        {
            // Cancel any in-flight startup type refresh so it cannot open recovery UI over Settings or apply stale data.
            ++_typeRefreshRequestVersion;

            string previousUrl = _config.OpenBisUrl;
            string previousToken = _config.Token;
            string previousTypeCode = _currentSelectedTypeCode;
            LabelTemplateProfile previousProfile = GetActiveTemplateProfile();

            using var settingsForm = new SetupSettingsForm(
                _configService,
                _openBisClient,
                _previewRenderer,
                SetupDialogMode.Settings,
                SetupWizardStep.Connection,
                installedPrintersProvider: _installedPrintersProvider);

            if (settingsForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ++_typeRefreshRequestVersion;
            ReloadConfiguredStateFromDisk();
            PopulateInstalledPrinters();

            bool credentialsChanged = !string.Equals(previousUrl, _config.OpenBisUrl, StringComparison.Ordinal)
                                      || !string.Equals(previousToken, _config.Token, StringComparison.Ordinal);

            bool activeTypeStillConfigured = !string.IsNullOrWhiteSpace(previousTypeCode)
                && _configuredTypes.Any(t => string.Equals(t.Code, previousTypeCode, StringComparison.OrdinalIgnoreCase));

            if (credentialsChanged || !activeTypeStillConfigured)
            {
                ++_typeSelectionRequestVersion;
                ++_sampleDetailsRequestVersion;
                PopulateOpenBisTypeComboBox(preferredTypeCode: null);
                openbisObjectsComboBox.Items.Clear();
                _loadedSamples.Clear();
                ClearAllPreviews();
                UpdateFreeTextControlsState();
            }
            else
            {
                PopulateOpenBisTypeComboBox(preferredTypeCode: previousTypeCode);
                LabelTemplateProfile updatedProfile = GetActiveTemplateProfile();
                UpdateFreeTextControlsState();

                if (_currentSampleDetails != null && updatedProfile != previousProfile)
                {
                    RenderAllSlotPreviews(updatedProfile, _currentSampleDetails);
                }
            }

            if (settingsForm.CachedServerTypes != null)
            {
                ClearStatusNotice();
                ApplyServerTypeDescriptions(settingsForm.CachedServerTypes);
            }
            else if (credentialsChanged)
            {
                ClearStatusNotice();
            }
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {
        }
    }

    /// <summary>
    /// Coalesces concurrent expired-token recovery triggers so only one recovery prompt
    /// executes at a time while all in-flight callers await its shared outcome.
    /// </summary>
    public sealed class TokenRecoveryCoordinator
    {
        private readonly object _syncLock = new object();
        private Task<bool>? _inFlightRecovery;

        /// <summary>
        /// Runs <paramref name="promptAsync"/> if no recovery is currently active, or returns
        /// the shared in-flight recovery task for concurrent callers.
        /// </summary>
        public Task<bool> RecoverAsync(Func<Task<bool>> promptAsync)
        {
            ArgumentNullException.ThrowIfNull(promptAsync);

            TaskCompletionSource<bool> tcs;
            lock (_syncLock)
            {
                if (_inFlightRecovery != null)
                {
                    return _inFlightRecovery;
                }

                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlightRecovery = tcs.Task;
            }

            _ = ExecutePromptAsync(promptAsync, tcs);
            return tcs.Task;
        }

        private async Task ExecutePromptAsync(Func<Task<bool>> promptAsync, TaskCompletionSource<bool> tcs)
        {
            bool recovered = false;
            Exception? capturedException = null;
            try
            {
                recovered = await promptAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }

            lock (_syncLock)
            {
                if (ReferenceEquals(_inFlightRecovery, tcs.Task))
                {
                    _inFlightRecovery = null;
                }
            }

            if (capturedException != null)
            {
                tcs.TrySetException(capturedException);
            }
            else
            {
                tcs.TrySetResult(recovered);
            }
        }
    }
}
