using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio.Backend.Contracts
{
    /// <summary>
    /// Prompts the user for replacement openBIS credentials when an active token expires.
    /// </summary>
    public interface ITokenRecoveryPrompter
    {
        /// <summary>
        /// Displays a credential recovery prompt and returns <c>true</c> if valid credentials were saved.
        /// </summary>
        Task<bool> PromptForNewTokenAsync(string currentUrl, string warningMessage, CancellationToken ct = default);
    }

    /// <summary>
    /// Backend facade contract exposing all openBIS, configuration, label preview, and printing workflows.
    /// </summary>
    public interface ILabelStudioBackend : IDisposable
    {
        void WarmUpInBackground();

        Task<WorkspaceStartupState> InitializeWorkspaceAsync(
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default);

        Task<BackgroundTypeRefreshResult> RefreshWorkspaceTypesAsync(
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default);

        Task<ConnectionCheckResult> TestConnectionAsync(
            string url,
            string token,
            bool persistOnSuccess,
            CancellationToken ct = default);

        Task<SettingsSnapshotDto> GetSettingsSnapshotAsync(CancellationToken ct = default);

        Task<ServerTypeSyncResult> FetchAndMergeServerTypesAsync(
            string url,
            string token,
            IReadOnlyList<ObjectTypeRowDto> currentRows,
            CancellationToken ct = default);

        Task<Dictionary<LabelTemplateType, byte[]>> GetExamplePreviewsAsync(
            LabelTemplateProfile profile,
            Action<LabelTemplateType, byte[]>? onSlotReady = null,
            CancellationToken ct = default);

        Task<OperationResult> SaveWizardStepAsync(WizardStepSaveRequest request, CancellationToken ct = default);

        Task<OperationResult> SaveSettingsOfflineAsync(SettingsSaveRequest request, CancellationToken ct = default);

        Task<SampleListResultDto> LoadObjectsForTypeAsync(
            string typeCode,
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default);

        void ClearActiveSample();

        Task<SamplePreviewBundleDto> LoadSamplePreviewsAsync(
            string typeCode,
            string permId,
            string magnetFreeText,
            string flaskFreeText,
            Action<LabelTemplateType, byte[]>? onSlotReady = null,
            ITokenRecoveryPrompter? recoveryPrompter = null,
            CancellationToken ct = default);

        Task<byte[]> RenderSlotWithFreeTextAsync(
            LabelTemplateType slot,
            string freeText,
            CancellationToken ct = default);

        Task<OperationResult> UpdatePrinterSelectionsAsync(
            string smallBoxPrinter,
            string magnetPrinter,
            string flaskPrinter);

        Task<LabelPrintBatchResult> PrintSelectedLabelsAsync(
            IReadOnlyList<PrintSlotSelection> selections,
            CancellationToken ct = default);

        byte[] GetLoadingPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile);

        byte[] GetFallbackPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile, string reason);
    }

    /// <summary>
    /// Brother PT-P700 dropdown items and pre-resolved queue selections.
    /// </summary>
    public sealed class PrinterDropdownState
    {
        public IReadOnlyList<string> AvailablePrinters { get; set; } = Array.Empty<string>();
        public bool HasInstalledBrotherPrinters { get; set; }
        public string SmallBoxPrinter { get; set; } = "";
        public string MagnetPrinter { get; set; } = "";
        public string FlaskPrinter { get; set; } = "";
        public int SmallBoxSelectedIndex { get; set; } = -1;
        public int MagnetSelectedIndex { get; set; } = -1;
        public int FlaskSelectedIndex { get; set; } = -1;
    }

    /// <summary>
    /// Display item for an enabled openBIS object type in the main window selector.
    /// </summary>
    public sealed class TypeOptionDto
    {
        public string Code { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public LabelTemplateProfile TemplateProfile { get; set; } = LabelTemplateProfile.Generic;
        public bool IsMoleculeProfile => TemplateProfile == LabelTemplateProfile.Molecule;

        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Editable object-type mapping row used in Setup/Settings.
    /// </summary>
    public sealed class ObjectTypeRowDto
    {
        public bool IsSelected { get; set; }
        public string Code { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public LabelTemplateProfile TemplateProfile { get; set; } = LabelTemplateProfile.Generic;
        public string DisplayLabel => $"{DisplayName} ({Code})";
    }

    /// <summary>
    /// Initial workspace configuration snapshot for the main application window.
    /// </summary>
    public sealed class WorkspaceStartupState
    {
        public SetupWizardStep MissingWizardStep { get; set; } = SetupWizardStep.None;
        public bool HasValidConnection { get; set; }
        public string CredentialFingerprint { get; set; } = "";
        public IReadOnlyList<TypeOptionDto> ConfiguredTypes { get; set; } = Array.Empty<TypeOptionDto>();
        public PrinterDropdownState Printers { get; set; } = new PrinterDropdownState();
        public string NoticeMessage { get; set; } = "";
    }

    /// <summary>
    /// Result of refreshing configured type display names against openBIS in the background.
    /// </summary>
    public sealed class BackgroundTypeRefreshResult
    {
        public bool Skipped { get; set; }
        public bool Success { get; set; }
        public bool TypesUpdated { get; set; }
        public bool WasCancelled { get; set; }
        public bool AttemptedRecovery { get; set; }
        public IReadOnlyList<TypeOptionDto> ConfiguredTypes { get; set; } = Array.Empty<TypeOptionDto>();
        public string NoticeMessage { get; set; } = "";
        public string NoticeTooltip { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// Outcome of validating and testing openBIS connection credentials.
    /// </summary>
    public sealed class ConnectionCheckResult
    {
        public bool Success { get; set; }
        public bool Persisted { get; set; }
        public bool ShouldFocusToken { get; set; }
        public string NormalizedUrl { get; set; } = "";
        public string ErrorType { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Configuration snapshot loaded for the Settings/Wizard/Recovery dialog.
    /// </summary>
    public sealed class SettingsSnapshotDto
    {
        public string OpenBisUrl { get; set; } = "";
        public string Token { get; set; } = "";
        public IReadOnlyList<ObjectTypeRowDto> ConfiguredTypes { get; set; } = Array.Empty<ObjectTypeRowDto>();
        public PrinterDropdownState Printers { get; set; } = new PrinterDropdownState();
        public bool HasValidOpenBisConfig { get; set; }
        public bool HasValidConfiguredTypes { get; set; }
        public bool HasPrinterConfig { get; set; }
        public SetupWizardStep FirstMissingWizardStep { get; set; } = SetupWizardStep.None;
    }

    /// <summary>
    /// Outcome of fetching server object types and merging them with in-progress row selections.
    /// </summary>
    public sealed class ServerTypeSyncResult
    {
        public bool Success { get; set; }
        public bool ValidationFailed { get; set; }
        public bool IsTokenExpired { get; set; }
        public string ErrorType { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public int ServerTypeCount { get; set; }
        public IReadOnlyList<ObjectTypeRowDto> MergedRows { get; set; } = Array.Empty<ObjectTypeRowDto>();
    }

    /// <summary>
    /// Request payload for saving a specific setup wizard step.
    /// </summary>
    public sealed class WizardStepSaveRequest
    {
        public SetupWizardStep Step { get; set; }
        public string OpenBisUrl { get; set; } = "";
        public string Token { get; set; } = "";
        public IReadOnlyList<ObjectTypeRowDto>? ObjectTypes { get; set; }
        public string SmallBoxPrinter { get; set; } = "";
        public string MagnetPrinter { get; set; } = "";
        public string FlaskPrinter { get; set; } = "";
    }

    /// <summary>
    /// Request payload for validating and saving all settings offline.
    /// </summary>
    public sealed class SettingsSaveRequest
    {
        public string OpenBisUrl { get; set; } = "";
        public string Token { get; set; } = "";
        public IReadOnlyList<ObjectTypeRowDto> ObjectTypes { get; set; } = Array.Empty<ObjectTypeRowDto>();
        public string SmallBoxPrinter { get; set; } = "";
        public string MagnetPrinter { get; set; } = "";
        public string FlaskPrinter { get; set; } = "";
    }

    /// <summary>
    /// Generic success/error result for synchronous validation and persistence operations.
    /// </summary>
    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";

        public static OperationResult Ok() => new OperationResult { Success = true };

        public static OperationResult Fail(string errorMessage) => new OperationResult
        {
            Success = false,
            ErrorMessage = errorMessage ?? ""
        };
    }

    /// <summary>
    /// Display item for an openBIS object in the object dropdown.
    /// </summary>
    public sealed class SampleSummaryDto
    {
        public string Name { get; set; } = "";
        public string PermId { get; set; } = "";
        public string ModificationDate { get; set; } = "";
        public string DisplayText => $"{Name} ({PermId})";

        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Result of querying objects for a selected openBIS object type.
    /// </summary>
    public sealed class SampleListResultDto
    {
        public bool Success { get; set; }
        public bool WasCancelled { get; set; }
        public bool IsMissingConfiguration { get; set; }
        public string NoticeMessage { get; set; } = "";
        public string NoticeTooltip { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public IReadOnlyList<SampleSummaryDto> Samples { get; set; } = Array.Empty<SampleSummaryDto>();
    }

    /// <summary>
    /// Rendered PNG label previews and resolved profile metadata for a loaded sample.
    /// </summary>
    public sealed class SamplePreviewBundleDto
    {
        public bool Success { get; set; }
        public bool WasCancelled { get; set; }
        public LabelTemplateProfile Profile { get; set; } = LabelTemplateProfile.Generic;
        public bool IsGenericProfile => Profile == LabelTemplateProfile.Generic;
        public string SampleName { get; set; } = "";
        public string PermId { get; set; } = "";
        public string NoticeMessage { get; set; } = "";
        public string NoticeTooltip { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public IReadOnlyDictionary<LabelTemplateType, byte[]> SlotPreviews { get; set; }
            = new Dictionary<LabelTemplateType, byte[]>();
    }

    /// <summary>
    /// Print selection for a single checked label template slot.
    /// </summary>
    public sealed class PrintSlotSelection
    {
        public LabelTemplateType Slot { get; set; }
        public string PrinterName { get; set; } = "";
        public string FreeText { get; set; } = "";
    }
}
