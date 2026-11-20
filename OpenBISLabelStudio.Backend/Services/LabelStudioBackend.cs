using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenBISLabelStudio.Backend.Contracts;
using OpenBISLabelStudio.Models;
using OpenBISLabelStudio.Services;

namespace OpenBISLabelStudio.Backend.Services
{
    /// <summary>
    /// Default implementation of <see cref="ILabelStudioBackend"/> coordinating configuration persistence,
    /// openBIS queries, expired-token recovery, label preview PNG rendering, and b-PAC printing.
    /// </summary>
    public sealed class LabelStudioBackend : ILabelStudioBackend
    {
        private readonly AppConfigService _configService;
        private readonly OpenBisClient _openBisClient;
        private readonly LabelPreviewRenderer _previewRenderer;
        private readonly Func<IReadOnlyList<string>>? _installedPrintersProvider;
        private readonly TokenRecoveryCoordinator _tokenRecoveryCoordinator = new TokenRecoveryCoordinator();
        private readonly object _activeSampleLock = new object();

        private ActiveSampleState? _activeSample;
        private int _disposed;

        /// <summary>
        /// Initializes a backend rooted at <paramref name="baseDirectory"/> using <paramref name="pythonExecutable"/>.
        /// </summary>
        public LabelStudioBackend(
            string? baseDirectory = null,
            string? pythonExecutable = null,
            Func<IReadOnlyList<string>>? installedPrintersProvider = null)
        {
            _configService = new AppConfigService(baseDirectory);
            _openBisClient = new OpenBisClient(baseDirectory, pythonExecutable);
            _previewRenderer = new LabelPreviewRenderer(_configService, installedPrintersProvider);
            _installedPrintersProvider = installedPrintersProvider;
        }

        /// <summary>
        /// Initializes a backend from pre-constructed service dependencies.
        /// </summary>
        public LabelStudioBackend(
            AppConfigService configService,
            OpenBisClient openBisClient,
            LabelPreviewRenderer previewRenderer,
            Func<IReadOnlyList<string>>? installedPrintersProvider = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _openBisClient = openBisClient ?? throw new ArgumentNullException(nameof(openBisClient));
            _previewRenderer = previewRenderer ?? throw new ArgumentNullException(nameof(previewRenderer));
            _installedPrintersProvider = installedPrintersProvider;
        }

        /// <summary>
        /// Runs the headless single-request-per-line JSON preview and print worker loop.
        /// </summary>
        public static void RunPreviewWorkerLoop(TextReader input, TextWriter output)
        {
            LabelPreviewRenderer.RunPreviewWorkerLoop(input, output);
        }

        /// <summary>
        /// Stops any active background preview/print worker process cleanly.
        /// </summary>
        public static void ShutdownPreviewWorker()
        {
            LabelPreviewRenderer.ShutdownPreviewWorker();
        }

        /// <summary>
        /// Merges server-discovered object types with existing saved mappings.
        /// Unconfigured <c>SUBSTANCE</c> defaults to <see cref="LabelTemplateProfile.Molecule"/>;
        /// all other unconfigured types default to <see cref="LabelTemplateProfile.Generic"/>.
        /// </summary>
        public static List<ConfiguredOpenBisType> BuildInitialTypeRows(
            IEnumerable<OpenBisTypeInfoDto> serverTypes,
            IReadOnlyList<ConfiguredOpenBisType> existingConfigured)
        {
            ArgumentNullException.ThrowIfNull(serverTypes);
            ArgumentNullException.ThrowIfNull(existingConfigured);

            var existingByCode = new Dictionary<string, ConfiguredOpenBisType>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfiguredOpenBisType existing in existingConfigured)
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

            var result = new List<ConfiguredOpenBisType>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (OpenBisTypeInfoDto serverType in serverTypes)
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

                existingByCode.TryGetValue(code, out ConfiguredOpenBisType? existing);

                string displayName = !string.IsNullOrWhiteSpace(serverType.Description)
                    ? serverType.Description.Trim()
                    : (!string.IsNullOrWhiteSpace(existing?.DisplayName)
                        ? existing.DisplayName.Trim()
                        : AppConfigService.FormatFriendlyName(code));

                LabelTemplateProfile profile = existing != null && Enum.IsDefined(typeof(LabelTemplateProfile), existing.TemplateProfile)
                    ? existing.TemplateProfile
                    : GetDefaultProfileForCode(code);

                result.Add(new ConfiguredOpenBisType
                {
                    Code = code,
                    DisplayName = displayName,
                    TemplateProfile = profile
                });
            }

            // Preserve any saved/existing entries absent from serverTypes (including offline fallback).
            foreach (ConfiguredOpenBisType existing in existingConfigured)
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

                LabelTemplateProfile profile = Enum.IsDefined(typeof(LabelTemplateProfile), existing.TemplateProfile)
                    ? existing.TemplateProfile
                    : GetDefaultProfileForCode(code);

                result.Add(new ConfiguredOpenBisType
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

        /// <inheritdoc />
        public void WarmUpInBackground()
        {
            if (Volatile.Read(ref _disposed) != 0 || _installedPrintersProvider != null)
            {
                return;
            }

            _previewRenderer.WarmUpWorkerInBackground();

            if (_configService.HasValidOpenBisConfig())
            {
                _openBisClient.WarmUpWorkerInBackground();
            }
        }

        /// <inheritdoc />
        public Task<WorkspaceStartupState> InitializeWorkspaceAsync(
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<WorkspaceStartupState>(ct);
            }

            SetupWizardStep missingStep = _configService.GetFirstMissingWizardStep();
            AppConfig? appConfig = _configService.LoadAppConfig();
            List<ConfiguredOpenBisType> configuredTypes = _configService.LoadConfiguredTypes();
            PrinterConfig? printerConfig = _configService.LoadPrinterConfig();

            bool hasValidConnection = appConfig != null
                                      && !string.IsNullOrWhiteSpace(appConfig.OpenBisUrl)
                                      && !string.IsNullOrWhiteSpace(appConfig.Token);

            var state = new WorkspaceStartupState
            {
                MissingWizardStep = missingStep,
                HasValidConnection = hasValidConnection,
                CredentialFingerprint = ComputeCredentialFingerprint(appConfig),
                ConfiguredTypes = BuildSortedTypeOptions(configuredTypes),
                Printers = BuildPrinterDropdownState(printerConfig)
            };

            return Task.FromResult(state);
        }

        /// <inheritdoc />
        public async Task<BackgroundTypeRefreshResult> RefreshWorkspaceTypesAsync(
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            AppConfig? appConfig = _configService.LoadAppConfig();
            if (appConfig == null
                || string.IsNullOrWhiteSpace(appConfig.OpenBisUrl)
                || string.IsNullOrWhiteSpace(appConfig.Token))
            {
                return new BackgroundTypeRefreshResult
                {
                    Skipped = true,
                    Success = true,
                    ConfiguredTypes = BuildSortedTypeOptions(_configService.LoadConfiguredTypes())
                };
            }

            RecoveryOutcome<OpenBisTypesResult> outcome = await ExecuteWithExpiredTokenRecoveryAsync(
                (url, token) => _openBisClient.QueryTypesAsync(url, token),
                r => r.Success,
                r => r.ErrorType,
                r => r.Error,
                recoveryPrompter,
                ct).ConfigureAwait(false);

            if (outcome.WasCancelled)
            {
                return new BackgroundTypeRefreshResult
                {
                    Success = false,
                    WasCancelled = true,
                    AttemptedRecovery = true,
                    NoticeMessage = "openBIS token expired or invalid — showing cached object types. Click Settings... to update credentials.",
                    NoticeTooltip = outcome.ExpiredError ?? "",
                    ConfiguredTypes = BuildSortedTypeOptions(_configService.LoadConfiguredTypes())
                };
            }

            if (outcome.HasFailure || outcome.Result == null)
            {
                string errDetail = FormatOpenBisError(outcome.ErrorMessage, "Could not refresh openBIS object types.");
                return new BackgroundTypeRefreshResult
                {
                    Success = false,
                    AttemptedRecovery = outcome.AttemptedRecovery,
                    NoticeMessage = $"Notice: could not refresh openBIS object types — showing cached object types. ({errDetail})",
                    NoticeTooltip = errDetail,
                    ErrorMessage = errDetail,
                    ConfiguredTypes = BuildSortedTypeOptions(_configService.LoadConfiguredTypes())
                };
            }

            OpenBisTypesResult result = outcome.Result;
            if (!result.Success)
            {
                string errDetail = FormatOpenBisError(result.Error, outcome.AttemptedRecovery
                    ? "Could not refresh openBIS object types."
                    : "Could not connect to openBIS.");
                string banner = outcome.AttemptedRecovery
                    ? $"Offline mode: showing cached object types ({errDetail})"
                    : string.Equals(result.ErrorType, "connection_error", StringComparison.OrdinalIgnoreCase)
                        ? $"Offline mode: could not reach openBIS — showing cached object types. ({errDetail})"
                        : $"Notice: could not refresh openBIS object types — showing cached object types. ({errDetail})";

                return new BackgroundTypeRefreshResult
                {
                    Success = false,
                    AttemptedRecovery = outcome.AttemptedRecovery,
                    NoticeMessage = banner,
                    NoticeTooltip = errDetail,
                    ErrorMessage = errDetail,
                    ConfiguredTypes = BuildSortedTypeOptions(_configService.LoadConfiguredTypes())
                };
            }

            List<ConfiguredOpenBisType> configuredTypes = _configService.LoadConfiguredTypes();
            bool anyChanged = MergeServerTypeDescriptions(configuredTypes, result.Types);
            if (anyChanged)
            {
                try
                {
                    _configService.SaveConfiguredTypes(configuredTypes);
                }
                catch
                {
                    // Ignore best-effort displayName cache persistence failure.
                }
            }

            return new BackgroundTypeRefreshResult
            {
                Success = true,
                TypesUpdated = anyChanged,
                AttemptedRecovery = outcome.AttemptedRecovery,
                ConfiguredTypes = BuildSortedTypeOptions(configuredTypes)
            };
        }

        /// <inheritdoc />
        public async Task<ConnectionCheckResult> TestConnectionAsync(
            string url,
            string token,
            bool persistOnSuccess,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!ValidateConnectionInputs(url, token, out string normalizedUrl, out string normalizedToken, out string inputError))
            {
                return new ConnectionCheckResult
                {
                    Success = false,
                    Persisted = false,
                    ShouldFocusToken = IsValidHttpUrl(normalizedUrl) && string.IsNullOrWhiteSpace(normalizedToken),
                    NormalizedUrl = normalizedUrl,
                    Message = inputError
                };
            }

            try
            {
                OpenBisConnectionResult result = await _openBisClient.CheckConnectionAsync(normalizedUrl, normalizedToken).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                if (!result.Success)
                {
                    bool shouldFocusToken = string.IsNullOrEmpty(normalizedToken)
                                            || string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase);

                    return new ConnectionCheckResult
                    {
                        Success = false,
                        Persisted = false,
                        ShouldFocusToken = shouldFocusToken,
                        NormalizedUrl = normalizedUrl,
                        ErrorType = result.ErrorType ?? "",
                        Message = FormatOpenBisFailureMessage(result.ErrorType, result.Error, "Failed to verify openBIS session.")
                    };
                }

                if (persistOnSuccess)
                {
                    try
                    {
                        _configService.SaveAppConfig(new AppConfig
                        {
                            OpenBisUrl = normalizedUrl,
                            Token = normalizedToken
                        });
                    }
                    catch (Exception ex)
                    {
                        return new ConnectionCheckResult
                        {
                            Success = false,
                            Persisted = false,
                            ShouldFocusToken = false,
                            NormalizedUrl = normalizedUrl,
                            ErrorType = "persistence_error",
                            Message = $"Could not save configuration files: {ex.Message}"
                        };
                    }
                }

                string suffix = persistOnSuccess ? " Credentials saved." : "";
                return new ConnectionCheckResult
                {
                    Success = true,
                    Persisted = persistOnSuccess,
                    ShouldFocusToken = false,
                    NormalizedUrl = normalizedUrl,
                    Message = $"Connected successfully (session active).{suffix}"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ConnectionCheckResult
                {
                    Success = false,
                    Persisted = false,
                    ShouldFocusToken = false,
                    NormalizedUrl = normalizedUrl,
                    ErrorType = "unknown_error",
                    Message = $"Unexpected error while checking openBIS connection: {ex.Message}"
                };
            }
        }

        /// <inheritdoc />
        public Task<SettingsSnapshotDto> GetSettingsSnapshotAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<SettingsSnapshotDto>(ct);
            }

            AppConfig? savedApp = _configService.LoadAppConfig();
            List<ConfiguredOpenBisType> savedTypes = _configService.LoadConfiguredTypes();
            PrinterConfig? savedPrinters = _configService.LoadPrinterConfig();

            var rows = new List<ObjectTypeRowDto>();
            foreach (ConfiguredOpenBisType savedType in savedTypes)
            {
                if (savedType == null || string.IsNullOrWhiteSpace(savedType.Code))
                {
                    continue;
                }

                string code = savedType.Code.Trim();
                rows.Add(new ObjectTypeRowDto
                {
                    IsSelected = true,
                    Code = code,
                    DisplayName = string.IsNullOrWhiteSpace(savedType.DisplayName)
                        ? AppConfigService.FormatFriendlyName(code)
                        : savedType.DisplayName.Trim(),
                    TemplateProfile = Enum.IsDefined(typeof(LabelTemplateProfile), savedType.TemplateProfile)
                        ? savedType.TemplateProfile
                        : GetDefaultProfileForCode(code)
                });
            }

            List<ObjectTypeRowDto> sortedRows = rows
                .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Code, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var snapshot = new SettingsSnapshotDto
            {
                OpenBisUrl = savedApp?.OpenBisUrl ?? "",
                Token = savedApp?.Token ?? "",
                ConfiguredTypes = sortedRows.AsReadOnly(),
                Printers = BuildPrinterDropdownState(savedPrinters),
                HasValidOpenBisConfig = _configService.HasValidOpenBisConfig(),
                HasValidConfiguredTypes = _configService.HasValidConfiguredTypes(),
                HasPrinterConfig = _configService.HasPrinterConfig(),
                FirstMissingWizardStep = _configService.GetFirstMissingWizardStep()
            };

            return Task.FromResult(snapshot);
        }

        /// <inheritdoc />
        public async Task<ServerTypeSyncResult> FetchAndMergeServerTypesAsync(
            string url,
            string token,
            IReadOnlyList<ObjectTypeRowDto> currentRows,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ObjectTypeRowDto> safeCurrentRows = currentRows ?? Array.Empty<ObjectTypeRowDto>();

            if (!ValidateConnectionInputs(url, token, out string normalizedUrl, out string normalizedToken, out string inputError))
            {
                return new ServerTypeSyncResult
                {
                    Success = false,
                    ValidationFailed = true,
                    StatusMessage = inputError,
                    MergedRows = CloneRowList(safeCurrentRows)
                };
            }

            try
            {
                OpenBisTypesResult result = await _openBisClient.QueryTypesAsync(normalizedUrl, normalizedToken).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                if (!result.Success)
                {
                    bool isTokenExpired = string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase);
                    return new ServerTypeSyncResult
                    {
                        Success = false,
                        IsTokenExpired = isTokenExpired,
                        ErrorType = result.ErrorType ?? "",
                        StatusMessage = FormatOpenBisFailureMessage(result.ErrorType, result.Error, "Failed to query openBIS object types."),
                        MergedRows = CloneRowList(safeCurrentRows)
                    };
                }

                List<OpenBisTypeInfoDto> validServerTypes = (result.Types ?? new List<OpenBisTypeInfoDto>())
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Code))
                    .Select(t => new OpenBisTypeInfoDto
                    {
                        Code = t.Code.Trim(),
                        Description = t.Description?.Trim() ?? ""
                    })
                    .ToList();

                var checkedCodes = new HashSet<string>(
                    safeCurrentRows
                        .Where(r => r != null && r.IsSelected && !string.IsNullOrWhiteSpace(r.Code))
                        .Select(r => r.Code.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                List<ConfiguredOpenBisType> existingSnapshot = safeCurrentRows
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Code))
                    .Select(r => new ConfiguredOpenBisType
                    {
                        Code = r.Code.Trim(),
                        DisplayName = r.DisplayName?.Trim() ?? "",
                        TemplateProfile = r.TemplateProfile
                    })
                    .ToList();

                List<ConfiguredOpenBisType> merged = BuildInitialTypeRows(validServerTypes, existingSnapshot);
                List<ObjectTypeRowDto> mergedRows = merged
                    .Select(item => new ObjectTypeRowDto
                    {
                        IsSelected = checkedCodes.Contains(item.Code),
                        Code = item.Code,
                        DisplayName = item.DisplayName,
                        TemplateProfile = item.TemplateProfile
                    })
                    .ToList();

                return new ServerTypeSyncResult
                {
                    Success = true,
                    ServerTypeCount = validServerTypes.Count,
                    StatusMessage = $"Loaded {validServerTypes.Count} object type(s) from openBIS.",
                    MergedRows = mergedRows.AsReadOnly()
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ServerTypeSyncResult
                {
                    Success = false,
                    ErrorType = "unknown_error",
                    StatusMessage = ex.Message,
                    MergedRows = CloneRowList(safeCurrentRows)
                };
            }
        }

        /// <inheritdoc />
        public async Task<Dictionary<LabelTemplateType, byte[]>> GetExamplePreviewsAsync(
            LabelTemplateProfile profile,
            Action<LabelTemplateType, byte[]>? onSlotReady = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var previews = new Dictionary<LabelTemplateType, byte[]>();
            foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
            {
                ct.ThrowIfCancellationRequested();

                byte[] pngBytes;
                try
                {
                    pngBytes = await _previewRenderer.RenderExampleSlotPreviewPngAsync(slot, profile, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pngBytes = LabelPreviewRenderer.CreateFallbackPreviewPng(
                        slot,
                        profile,
                        $"Preview unavailable: {ex.Message}");
                }

                previews[slot] = pngBytes;
                if (!ct.IsCancellationRequested)
                {
                    onSlotReady?.Invoke(slot, pngBytes);
                }
            }

            return previews;
        }

        /// <inheritdoc />
        public Task<OperationResult> SaveWizardStepAsync(WizardStepSaveRequest request, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<OperationResult>(ct);
            }

            ArgumentNullException.ThrowIfNull(request);

            try
            {
                switch (request.Step)
                {
                    case SetupWizardStep.Connection:
                    {
                        if (!ValidateConnectionInputs(request.OpenBisUrl, request.Token, out string url, out string token, out string validationError))
                        {
                            return Task.FromResult(OperationResult.Fail(validationError));
                        }

                        _configService.SaveAppConfig(new AppConfig
                        {
                            OpenBisUrl = url,
                            Token = token
                        });
                        return Task.FromResult(OperationResult.Ok());
                    }

                    case SetupWizardStep.ObjectTypes:
                    {
                        List<ConfiguredOpenBisType> selectedTypes = ExtractSelectedConfiguredTypes(request.ObjectTypes);
                        if (selectedTypes.Count == 0)
                        {
                            return Task.FromResult(OperationResult.Fail("Select at least one object type."));
                        }

                        _configService.SaveConfiguredTypes(selectedTypes);
                        return Task.FromResult(OperationResult.Ok());
                    }

                    case SetupWizardStep.Printers:
                    {
                        if (request.ObjectTypes != null)
                        {
                            List<ConfiguredOpenBisType> selectedTypes = ExtractSelectedConfiguredTypes(request.ObjectTypes);
                            if (selectedTypes.Count == 0)
                            {
                                return Task.FromResult(OperationResult.Fail("Select at least one object type."));
                            }

                            _configService.SaveConfiguredTypes(selectedTypes);
                        }

                        PrinterConfig printerConfig = NormalizePrinterConfig(
                            request.SmallBoxPrinter,
                            request.MagnetPrinter,
                            request.FlaskPrinter);
                        _configService.SavePrinterConfig(printerConfig);
                        return Task.FromResult(OperationResult.Ok());
                    }

                    default:
                        return Task.FromResult(OperationResult.Fail($"Unsupported setup wizard step: {request.Step}"));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult.Fail($"Could not save configuration files: {ex.Message}"));
            }
        }

        /// <inheritdoc />
        public Task<OperationResult> SaveSettingsOfflineAsync(SettingsSaveRequest request, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<OperationResult>(ct);
            }

            ArgumentNullException.ThrowIfNull(request);

            if (!ValidateConnectionInputs(request.OpenBisUrl, request.Token, out string url, out string token, out string validationError))
            {
                return Task.FromResult(OperationResult.Fail(validationError));
            }

            List<ConfiguredOpenBisType> selectedTypes = ExtractSelectedConfiguredTypes(request.ObjectTypes);
            if (selectedTypes.Count == 0)
            {
                return Task.FromResult(OperationResult.Fail("Select at least one object type."));
            }

            PrinterConfig printerConfig = NormalizePrinterConfig(
                request.SmallBoxPrinter,
                request.MagnetPrinter,
                request.FlaskPrinter);

            try
            {
                _configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = url,
                    Token = token
                });
                _configService.SaveConfiguredTypes(selectedTypes);
                _configService.SavePrinterConfig(printerConfig);
                return Task.FromResult(OperationResult.Ok());
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult.Fail($"Could not save configuration files: {ex.Message}"));
            }
        }

        /// <inheritdoc />
        public async Task<SampleListResultDto> LoadObjectsForTypeAsync(
            string typeCode,
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default)
        {
            ClearActiveSample();
            ct.ThrowIfCancellationRequested();

            string normalizedTypeCode = (typeCode ?? "").Trim();
            if (string.IsNullOrEmpty(normalizedTypeCode))
            {
                return new SampleListResultDto
                {
                    Success = true,
                    Samples = Array.Empty<SampleSummaryDto>()
                };
            }

            AppConfig? appConfig = _configService.LoadAppConfig();
            if (appConfig == null || string.IsNullOrWhiteSpace(appConfig.OpenBisUrl))
            {
                return new SampleListResultDto
                {
                    Success = false,
                    IsMissingConfiguration = true,
                    ErrorMessage = "openBIS URL is missing. Please configure it in Settings..."
                };
            }

            RecoveryOutcome<OpenBisListResult> outcome = await ExecuteWithExpiredTokenRecoveryAsync(
                (url, token) => _openBisClient.QuerySamplesByTypeAsync(url, token, normalizedTypeCode),
                r => r.Success,
                r => r.ErrorType,
                r => r.Error,
                recoveryPrompter,
                ct).ConfigureAwait(false);

            if (outcome.WasCancelled)
            {
                return new SampleListResultDto
                {
                    Success = false,
                    WasCancelled = true,
                    NoticeMessage = "openBIS token expired or invalid — click Settings... to update credentials.",
                    NoticeTooltip = outcome.ExpiredError ?? ""
                };
            }

            if (outcome.HasFailure || outcome.Result == null)
            {
                return new SampleListResultDto
                {
                    Success = false,
                    ErrorMessage = FormatOpenBisError(outcome.ErrorMessage, "Failed to fetch items from openBIS.")
                };
            }

            OpenBisListResult result = outcome.Result;
            if (!result.Success || result.Data == null)
            {
                return new SampleListResultDto
                {
                    Success = false,
                    ErrorMessage = FormatOpenBisError(result.Error, "Failed to fetch items from openBIS.")
                };
            }

            IReadOnlyList<SampleSummaryDto> samples = SampleSorter.Sort(
                result.Data
                    .Where(s => s != null)
                    .Select(s => new SampleSummaryDto
                    {
                        Name = s.Name ?? "",
                        PermId = s.PermId ?? "",
                        ModificationDate = s.ModificationDate ?? ""
                    })
                    .ToList(),
                SampleSortField.ModificationDate,
                SampleSortDirection.Descending);

            return new SampleListResultDto
            {
                Success = true,
                Samples = samples
            };
        }

        /// <inheritdoc />
        public void ClearActiveSample()
        {
            lock (_activeSampleLock)
            {
                _activeSample = null;
            }
        }

        /// <inheritdoc />
        public async Task<SamplePreviewBundleDto> LoadSamplePreviewsAsync(
            string typeCode,
            string permId,
            string magnetFreeText,
            string flaskFreeText,
            Action<LabelTemplateType, byte[]>? onSlotReady = null,
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            string normalizedTypeCode = (typeCode ?? "").Trim();
            string normalizedPermId = (permId ?? "").Trim();
            if (string.IsNullOrEmpty(normalizedPermId))
            {
                return new SamplePreviewBundleDto
                {
                    Success = false,
                    ErrorMessage = "Please select an openBIS object first."
                };
            }

            AppConfig? appConfig = _configService.LoadAppConfig();
            if (appConfig == null || string.IsNullOrWhiteSpace(appConfig.OpenBisUrl))
            {
                return new SamplePreviewBundleDto
                {
                    Success = false,
                    ErrorMessage = "openBIS URL is missing. Please configure it in Settings..."
                };
            }

            List<ConfiguredOpenBisType> configuredTypes = _configService.LoadConfiguredTypes();
            bool skipElnPreview = ResolveTemplateProfile(normalizedTypeCode, configuredTypes, null) == LabelTemplateProfile.Generic;

            RecoveryOutcome<OpenBisDetailsResult> outcome = await ExecuteWithExpiredTokenRecoveryAsync(
                (url, token) => _openBisClient.QuerySampleDetailsAsync(url, token, normalizedPermId, skipElnPreview),
                r => r.Success,
                r => r.ErrorType,
                r => r.Error,
                recoveryPrompter,
                ct).ConfigureAwait(false);

            if (outcome.WasCancelled)
            {
                return new SamplePreviewBundleDto
                {
                    Success = false,
                    WasCancelled = true,
                    NoticeMessage = "openBIS token expired or invalid — click Settings... to update credentials.",
                    NoticeTooltip = outcome.ExpiredError ?? ""
                };
            }

            if (outcome.HasFailure || outcome.Result == null)
            {
                return new SamplePreviewBundleDto
                {
                    Success = false,
                    ErrorMessage = FormatOpenBisError(outcome.ErrorMessage, "Could not fetch sample details.")
                };
            }

            OpenBisDetailsResult result = outcome.Result;
            if (!result.Success || result.Details == null)
            {
                return new SamplePreviewBundleDto
                {
                    Success = false,
                    ErrorMessage = FormatOpenBisError(result.Error, "Could not fetch sample details.")
                };
            }

            ct.ThrowIfCancellationRequested();

            // Re-read configured types in case recovery reopened Settings/changed mappings while in flight.
            configuredTypes = _configService.LoadConfiguredTypes();
            LabelTemplateProfile activeProfile = ResolveTemplateProfile(
                normalizedTypeCode,
                configuredTypes,
                result.Details.SampleType);

            string effectiveMagnetText = activeProfile == LabelTemplateProfile.Generic ? (magnetFreeText ?? "") : "";
            string effectiveFlaskText = activeProfile == LabelTemplateProfile.Generic ? (flaskFreeText ?? "") : "";

            var slotRequests = new (LabelTemplateType Slot, string FreeText)[]
            {
                (LabelTemplateType.SmallBox12mm, ""),
                (LabelTemplateType.Magnet12mm, effectiveMagnetText),
                (LabelTemplateType.Flask24mm, effectiveFlaskText)
            };

            var slotPreviews = new Dictionary<LabelTemplateType, byte[]>();
            var slotLock = new object();
            var callbackLock = new object();

            Task[] tasks = slotRequests.Select(async item =>
            {
                byte[] pngBytes;
                try
                {
                    pngBytes = await _previewRenderer.RenderTemplatePreviewPngAsync(
                        item.Slot,
                        activeProfile,
                        result.Details,
                        item.FreeText,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pngBytes = LabelPreviewRenderer.CreateFallbackPreviewPng(
                        item.Slot,
                        activeProfile,
                        $"Preview unavailable: {ex.Message}");
                }

                lock (slotLock)
                {
                    slotPreviews[item.Slot] = pngBytes;
                }

                if (onSlotReady != null && !ct.IsCancellationRequested)
                {
                    lock (callbackLock)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            onSlotReady(item.Slot, pngBytes);
                        }
                    }
                }
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            lock (_activeSampleLock)
            {
                if (Volatile.Read(ref _disposed) == 0 && !ct.IsCancellationRequested)
                {
                    _activeSample = new ActiveSampleState(result.Details, activeProfile, normalizedTypeCode, normalizedPermId);
                }
            }

            return new SamplePreviewBundleDto
            {
                Success = true,
                Profile = activeProfile,
                SampleName = result.Details.Name ?? "",
                PermId = string.IsNullOrEmpty(result.Details.PermId) ? normalizedPermId : result.Details.PermId,
                SlotPreviews = slotPreviews
            };
        }

        /// <inheritdoc />
        public async Task<byte[]> RenderSlotWithFreeTextAsync(
            LabelTemplateType slot,
            string freeText,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            ActiveSampleState? snapshot;
            lock (_activeSampleLock)
            {
                snapshot = Volatile.Read(ref _disposed) == 0 ? _activeSample : null;
            }

            if (snapshot == null)
            {
                return Array.Empty<byte>();
            }

            string effectiveFreeText = snapshot.Profile == LabelTemplateProfile.Generic && slot != LabelTemplateType.SmallBox12mm
                ? (freeText ?? "")
                : "";

            try
            {
                return await _previewRenderer.RenderTemplatePreviewPngAsync(
                    slot,
                    snapshot.Profile,
                    snapshot.Details,
                    effectiveFreeText,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return LabelPreviewRenderer.CreateFallbackPreviewPng(
                    slot,
                    snapshot.Profile,
                    $"Preview unavailable: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public Task<OperationResult> UpdatePrinterSelectionsAsync(
            string smallBoxPrinter,
            string magnetPrinter,
            string flaskPrinter)
        {
            PrinterConfig? existing = _configService.LoadPrinterConfig();
            PrinterConfig baseline = existing ?? AppConfigService.CreateDefaultPrinterConfig();

            string resolvedBox = string.IsNullOrWhiteSpace(smallBoxPrinter)
                ? baseline.SmallBoxPrinter
                : smallBoxPrinter.Trim();
            string resolvedMagnet = string.IsNullOrWhiteSpace(magnetPrinter)
                ? baseline.MagnetPrinter
                : magnetPrinter.Trim();
            string resolvedFlask = string.IsNullOrWhiteSpace(flaskPrinter)
                ? baseline.FlaskPrinter
                : flaskPrinter.Trim();

            if (existing != null
                && string.Equals(existing.SmallBoxPrinter, resolvedBox, StringComparison.Ordinal)
                && string.Equals(existing.MagnetPrinter, resolvedMagnet, StringComparison.Ordinal)
                && string.Equals(existing.FlaskPrinter, resolvedFlask, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult.Ok());
            }

            try
            {
                _configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = resolvedBox,
                    MagnetPrinter = resolvedMagnet,
                    FlaskPrinter = resolvedFlask
                });
                return Task.FromResult(OperationResult.Ok());
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult.Fail($"Could not save printer selection: {ex.Message}"));
            }
        }

        /// <inheritdoc />
        public async Task<LabelPrintBatchResult> PrintSelectedLabelsAsync(
            IReadOnlyList<PrintSlotSelection> selections,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            ActiveSampleState? snapshot;
            lock (_activeSampleLock)
            {
                snapshot = Volatile.Read(ref _disposed) == 0 ? _activeSample : null;
            }

            if (snapshot == null)
            {
                return new LabelPrintBatchResult
                {
                    AllSucceeded = false,
                    Error = "No active sample loaded. Click 'Load Templates' first."
                };
            }

            if (selections == null || selections.Count == 0)
            {
                return new LabelPrintBatchResult
                {
                    AllSucceeded = false,
                    Error = "No label templates selected for printing."
                };
            }

            var jobs = new List<LabelPrintJobRequest>(selections.Count);
            foreach (PrintSlotSelection selection in selections)
            {
                if (selection == null)
                {
                    continue;
                }

                string freeText = snapshot.Profile == LabelTemplateProfile.Generic && selection.Slot != LabelTemplateType.SmallBox12mm
                    ? (selection.FreeText ?? "")
                    : "";

                jobs.Add(new LabelPrintJobRequest
                {
                    Slot = selection.Slot,
                    LabelName = GetSlotLabelName(selection.Slot),
                    PrinterName = (selection.PrinterName ?? "").Trim(),
                    FreeText = freeText
                });
            }

            if (jobs.Count == 0)
            {
                return new LabelPrintBatchResult
                {
                    AllSucceeded = false,
                    Error = "No label templates selected for printing."
                };
            }

            return await _previewRenderer.PrintLabelsAsync(
                snapshot.Profile,
                snapshot.Details,
                jobs,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public byte[] GetLoadingPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile)
        {
            return LabelPreviewRenderer.CreateLoadingPreviewPng(slot, profile);
        }

        /// <inheritdoc />
        public byte[] GetFallbackPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile, string reason)
        {
            return LabelPreviewRenderer.CreateFallbackPreviewPng(slot, profile, reason);
        }

        /// <inheritdoc />
        public IReadOnlyList<SampleSummaryDto> SortSamples(
            IReadOnlyList<SampleSummaryDto>? samples,
            SampleSortField sortBy,
            SampleSortDirection direction)
        {
            return SampleSorter.Sort(samples, sortBy, direction);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _openBisClient.Dispose();
            ClearActiveSample();
        }

        private sealed class ActiveSampleState
        {
            public ActiveSampleState(
                SampleDetailsDto details,
                LabelTemplateProfile profile,
                string typeCode,
                string permId)
            {
                Details = details;
                Profile = profile;
                TypeCode = typeCode;
                PermId = permId;
            }

            public SampleDetailsDto Details { get; }
            public LabelTemplateProfile Profile { get; }
            public string TypeCode { get; }
            public string PermId { get; }
        }

        private sealed class RecoveryOutcome<TResult>
        {
            public bool WasCancelled { get; private set; }
            public bool HasFailure { get; private set; }
            public bool AttemptedRecovery { get; private set; }
            public string? ExpiredError { get; private set; }
            public string ErrorMessage { get; private set; } = "";
            public TResult? Result { get; private set; }

            public static RecoveryOutcome<TResult> Completed(TResult result, bool attemptedRecovery) =>
                new RecoveryOutcome<TResult>
                {
                    Result = result,
                    AttemptedRecovery = attemptedRecovery
                };

            public static RecoveryOutcome<TResult> Cancelled(string? expiredError) =>
                new RecoveryOutcome<TResult>
                {
                    WasCancelled = true,
                    AttemptedRecovery = true,
                    ExpiredError = expiredError
                };

            public static RecoveryOutcome<TResult> Failed(string errorMessage, string? expiredError) =>
                new RecoveryOutcome<TResult>
                {
                    HasFailure = true,
                    AttemptedRecovery = true,
                    ErrorMessage = errorMessage,
                    ExpiredError = expiredError
                };
        }

        private async Task<RecoveryOutcome<TResult>> ExecuteWithExpiredTokenRecoveryAsync<TResult>(
            Func<string, string, Task<TResult>> queryAsync,
            Func<TResult, bool> isSuccess,
            Func<TResult, string?> getErrorType,
            Func<TResult, string?> getError,
            ITokenRecoveryPrompter? recoveryPrompter,
            CancellationToken ct)
        {
            AppConfig initialConfig = _configService.LoadAppConfig() ?? new AppConfig();
            string requestUrl = initialConfig.OpenBisUrl;
            string requestToken = initialConfig.Token;

            TResult result = await queryAsync(requestUrl, requestToken).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (!isSuccess(result) && IsTokenExpiredError(getErrorType(result)))
            {
                AppConfig? currentConfig = _configService.LoadAppConfig();
                if (CredentialsChangedSinceRequest(requestUrl, requestToken, currentConfig))
                {
                    requestUrl = currentConfig!.OpenBisUrl;
                    requestToken = currentConfig.Token;
                    result = await queryAsync(requestUrl, requestToken).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }
            }

            if (!isSuccess(result) && IsTokenExpiredError(getErrorType(result)))
            {
                if (recoveryPrompter == null)
                {
                    return RecoveryOutcome<TResult>.Completed(result, attemptedRecovery: false);
                }

                string? expiredError = getError(result);
                string warning = BuildExpiredTokenWarning(expiredError);

                bool recovered;
                try
                {
                    // Run shared prompt without coupling one caller's CancellationToken to another caller's recovery.
                    recovered = await _tokenRecoveryCoordinator.RecoverAsync(
                        () => recoveryPrompter.PromptForNewTokenAsync(requestUrl, warning, CancellationToken.None))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ct.ThrowIfCancellationRequested();
                    return RecoveryOutcome<TResult>.Failed(ex.Message, expiredError);
                }

                ct.ThrowIfCancellationRequested();
                if (!recovered)
                {
                    return RecoveryOutcome<TResult>.Cancelled(expiredError);
                }

                AppConfig? updatedConfig = _configService.LoadAppConfig();
                if (updatedConfig != null)
                {
                    requestUrl = updatedConfig.OpenBisUrl;
                    requestToken = updatedConfig.Token;
                }

                result = await queryAsync(requestUrl, requestToken).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return RecoveryOutcome<TResult>.Completed(result, attemptedRecovery: true);
            }

            return RecoveryOutcome<TResult>.Completed(result, attemptedRecovery: false);
        }

        private PrinterDropdownState BuildPrinterDropdownState(PrinterConfig? savedOrActiveConfig)
        {
            IReadOnlyList<string> rawPrinters = _installedPrintersProvider != null
                ? _installedPrintersProvider()
                : GetSystemInstalledPrinters();

            (List<string> printers, bool hasInstalledBrotherPrinters) = AppConfigService.ResolveBrotherPrinterDropdownItems(
                rawPrinters,
                savedOrActiveConfig);

            PrinterConfig defaults = AppConfigService.CreateDefaultPrinterConfig();
            string preferredBox = !string.IsNullOrWhiteSpace(savedOrActiveConfig?.SmallBoxPrinter)
                ? savedOrActiveConfig!.SmallBoxPrinter.Trim()
                : defaults.SmallBoxPrinter;
            string preferredMagnet = !string.IsNullOrWhiteSpace(savedOrActiveConfig?.MagnetPrinter)
                ? savedOrActiveConfig!.MagnetPrinter.Trim()
                : defaults.MagnetPrinter;
            string preferredFlask = !string.IsNullOrWhiteSpace(savedOrActiveConfig?.FlaskPrinter)
                ? savedOrActiveConfig!.FlaskPrinter.Trim()
                : defaults.FlaskPrinter;

            int boxIdx = AppConfigService.FindBestPrinterIndex(printers, preferredBox);
            int magnetIdx = AppConfigService.FindBestPrinterIndex(printers, preferredMagnet);
            int flaskIdx = AppConfigService.FindBestPrinterIndex(printers, preferredFlask);

            return new PrinterDropdownState
            {
                AvailablePrinters = printers.AsReadOnly(),
                HasInstalledBrotherPrinters = hasInstalledBrotherPrinters,
                SmallBoxSelectedIndex = boxIdx,
                MagnetSelectedIndex = magnetIdx,
                FlaskSelectedIndex = flaskIdx,
                SmallBoxPrinter = boxIdx >= 0 && boxIdx < printers.Count ? printers[boxIdx] : preferredBox,
                MagnetPrinter = magnetIdx >= 0 && magnetIdx < printers.Count ? printers[magnetIdx] : preferredMagnet,
                FlaskPrinter = flaskIdx >= 0 && flaskIdx < printers.Count ? printers[flaskIdx] : preferredFlask
            };
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

        private static IReadOnlyList<TypeOptionDto> BuildSortedTypeOptions(IEnumerable<ConfiguredOpenBisType> configuredTypes)
        {
            return configuredTypes
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Code))
                .Select(t =>
                {
                    string cleanCode = t.Code.Trim();
                    string displayText = string.IsNullOrWhiteSpace(t.DisplayName)
                        ? AppConfigService.FormatFriendlyName(cleanCode)
                        : t.DisplayName.Trim();
                    return new TypeOptionDto
                    {
                        Code = cleanCode,
                        DisplayText = displayText,
                        TemplateProfile = Enum.IsDefined(typeof(LabelTemplateProfile), t.TemplateProfile)
                            ? t.TemplateProfile
                            : LabelTemplateProfile.Generic
                    };
                })
                .OrderBy(t => t.DisplayText, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Code, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        private static bool MergeServerTypeDescriptions(
            List<ConfiguredOpenBisType> configuredTypes,
            IReadOnlyList<OpenBisTypeInfoDto>? serverTypes)
        {
            if (serverTypes == null || serverTypes.Count == 0)
            {
                return false;
            }

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
            foreach (ConfiguredOpenBisType configured in configuredTypes)
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

            return anyChanged;
        }

        private static List<ConfiguredOpenBisType> ExtractSelectedConfiguredTypes(IEnumerable<ObjectTypeRowDto>? rows)
        {
            if (rows == null)
            {
                return new List<ConfiguredOpenBisType>();
            }

            return rows
                .Where(r => r != null && r.IsSelected && !string.IsNullOrWhiteSpace(r.Code))
                .Select(r =>
                {
                    string code = r.Code.Trim();
                    return new ConfiguredOpenBisType
                    {
                        Code = code,
                        DisplayName = string.IsNullOrWhiteSpace(r.DisplayName)
                            ? AppConfigService.FormatFriendlyName(code)
                            : r.DisplayName.Trim(),
                        TemplateProfile = Enum.IsDefined(typeof(LabelTemplateProfile), r.TemplateProfile)
                            ? r.TemplateProfile
                            : GetDefaultProfileForCode(code)
                    };
                })
                .ToList();
        }

        private static PrinterConfig NormalizePrinterConfig(string? smallBox, string? magnet, string? flask)
        {
            PrinterConfig defaults = AppConfigService.CreateDefaultPrinterConfig();
            return new PrinterConfig
            {
                SmallBoxPrinter = string.IsNullOrWhiteSpace(smallBox) ? defaults.SmallBoxPrinter : smallBox.Trim(),
                MagnetPrinter = string.IsNullOrWhiteSpace(magnet) ? defaults.MagnetPrinter : magnet.Trim(),
                FlaskPrinter = string.IsNullOrWhiteSpace(flask) ? defaults.FlaskPrinter : flask.Trim()
            };
        }

        private static IReadOnlyList<ObjectTypeRowDto> CloneRowList(IReadOnlyList<ObjectTypeRowDto> rows)
        {
            return rows
                .Where(r => r != null)
                .Select(r => new ObjectTypeRowDto
                {
                    IsSelected = r.IsSelected,
                    Code = r.Code,
                    DisplayName = r.DisplayName,
                    TemplateProfile = r.TemplateProfile
                })
                .ToList()
                .AsReadOnly();
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

        private static LabelTemplateProfile GetDefaultProfileForCode(string code)
        {
            return code.Trim().Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase)
                ? LabelTemplateProfile.Molecule
                : LabelTemplateProfile.Generic;
        }

        private static bool IsValidHttpUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri)
                   && (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool ValidateConnectionInputs(
            string? rawUrl,
            string? rawToken,
            out string url,
            out string token,
            out string errorMessage)
        {
            url = (rawUrl ?? "").Trim();
            token = (rawToken ?? "").Trim();

            if (!IsValidHttpUrl(url))
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

        private static bool IsTokenExpiredError(string? errorType)
        {
            return string.Equals(errorType, "token_expired", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildExpiredTokenWarning(string? errorDetail)
        {
            return string.IsNullOrWhiteSpace(errorDetail)
                ? "Your openBIS personal access token has expired or is invalid. Please enter a new token."
                : $"Your openBIS personal access token has expired or is invalid ({errorDetail.Trim()}). Please enter a new token.";
        }

        private static string FormatOpenBisError(string? error, string fallback)
        {
            return string.IsNullOrWhiteSpace(error) ? fallback : error;
        }

        private static string FormatOpenBisFailureMessage(string? errorType, string? error, string fallback)
        {
            if (string.Equals(errorType, "token_expired", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(error)
                    ? "Your openBIS personal access token has expired or is invalid. Please enter a new token."
                    : $"Token expired or invalid: {error}";
            }

            if (string.Equals(errorType, "connection_error", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(error)
                    ? "Could not connect to the openBIS server."
                    : $"Connection error: {error}";
            }

            return string.IsNullOrWhiteSpace(error)
                ? fallback
                : error;
        }

        private static string GetSlotLabelName(LabelTemplateType slot)
        {
            return slot switch
            {
                LabelTemplateType.SmallBox12mm => "Small Box 12mm Label",
                LabelTemplateType.Magnet12mm => "Magnet 12mm Label",
                LabelTemplateType.Flask24mm => "Flask 24mm Label",
                _ => slot.ToString()
            };
        }

        private static string ComputeCredentialFingerprint(AppConfig? config)
        {
            if (config == null
                || (string.IsNullOrWhiteSpace(config.OpenBisUrl) && string.IsNullOrWhiteSpace(config.Token)))
            {
                return "";
            }

            byte[] payload = Encoding.UTF8.GetBytes($"{config.OpenBisUrl ?? ""}\0{config.Token ?? ""}");
            return Convert.ToHexString(SHA256.HashData(payload));
        }
    }
}
