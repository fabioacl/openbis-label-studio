using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenBISLabelStudio.Tests
{
    using OpenBISLabelStudio.Backend.Contracts;
    using OpenBISLabelStudio.Backend.Services;
    using OpenBISLabelStudio.Models;
    using OpenBISLabelStudio.Services;
    using ModelTypes = OpenBISLabelStudio.Models;

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var tests = new (string Name, Action Body)[]
            {
                (nameof(IgnoresLegacyRootJsonFiles), IgnoresLegacyRootJsonFiles),
                (nameof(CorruptOrEmptyJsonDoesNotThrowAndTriggersSetup), CorruptOrEmptyJsonDoesNotThrowAndTriggersSetup),
                (nameof(SaveAndLoadAllConfigsRoundTrip), SaveAndLoadAllConfigsRoundTrip),
                (nameof(LegacyStringArrayTypesReadCompatibility), LegacyStringArrayTypesReadCompatibility),
                (nameof(ResolvesTemplatePathsUnderTemplatesFolder), ResolvesTemplatePathsUnderTemplatesFolder),
                (nameof(FindBestPrinterIndexHandlesZeroPrinters), FindBestPrinterIndexHandlesZeroPrinters),
                (nameof(OpenBisClientSurfacesStderrWhenPythonFails), OpenBisClientSurfacesStderrWhenPythonFails),
                (nameof(OpenBisClientExecutesScriptAndCleansTempOutput), OpenBisClientExecutesScriptAndCleansTempOutput),
                (nameof(OpenBisClientWorkerReusesPersistentPythonProcessAcrossQueries), OpenBisClientWorkerReusesPersistentPythonProcessAcrossQueries),
                (nameof(OpenBisClientWorkerHandlesMidSessionCrashAndCorruptStdoutCleanly), OpenBisClientWorkerHandlesMidSessionCrashAndCorruptStdoutCleanly),
                (nameof(OpenBisClientFallsBackToCliWhenStubScriptRejectsWorker), OpenBisClientFallsBackToCliWhenStubScriptRejectsWorker),
                (nameof(BuildExampleSampleDetailsMatchesProfile), BuildExampleSampleDetailsMatchesProfile),
                (nameof(RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing), RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing),
                (nameof(LbxPaperSizeAndFallbackPrinterHelpersWorkCorrectly), LbxPaperSizeAndFallbackPrinterHelpersWorkCorrectly),
                (nameof(CropFallbackExportBitmapTrimsA4AndPreservesNativeSize), CropFallbackExportBitmapTrimsA4AndPreservesNativeSize),
                (nameof(RenderTemplatePreviewAsyncCompletesOffThreadAndSupportsCancellation), RenderTemplatePreviewAsyncCompletesOffThreadAndSupportsCancellation),
                (nameof(PreviewWorkerLoopHandlesRequestsAndErrorsCleanly), PreviewWorkerLoopHandlesRequestsAndErrorsCleanly),
                (nameof(PrintLabelsValidatesPrintersAndTemplatesSafely), PrintLabelsValidatesPrintersAndTemplatesSafely),
                (nameof(BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved), BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved),
                (nameof(SetupSettingsFormInitializesSafelyWithZeroPrinters), SetupSettingsFormInitializesSafelyWithZeroPrinters),
                (nameof(SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken), SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken),
                (nameof(SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards), SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards),
                (nameof(SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails), SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails),
                (nameof(Form1CredentialsChangedSinceRequestDetectsUrlAndTokenChanges), Form1CredentialsChangedSinceRequestDetectsUrlAndTokenChanges),
                (nameof(Form1ResolveTemplateProfileUsesConfiguredMappingAndFallbacks), Form1ResolveTemplateProfileUsesConfiguredMappingAndFallbacks),
                (nameof(Form1HasSettingsButtonWithoutOverlaps), Form1HasSettingsButtonWithoutOverlaps),
                (nameof(Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges), Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges),
                (nameof(TokenRecoveryCoordinatorCoalescesConcurrentCallsAndResetsAfterCompletion), TokenRecoveryCoordinatorCoalescesConcurrentCallsAndResetsAfterCompletion),
                (nameof(BackendAssemblyHasNoUiControlDependencies), BackendAssemblyHasNoUiControlDependencies),
                (nameof(BackendExpiresTokenRecoveryCoalescesAndRetriesWithoutUiForms), BackendExpiresTokenRecoveryCoalescesAndRetriesWithoutUiForms),
                (nameof(BackendLoadSamplePreviewsAndFreeTextIsolationPreventsStaleState), BackendLoadSamplePreviewsAndFreeTextIsolationPreventsStaleState),
                (nameof(BackendValidatesAndSavesWizardAndSettingsIsolatingBaseDirectory), BackendValidatesAndSavesWizardAndSettingsIsolatingBaseDirectory),
                (nameof(BackendHelperMethodsBuildTypesResolveProfileAndDetectCredentialChanges), BackendHelperMethodsBuildTypesResolveProfileAndDetectCredentialChanges),
                (nameof(Form1AndSetupSettingsFormRenderWithMockBackendAndSurviveCorruptPngBytes), Form1AndSetupSettingsFormRenderWithMockBackendAndSurviveCorruptPngBytes),
                (nameof(Form1CancellationStopsStaleOpenBisCallsAndPreventsRecoveryOverSettings), Form1CancellationStopsStaleOpenBisCallsAndPreventsRecoveryOverSettings),
                (nameof(SetupSettingsFormPreservesInFlightGridEditsDuringBackgroundTypeSync), SetupSettingsFormPreservesInFlightGridEditsDuringBackgroundTypeSync),
                (nameof(SetupSettingsFormExamplePreviewExceptionDoesNotLeakUnobservedTask), SetupSettingsFormExamplePreviewExceptionDoesNotLeakUnobservedTask),
                (nameof(BackendPrintSelectedLabelsStripsFreeTextForMoleculeAndRejectsMissingPrinter), BackendPrintSelectedLabelsStripsFreeTextForMoleculeAndRejectsMissingPrinter),
                (nameof(BackendSaveWizardStepRoundTripsPersistEachStepCleanly), BackendSaveWizardStepRoundTripsPersistEachStepCleanly),
                (nameof(BackendSampleSorterOrdersByNamePermIdAndModificationDate), BackendSampleSorterOrdersByNamePermIdAndModificationDate),
                (nameof(BackendSampleSorterHandlesEdgeCasesWithoutThrowing), BackendSampleSorterHandlesEdgeCasesWithoutThrowing),
                (nameof(BackendSortSamplesIsolatesInputAndBreaksDuplicateTies), BackendSortSamplesIsolatesInputAndBreaksDuplicateTies),
                (nameof(FrontendFormsContainNoBackendServiceOrSecretReferences), FrontendFormsContainNoBackendServiceOrSecretReferences)
            };

            int failures = 0;
            foreach (var (name, body) in tests)
            {
                try
                {
                    body();
                    Console.WriteLine($"[PASS] {name}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine($"[FAIL] {name}: {ex}");
                }
            }

            Console.WriteLine($"Executed {tests.Length} tests, {failures} failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void IgnoresLegacyRootJsonFiles()
        {
            WithTempDir(tempRoot =>
            {
                File.WriteAllText(Path.Combine(tempRoot, "openbis_config.json"),
                    "{\"openbisUrl\": \"https://legacy.example.org\", \"token\": \"legacy-token\"}");
                File.WriteAllText(Path.Combine(tempRoot, "openbis_types.json"),
                    "[\"SUBSTANCE\", \"CRYSTAL\"]");
                File.WriteAllText(Path.Combine(tempRoot, "printers_config.json"),
                    "{\"smallBoxPrinter\": \"Legacy Printer\"}");

                var service = new AppConfigService(tempRoot);

                AssertEqual(SetupWizardStep.Connection, service.GetFirstMissingWizardStep(), "Expected missing step to be Connection");
                AssertFalse(service.HasValidOpenBisConfig(), "Legacy root openbis_config.json must be ignored");
                AssertFalse(service.HasValidConfiguredTypes(), "Legacy root openbis_types.json must be ignored");
                AssertFalse(service.HasPrinterConfig(), "Legacy root printers_config.json must be ignored");
                AssertNull(service.LoadAppConfig(), "LoadAppConfig should return null when config/openbis_config.json is absent");
                AssertEqual(0, service.LoadConfiguredTypes().Count, "LoadConfiguredTypes should be empty when config/openbis_types.json is absent");
                AssertNull(service.LoadPrinterConfig(), "LoadPrinterConfig should return null when config/printers_config.json is absent");
            });
        }

        private static void CorruptOrEmptyJsonDoesNotThrowAndTriggersSetup()
        {
            WithTempDir(tempRoot =>
            {
                string configDir = Path.Combine(tempRoot, "config");
                Directory.CreateDirectory(configDir);

                File.WriteAllText(Path.Combine(configDir, "openbis_config.json"), "{not-json");
                File.WriteAllText(Path.Combine(configDir, "openbis_types.json"), "[]");
                File.WriteAllText(Path.Combine(configDir, "printers_config.json"), "");

                var service = new AppConfigService(tempRoot);

                AssertFalse(service.HasValidOpenBisConfig(), "Corrupt openbis_config.json must be invalid");
                AssertFalse(service.HasValidConfiguredTypes(), "Empty types array must be invalid");
                AssertFalse(service.HasPrinterConfig(), "Empty printers_config.json must be invalid");
                AssertNull(service.LoadAppConfig(), "Corrupt openbis_config.json should return null");
                AssertEqual(0, service.LoadConfiguredTypes().Count, "Empty types array should load 0 items");
                AssertNull(service.LoadPrinterConfig(), "Empty printers_config.json should return null");
                AssertEqual(SetupWizardStep.Connection, service.GetFirstMissingWizardStep(), "Expected Connection step");

                // Also verify corrupt JSON in types and printers does not throw
                File.WriteAllText(Path.Combine(configDir, "openbis_types.json"), "{not-json");
                File.WriteAllText(Path.Combine(configDir, "printers_config.json"), "{not-json");
                AssertFalse(service.HasValidConfiguredTypes(), "Corrupt openbis_types.json must be invalid");
                AssertEqual(0, service.LoadConfiguredTypes().Count, "Corrupt openbis_types.json should return 0 items");
                AssertFalse(service.HasPrinterConfig(), "Corrupt printers_config.json must be invalid");
                AssertNull(service.LoadPrinterConfig(), "Corrupt printers_config.json should return null");
            });
        }

        private static void SaveAndLoadAllConfigsRoundTrip()
        {
            WithTempDir(tempRoot =>
            {
                var service = new AppConfigService(tempRoot);

                AssertEqual(SetupWizardStep.Connection, service.GetFirstMissingWizardStep(), "Expected Connection step initially");

                service.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "secret-token-123"
                });
                AssertEqual(SetupWizardStep.ObjectTypes, service.GetFirstMissingWizardStep(), "Expected ObjectTypes step after saving AppConfig");

                service.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType
                    {
                        Code = "SUBSTANCE",
                        DisplayName = "Substance",
                        TemplateProfile = LabelTemplateProfile.Molecule
                    },
                    new ConfiguredOpenBisType
                    {
                        Code = "CRYSTAL",
                        DisplayName = "Crystal",
                        TemplateProfile = LabelTemplateProfile.Generic
                    }
                });
                AssertEqual(SetupWizardStep.Printers, service.GetFirstMissingWizardStep(), "Expected Printers step after saving types");

                service.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Brother PT-P700",
                    MagnetPrinter = "Brother PT-P700 (Kopie 1)",
                    FlaskPrinter = "Brother PT-P700 (Kopie 2)"
                });

                AssertTrue(File.Exists(service.OpenBisConfigPath), "openbis_config.json should exist in config/");
                AssertTrue(File.Exists(service.OpenBisTypesPath), "openbis_types.json should exist in config/");
                AssertTrue(File.Exists(service.PrintersConfigPath), "printers_config.json should exist in config/");

                string typesJson = File.ReadAllText(service.OpenBisTypesPath);
                AssertTrue(typesJson.Contains("\"Molecule\"", StringComparison.Ordinal), "TemplateProfile Molecule should serialize as string");
                AssertTrue(typesJson.Contains("\"Generic\"", StringComparison.Ordinal), "TemplateProfile Generic should serialize as string");

                AssertEqual(SetupWizardStep.None, service.GetFirstMissingWizardStep(), "All configs present so wizard step should be None");

                AppConfig? loadedApp = service.LoadAppConfig();
                AssertNotNull(loadedApp, "Loaded AppConfig should not be null");
                AssertEqual("https://openbis.example.org", loadedApp!.OpenBisUrl, "OpenBisUrl mismatch");
                AssertEqual("secret-token-123", loadedApp.Token, "Token mismatch");

                List<ConfiguredOpenBisType> loadedTypes = service.LoadConfiguredTypes();
                AssertEqual(2, loadedTypes.Count, "Loaded types count mismatch");
                AssertEqual("SUBSTANCE", loadedTypes[0].Code, "First type code mismatch");
                AssertEqual("Substance", loadedTypes[0].DisplayName, "First type displayName mismatch");
                AssertEqual(LabelTemplateProfile.Molecule, loadedTypes[0].TemplateProfile, "First type profile mismatch");
                AssertEqual("CRYSTAL", loadedTypes[1].Code, "Second type code mismatch");
                AssertEqual("Crystal", loadedTypes[1].DisplayName, "Second type displayName mismatch");
                AssertEqual(LabelTemplateProfile.Generic, loadedTypes[1].TemplateProfile, "Second type profile mismatch");

                PrinterConfig? loadedPrinters = service.LoadPrinterConfig();
                AssertNotNull(loadedPrinters, "Loaded PrinterConfig should not be null");
                AssertEqual("Brother PT-P700", loadedPrinters!.SmallBoxPrinter, "SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (Kopie 1)", loadedPrinters.MagnetPrinter, "MagnetPrinter mismatch");
                AssertEqual("Brother PT-P700 (Kopie 2)", loadedPrinters.FlaskPrinter, "FlaskPrinter mismatch");
            });
        }

        private static void LegacyStringArrayTypesReadCompatibility()
        {
            WithTempDir(tempRoot =>
            {
                string configDir = Path.Combine(tempRoot, "config");
                Directory.CreateDirectory(configDir);
                File.WriteAllText(Path.Combine(configDir, "openbis_types.json"), "[\"SUBSTANCE\", \"CRYSTAL\"]");

                var service = new AppConfigService(tempRoot);
                List<ConfiguredOpenBisType> loaded = service.LoadConfiguredTypes();

                AssertTrue(service.HasValidConfiguredTypes(), "Legacy string array should satisfy HasValidConfiguredTypes");
                AssertEqual(2, loaded.Count, "Expected 2 types loaded from legacy array");
                AssertEqual("SUBSTANCE", loaded[0].Code, "Expected SUBSTANCE code");
                AssertEqual(LabelTemplateProfile.Molecule, loaded[0].TemplateProfile, "SUBSTANCE should map to Molecule");
                AssertEqual("CRYSTAL", loaded[1].Code, "Expected CRYSTAL code");
                AssertEqual(LabelTemplateProfile.Generic, loaded[1].TemplateProfile, "CRYSTAL should map to Generic");
            });
        }

        private static void ResolvesTemplatePathsUnderTemplatesFolder()
        {
            WithTempDir(tempRoot =>
            {
                var service = new AppConfigService(tempRoot);

                string moleculeMagnet = service.GetTemplateFilePath(LabelTemplateType.Magnet12mm, LabelTemplateProfile.Molecule);
                string genericMagnet = service.GetTemplateFilePath(LabelTemplateType.Magnet12mm, LabelTemplateProfile.Generic);
                string moleculeBox = service.GetTemplateFilePath(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule);
                string genericBox = service.GetTemplateFilePath(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Generic);
                string moleculeFlask = service.GetTemplateFilePath(LabelTemplateType.Flask24mm, LabelTemplateProfile.Molecule);
                string genericFlask = service.GetTemplateFilePath(LabelTemplateType.Flask24mm, LabelTemplateProfile.Generic);

                AssertTrue(moleculeMagnet.EndsWith(Path.Combine("templates", "template_molecule_magnet_12mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Molecule Magnet path: {moleculeMagnet}");
                AssertTrue(genericMagnet.EndsWith(Path.Combine("templates", "template_magnet_12mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Generic Magnet path: {genericMagnet}");
                AssertTrue(moleculeBox.EndsWith(Path.Combine("templates", "template_molecule_box_12mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Molecule SmallBox path: {moleculeBox}");
                AssertTrue(genericBox.EndsWith(Path.Combine("templates", "template_small_box_12mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Generic SmallBox path: {genericBox}");
                AssertTrue(moleculeFlask.EndsWith(Path.Combine("templates", "template_molecule_flask_24mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Molecule Flask path: {moleculeFlask}");
                AssertTrue(genericFlask.EndsWith(Path.Combine("templates", "template_flask_24mm.lbx"), StringComparison.OrdinalIgnoreCase),
                    $"Unexpected Generic Flask path: {genericFlask}");
            });
        }

        private static void FindBestPrinterIndexHandlesZeroPrinters()
        {
            AssertEqual(-1, AppConfigService.FindBestPrinterIndex(Array.Empty<string>(), "Brother PT-P700"),
                "Zero printers must return -1");
            AssertEqual(1, AppConfigService.FindBestPrinterIndex(new[] { "Brother PT-P700 (Kopie 1)", "Brother PT-P700" }, "Brother PT-P700"),
                "Exact match should beat partial match");
            AssertEqual(0, AppConfigService.FindBestPrinterIndex(new[] { "Brother PT-P700 (Kopie 1)" }, "Brother PT-P700"),
                "Partial match should succeed when exact match absent");
            AssertEqual(0, AppConfigService.FindBestPrinterIndex(new[] { "Office Printer", "Lab Printer" }, "Missing Printer"),
                "Unmatched preferred printer should fall back to index 0");
        }

        private static void OpenBisClientSurfacesStderrWhenPythonFails()
        {
            ModelTypes.OpenBisTypesResult mockErr = OpenBisClient.DeserializeOrError(
                null,
                1,
                "ModuleNotFoundError: No module named 'pybis'",
                (errorType, error) => new ModelTypes.OpenBisTypesResult
                {
                    Success = false,
                    ErrorType = errorType,
                    Error = error
                });

            AssertFalse(mockErr.Success, "DeserializeOrError must return Success = false on missing JSON");
            AssertEqual("unknown_error", mockErr.ErrorType, "DeserializeOrError must classify missing JSON/stderr as unknown_error");
            AssertTrue(mockErr.Error.Contains("pybis", StringComparison.OrdinalIgnoreCase),
                $"Expected stderr error to mention pybis, actual: {mockErr.Error}");

            WithTempDir(tempRoot =>
            {
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import sys\n" +
                    "sys.stderr.write(\"ModuleNotFoundError: No module named 'pybis'\\n\")\n" +
                    "sys.exit(1)\n");

                var missingExeClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                ModelTypes.OpenBisTypesResult launchErr = missingExeClient.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertFalse(launchErr.Success, "Missing python binary must fail");
                AssertEqual("unknown_error", launchErr.ErrorType, "Missing python binary must report unknown_error");
                AssertTrue(!string.IsNullOrWhiteSpace(launchErr.Error), "Missing python binary must surface diagnostic error text");

                var stderrClient = new OpenBisClient(tempRoot);
                ModelTypes.OpenBisTypesResult stderrErr = stderrClient.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertFalse(stderrErr.Success, "Failing Python script without --out must fail");
                AssertEqual("unknown_error", stderrErr.ErrorType, "Failing Python script without --out must report unknown_error");
                AssertTrue(stderrErr.Error.Contains("pybis", StringComparison.OrdinalIgnoreCase),
                    $"Expected Python process stderr to surface pybis error, actual: {stderrErr.Error}");
            });
        }

        private static void OpenBisClientExecutesScriptAndCleansTempOutput()
        {
            WithTempDir(tempRoot =>
            {
                string recordFile = Path.Combine(tempRoot, "recorded_out_path.txt");
                string argsFile = Path.Combine(tempRoot, "recorded_args.json");
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    "args = sys.argv[1:]\n" +
                    "out_idx = args.index('--out')\n" +
                    "out_path = args[out_idx + 1]\n" +
                    $"with open(r'''{recordFile}''', 'w', encoding='utf-8') as rf:\n" +
                    "    rf.write(out_path)\n" +
                    $"with open(r'''{argsFile}''', 'w', encoding='utf-8') as af:\n" +
                    "    json.dump(args, af)\n" +
                    "if '--check' in args:\n" +
                    "    payload = {'success': True, 'sessionActive': True}\n" +
                    "elif '--types' in args:\n" +
                    "    payload = {'success': False, 'errorType': 'token_expired', 'error': 'Expired'}\n" +
                    "elif '--details' in args:\n" +
                    "    payload = {'success': True, 'details': {'permId': '2026-1', 'name': 'CuPc', 'sampleType': 'SUBSTANCE', 'elnPreviewPath': 'skipped' if '--skip-eln' in args else 'full'}}\n" +
                    "else:\n" +
                    "    payload = {'success': True, 'data': [{'name': 'Sample-A', 'permId': '2026-9', 'modificationDate': '2026-11-13'}]}\n" +
                    "with open(out_path, 'w', encoding='utf-8') as f:\n" +
                    "    json.dump(payload, f)\n");

                var client = new OpenBisClient(tempRoot);
                string tokenWithSpecialChars = "tok&1 \"quoted\" %TEMP%";
                ModelTypes.OpenBisConnectionResult checkResult = client.CheckConnectionAsync("https://openbis.example.org", tokenWithSpecialChars).GetAwaiter().GetResult();
                AssertTrue(checkResult.Success && checkResult.SessionActive, "CheckConnectionAsync (--check) must succeed");

                string checkRecordedOutPath = File.ReadAllText(recordFile).Trim();
                AssertFalse(File.Exists(checkRecordedOutPath), $"Check temp JSON output file must be deleted: {checkRecordedOutPath}");

                ModelTypes.OpenBisTypesResult result = client.QueryTypesAsync("https://openbis.example.org", tokenWithSpecialChars).GetAwaiter().GetResult();

                AssertFalse(result.Success, "Stub returned success=false");
                AssertEqual("token_expired", result.ErrorType, "Expected ErrorType token_expired from stub JSON");
                AssertEqual("Expired", result.Error, "Expected Error message from stub JSON");

                AssertTrue(File.Exists(recordFile), "Stub script did not execute and record --out path");
                string recordedOutPath = File.ReadAllText(recordFile).Trim();
                AssertTrue(recordedOutPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
                    $"Expected --out path under temp directory, actual: {recordedOutPath}");
                AssertFalse(File.Exists(recordedOutPath), $"Temp JSON output file must be deleted after reading: {recordedOutPath}");

                string recordedArgsJson = File.ReadAllText(argsFile);
                AssertTrue(recordedArgsJson.Contains(tokenWithSpecialChars.Replace("\"", "\\\""), StringComparison.Ordinal),
                    $"Expected ArgumentList to preserve special characters in token, actual args: {recordedArgsJson}");

                ModelTypes.OpenBisDetailsResult detailsResult = client.QuerySampleDetailsAsync("https://openbis.example.org", tokenWithSpecialChars, "2026-1").GetAwaiter().GetResult();
                AssertTrue(detailsResult.Success, "Details stub should succeed");
                AssertNotNull(detailsResult.Details, "Details payload should be deserialized");
                AssertEqual("CuPc", detailsResult.Details!.Name, "Details Name mismatch");

                string secondRecordedOutPath = File.ReadAllText(recordFile).Trim();
                AssertFalse(File.Exists(secondRecordedOutPath), $"Second temp JSON output file must be deleted: {secondRecordedOutPath}");

                ModelTypes.OpenBisDetailsResult skippedDetails = client.QuerySampleDetailsAsync("https://openbis.example.org", tokenWithSpecialChars, "2026-1", skipElnPreview: true).GetAwaiter().GetResult();
                AssertTrue(skippedDetails.Success && skippedDetails.Details?.ElnPreviewPath == "skipped", "QuerySampleDetailsAsync should pass --skip-eln when requested");

                ModelTypes.OpenBisListResult listResult = client.QuerySamplesByTypeAsync("https://openbis.example.org", tokenWithSpecialChars, "SUBSTANCE").GetAwaiter().GetResult();
                AssertTrue(listResult.Success, "Sample list stub should succeed");
                AssertEqual(1, listResult.Data.Count, "Sample list count mismatch");
                AssertEqual("Sample-A", listResult.Data[0].Name, "Sample list item Name mismatch");

                string thirdRecordedOutPath = File.ReadAllText(recordFile).Trim();
                AssertFalse(File.Exists(thirdRecordedOutPath), $"Third temp JSON output file must be deleted: {thirdRecordedOutPath}");
            });
        }

        private static void OpenBisClientWorkerReusesPersistentPythonProcessAcrossQueries()
        {
            WithTempDir(tempRoot =>
            {
                string startupCountFile = Path.Combine(tempRoot, "worker_starts.txt");
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, os, sys\n" +
                    "if '--worker' not in sys.argv[1:]:\n" +
                    "    sys.stderr.write('Expected --worker mode\\n')\n" +
                    "    sys.exit(2)\n" +
                    $"count_path = r'''{startupCountFile}'''\n" +
                    "count = 0\n" +
                    "if os.path.exists(count_path):\n" +
                    "    with open(count_path, 'r', encoding='utf-8') as f:\n" +
                    "        count = int(f.read().strip() or '0')\n" +
                    "with open(count_path, 'w', encoding='utf-8') as f:\n" +
                    "    f.write(str(count + 1))\n" +
                    "while True:\n" +
                    "    line = sys.stdin.readline()\n" +
                    "    if line == '':\n" +
                    "        break\n" +
                    "    line = line.strip()\n" +
                    "    if not line:\n" +
                    "        continue\n" +
                    "    req = json.loads(line)\n" +
                    "    action = req.get('action')\n" +
                    "    if action == 'check':\n" +
                    "        resp = {'success': True, 'sessionActive': True}\n" +
                    "    elif action == 'types':\n" +
                    "        resp = {'success': True, 'types': [{'code': 'SUBSTANCE', 'description': 'Substance'}]}\n" +
                    "    elif action == 'details':\n" +
                    "        resp = {'success': True, 'details': {'permId': req.get('permId', ''), 'name': 'CuPc', 'sampleType': 'SUBSTANCE', 'elnPreviewPath': 'skipped' if req.get('skipEln') else 'full'}}\n" +
                    "    else:\n" +
                    "        resp = {'success': False, 'errorType': 'unknown_error', 'error': 'unexpected action'}\n" +
                    "    sys.stdout.write(json.dumps(resp) + '\\n')\n" +
                    "    sys.stdout.flush()\n");

                // Missing script and missing Python binary must not spawn hanging workers or throw during warmup.
                using (var missingScriptClient = new OpenBisClient(Path.Combine(tempRoot, "missing-dir")))
                {
                    missingScriptClient.WarmUpWorkerInBackground();
                }

                using (var missingPythonClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz"))
                {
                    missingPythonClient.WarmUpWorkerInBackground();
                }

                using (var client = new OpenBisClient(tempRoot))
                {
                    client.WarmUpWorkerInBackground();

                    ModelTypes.OpenBisConnectionResult checkResult = client.CheckConnectionAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                    AssertTrue(checkResult.Success && checkResult.SessionActive, $"CheckConnectionAsync must succeed via worker, error: {checkResult.Error}");

                    ModelTypes.OpenBisTypesResult typesResult = client.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                    AssertTrue(typesResult.Success, $"QueryTypesAsync must succeed via worker, error: {typesResult.Error}");
                    AssertEqual(1, typesResult.Types.Count, "Expected 1 type from worker");
                    AssertEqual("SUBSTANCE", typesResult.Types[0].Code, "Expected SUBSTANCE type code");

                    ModelTypes.OpenBisDetailsResult detailsResult = client.QuerySampleDetailsAsync("https://openbis.example.org", "tok", "P-1", skipElnPreview: true).GetAwaiter().GetResult();
                    AssertTrue(detailsResult.Success && detailsResult.Details?.ElnPreviewPath == "skipped",
                        $"QuerySampleDetailsAsync should forward skipEln=true to worker, error: {detailsResult.Error}");

                    AssertTrue(File.Exists(startupCountFile), "Worker startup count file must exist");
                    AssertEqual("1", File.ReadAllText(startupCountFile).Trim(), "Persistent worker should only be started once across warmup and queries");

                    client.ShutdownWorker();
                    ModelTypes.OpenBisConnectionResult afterShutdown = client.CheckConnectionAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                    AssertTrue(afterShutdown.Success, $"Query after ShutdownWorker should respawn worker cleanly, error: {afterShutdown.Error}");
                    AssertEqual("2", File.ReadAllText(startupCountFile).Trim(), "Expected fresh worker start after ShutdownWorker");
                }

                // Also verify real Backend openbis_fetch.py --worker pipe round-trip when present in output directory.
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openbis_fetch.py")))
                {
                    using var realClient = new OpenBisClient();
                    ModelTypes.OpenBisConnectionResult emptyTokCheck = realClient.CheckConnectionAsync("https://openbis.example.org", "").GetAwaiter().GetResult();
                    AssertFalse(emptyTokCheck.Success, "Real worker should reject empty token");
                    AssertEqual("token_expired", emptyTokCheck.ErrorType, "Real worker empty token should return token_expired");

                    ModelTypes.OpenBisConnectionResult httpCheck = realClient.CheckConnectionAsync("http://openbis.example.org", "tok").GetAwaiter().GetResult();
                    AssertFalse(httpCheck.Success, "Real worker should reject http:// URL");
                    AssertEqual("connection_error", httpCheck.ErrorType, "Real worker http:// URL should return connection_error");

                    ModelTypes.OpenBisTypesResult emptyTokTypes = realClient.QueryTypesAsync("https://openbis.example.org", "").GetAwaiter().GetResult();
                    AssertFalse(emptyTokTypes.Success, "Real worker --types should reject empty token");
                    AssertEqual("token_expired", emptyTokTypes.ErrorType, "Real worker --types empty token should return token_expired");
                }
            });
        }

        private static void OpenBisClientWorkerHandlesMidSessionCrashAndCorruptStdoutCleanly()
        {
            WithTempDir(tempRoot =>
            {
                string reqCountFile = Path.Combine(tempRoot, "req_count.txt");
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, os, sys\n" +
                    "if '--worker' not in sys.argv[1:]:\n" +
                    "    sys.stderr.write('Expected --worker mode\\n')\n" +
                    "    sys.exit(2)\n" +
                    $"req_path = r'''{reqCountFile}'''\n" +
                    "def next_req():\n" +
                    "    n = 0\n" +
                    "    if os.path.exists(req_path):\n" +
                    "        with open(req_path, 'r', encoding='utf-8') as f:\n" +
                    "            n = int(f.read().strip() or '0')\n" +
                    "    n += 1\n" +
                    "    with open(req_path, 'w', encoding='utf-8') as f:\n" +
                    "        f.write(str(n))\n" +
                    "    return n\n" +
                    "while True:\n" +
                    "    line = sys.stdin.readline()\n" +
                    "    if line == '':\n" +
                    "        break\n" +
                    "    if not line.strip():\n" +
                    "        continue\n" +
                    "    n = next_req()\n" +
                    "    if n == 1:\n" +
                    "        sys.stdout.write(json.dumps({'success': True, 'sessionActive': True}) + '\\n')\n" +
                    "        sys.stdout.flush()\n" +
                    "    elif n == 2:\n" +
                    "        sys.stdout.write('<<<corrupt-json>>>\\n')\n" +
                    "        sys.stdout.flush()\n" +
                    "        sys.exit(1)\n" +
                    "    elif n == 3:\n" +
                    "        sys.stdout.write(json.dumps({'success': True, 'types': [{'code': 'CRYSTAL', 'description': 'Crystal'}]}) + '\\n')\n" +
                    "        sys.stdout.flush()\n" +
                    "    elif n == 4:\n" +
                    "        sys.stderr.write('mid-session crash boom\\n')\n" +
                    "        sys.stderr.flush()\n" +
                    "        sys.exit(1)\n" +
                    "    else:\n" +
                    "        sys.stdout.write(json.dumps({'success': True, 'data': [{'name': 'Sample-X', 'permId': 'P-1', 'modificationDate': '2026-11-20'}]}) + '\\n')\n" +
                    "        sys.stdout.flush()\n");

                using var client = new OpenBisClient(tempRoot);
                ModelTypes.OpenBisConnectionResult r1 = client.CheckConnectionAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertTrue(r1.Success && r1.SessionActive, $"Request 1 should succeed, error: {r1.Error}");

                ModelTypes.OpenBisTypesResult r2 = client.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertFalse(r2.Success, "Request 2 with corrupt stdout must fail");
                AssertEqual("unknown_error", r2.ErrorType, "Request 2 corrupt stdout must return unknown_error");

                ModelTypes.OpenBisTypesResult r3 = client.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertTrue(r3.Success, $"Request 3 should succeed on respawned worker, error: {r3.Error}");
                AssertEqual(1, r3.Types.Count, "Request 3 expected 1 type");
                AssertEqual("CRYSTAL", r3.Types[0].Code, "Request 3 expected CRYSTAL type");

                ModelTypes.OpenBisListResult r4 = client.QuerySamplesByTypeAsync("https://openbis.example.org", "tok", "CRYSTAL").GetAwaiter().GetResult();
                AssertFalse(r4.Success, "Request 4 abrupt worker exit must fail");
                AssertEqual("unknown_error", r4.ErrorType, "Request 4 abrupt worker exit must return unknown_error");
                AssertTrue(r4.Error.Contains("boom", StringComparison.OrdinalIgnoreCase),
                    $"Request 4 should surface worker stderr crash message, actual: {r4.Error}");

                ModelTypes.OpenBisListResult r5 = client.QuerySamplesByTypeAsync("https://openbis.example.org", "tok", "CRYSTAL").GetAwaiter().GetResult();
                AssertTrue(r5.Success, $"Request 5 should succeed on freshly respawned worker, error: {r5.Error}");
                AssertEqual(1, r5.Data.Count, "Request 5 expected 1 sample");
                AssertEqual("Sample-X", r5.Data[0].Name, "Request 5 expected Sample-X");
            });
        }

        private static void OpenBisClientFallsBackToCliWhenStubScriptRejectsWorker()
        {
            WithTempDir(tempRoot =>
            {
                string invocationsFile = Path.Combine(tempRoot, "invocations.log");
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    "args = sys.argv[1:]\n" +
                    $"with open(r'''{invocationsFile}''', 'a', encoding='utf-8') as lf:\n" +
                    "    lf.write(' '.join(args) + '\\n')\n" +
                    "if args == ['--worker']:\n" +
                    "    sys.stderr.write('Invalid arguments\\n')\n" +
                    "    sys.exit(1)\n" +
                    "if '--types' in args and '--out' in args:\n" +
                    "    out_path = args[args.index('--out') + 1]\n" +
                    "    with open(out_path, 'w', encoding='utf-8') as f:\n" +
                    "        json.dump({'success': True, 'types': [{'code': 'SUBSTANCE', 'description': 'Substance'}]}, f)\n" +
                    "    sys.exit(0)\n" +
                    "sys.stderr.write('Unsupported args\\n')\n" +
                    "sys.exit(1)\n");

                using var client = new OpenBisClient(tempRoot);
                ModelTypes.OpenBisTypesResult res1 = client.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertTrue(res1.Success, $"First QueryTypesAsync should fall back to CLI and succeed, error: {res1.Error}");
                AssertEqual(1, res1.Types.Count, "Expected 1 type from CLI fallback");

                ModelTypes.OpenBisTypesResult res2 = client.QueryTypesAsync("https://openbis.example.org", "tok").GetAwaiter().GetResult();
                AssertTrue(res2.Success, $"Second QueryTypesAsync should succeed via CLI, error: {res2.Error}");

                AssertTrue(File.Exists(invocationsFile), "Invocation log should exist");
                string[] lines = File.ReadAllLines(invocationsFile);
                AssertEqual(3, lines.Length, $"Expected 1 --worker attempt + 2 CLI invocations, got {lines.Length}");
                AssertEqual("--worker", lines[0], "First invocation should attempt --worker");
                AssertTrue(lines[1].Contains("--types", StringComparison.Ordinal) && lines[1].Contains("--out", StringComparison.Ordinal),
                    $"Second invocation should be CLI fallback, actual: {lines[1]}");
                AssertTrue(lines[2].Contains("--types", StringComparison.Ordinal) && lines[2].Contains("--out", StringComparison.Ordinal),
                    $"Third invocation should go straight to CLI once --worker is unsupported, actual: {lines[2]}");
            });
        }

        private static void BuildExampleSampleDetailsMatchesProfile()
        {
            ModelTypes.SampleDetailsDto molecule = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule);
            AssertEqual("CuPc-F16", molecule.Name, "Molecule example Name mismatch");
            AssertEqual("20261113-EX01", molecule.PermId, "Molecule example PermId mismatch");
            AssertEqual("520 K", molecule.EvaporationTemperature, "Molecule example EvaporationTemperature mismatch");
            AssertEqual("Jane Doe", molecule.StudiedByName, "Molecule example StudiedByName mismatch");
            AssertEqual("jdoe", molecule.StudiedByUsername, "Molecule example StudiedByUsername mismatch");
            AssertEqual("Cabinet A-3", molecule.StorageLocation, "Molecule example StorageLocation mismatch");
            AssertEqual("Sigma-Aldrich", molecule.SupplierOwnName, "Molecule example SupplierOwnName mismatch");
            AssertTrue(molecule.IconFlags.Poisonous, "Molecule example Poisonous flag must be true");
            AssertTrue(molecule.IconFlags.Dry, "Molecule example Dry flag must be true");
            AssertFalse(molecule.IconFlags.Flammable, "Molecule example Flammable flag must be false");

            ModelTypes.SampleDetailsDto generic = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Generic);
            AssertEqual("Sample-001", generic.Name, "Generic example Name mismatch");
            AssertEqual("20261113-EX02", generic.PermId, "Generic example PermId mismatch");
        }

        private static void RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing()
        {
            static void AssertValidAndDispose(Dictionary<LabelTemplateType, Image> previews, string label)
            {
                try
                {
                    AssertEqual(3, previews.Count, $"Expected {label} previews for all 3 template slots");
                    foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
                    {
                        AssertTrue(previews.TryGetValue(slot, out Image? image), $"Missing {label} preview for slot {slot}");
                        AssertNotNull(image, $"{label} preview image for {slot} must not be null");
                        AssertTrue(image is Bitmap, $"{label} preview image for {slot} must be a Bitmap");
                        AssertTrue(image!.Width > 0 && image.Height > 0, $"{label} preview bitmap for {slot} must have non-zero dimensions");
                    }
                }
                finally
                {
                    foreach (Image img in previews.Values)
                    {
                        img.Dispose();
                    }
                }
            }

            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                var renderer = new LabelPreviewRenderer(configService);
                AssertValidAndDispose(renderer.RenderExamplePreviews(LabelTemplateProfile.Molecule), "molecule");

                // Also verify invalid/corrupt .lbx file triggers b-PAC fallback cleanly without throwing.
                Directory.CreateDirectory(configService.TemplatesDirectoryPath);
                string corruptTemplate = configService.GetTemplateFilePath(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Generic);
                File.WriteAllText(corruptTemplate, "not a valid brother lbx file");

                AssertValidAndDispose(renderer.RenderExamplePreviews(LabelTemplateProfile.Generic), "generic");
            });
        }

        private static void LbxPaperSizeAndFallbackPrinterHelpersWorkCorrectly()
        {
            string templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            if (Directory.Exists(templatesDir))
            {
                AssertTrue(LabelPreviewRenderer.TryReadLbxPaperSizePx(
                    Path.Combine(templatesDir, "template_small_box_12mm.lbx"), out int sbW, out int sbH),
                    "Should read SmallBox template paper size");
                AssertEqual(342, sbW, "SmallBox landscape width px mismatch");
                AssertEqual(168, sbH, "SmallBox landscape height px mismatch");

                AssertTrue(LabelPreviewRenderer.TryReadLbxPaperSizePx(
                    Path.Combine(templatesDir, "template_flask_24mm.lbx"), out int flW, out int flH),
                    "Should read Flask template paper size");
                AssertEqual(676, flW, "Flask landscape width px mismatch");
                AssertEqual(340, flH, "Flask landscape height px mismatch");
            }

            // Test printer override selection rules independently of machine printers.
            AssertNull(
                LabelPreviewRenderer.SelectPreviewPrinterOverride(
                    "Brother PT-P700 (Kopie 2)",
                    new[] { "Microsoft Print to PDF", "Brother PT-P700 (Kopie 2)" }),
                "Exact Brother printer installed must use original .lbx without override");

            AssertEqual(
                "Brother PT-P700",
                LabelPreviewRenderer.SelectPreviewPrinterOverride(
                    "Brother PT-P700 (Kopie 2)",
                    new[] { "Microsoft Print to PDF", "Brother PT-P700" }),
                "Should prefer another installed Brother printer when exact copy name is absent");

            AssertEqual(
                "Microsoft Print to PDF",
                LabelPreviewRenderer.SelectPreviewPrinterOverride(
                    "Brother PT-P700 (Kopie 2)",
                    new[] { "OneNote (Desktop)", "Microsoft Print to PDF", @"\\emp-ps2\FollowMe" }),
                "Should prefer Microsoft Print to PDF (not OneNote) when no Brother printer is installed");

            AssertNull(
                LabelPreviewRenderer.SelectPreviewPrinterOverride(
                    "Brother PT-P700 (Kopie 2)",
                    new[] { "OneNote (Desktop)", @"\\emp-ps2\FollowMe" }),
                "Should return null when neither Brother nor Microsoft Print to PDF is installed");

            List<string> filteredBrother = AppConfigService.FilterBrotherPtP700Printers(
                new[] { "OneNote (Desktop)", "Brother PT-P700", "Microsoft Print to PDF", "Brother PT-P700 (Kopie 1)", @"\\emp-ps2\FollowMe" });
            AssertEqual(2, filteredBrother.Count, "Printer filter must keep only Brother PT-P700 queues");
            AssertEqual("Brother PT-P700", filteredBrother[0], "Expected first Brother PT-P700 printer");
            AssertEqual("Brother PT-P700 (Kopie 1)", filteredBrother[1], "Expected second Brother PT-P700 printer");

            // Test unit parsing (mm, portrait) and non-mutating temp .lbx rewrite.
            WithTempDir(tempRoot =>
            {
                string lbxPath = Path.Combine(tempRoot, "synthetic.lbx");
                string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                             "<pt:document xmlns:pt=\"http://schemas.brother.info/ptouch/2007/lbx/main\" " +
                             "xmlns:style=\"http://schemas.brother.info/ptouch/2007/lbx/style\">" +
                             "<style:paper width=\"25.4mm\" height=\"50.8mm\" orientation=\"portrait\" printerName=\"Missing &amp; Old\" />" +
                             "</pt:document>";

                using (var archive = System.IO.Compression.ZipFile.Open(lbxPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("label.xml");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(xml);
                }

                AssertTrue(LabelPreviewRenderer.TryReadLbxPaperSizePx(lbxPath, out int wPx, out int hPx),
                    "Should parse mm portrait .lbx paper dimensions");
                AssertEqual(360, wPx, "25.4mm portrait width at 360 DPI should be 360px");
                AssertEqual(720, hPx, "50.8mm portrait height at 360 DPI should be 720px");

                string? tempLbx = LabelPreviewRenderer.TryCreateTempLbxWithPrinter(lbxPath, "Microsoft Print to PDF");
                AssertNotNull(tempLbx, "Should create temp overridden .lbx");
                try
                {
                    AssertTrue(File.Exists(tempLbx!), "Overridden temp .lbx file must exist");
                    using var origArchive = System.IO.Compression.ZipFile.OpenRead(lbxPath);
                    using var origReader = new StreamReader(origArchive.GetEntry("label.xml")!.Open());
                    AssertTrue(origReader.ReadToEnd().Contains("Missing &amp; Old", StringComparison.Ordinal),
                        "Original .lbx must remain unmodified");

                    using var tempArchive = System.IO.Compression.ZipFile.OpenRead(tempLbx!);
                    using var tempReader = new StreamReader(tempArchive.GetEntry("label.xml")!.Open());
                    string updatedXml = tempReader.ReadToEnd();
                    AssertTrue(updatedXml.Contains("printerName=\"Microsoft Print to PDF\"", StringComparison.Ordinal),
                        $"Temp .lbx should replace printerName cleanly, actual: {updatedXml}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempLbx) && File.Exists(tempLbx))
                    {
                        File.Delete(tempLbx);
                    }
                }
            });
        }

        private static void CropFallbackExportBitmapTrimsA4AndPreservesNativeSize()
        {
            // 1. Oversized A4 canvas (e.g. 4210x2977) with label in top-left -> cropped to expected paper bounds.
            using (var a4 = new Bitmap(4210, 2977, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(a4))
                {
                    g.Clear(Color.White);
                    g.FillRectangle(Brushes.Black, 45, 30, 180, 90);
                }

                using Bitmap cropped = LabelPreviewRenderer.CropFallbackExportBitmap(a4, 506, 168);
                AssertEqual(506, cropped.Width, "Oversized A4 bitmap should crop to expected label width");
                AssertEqual(168, cropped.Height, "Oversized A4 bitmap should crop to expected label height");
                AssertTrue(cropped.GetPixel(60, 50).R < 50, "Cropped bitmap must preserve label ink pixels");
            }

            // 2. Native-sized Brother bitmap -> returned untouched without trimming.
            using (var native = new Bitmap(520, 172, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Bitmap untouched = LabelPreviewRenderer.CropFallbackExportBitmap(native, 506, 168))
            {
                AssertEqual(520, untouched.Width, "Native-sized bitmap width must not be trimmed");
                AssertEqual(172, untouched.Height, "Native-sized bitmap height must not be trimmed");
            }

            // 3. Loading preview placeholder card -> non-null disposable Bitmap.
            using Bitmap loading = LabelPreviewRenderer.CreateLoadingPreviewBitmap(
                LabelTemplateType.SmallBox12mm,
                LabelTemplateProfile.Molecule);
            AssertTrue(loading.Width > 0 && loading.Height > 0, "Loading placeholder bitmap must have non-zero size");
        }

        private static void RenderTemplatePreviewAsyncCompletesOffThreadAndSupportsCancellation()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                var renderer = new LabelPreviewRenderer(configService, () => Array.Empty<string>());
                ModelTypes.SampleDetailsDto sample = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule);

                using Image fallback = renderer.RenderTemplatePreviewAsync(
                    LabelTemplateType.SmallBox12mm,
                    LabelTemplateProfile.Molecule,
                    sample).GetAwaiter().GetResult();
                AssertTrue(fallback.Width > 0 && fallback.Height > 0, "Async preview must return fallback bitmap when template missing");

                using var cts = new System.Threading.CancellationTokenSource();
                cts.Cancel();
                bool canceled = false;
                try
                {
                    renderer.RenderTemplatePreviewAsync(
                        LabelTemplateType.SmallBox12mm,
                        LabelTemplateProfile.Molecule,
                        sample,
                        cancellationToken: cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                AssertTrue(canceled, "Pre-canceled token must cancel RenderTemplatePreviewAsync");
            });
        }

        private static void PreviewWorkerLoopHandlesRequestsAndErrorsCleanly()
        {
            WithTempDir(tempRoot =>
            {
                string outPng = Path.Combine(tempRoot, "worker_out.png");
                string warmupJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "warmup",
                    baseDir = tempRoot
                });
                string reqJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "render",
                    baseDir = tempRoot,
                    slot = "SmallBox12mm",
                    profile = "Molecule",
                    details = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule),
                    freeText = "",
                    outPath = outPng
                });
                string printErrJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "print",
                    baseDir = tempRoot,
                    profile = "Molecule",
                    details = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule),
                    jobs = new[]
                    {
                        new { slot = "SmallBox12mm", labelName = "Small Box 12mm Label", printerName = "Missing Printer", freeText = "" }
                    }
                });

                string inputLines = "not-valid-json\n" + warmupJson + "\n" + reqJson + "\n" + printErrJson + "\n";
                using var reader = new StringReader(inputLines);
                using var writer = new StringWriter();

                LabelPreviewRenderer.RunPreviewWorkerLoop(reader, writer);

                string[] responses = writer.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                AssertEqual(4, responses.Length, "Worker should emit one JSON response per request line before EOF");
                AssertTrue(responses[0].Contains("\"success\":false", StringComparison.OrdinalIgnoreCase),
                    $"Malformed line should yield success:false, actual: {responses[0]}");
                AssertTrue(responses[1].Contains("\"success\":true", StringComparison.OrdinalIgnoreCase),
                    $"Warmup action should yield success:true, actual: {responses[1]}");
                AssertTrue(responses[2].Contains("\"success\":true", StringComparison.OrdinalIgnoreCase),
                    $"Valid render request should yield success:true fallback PNG, actual: {responses[2]}");
                AssertTrue(responses[3].Contains("\"success\":false", StringComparison.OrdinalIgnoreCase),
                    $"Missing template print job should yield success:false, actual: {responses[3]}");
                AssertTrue(File.Exists(outPng), "Worker must write rendered/fallback PNG to outPath");
            });
        }

        private static void PrintLabelsValidatesPrintersAndTemplatesSafely()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                var sample = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule);

                var rendererNoTemplate = new LabelPreviewRenderer(configService, () => new[] { "Brother PT-P700" });
                LabelPrintBatchResult missingTemplateRes = rendererNoTemplate.PrintLabelsAsync(
                    LabelTemplateProfile.Molecule,
                    sample,
                    new[]
                    {
                        new LabelPrintJobRequest
                        {
                            Slot = LabelTemplateType.SmallBox12mm,
                            LabelName = "Small Box 12mm Label",
                            PrinterName = "Brother PT-P700"
                        }
                    }).GetAwaiter().GetResult();

                AssertFalse(missingTemplateRes.AllSucceeded, "PrintLabelsAsync must fail when template .lbx file is missing");
                AssertTrue(missingTemplateRes.Error.Contains("missing", StringComparison.OrdinalIgnoreCase),
                    $"Expected missing template error message, actual: {missingTemplateRes.Error}");

                // Create template file, then confirm an uninstalled printer fails safely without falling back to default printer.
                Directory.CreateDirectory(configService.TemplatesDirectoryPath);
                File.WriteAllText(
                    configService.GetTemplateFilePath(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule),
                    "placeholder");

                var rendererMissingPrinter = new LabelPreviewRenderer(configService, () => new[] { "OneNote (Desktop)" });
                LabelPrintBatchResult missingPrinterRes = rendererMissingPrinter.PrintLabelsAsync(
                    LabelTemplateProfile.Molecule,
                    sample,
                    new[]
                    {
                        new LabelPrintJobRequest
                        {
                            Slot = LabelTemplateType.SmallBox12mm,
                            LabelName = "Small Box 12mm Label",
                            PrinterName = "Missing Brother Printer"
                        }
                    }).GetAwaiter().GetResult();

                AssertFalse(missingPrinterRes.AllSucceeded, "PrintLabelsAsync must reject printers absent from installed queues");
                AssertTrue(missingPrinterRes.Error.Contains("not installed", StringComparison.OrdinalIgnoreCase),
                    $"Expected not-installed error message, actual: {missingPrinterRes.Error}");
            });
        }

        private static void BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved()
        {
            var serverTypes = new[]
            {
                new OpenBisTypeInfoDto { Code = "SUBSTANCE", Description = "Substance" },
                new OpenBisTypeInfoDto { Code = "CRYSTAL", Description = "Crystal" },
                new OpenBisTypeInfoDto { Code = "CUSTOM_PLATE", Description = "" }
            };

            List<ConfiguredOpenBisType> initialRows = LabelStudioBackend.BuildInitialTypeRows(
                serverTypes,
                Array.Empty<ConfiguredOpenBisType>());

            AssertEqual(3, initialRows.Count, "Expected one row per server type");
            AssertEqual(LabelTemplateProfile.Molecule, initialRows[0].TemplateProfile, "SUBSTANCE must default to Molecule");
            AssertEqual("Substance", initialRows[0].DisplayName, "SUBSTANCE display name mismatch");
            AssertEqual(LabelTemplateProfile.Generic, initialRows[1].TemplateProfile, "CRYSTAL must default to Generic");
            AssertEqual(LabelTemplateProfile.Generic, initialRows[2].TemplateProfile, "CUSTOM_PLATE must default to Generic");
            AssertEqual("Custom Plate", initialRows[2].DisplayName, "Blank description should fall back to formatted code");

            var saved = new[]
            {
                new ConfiguredOpenBisType
                {
                    Code = "CRYSTAL",
                    DisplayName = "Crystal",
                    TemplateProfile = LabelTemplateProfile.Molecule
                }
            };

            List<ConfiguredOpenBisType> mergedRows = LabelStudioBackend.BuildInitialTypeRows(serverTypes, saved);
            AssertEqual(LabelTemplateProfile.Molecule, mergedRows[0].TemplateProfile, "SUBSTANCE should still default to Molecule");
            AssertEqual(LabelTemplateProfile.Molecule, mergedRows[1].TemplateProfile, "Saved CRYSTAL mapping must override Generic default");
            AssertEqual(LabelTemplateProfile.Generic, mergedRows[2].TemplateProfile, "Unconfigured CUSTOM_PLATE should default to Generic");

            // Also verify inside SetupSettingsForm that existing saved mappings stay checked while unseen types stay unchecked.
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok" });
                configService.SaveConfiguredTypes(saved);
                configService.SavePrinterConfig(AppConfigService.CreateDefaultPrinterConfig());

                var client = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);
                using var backend = new LabelStudioBackend(
                    configService,
                    client,
                    renderer,
                    installedPrintersProvider: () => new[] { "Brother PT-P700" });
                using var form = new SetupSettingsForm(backend, SetupDialogMode.Settings);

                form.ApplyTypeRows(new[]
                {
                    new ObjectTypeRowDto
                    {
                        IsSelected = false,
                        Code = mergedRows[0].Code,
                        DisplayName = mergedRows[0].DisplayName,
                        TemplateProfile = mergedRows[0].TemplateProfile
                    },
                    new ObjectTypeRowDto
                    {
                        IsSelected = true,
                        Code = mergedRows[1].Code,
                        DisplayName = mergedRows[1].DisplayName,
                        TemplateProfile = mergedRows[1].TemplateProfile
                    },
                    new ObjectTypeRowDto
                    {
                        IsSelected = false,
                        Code = mergedRows[2].Code,
                        DisplayName = mergedRows[2].DisplayName,
                        TemplateProfile = mergedRows[2].TemplateProfile
                    }
                });
                var grid = FindControl<DataGridView>(form, "dgvObjectTypes");
                var txtFilter = FindControl<TextBox>(form, "txtTypeFilter");
                AssertEqual(3, grid.Rows.Count, "Grid should display all 3 server types");
                AssertFalse(Convert.ToBoolean(grid.Rows[0].Cells[0].Value), "Unconfigured SUBSTANCE should not be pre-checked when saved config exists");
                AssertTrue(Convert.ToBoolean(grid.Rows[1].Cells[0].Value), "Saved CRYSTAL type must stay checked");
                AssertFalse(Convert.ToBoolean(grid.Rows[2].Cells[0].Value), "Unconfigured CUSTOM_PLATE should not be checked");

                // Filtering must preserve checked/style state across hidden rows.
                txtFilter.Text = "CUSTOM_PLATE";
                AssertEqual(1, grid.Rows.Count, "Filter should narrow visible rows to CUSTOM_PLATE");
                AssertTrue(form.TrySaveSettingsOffline(out string filteredErr), $"Expected offline save while filtered to succeed: {filteredErr}");
                txtFilter.Text = "";
                AssertEqual(3, grid.Rows.Count, "Clearing filter should restore all 3 rows");
                AssertTrue(Convert.ToBoolean(grid.Rows[1].Cells[0].Value), "CRYSTAL must stay checked after filter round-trip");

                List<ConfiguredOpenBisType> persisted = configService.LoadConfiguredTypes();
                AssertEqual(1, persisted.Count, "Only saved/checked CRYSTAL should be persisted");
                AssertEqual("CRYSTAL", persisted[0].Code, "Persisted code mismatch");
                AssertEqual(LabelTemplateProfile.Molecule, persisted[0].TemplateProfile, "Persisted CRYSTAL profile mismatch");
            });
        }

        private static void SetupSettingsFormInitializesSafelyWithZeroPrinters()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                var client = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);

                using (var wizardBackend = new LabelStudioBackend(
                    configService,
                    client,
                    renderer,
                    installedPrintersProvider: () => Array.Empty<string>()))
                using (var wizardForm = new SetupSettingsForm(
                    wizardBackend,
                    SetupDialogMode.Wizard,
                    SetupWizardStep.Printers))
                {
                    AssertEqual(SetupDialogMode.Wizard, wizardForm.Mode, "Expected Wizard mode");
                    ShowOffscreen(wizardForm);
                    var warningLabel = FindControl<Label>(wizardForm, "lblNoPrintersWarning");
                    AssertTrue(warningLabel.Visible, "Zero-printer warning label must be visible on Printers step");

                    var cmbBox = FindControl<ComboBox>(wizardForm, "cmbSmallBoxPrinter");
                    var cmbMagnet = FindControl<ComboBox>(wizardForm, "cmbMagnetPrinter");
                    var cmbFlask = FindControl<ComboBox>(wizardForm, "cmbFlaskPrinter");
                    AssertEqual("Brother PT-P700", cmbBox.Text, "SmallBox printer should fall back to default");
                    AssertEqual("Brother PT-P700 (Kopie 1)", cmbMagnet.Text, "Magnet printer should fall back to default");
                    AssertEqual("Brother PT-P700 (Kopie 2)", cmbFlask.Text, "Flask printer should fall back to default");
                }

                configService.SaveAppConfig(new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok" });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType { Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule }
                });
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Brother PT-P700 (Custom Box)",
                    MagnetPrinter = "Brother PT-P700 (Custom Magnet)",
                    FlaskPrinter = "Brother PT-P700 (Custom Flask)"
                });

                using (var settingsBackend = new LabelStudioBackend(
                    configService,
                    client,
                    renderer,
                    installedPrintersProvider: () => new[] { "OneNote (Desktop)", "Microsoft Print to PDF" }))
                using (var settingsForm = new SetupSettingsForm(
                    settingsBackend,
                    SetupDialogMode.Settings,
                    SetupWizardStep.Printers))
                {
                    AssertEqual(SetupDialogMode.Settings, settingsForm.Mode, "Expected Settings mode");
                    var tabObjectTypes = FindControl<Button>(settingsForm, "btnTabObjectTypes");
                    var lblWizardTitle = FindControl<Label>(settingsForm, "lblWizardTitle");
                    var prgTypes = FindControl<ProgressBar>(settingsForm, "prgTypesLoading");
                    var prgConn = FindControl<ProgressBar>(settingsForm, "prgConnectionLoading");
                    AssertFalse(tabObjectTypes.UseMnemonic, "Tab button must disable UseMnemonic so '&' does not turn into an extra space");
                    AssertFalse(lblWizardTitle.UseMnemonic, "Wizard title must disable UseMnemonic so '&' renders cleanly");
                    AssertFalse(prgTypes.Visible, "Types loading bar should start hidden");
                    AssertFalse(prgConn.Visible, "Connection loading bar should start hidden");
                    ShowOffscreen(settingsForm);
                    var warningLabel = FindControl<Label>(settingsForm, "lblNoPrintersWarning");
                    AssertTrue(warningLabel.Visible, "Zero-Brother-printer warning label must be visible when only non-Brother printers exist");

                    var cmbBox = FindControl<ComboBox>(settingsForm, "cmbSmallBoxPrinter");
                    var cmbMagnet = FindControl<ComboBox>(settingsForm, "cmbMagnetPrinter");
                    var cmbFlask = FindControl<ComboBox>(settingsForm, "cmbFlaskPrinter");
                    AssertEqual("Brother PT-P700 (Custom Box)", cmbBox.Text, "SmallBox printer should keep configured Brother name");
                    AssertEqual("Brother PT-P700 (Custom Magnet)", cmbMagnet.Text, "Magnet printer should keep configured Brother name");
                    AssertEqual("Brother PT-P700 (Custom Flask)", cmbFlask.Text, "Flask printer should keep configured Brother name");
                    AssertFalse(cmbBox.Items.Contains("Microsoft Print to PDF"), "Non-Brother printers must never appear in Printer dropdown");
                }
            });
        }

        private static void SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "stale-expired-token"
                });

                var client = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);
                using var backend = new LabelStudioBackend(
                    configService,
                    client,
                    renderer,
                    installedPrintersProvider: () => Array.Empty<string>());

                using var form = new SetupSettingsForm(
                    backend,
                    SetupDialogMode.ExpiredTokenRecovery,
                    SetupWizardStep.Connection,
                    initialWarning: "Token expired");

                AssertEqual(SetupDialogMode.ExpiredTokenRecovery, form.Mode, "Expected ExpiredTokenRecovery mode");
                var txtUrl = FindControl<TextBox>(form, "txtInstanceUrl");
                var txtToken = FindControl<TextBox>(form, "txtToken");
                var lblStatus = FindControl<Label>(form, "lblConnectionStatus");

                AssertEqual("https://openbis.example.org", txtUrl.Text, "Saved URL must be pre-filled");
                AssertEqual("", txtToken.Text, "Token field must be cleared in ExpiredTokenRecovery mode");
                AssertFalse(txtUrl.ReadOnly, "Instance URL textbox must not be ReadOnly before async validation");
                AssertFalse(txtToken.ReadOnly, "Token textbox must not be ReadOnly before async validation");
                AssertTrue(lblStatus.Text.Contains("Token expired", StringComparison.OrdinalIgnoreCase),
                    $"Expected inline warning to show initial warning, actual: {lblStatus.Text}");
                AssertTrue(ReferenceEquals(form.ActiveControl, txtToken), "Token textbox must be ActiveControl for immediate replacement");

                ShowOffscreen(form);
                AssertTrue(ReferenceEquals(form.ActiveControl, txtToken), "Token textbox must stay active after Shown");
            });
        }

        private static void SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://initial.example.org",
                    Token = "initial-token"
                });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType { Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                    new ConfiguredOpenBisType { Code = "CRYSTAL", DisplayName = "Crystal", TemplateProfile = LabelTemplateProfile.Generic }
                });
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Brother PT-P700 (A)",
                    MagnetPrinter = "Brother PT-P700 (B)",
                    FlaskPrinter = "Brother PT-P700 (C)"
                });

                string origAppJson = File.ReadAllText(configService.OpenBisConfigPath);
                string origTypesJson = File.ReadAllText(configService.OpenBisTypesPath);
                string origPrintersJson = File.ReadAllText(configService.PrintersConfigPath);

                var offlineClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);
                string[] printers = { "Brother PT-P700 (A)", "Brother PT-P700 (B)", "Brother PT-P700 (C)", "Brother PT-P700 (X)", "Brother PT-P700 (Y)", "Brother PT-P700 (Z)" };
                using var backend = new LabelStudioBackend(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => printers);

                // 1. Modify state and Cancel -> disk files must remain untouched.
                using (var cancelForm = new SetupSettingsForm(backend, SetupDialogMode.Settings))
                {
                    FindControl<TextBox>(cancelForm, "txtInstanceUrl").Text = "https://changed.example.org";
                    FindControl<TextBox>(cancelForm, "txtToken").Text = "changed-token";
                    FindControl<ComboBox>(cancelForm, "cmbSmallBoxPrinter").SelectedItem = "Brother PT-P700 (X)";
                    FindControl<DataGridView>(cancelForm, "dgvObjectTypes").Rows[1].Cells[2].Value = "Molecule / Substance";

                    ShowOffscreen(cancelForm);
                    FindControl<Button>(cancelForm, "btnCancel").PerformClick();
                }

                AssertEqual(origAppJson, File.ReadAllText(configService.OpenBisConfigPath), "Cancel must leave openbis_config.json unchanged");
                AssertEqual(origTypesJson, File.ReadAllText(configService.OpenBisTypesPath), "Cancel must leave openbis_types.json unchanged");
                AssertEqual(origPrintersJson, File.ReadAllText(configService.PrintersConfigPath), "Cancel must leave printers_config.json unchanged");

                // 2. Verify live example preview rendering on Page 2 + invalid save guard + valid TrySaveSettingsOffline.
                using (var saveForm = new SetupSettingsForm(
                    backend,
                    SetupDialogMode.Settings,
                    SetupWizardStep.ObjectTypes))
                {
                    // Pre-seed TypesSyncedFromServer so OnShown does not spawn offlineClient during UI preview verification.
                    saveForm.ApplyTypeRows(new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new ObjectTypeRowDto { IsSelected = true, Code = "CRYSTAL", DisplayName = "Crystal", TemplateProfile = LabelTemplateProfile.Generic }
                    });
                    ShowOffscreen(saveForm);

                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewSmallBox").Image, "SmallBox preview image should be populated");
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewMagnet").Image, "Magnet preview image should be populated");
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewFlask").Image, "Flask preview image should be populated");

                    FindControl<Button>(saveForm, "btnPreviewGeneric").PerformClick();
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewMagnet").Image, "Generic Magnet preview image should be populated");
                    AssertTrue(saveForm.Visible, "ObjectTypes page should remain responsive while async previews render");

                    // Verify ApplyTypeRows preserves current row selection instead of jumping back to row 0.
                    var typesGrid = FindControl<DataGridView>(saveForm, "dgvObjectTypes");
                    typesGrid.CurrentCell = typesGrid.Rows[1].Cells[1];
                    saveForm.ApplyTypeRows(new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new ObjectTypeRowDto { IsSelected = true, Code = "CRYSTAL", DisplayName = "Crystal", TemplateProfile = LabelTemplateProfile.Generic },
                        new ObjectTypeRowDto { IsSelected = false, Code = "SAMPLE", DisplayName = "Sample", TemplateProfile = LabelTemplateProfile.Generic }
                    });
                    string? activeText = typesGrid.CurrentRow?.Cells[1].Value?.ToString();
                    AssertTrue(activeText != null && activeText.Contains("CRYSTAL", StringComparison.OrdinalIgnoreCase),
                        $"ApplyTypeRows must preserve selected row across background sync, actual: {activeText}");

                    FindControl<TextBox>(saveForm, "txtInstanceUrl").Text = "https://updated.example.org";
                    FindControl<TextBox>(saveForm, "txtToken").Text = "";
                    AssertFalse(saveForm.TrySaveSettingsOffline(out string emptyTokenErr), "Empty token must fail offline validation");
                    AssertTrue(!string.IsNullOrWhiteSpace(emptyTokenErr), "Expected non-empty validation error for empty token");

                    FindControl<TextBox>(saveForm, "txtToken").Text = "updated-token";
                    var grid = FindControl<DataGridView>(saveForm, "dgvObjectTypes");
                    grid.Rows[0].Cells[0].Value = false;
                    grid.Rows[1].Cells[0].Value = false;
                    AssertFalse(saveForm.TrySaveSettingsOffline(out string noTypeErr), "Zero checked types must fail offline validation");
                    AssertEqual("Select at least one object type.", noTypeErr, "Expected exact empty object types error message");

                    grid.Rows[0].Cells[0].Value = true;
                    grid.Rows[1].Cells[0].Value = true;
                    grid.Rows[1].Cells[2].Value = "Molecule / Substance";

                    FindControl<ComboBox>(saveForm, "cmbSmallBoxPrinter").SelectedItem = "Brother PT-P700 (X)";
                    FindControl<ComboBox>(saveForm, "cmbMagnetPrinter").SelectedItem = "Brother PT-P700 (Y)";
                    FindControl<ComboBox>(saveForm, "cmbFlaskPrinter").SelectedItem = "Brother PT-P700 (Z)";

                    bool saved = saveForm.TrySaveSettingsOffline(out string validationError);
                    AssertTrue(saved, $"Expected TrySaveSettingsOffline to succeed, got error: {validationError}");
                    AssertEqual("", validationError, "Expected empty validation error on successful save");
                }

                var verifyService = new AppConfigService(tempRoot);
                AppConfig? savedApp = verifyService.LoadAppConfig();
                AssertNotNull(savedApp, "Saved AppConfig missing");
                AssertEqual("https://updated.example.org", savedApp!.OpenBisUrl, "Updated URL mismatch");
                AssertEqual("updated-token", savedApp.Token, "Updated token mismatch");

                List<ConfiguredOpenBisType> savedTypes = verifyService.LoadConfiguredTypes();
                AssertEqual(2, savedTypes.Count, "Expected 2 saved types");
                AssertEqual(LabelTemplateProfile.Molecule, savedTypes[1].TemplateProfile, "Updated CRYSTAL template profile mismatch");

                PrinterConfig? savedPrinters = verifyService.LoadPrinterConfig();
                AssertNotNull(savedPrinters, "Saved PrinterConfig missing");
                AssertEqual("Brother PT-P700 (X)", savedPrinters!.SmallBoxPrinter, "Updated SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (Y)", savedPrinters.MagnetPrinter, "Updated MagnetPrinter mismatch");
                AssertEqual("Brother PT-P700 (Z)", savedPrinters.FlaskPrinter, "Updated FlaskPrinter mismatch");
            });
        }

        private static void SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"OpenBISLabelStudioFileBase_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFile, "blocking directory creation");

            try
            {
                var configService = new AppConfigService(tempFile);
                var offlineClient = new OpenBisClient(Path.GetTempPath(), "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);
                using var backend = new LabelStudioBackend(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => new[] { "Printer A" });

                using var form = new SetupSettingsForm(backend, SetupDialogMode.Settings);

                var txtUrl = FindControl<TextBox>(form, "txtInstanceUrl");
                var txtToken = FindControl<TextBox>(form, "txtToken");
                AssertFalse(txtUrl.ReadOnly, "Instance URL textbox must start writable");
                AssertFalse(txtToken.ReadOnly, "Token textbox must start writable");

                txtUrl.Text = "https://openbis.example.org";
                txtToken.Text = "tok-123";
                form.ApplyTypeRows(new[]
                {
                    new ObjectTypeRowDto
                    {
                        IsSelected = true,
                        Code = "SUBSTANCE",
                        DisplayName = "Substance",
                        TemplateProfile = LabelTemplateProfile.Molecule
                    }
                });
                var grid = FindControl<DataGridView>(form, "dgvObjectTypes");
                grid.Rows[0].Cells[0].Value = true;

                bool saved = form.TrySaveSettingsOffline(out string diskError);
                AssertFalse(saved, "TrySaveSettingsOffline must return false when config directory creation fails");
                AssertTrue(!string.IsNullOrWhiteSpace(diskError), "TrySaveSettingsOffline must return a non-empty inline error on disk failure");
                AssertTrue(diskError.Contains("Could not save configuration files", StringComparison.OrdinalIgnoreCase),
                    $"Expected clear save error prefix, actual: {diskError}");

                ShowOffscreen(form);
                FindControl<Button>(form, "btnNextFinish").PerformClick();
                var lblFooterError = FindControl<Label>(form, "lblFooterError");
                AssertTrue(form.Visible, "Settings form must remain open when disk save fails");
                AssertFalse(form.DialogResult == DialogResult.OK, "DialogResult must not be OK when disk save fails");
                AssertTrue(lblFooterError.Visible && lblFooterError.Text.Contains("Could not save configuration files", StringComparison.OrdinalIgnoreCase),
                    $"Expected visible footer error on save failure, actual: {lblFooterError.Text}");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static void Form1CredentialsChangedSinceRequestDetectsUrlAndTokenChanges()
        {
            AssertFalse(
                LabelStudioBackend.CredentialsChangedSinceRequest("https://openbis.example.org", "tok-1", null),
                "Null currentConfig must return false");

            AssertFalse(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://other.example.org", Token = "" }),
                "Empty token in currentConfig must return false");

            AssertFalse(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://other.example.org", Token = "   " }),
                "Whitespace token in currentConfig must return false");

            AssertFalse(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok-1" }),
                "Matching URL and token must return false");

            AssertTrue(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok-2" }),
                "Updated token must return true");

            AssertTrue(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://updated.example.org", Token = "tok-1" }),
                "Updated URL with non-empty token must return true");

            AssertTrue(
                LabelStudioBackend.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://updated.example.org", Token = "tok-2" }),
                "Updated URL and token must return true");
        }

        private static void Form1ResolveTemplateProfileUsesConfiguredMappingAndFallbacks()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType
                    {
                        Code = "CRYSTAL",
                        DisplayName = "Crystal",
                        TemplateProfile = LabelTemplateProfile.Molecule
                    },
                    new ConfiguredOpenBisType
                    {
                        Code = "SUBSTANCE",
                        DisplayName = "Substance",
                        TemplateProfile = LabelTemplateProfile.Generic
                    },
                    new ConfiguredOpenBisType
                    {
                        Code = "CUSTOM_2D",
                        DisplayName = "Custom 2D",
                        TemplateProfile = LabelTemplateProfile.Molecule
                    }
                });

                List<ConfiguredOpenBisType> configuredTypes = configService.LoadConfiguredTypes();

                // Mapped non-SUBSTANCE type must resolve to Molecule.
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    LabelStudioBackend.ResolveTemplateProfile("CRYSTAL", configuredTypes, null),
                    "Mapped non-SUBSTANCE type should resolve to Molecule");

                // Lookup must be case-insensitive and trim whitespace.
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    LabelStudioBackend.ResolveTemplateProfile("  crystal  ", configuredTypes, null),
                    "ResolveTemplateProfile should be case-insensitive and trim whitespace");

                // Explicit configured mapping overrides legacy SUBSTANCE assumption.
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    LabelStudioBackend.ResolveTemplateProfile("SUBSTANCE", configuredTypes, null),
                    "Configured Generic profile for SUBSTANCE must override legacy hardcoded assumption");

                // Fallback via sampleDetailsType when selectedTypeCode is empty, null, or unconfigured.
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    LabelStudioBackend.ResolveTemplateProfile("", configuredTypes, "CUSTOM_2D"),
                    "Empty selectedTypeCode should fall back to sampleDetailsType");
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    LabelStudioBackend.ResolveTemplateProfile(null, configuredTypes, "custom_2d"),
                    "Null selectedTypeCode should fall back to case-insensitive sampleDetailsType");
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    LabelStudioBackend.ResolveTemplateProfile("UNCONFIGURED", configuredTypes, "CRYSTAL"),
                    "Unconfigured selectedTypeCode should fall back to sampleDetailsType");

                // Unmapped types default to Generic regardless of hardcoded SUBSTANCE name.
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    LabelStudioBackend.ResolveTemplateProfile("UNCONFIGURED", configuredTypes, "ALSO_UNCONFIGURED"),
                    "Unmapped type should resolve to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    LabelStudioBackend.ResolveTemplateProfile(null, configuredTypes, null),
                    "Null inputs should resolve to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    LabelStudioBackend.ResolveTemplateProfile("SUBSTANCE", Array.Empty<ConfiguredOpenBisType>(), "SUBSTANCE"),
                    "Empty configured types should default to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    LabelStudioBackend.ResolveTemplateProfile("SUBSTANCE", null, "SUBSTANCE"),
                    "Null configured types should default safely to Generic");
            });
        }

        private static void Form1HasSettingsButtonWithoutOverlaps()
        {
            using var form = new Form1();
            Button btnLoad = FindControl<Button>(form, "loadPrintTemplates");
            Button btnPrint = FindControl<Button>(form, "btnPrintLabel");
            Button btnSettings = FindControl<Button>(form, "btnSettings");
            Label lblStatus = FindControl<Label>(form, "lblStatusNotice");
            ProgressBar prgLoading = FindControl<ProgressBar>(form, "prgLoading");
            ComboBox cmbObjects = FindControl<ComboBox>(form, "openbisObjectsComboBox");
            GroupBox grpBox = FindControl<GroupBox>(form, "smallBoxLabelGroupBox");
            GroupBox grpMagnet = FindControl<GroupBox>(form, "magnetLabelGroupBox");
            GroupBox grpFlask = FindControl<GroupBox>(form, "flaskLabelGroupBox");

            AssertEqual("Settings...", btnSettings.Text, "Settings button text mismatch");
            AssertTrue(btnSettings.Enabled, "Settings button must be enabled");
            AssertFalse(prgLoading.Visible, "Main loading bar should start hidden");

            Rectangle clientBounds = new Rectangle(Point.Empty, form.ClientSize);
            AssertTrue(clientBounds.Contains(btnLoad.Bounds), $"Load button outside ClientSize: {btnLoad.Bounds}");
            AssertTrue(clientBounds.Contains(btnPrint.Bounds), $"Print button outside ClientSize: {btnPrint.Bounds}");
            AssertTrue(clientBounds.Contains(btnSettings.Bounds), $"Settings button outside ClientSize: {btnSettings.Bounds}");
            AssertTrue(clientBounds.Contains(lblStatus.Bounds), $"Status notice label outside ClientSize: {lblStatus.Bounds}");
            AssertTrue(clientBounds.Contains(prgLoading.Bounds), $"Loading bar outside ClientSize: {prgLoading.Bounds}");

            AssertFalse(btnLoad.Bounds.IntersectsWith(btnPrint.Bounds), "Load and Print buttons must not overlap");
            AssertFalse(btnLoad.Bounds.IntersectsWith(btnSettings.Bounds), "Load and Settings buttons must not overlap");
            AssertFalse(btnPrint.Bounds.IntersectsWith(btnSettings.Bounds), "Print and Settings buttons must not overlap");
            AssertFalse(lblStatus.Bounds.IntersectsWith(cmbObjects.Bounds), "Status notice must not overlap objects dropdown");
            AssertFalse(lblStatus.Bounds.IntersectsWith(prgLoading.Bounds), "Status notice must not overlap loading bar");
            AssertFalse(prgLoading.Bounds.IntersectsWith(cmbObjects.Bounds), "Loading bar must not overlap objects dropdown");
            AssertFalse(prgLoading.Bounds.IntersectsWith(grpBox.Bounds), "Loading bar must not overlap Small Box group");
            AssertFalse(prgLoading.Bounds.IntersectsWith(grpMagnet.Bounds), "Loading bar must not overlap Magnet group");
            AssertFalse(prgLoading.Bounds.IntersectsWith(grpFlask.Bounds), "Loading bar must not overlap Flask group");
            AssertFalse(lblStatus.Bounds.IntersectsWith(grpBox.Bounds), "Status notice must not overlap Small Box group");
            AssertFalse(lblStatus.Bounds.IntersectsWith(grpMagnet.Bounds), "Status notice must not overlap Magnet group");
            AssertFalse(lblStatus.Bounds.IntersectsWith(grpFlask.Bounds), "Status notice must not overlap Flask group");
            AssertFalse(grpBox.Bounds.IntersectsWith(btnLoad.Bounds), "GroupBoxes must not overlap bottom buttons");
            AssertFalse(grpMagnet.Bounds.IntersectsWith(btnPrint.Bounds), "Magnet group must not overlap Print button");
            AssertFalse(grpFlask.Bounds.IntersectsWith(btnSettings.Bounds), "Flask group must not overlap Settings button");
        }

        private static void Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "valid-looking-token"
                });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType
                    {
                        Code = "CRYSTAL",
                        DisplayName = "Lab Crystal",
                        TemplateProfile = LabelTemplateProfile.Molecule
                    },
                    new ConfiguredOpenBisType
                    {
                        Code = "SUBSTRATE",
                        DisplayName = "Silicon Substrate",
                        TemplateProfile = LabelTemplateProfile.Generic
                    }
                });
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Brother PT-P700 (A)",
                    MagnetPrinter = "Brother PT-P700 (B)",
                    FlaskPrinter = "Brother PT-P700 (C)"
                });

                var offlineClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);

                // 1. Verify safe startup with zero Brother PT-P700 printers and cached types shown alongside non-fatal notice.
                using (var zeroPrinterBackend = new LabelStudioBackend(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => new[] { "OneNote (Desktop)", "Microsoft Print to PDF" }))
                using (var zeroPrinterForm = new Form1(zeroPrinterBackend))
                {
                    ShowOffscreen(zeroPrinterForm);

                    var cmbTypes = FindControl<ComboBox>(zeroPrinterForm, "openbisTypeComboBox");
                    var lblNotice = FindControl<Label>(zeroPrinterForm, "lblStatusNotice");
                    var btnSettings = FindControl<Button>(zeroPrinterForm, "btnSettings");
                    var cmbBox = FindControl<ComboBox>(zeroPrinterForm, "smallBoxPrinterComboBox");
                    var cmbMagnet = FindControl<ComboBox>(zeroPrinterForm, "magnetPrinterComboBox");
                    var cmbFlask = FindControl<ComboBox>(zeroPrinterForm, "flaskPrinterComboBox");

                    AssertTrue(btnSettings.Visible && btnSettings.Enabled, "Settings button must be visible and enabled when shown");
                    AssertEqual(3, cmbTypes.Items.Count, "Expected placeholder plus 2 cached types in dropdown");
                    AssertTrue(lblNotice.Visible && !string.IsNullOrWhiteSpace(lblNotice.Text),
                        "Expected non-fatal notice banner when startup type refresh fails");
                    AssertEqual("Brother PT-P700 (A)", cmbBox.SelectedItem?.ToString(), "Zero-Brother-printer fallback should preserve SmallBoxPrinter");
                    AssertEqual("Brother PT-P700 (B)", cmbMagnet.SelectedItem?.ToString(), "Zero-Brother-printer fallback should preserve MagnetPrinter");
                    AssertEqual("Brother PT-P700 (C)", cmbFlask.SelectedItem?.ToString(), "Zero-Brother-printer fallback should preserve FlaskPrinter");
                    AssertFalse(cmbBox.Items.Contains("Microsoft Print to PDF"), "Main form printer dropdown must exclude non-Brother printers");
                }

                // 2. Verify initial mismatch does not overwrite disk during startup population, and manual selection immediately persists.
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Missing Brother PT-P700",
                    MagnetPrinter = "Brother PT-P700 (B)",
                    FlaskPrinter = "Brother PT-P700 (C)"
                });

                using (var printerBackend = new LabelStudioBackend(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => new[] { "Microsoft Print to PDF", "Brother PT-P700 (A)", "Brother PT-P700 (B)", "Brother PT-P700 (C)" }))
                using (var form = new Form1(printerBackend))
                {
                    ShowOffscreen(form);

                    var cmbBox = FindControl<ComboBox>(form, "smallBoxPrinterComboBox");
                    var cmbMagnet = FindControl<ComboBox>(form, "magnetPrinterComboBox");
                    var cmbFlask = FindControl<ComboBox>(form, "flaskPrinterComboBox");

                    AssertEqual(3, cmbBox.Items.Count, "Dropdown should contain only the 3 Brother PT-P700 queues");
                    AssertEqual("Brother PT-P700 (A)", cmbBox.SelectedItem?.ToString(), "Initial SmallBoxPrinter fallback selection mismatch");
                    AssertEqual("Brother PT-P700 (B)", cmbMagnet.SelectedItem?.ToString(), "Initial MagnetPrinter selection mismatch");
                    AssertEqual("Brother PT-P700 (C)", cmbFlask.SelectedItem?.ToString(), "Initial FlaskPrinter selection mismatch");
                    AssertEqual("Missing Brother PT-P700", configService.LoadPrinterConfig()!.SmallBoxPrinter,
                        "Startup printer fallback must not overwrite config/printers_config.json before manual selection");

                    cmbBox.SelectedIndex = 1;    // Brother PT-P700 (B)
                    cmbMagnet.SelectedIndex = 2; // Brother PT-P700 (C)
                    cmbFlask.SelectedIndex = 0;  // Brother PT-P700 (A)
                }

                PrinterConfig? persisted = configService.LoadPrinterConfig();
                AssertNotNull(persisted, "PrinterConfig should exist after auto-save");
                AssertEqual("Brother PT-P700 (B)", persisted!.SmallBoxPrinter, "Auto-saved SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (C)", persisted.MagnetPrinter, "Auto-saved MagnetPrinter mismatch");
                AssertEqual("Brother PT-P700 (A)", persisted.FlaskPrinter, "Auto-saved FlaskPrinter mismatch");
            });
        }

        private static void TokenRecoveryCoordinatorCoalescesConcurrentCallsAndResetsAfterCompletion()
        {
            static void WaitWithinTimeout(Task task, string timeoutMessage)
            {
                Task completed = Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
                AssertTrue(ReferenceEquals(completed, task), timeoutMessage);
            }

            SynchronizationContext? previousContext = SynchronizationContext.Current;
            WindowsFormsSynchronizationContext? ownedContext = null;
            if (previousContext is not WindowsFormsSynchronizationContext)
            {
                ownedContext = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(ownedContext);
            }

            try
            {
                var coordinator = new TokenRecoveryCoordinator();
                int promptCallCount = 0;

                var firstPromptGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<bool> firstCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return firstPromptGate.Task;
                });
                Task<bool> secondCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return Task.FromResult(false);
                });

                AssertEqual(1, promptCallCount, "Concurrent token recovery should invoke prompt only once");
                AssertTrue(ReferenceEquals(firstCall, secondCall), "Concurrent callers should share the active recovery task");

                firstPromptGate.SetResult(true);
                Task<bool[]> firstAll = Task.WhenAll(firstCall, secondCall);
                WaitWithinTimeout(firstAll, "Timed out waiting for concurrent recovery success under WindowsFormsSynchronizationContext");
                bool[] firstResults = firstAll.GetAwaiter().GetResult();
                AssertTrue(firstResults[0] && firstResults[1], "Concurrent callers should receive shared success result");

                var secondPromptGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<bool> thirdCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return secondPromptGate.Task;
                });
                Task<bool> fourthCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return Task.FromResult(true);
                });

                AssertEqual(2, promptCallCount, "Completed recovery should allow a new prompt invocation");
                secondPromptGate.SetResult(false);
                Task<bool[]> secondAll = Task.WhenAll(thirdCall, fourthCall);
                WaitWithinTimeout(secondAll, "Timed out waiting for concurrent recovery cancellation");
                bool[] secondResults = secondAll.GetAwaiter().GetResult();
                AssertTrue(!secondResults[0] && !secondResults[1], "Concurrent callers should receive shared cancel result");

                var failingPromptGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<bool> fifthCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return failingPromptGate.Task;
                });
                Task<bool> sixthCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return Task.FromResult(true);
                });

                AssertEqual(3, promptCallCount, "Cancelled recovery should reset in-flight state for subsequent prompts");
                failingPromptGate.SetException(new InvalidOperationException("Dialog failed"));

                Task faultedAll = Task.WhenAll(fifthCall, sixthCall);
                WaitWithinTimeout(faultedAll, "Timed out waiting for faulted concurrent recovery calls");

                foreach (Task<bool> call in new[] { fifthCall, sixthCall })
                {
                    bool observedFailure = false;
                    try
                    {
                        call.GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "Dialog failed")
                    {
                        observedFailure = true;
                    }

                    AssertTrue(observedFailure, "Concurrent callers should observe prompt exception");
                }

                Task<bool> seventhCall = coordinator.RecoverAsync(() =>
                {
                    promptCallCount++;
                    return Task.FromResult(true);
                });
                WaitWithinTimeout(seventhCall, "Timed out waiting for recovery after fault");
                bool seventhResult = seventhCall.GetAwaiter().GetResult();

                AssertEqual(4, promptCallCount, "Faulted recovery should clear in-flight state for later callers");
                AssertTrue(seventhResult, "Subsequent recovery after fault should succeed");
            }
            finally
            {
                if (ownedContext != null)
                {
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                    ownedContext.Dispose();
                }
            }
        }

        private static void BackendAssemblyHasNoUiControlDependencies()
        {
            Assembly backendAssembly = typeof(AppConfigService).Assembly;
            AssertEqual("OpenBISLabelStudio.Backend", backendAssembly.GetName().Name,
                "AppConfigService must live in OpenBISLabelStudio.Backend assembly");

            static bool IsDisallowedUiType(Type type) =>
                typeof(Control).IsAssignableFrom(type)
                || typeof(Form).IsAssignableFrom(type)
                || string.Equals(type.FullName, "System.Windows.Forms.MessageBox", StringComparison.Ordinal)
                || string.Equals(type.FullName, "System.Windows.Forms.Application", StringComparison.Ordinal);

            foreach (Type t in backendAssembly.GetTypes())
            {
                AssertFalse(IsDisallowedUiType(t), $"Backend type '{t.FullName}' must not reference WinForms UI types");
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    AssertFalse(IsDisallowedUiType(f.FieldType), $"Backend field '{t.FullName}.{f.Name}' must not reference UI types");
                }
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    AssertFalse(IsDisallowedUiType(m.ReturnType), $"Backend method '{t.FullName}.{m.Name}' return type must not reference UI types");
                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        AssertFalse(IsDisallowedUiType(p.ParameterType), $"Backend method '{t.FullName}.{m.Name}' parameter '{p.Name}' must not reference UI types");
                    }
                }
            }
        }

        private sealed class DelegateTokenRecoveryPrompter : ITokenRecoveryPrompter
        {
            private readonly Func<string, string, CancellationToken, Task<bool>> _handler;

            public DelegateTokenRecoveryPrompter(Func<string, string, CancellationToken, Task<bool>> handler)
            {
                _handler = handler;
            }

            public Task<bool> PromptForNewTokenAsync(string currentUrl, string warningMessage, CancellationToken ct = default)
            {
                return _handler(currentUrl, warningMessage, ct);
            }
        }

        private static void BackendExpiresTokenRecoveryCoalescesAndRetriesWithoutUiForms()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "old-tok"
                });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType { Code = "CRYSTAL", DisplayName = "Crystal", TemplateProfile = LabelTemplateProfile.Generic }
                });
                configService.SavePrinterConfig(AppConfigService.CreateDefaultPrinterConfig());

                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    "if '--worker' in sys.argv[1:]:\n" +
                    "    while True:\n" +
                    "        line = sys.stdin.readline()\n" +
                    "        if line == '':\n" +
                    "            break\n" +
                    "        if not line.strip():\n" +
                    "            continue\n" +
                    "        req = json.loads(line)\n" +
                    "        tok = req.get('token', '')\n" +
                    "        action = req.get('action', '')\n" +
                    "        if tok != 'new-tok':\n" +
                    "            resp = {'success': False, 'errorType': 'token_expired', 'error': 'Token invalid'}\n" +
                    "        elif action == 'list':\n" +
                    "            resp = {'success': True, 'data': [{'permId': 'P-1', 'name': 'Obj-1', 'modificationDate': '2026-11-20'}]}\n" +
                    "        elif action == 'types':\n" +
                    "            resp = {'success': True, 'types': [{'code': 'CRYSTAL', 'description': 'Crystal'}]}\n" +
                    "        else:\n" +
                    "            resp = {'success': True, 'sessionActive': True}\n" +
                    "        sys.stdout.write(json.dumps(resp) + '\\n')\n" +
                    "        sys.stdout.flush()\n");

                using var backend = new LabelStudioBackend(
                    tempRoot,
                    installedPrintersProvider: () => new[] { "Brother PT-P700" });

                int promptCount = 0;
                var promptEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var promptRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var coalescingPrompter = new DelegateTokenRecoveryPrompter(async (url, warning, ct) =>
                {
                    Interlocked.Increment(ref promptCount);
                    AssertEqual("https://openbis.example.org", url, "Recovery prompt URL mismatch");
                    AssertTrue(warning.Contains("Token invalid", StringComparison.Ordinal), $"Expected error detail in warning: {warning}");
                    promptEntered.TrySetResult(true);
                    await promptRelease.Task.ConfigureAwait(false);
                    configService.SaveAppConfig(new AppConfig
                    {
                        OpenBisUrl = url,
                        Token = "new-tok"
                    });
                    return true;
                });

                Task<SampleListResultDto> firstLoad = backend.LoadObjectsForTypeAsync("CRYSTAL", coalescingPrompter);
                AssertTrue(promptEntered.Task.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting for first recovery prompt");

                Task<SampleListResultDto> secondLoad = backend.LoadObjectsForTypeAsync("CRYSTAL", coalescingPrompter);
                Thread.Sleep(100);
                AssertEqual(1, Volatile.Read(ref promptCount), "Concurrent expired-token calls must coalesce to a single recovery prompt");

                promptRelease.SetResult(true);
                SampleListResultDto[] results = Task.WhenAll(firstLoad, secondLoad).GetAwaiter().GetResult();

                AssertEqual(1, Volatile.Read(ref promptCount), "Prompt must only run once for coalesced concurrent calls");
                foreach (SampleListResultDto res in results)
                {
                    AssertTrue(res.Success, $"Expected coalesced recovery retry to succeed, error: {res.ErrorMessage}");
                    AssertFalse(res.WasCancelled, "Successful recovery must not mark WasCancelled");
                    AssertEqual(1, res.Samples.Count, "Expected 1 sample after recovery retry");
                    AssertEqual("P-1", res.Samples[0].PermId, "Recovered sample PermId mismatch");
                    AssertEqual("Obj-1", res.Samples[0].Name, "Recovered sample Name mismatch");
                }

                // Reset token to stale value and verify canceled prompter returns WasCancelled + NoticeMessage cleanly.
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "old-tok"
                });

                int cancelPromptCount = 0;
                var cancelPrompter = new DelegateTokenRecoveryPrompter((_, _, _) =>
                {
                    cancelPromptCount++;
                    return Task.FromResult(false);
                });

                SampleListResultDto cancelledResult = backend.LoadObjectsForTypeAsync("CRYSTAL", cancelPrompter).GetAwaiter().GetResult();
                AssertFalse(cancelledResult.Success, "Canceled token recovery must return Success == false");
                AssertTrue(cancelledResult.WasCancelled, "Canceled token recovery must set WasCancelled == true");
                AssertTrue(!string.IsNullOrWhiteSpace(cancelledResult.NoticeMessage), "Canceled token recovery must provide non-empty NoticeMessage");
                AssertEqual(1, cancelPromptCount, "Canceled recovery should prompt once");

                // Verify faulted prompter does not wedge subsequent recovery attempts.
                int faultCount = 0;
                var faultPrompter = new DelegateTokenRecoveryPrompter((_, _, _) =>
                {
                    faultCount++;
                    return Task.FromException<bool>(new InvalidOperationException("Prompter boom"));
                });

                SampleListResultDto faultedResult = backend.LoadObjectsForTypeAsync("CRYSTAL", faultPrompter).GetAwaiter().GetResult();
                AssertFalse(faultedResult.Success, "Faulted prompter must return Success == false cleanly");
                AssertEqual(1, faultCount, "Faulted prompter should be invoked once");
                AssertTrue(faultedResult.ErrorMessage.Contains("Prompter boom", StringComparison.Ordinal),
                    $"Expected fault message to propagate cleanly, actual: {faultedResult.ErrorMessage}");

                // Verify RefreshWorkspaceTypesAsync recovers via prompter and updates type display name on disk.
                int refreshPromptCount = 0;
                var refreshPrompter = new DelegateTokenRecoveryPrompter((url, _, _) =>
                {
                    refreshPromptCount++;
                    configService.SaveAppConfig(new AppConfig { OpenBisUrl = url, Token = "new-tok" });
                    return Task.FromResult(true);
                });

                BackgroundTypeRefreshResult refreshRes = backend.RefreshWorkspaceTypesAsync(refreshPrompter).GetAwaiter().GetResult();
                AssertTrue(refreshRes.Success, $"RefreshWorkspaceTypesAsync should succeed after recovery, error: {refreshRes.ErrorMessage}");
                AssertEqual(1, refreshPromptCount, "RefreshWorkspaceTypesAsync should prompt once when token expired");
                AssertEqual("Crystal", refreshRes.ConfiguredTypes[0].DisplayText, "RefreshWorkspaceTypesAsync should update DisplayText");
            });
        }

        private static void BackendLoadSamplePreviewsAndFreeTextIsolationPreventsStaleState()
        {
            static void AssertValidPngBytes(byte[] bytes, string context)
            {
                AssertTrue(bytes != null && bytes.Length > 8, $"{context}: PNG byte array must be non-empty");
                AssertEqual((byte)0x89, bytes![0], $"{context}: PNG byte[0] signature mismatch");
                AssertEqual((byte)0x50, bytes[1], $"{context}: PNG byte[1] signature mismatch");
                AssertEqual((byte)0x4E, bytes[2], $"{context}: PNG byte[2] signature mismatch");
                AssertEqual((byte)0x47, bytes[3], $"{context}: PNG byte[3] signature mismatch");
            }

            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "valid-tok"
                });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType { Code = "SUBSTRATE", DisplayName = "Silicon Substrate", TemplateProfile = LabelTemplateProfile.Generic },
                    new ConfiguredOpenBisType { Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule }
                });
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Brother PT-P700",
                    MagnetPrinter = "Brother PT-P700 (Kopie 1)",
                    FlaskPrinter = "Brother PT-P700 (Kopie 2)"
                });

                string detailsLog = Path.Combine(tempRoot, "details_requests.log");
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    $"log_path = r'''{detailsLog}'''\n" +
                    "if '--worker' in sys.argv[1:]:\n" +
                    "    while True:\n" +
                    "        line = sys.stdin.readline()\n" +
                    "        if line == '':\n" +
                    "            break\n" +
                    "        if not line.strip():\n" +
                    "            continue\n" +
                    "        req = json.loads(line)\n" +
                    "        action = req.get('action', '')\n" +
                    "        if action == 'list':\n" +
                    "            resp = {'success': True, 'data': [{'permId': 'PERM-1', 'name': 'Sub-1', 'modificationDate': '2026-11-20'}]}\n" +
                    "        elif action == 'details':\n" +
                    "            with open(log_path, 'a', encoding='utf-8') as f:\n" +
                    "                f.write(json.dumps({'permId': req.get('permId'), 'skipEln': req.get('skipEln', False)}) + '\\n')\n" +
                    "            resp = {'success': True, 'details': {'permId': req.get('permId', ''), 'sampleType': 'SUBSTRATE', 'name': 'Sub-1'}}\n" +
                    "        else:\n" +
                    "            resp = {'success': True, 'sessionActive': True}\n" +
                    "        sys.stdout.write(json.dumps(resp) + '\\n')\n" +
                    "        sys.stdout.flush()\n");

                using var backend = new LabelStudioBackend(
                    tempRoot,
                    installedPrintersProvider: () => new[] { "Brother PT-P700" });

                // 1. Before any sample is loaded: FreeText render returns empty byte[], Print returns exact error message.
                byte[] preLoadFreeText = backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "custom").GetAwaiter().GetResult();
                AssertEqual(0, preLoadFreeText.Length, "RenderSlotWithFreeTextAsync before sample load must return empty byte[]");

                LabelPrintBatchResult preLoadPrint = backend.PrintSelectedLabelsAsync(new[]
                {
                    new PrintSlotSelection { Slot = LabelTemplateType.SmallBox12mm, PrinterName = "Brother PT-P700" }
                }).GetAwaiter().GetResult();
                AssertFalse(preLoadPrint.AllSucceeded, "PrintSelectedLabelsAsync before sample load must fail");
                AssertEqual("No active sample loaded. Click 'Load Templates' first.", preLoadPrint.Error,
                    "Expected exact no-active-sample print error message");

                // 2. Loading sample previews sets active sample, skips ELN for Generic, and streams 3 valid PNGs.
                var streamedSlots = new List<(LabelTemplateType Slot, byte[] Bytes)>();
                SamplePreviewBundleDto bundle = backend.LoadSamplePreviewsAsync(
                    "SUBSTRATE",
                    "PERM-1",
                    "M1",
                    "F1",
                    onSlotReady: (slot, png) => streamedSlots.Add((slot, png))).GetAwaiter().GetResult();

                AssertTrue(bundle.Success, $"LoadSamplePreviewsAsync should succeed, error: {bundle.ErrorMessage}");
                AssertTrue(bundle.IsGenericProfile, "SUBSTRATE must resolve to IsGenericProfile == true");
                AssertEqual(3, streamedSlots.Count, "onSlotReady must be invoked once per template slot");
                AssertEqual(3, bundle.SlotPreviews.Count, "SlotPreviews must contain all 3 slots");
                foreach (var (slot, png) in streamedSlots)
                {
                    AssertValidPngBytes(png, $"Streamed slot {slot}");
                }

                AssertTrue(File.Exists(detailsLog), "Expected details request log from stub Python script");
                string loggedDetails = File.ReadAllText(detailsLog);
                AssertTrue(loggedDetails.Contains("\"skipEln\": true", StringComparison.OrdinalIgnoreCase),
                    $"Expected Generic profile details call to pass skipEln=true, actual: {loggedDetails}");

                // 3. Subsequent FreeText re-render produces valid PNG bytes while active sample is loaded.
                byte[] updatedMagnet = backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "M2").GetAwaiter().GetResult();
                AssertValidPngBytes(updatedMagnet, "FreeText Magnet re-render");

                // 4. ClearActiveSample clears state cleanly.
                backend.ClearActiveSample();
                byte[] afterClear = backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "M3").GetAwaiter().GetResult();
                AssertEqual(0, afterClear.Length, "RenderSlotWithFreeTextAsync after ClearActiveSample must return empty byte[]");

                // 5. Loading a sample then switching object type (LoadObjectsForTypeAsync) clears active sample.
                SamplePreviewBundleDto reloaded = backend.LoadSamplePreviewsAsync("SUBSTRATE", "PERM-1", "M1", "F1").GetAwaiter().GetResult();
                AssertTrue(reloaded.Success, "Second LoadSamplePreviewsAsync should succeed");
                SampleListResultDto switched = backend.LoadObjectsForTypeAsync("SUBSTANCE").GetAwaiter().GetResult();
                AssertTrue(switched.Success, "LoadObjectsForTypeAsync should succeed");
                byte[] afterSwitch = backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "M4").GetAwaiter().GetResult();
                AssertEqual(0, afterSwitch.Length, "LoadObjectsForTypeAsync must clear active sample to prevent stale FreeText renders");

                // 6. Loading & fallback PNG helpers + example previews + empty-selection print + printer updates.
                AssertValidPngBytes(backend.GetLoadingPreviewPng(LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule), "GetLoadingPreviewPng");
                AssertValidPngBytes(backend.GetFallbackPreviewPng(LabelTemplateType.Flask24mm, LabelTemplateProfile.Generic, "Missing"), "GetFallbackPreviewPng");
                Dictionary<LabelTemplateType, byte[]> examples = backend.GetExamplePreviewsAsync(LabelTemplateProfile.Molecule).GetAwaiter().GetResult();
                AssertEqual(3, examples.Count, "GetExamplePreviewsAsync must return 3 slots");
                foreach (var kvp in examples)
                {
                    AssertValidPngBytes(kvp.Value, $"Example {kvp.Key}");
                }

                // Reload active sample, then check empty print selections error and UpdatePrinterSelectionsAsync persistence.
                SamplePreviewBundleDto activeAgain = backend.LoadSamplePreviewsAsync("SUBSTRATE", "PERM-1", "M1", "F1").GetAwaiter().GetResult();
                AssertTrue(activeAgain.Success, "Reloading sample for print selection validation should succeed");

                LabelPrintBatchResult emptyPrint = backend.PrintSelectedLabelsAsync(Array.Empty<PrintSlotSelection>()).GetAwaiter().GetResult();
                AssertFalse(emptyPrint.AllSucceeded, "Empty print selections must fail");
                AssertEqual("No label templates selected for printing.", emptyPrint.Error, "Empty selections print error mismatch");

                OperationResult printerSave = backend.UpdatePrinterSelectionsAsync(
                    "Brother PT-P700 (Box)",
                    "Brother PT-P700 (Mag)",
                    "Brother PT-P700 (Flask)").GetAwaiter().GetResult();
                AssertTrue(printerSave.Success, $"UpdatePrinterSelectionsAsync should succeed, error: {printerSave.ErrorMessage}");
                PrinterConfig? updatedPrinters = configService.LoadPrinterConfig();
                AssertNotNull(updatedPrinters, "Updated printer config should exist");
                AssertEqual("Brother PT-P700 (Box)", updatedPrinters!.SmallBoxPrinter, "Updated SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (Mag)", updatedPrinters.MagnetPrinter, "Updated MagnetPrinter mismatch");
                AssertEqual("Brother PT-P700 (Flask)", updatedPrinters.FlaskPrinter, "Updated FlaskPrinter mismatch");

                // Direct LabelPreviewRenderer PNG helper coverage.
                var renderer = new LabelPreviewRenderer(configService, () => Array.Empty<string>());
                SampleDetailsDto exampleDetails = LabelPreviewRenderer.BuildExampleSampleDetails(LabelTemplateProfile.Molecule);
                byte[] directTemplatePng = renderer.RenderTemplatePreviewPngAsync(
                    LabelTemplateType.SmallBox12mm,
                    LabelTemplateProfile.Molecule,
                    exampleDetails).GetAwaiter().GetResult();
                AssertValidPngBytes(directTemplatePng, "Direct RenderTemplatePreviewPngAsync");
                byte[] directExamplePng = renderer.RenderExampleSlotPreviewPngAsync(
                    LabelTemplateType.Magnet12mm,
                    LabelTemplateProfile.Generic).GetAwaiter().GetResult();
                AssertValidPngBytes(directExamplePng, "Direct RenderExampleSlotPreviewPngAsync");
                using var testBmp = new Bitmap(4, 4);
                AssertValidPngBytes(LabelPreviewRenderer.EncodeImageAsPngBytes(testBmp), "EncodeImageAsPngBytes");
            });
        }

        private static void BackendValidatesAndSavesWizardAndSettingsIsolatingBaseDirectory()
        {
            WithTempDir(tempRoot =>
            {
                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    "if '--worker' in sys.argv[1:]:\n" +
                    "    while True:\n" +
                    "        line = sys.stdin.readline()\n" +
                    "        if line == '':\n" +
                    "            break\n" +
                    "        if not line.strip():\n" +
                    "            continue\n" +
                    "        req = json.loads(line)\n" +
                    "        action = req.get('action', '')\n" +
                    "        if req.get('token') != 'good-tok':\n" +
                    "            resp = {'success': False, 'errorType': 'token_expired', 'error': 'Token rejected'}\n" +
                    "        elif action == 'types':\n" +
                    "            resp = {'success': True, 'types': [{'code': 'SUBSTANCE', 'description': 'Substance'}, {'code': 'CRYSTAL', 'description': 'Lab Crystal'}, {'code': 'WAFER', 'description': 'Wafer'}]}\n" +
                    "        else:\n" +
                    "            resp = {'success': True, 'sessionActive': True}\n" +
                    "        sys.stdout.write(json.dumps(resp) + '\\n')\n" +
                    "        sys.stdout.flush()\n");

                using var backend = new LabelStudioBackend(
                    tempRoot,
                    installedPrintersProvider: () => new[] { "Brother PT-P700", "Microsoft Print to PDF" });

                WorkspaceStartupState initial = backend.InitializeWorkspaceAsync().GetAwaiter().GetResult();
                AssertEqual(SetupWizardStep.Connection, initial.MissingWizardStep, "Fresh tempRoot must require Connection step");
                AssertFalse(initial.HasValidConnection, "Fresh tempRoot must not have valid connection");

                // Offline save validations
                OperationResult badUrlSave = backend.SaveSettingsOfflineAsync(new SettingsSaveRequest
                {
                    OpenBisUrl = "ftp://invalid",
                    Token = "good-tok",
                    ObjectTypes = new[] { new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE" } }
                }).GetAwaiter().GetResult();
                AssertFalse(badUrlSave.Success, "Invalid URL must fail offline save");
                AssertEqual("Enter a valid openBIS Instance URL starting with http:// or https://.", badUrlSave.ErrorMessage, "URL validation message mismatch");

                OperationResult emptyTokenSave = backend.SaveSettingsOfflineAsync(new SettingsSaveRequest
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "  ",
                    ObjectTypes = new[] { new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE" } }
                }).GetAwaiter().GetResult();
                AssertFalse(emptyTokenSave.Success, "Empty token must fail offline save");
                AssertEqual("Enter an openBIS Personal Access Token.", emptyTokenSave.ErrorMessage, "Token validation message mismatch");

                OperationResult noTypesSave = backend.SaveSettingsOfflineAsync(new SettingsSaveRequest
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "good-tok",
                    ObjectTypes = new[] { new ObjectTypeRowDto { IsSelected = false, Code = "SUBSTANCE" } }
                }).GetAwaiter().GetResult();
                AssertFalse(noTypesSave.Success, "Zero selected object types must fail offline save");
                AssertEqual("Select at least one object type.", noTypesSave.ErrorMessage, "Object types validation message mismatch");

                // Wizard step validations
                OperationResult wizardBadUrl = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.Connection,
                    OpenBisUrl = "not-a-url",
                    Token = "good-tok"
                }).GetAwaiter().GetResult();
                AssertFalse(wizardBadUrl.Success, "Wizard Connection step must validate URL");
                AssertEqual("Enter a valid openBIS Instance URL starting with http:// or https://.", wizardBadUrl.ErrorMessage, "Wizard URL validation mismatch");

                OperationResult wizardNoTypes = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.ObjectTypes,
                    ObjectTypes = Array.Empty<ObjectTypeRowDto>()
                }).GetAwaiter().GetResult();
                AssertFalse(wizardNoTypes.Success, "Wizard ObjectTypes step must require at least one selected type");
                AssertEqual("Select at least one object type.", wizardNoTypes.ErrorMessage, "Wizard types validation mismatch");

                // TestConnectionAsync validation focus + auth focus + persistence isolation
                ConnectionCheckResult badUrlEmptyToken = backend.TestConnectionAsync("ftp://invalid", "", persistOnSuccess: false).GetAwaiter().GetResult();
                AssertFalse(badUrlEmptyToken.Success, "Invalid URL with empty token must return Success == false");
                AssertFalse(badUrlEmptyToken.ShouldFocusToken, "Invalid URL with empty token must not focus token input");
                AssertEqual("Enter a valid openBIS Instance URL starting with http:// or https://.", badUrlEmptyToken.Message, "Invalid URL message mismatch");

                ConnectionCheckResult malformedUrlBlankToken = backend.TestConnectionAsync("not-a-url", "   ", persistOnSuccess: false).GetAwaiter().GetResult();
                AssertFalse(malformedUrlBlankToken.Success, "Malformed URL with blank token must return Success == false");
                AssertFalse(malformedUrlBlankToken.ShouldFocusToken, "Malformed URL with blank token must not focus token input");

                ConnectionCheckResult validUrlEmptyToken = backend.TestConnectionAsync("https://openbis.example.org", "   ", persistOnSuccess: false).GetAwaiter().GetResult();
                AssertFalse(validUrlEmptyToken.Success, "Valid URL with blank token must return Success == false");
                AssertTrue(validUrlEmptyToken.ShouldFocusToken, "Valid URL with blank token must set ShouldFocusToken == true");
                AssertEqual("Enter an openBIS Personal Access Token.", validUrlEmptyToken.Message, "Empty token message mismatch");

                ConnectionCheckResult authFail = backend.TestConnectionAsync("https://openbis.example.org", "bad-tok", persistOnSuccess: true).GetAwaiter().GetResult();
                AssertFalse(authFail.Success, "Auth failure must return Success == false");
                AssertTrue(authFail.ShouldFocusToken, "Auth failure must set ShouldFocusToken == true");
                AssertFalse(File.Exists(Path.Combine(tempRoot, "config", "openbis_config.json")), "Failed connection test must not persist config");

                ConnectionCheckResult noPersistOk = backend.TestConnectionAsync("https://openbis.example.org", "good-tok", persistOnSuccess: false).GetAwaiter().GetResult();
                AssertTrue(noPersistOk.Success, $"Valid connection check should succeed, message: {noPersistOk.Message}");
                AssertFalse(noPersistOk.Persisted, "persistOnSuccess=false must not persist config");
                AssertFalse(File.Exists(Path.Combine(tempRoot, "config", "openbis_config.json")), "persistOnSuccess=false must not create openbis_config.json");

                ConnectionCheckResult persistOk = backend.TestConnectionAsync("https://openbis.example.org", "good-tok", persistOnSuccess: true).GetAwaiter().GetResult();
                AssertTrue(persistOk.Success && persistOk.Persisted, "persistOnSuccess=true must persist config");
                AssertTrue(File.Exists(Path.Combine(tempRoot, "config", "openbis_config.json")), "openbis_config.json must be written under tempRoot/config/");

                // Full offline save under tempRoot only
                OperationResult savedAll = backend.SaveSettingsOfflineAsync(new SettingsSaveRequest
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "good-tok",
                    ObjectTypes = new[]
                    {
                        new ObjectTypeRowDto
                        {
                            IsSelected = true,
                            Code = "CRYSTAL",
                            DisplayName = "Crystal",
                            TemplateProfile = LabelTemplateProfile.Molecule
                        }
                    },
                    SmallBoxPrinter = "Brother PT-P700",
                    MagnetPrinter = "Brother PT-P700",
                    FlaskPrinter = "Brother PT-P700"
                }).GetAwaiter().GetResult();
                AssertTrue(savedAll.Success, $"Offline save should succeed, error: {savedAll.ErrorMessage}");
                AssertTrue(File.Exists(Path.Combine(tempRoot, "config", "openbis_types.json")), "openbis_types.json must exist under tempRoot/config/");
                AssertTrue(File.Exists(Path.Combine(tempRoot, "config", "printers_config.json")), "printers_config.json must exist under tempRoot/config/");

                WorkspaceStartupState ready = backend.InitializeWorkspaceAsync().GetAwaiter().GetResult();
                AssertEqual(SetupWizardStep.None, ready.MissingWizardStep, "Completed config under tempRoot must return SetupWizardStep.None");
                AssertEqual(1, ready.ConfiguredTypes.Count, "Expected 1 configured type");
                AssertEqual("CRYSTAL", ready.ConfiguredTypes[0].Code, "Configured type code mismatch");
                AssertTrue(ready.ConfiguredTypes[0].IsMoleculeProfile, "Expected Molecule profile on CRYSTAL");
                AssertEqual(1, ready.Printers.AvailablePrinters.Count, "Non-Brother printers must be filtered out of PrinterDropdownState");
                AssertTrue(ready.Printers.HasInstalledBrotherPrinters, "Installed Brother printer should set HasInstalledBrotherPrinters");

                // Verify GetSettingsSnapshotAsync & FetchAndMergeServerTypesAsync
                SettingsSnapshotDto snapshot = backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
                AssertEqual("https://openbis.example.org", snapshot.OpenBisUrl, "Snapshot URL mismatch");
                AssertEqual("good-tok", snapshot.Token, "Snapshot Token mismatch");
                AssertTrue(snapshot.HasValidOpenBisConfig && snapshot.HasValidConfiguredTypes && snapshot.HasPrinterConfig,
                    "Snapshot readiness flags must all be true");

                ServerTypeSyncResult expiredSync = backend.FetchAndMergeServerTypesAsync(
                    "https://openbis.example.org",
                    "bad-tok",
                    snapshot.ConfiguredTypes).GetAwaiter().GetResult();
                AssertFalse(expiredSync.Success, "Expired token sync must fail");
                AssertTrue(expiredSync.IsTokenExpired, "Expired token sync must set IsTokenExpired == true");

                ServerTypeSyncResult mergedSync = backend.FetchAndMergeServerTypesAsync(
                    "https://openbis.example.org",
                    "good-tok",
                    snapshot.ConfiguredTypes).GetAwaiter().GetResult();
                AssertTrue(mergedSync.Success, $"FetchAndMergeServerTypesAsync should succeed, error: {mergedSync.StatusMessage}");
                AssertEqual(3, mergedSync.MergedRows.Count, "Expected 3 merged server types");
                AssertFalse(mergedSync.MergedRows[0].IsSelected, "Unconfigured SUBSTANCE should stay unchecked");
                AssertEqual(LabelTemplateProfile.Molecule, mergedSync.MergedRows[0].TemplateProfile, "SUBSTANCE should default to Molecule");
                AssertTrue(mergedSync.MergedRows[1].IsSelected, "Existing CRYSTAL should preserve checked state");
                AssertEqual(LabelTemplateProfile.Molecule, mergedSync.MergedRows[1].TemplateProfile, "CRYSTAL should preserve Molecule profile");
                AssertEqual("Lab Crystal", mergedSync.MergedRows[1].DisplayName, "CRYSTAL DisplayName should update from server description");
                AssertFalse(mergedSync.MergedRows[2].IsSelected, "New WAFER should start unchecked");

                // Verify GetSettingsSnapshotAsync sorts ConfiguredTypes by DisplayName then Code (CurrentCultureIgnoreCase)
                OperationResult multiSave = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.ObjectTypes,
                    ObjectTypes = new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "Z_TYPE", DisplayName = "Zeta", TemplateProfile = LabelTemplateProfile.Generic },
                        new ObjectTypeRowDto { IsSelected = true, Code = "B_TIE", DisplayName = "Alpha", TemplateProfile = LabelTemplateProfile.Generic },
                        new ObjectTypeRowDto { IsSelected = true, Code = "CUSTOM_NODE", DisplayName = "", TemplateProfile = LabelTemplateProfile.Generic },
                        new ObjectTypeRowDto { IsSelected = true, Code = "A_TIE", DisplayName = "alpha", TemplateProfile = LabelTemplateProfile.Molecule }
                    }
                }).GetAwaiter().GetResult();
                AssertTrue(multiSave.Success, $"Multi-type wizard save should succeed, error: {multiSave.ErrorMessage}");

                SettingsSnapshotDto sortedSnapshot = backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
                AssertEqual(4, sortedSnapshot.ConfiguredTypes.Count, "Expected 4 configured types in sorted snapshot");
                AssertEqual("A_TIE", sortedSnapshot.ConfiguredTypes[0].Code, "Snapshot type[0] should break DisplayName tie by Code ascending");
                AssertEqual("B_TIE", sortedSnapshot.ConfiguredTypes[1].Code, "Snapshot type[1] should be B_TIE after A_TIE tie-breaker");
                AssertEqual("CUSTOM_NODE", sortedSnapshot.ConfiguredTypes[2].Code, "Snapshot type[2] should sort formatted DisplayName 'Custom Node' after 'Alpha'");
                AssertEqual("Custom Node", sortedSnapshot.ConfiguredTypes[2].DisplayName, "Snapshot blank DisplayName should fall back to FormatFriendlyName");
                AssertEqual("Z_TYPE", sortedSnapshot.ConfiguredTypes[3].Code, "Snapshot type[3] should be Zeta last");
            });
        }

        private static void BackendHelperMethodsBuildTypesResolveProfileAndDetectCredentialChanges()
        {
            var serverTypes = new[]
            {
                new OpenBisTypeInfoDto { Code = "SUBSTANCE", Description = "Substance" },
                new OpenBisTypeInfoDto { Code = "CRYSTAL", Description = "Crystal" },
                new OpenBisTypeInfoDto { Code = "CUSTOM_PLATE", Description = "" }
            };

            List<ConfiguredOpenBisType> defaults = LabelStudioBackend.BuildInitialTypeRows(
                serverTypes,
                Array.Empty<ConfiguredOpenBisType>());
            AssertEqual(3, defaults.Count, "Expected 3 default rows");
            AssertEqual(LabelTemplateProfile.Molecule, defaults[0].TemplateProfile, "SUBSTANCE must default to Molecule");
            AssertEqual(LabelTemplateProfile.Generic, defaults[1].TemplateProfile, "CRYSTAL must default to Generic");
            AssertEqual("Custom Plate", defaults[2].DisplayName, "Empty description must fall back to formatted friendly name");

            var saved = new[]
            {
                new ConfiguredOpenBisType
                {
                    Code = "CRYSTAL",
                    DisplayName = "Crystal",
                    TemplateProfile = LabelTemplateProfile.Molecule
                }
            };
            List<ConfiguredOpenBisType> merged = LabelStudioBackend.BuildInitialTypeRows(serverTypes, saved);
            AssertEqual(LabelTemplateProfile.Molecule, merged[1].TemplateProfile, "Saved profile must override default");

            AssertEqual(LabelTemplateProfile.Molecule,
                LabelStudioBackend.ResolveTemplateProfile(" crystal ", saved, null),
                "ResolveTemplateProfile must match configured type case-insensitively");
            AssertEqual(LabelTemplateProfile.Molecule,
                LabelStudioBackend.ResolveTemplateProfile("UNKNOWN", saved, "CRYSTAL"),
                "ResolveTemplateProfile must fall back to sampleDetailsType");
            AssertEqual(LabelTemplateProfile.Generic,
                LabelStudioBackend.ResolveTemplateProfile("UNKNOWN", saved, "ALSO_UNKNOWN"),
                "ResolveTemplateProfile must default unconfigured types to Generic");

            AssertFalse(LabelStudioBackend.CredentialsChangedSinceRequest("https://a", "t1", null),
                "Null config must return false");
            AssertFalse(LabelStudioBackend.CredentialsChangedSinceRequest("https://a", "t1", new AppConfig { OpenBisUrl = "https://b", Token = "" }),
                "Empty token must return false");
            AssertFalse(LabelStudioBackend.CredentialsChangedSinceRequest("https://a", "t1", new AppConfig { OpenBisUrl = "https://a", Token = "t1" }),
                "Identical credentials must return false");
            AssertTrue(LabelStudioBackend.CredentialsChangedSinceRequest("https://a", "t1", new AppConfig { OpenBisUrl = "https://a", Token = "t2" }),
                "Changed token must return true");
            AssertTrue(LabelStudioBackend.CredentialsChangedSinceRequest("https://a", "t1", new AppConfig { OpenBisUrl = "https://b", Token = "t1" }),
                "Changed URL must return true");
        }

        private sealed class StubLabelStudioBackend : ILabelStudioBackend
        {
            private readonly byte[] _validPngBytes;
            private readonly object _callsLock = new object();

            public StubLabelStudioBackend(byte[] validPngBytes)
            {
                _validPngBytes = validPngBytes;
            }

            public int WarmUpCalls { get; private set; }
            public int InitializeWorkspaceCalls { get; private set; }
            public int RefreshWorkspaceTypesCalls { get; private set; }
            public int GetSettingsSnapshotCalls { get; private set; }
            public int ClearActiveSampleCalls { get; private set; }
            public int SortSamplesCalls { get; private set; }
            public bool Disposed { get; private set; }
            public List<string> LoadObjectsCalls { get; } = new List<string>();
            public List<(string TypeCode, string PermId, string MagnetText, string FlaskText)> LoadSamplePreviewsCalls { get; }
                = new List<(string, string, string, string)>();
            public List<(LabelTemplateType Slot, string FreeText)> RenderSlotWithFreeTextCalls { get; }
                = new List<(LabelTemplateType, string)>();
            public List<LabelTemplateProfile> GetExamplePreviewsCalls { get; } = new List<LabelTemplateProfile>();
            public List<(LabelTemplateType Slot, LabelTemplateProfile Profile, string Reason)> GetFallbackPreviewCalls { get; }
                = new List<(LabelTemplateType, LabelTemplateProfile, string)>();
            public IReadOnlyList<ObjectTypeRowDto>? SnapshotConfiguredTypes { get; set; }
            public SettingsSaveRequest? LastSavedOfflineRequest { get; private set; }
            public Func<ITokenRecoveryPrompter?, CancellationToken, Task<BackgroundTypeRefreshResult>>? OnRefreshWorkspaceTypesAsync { get; set; }
            public Func<string, string, IReadOnlyList<ObjectTypeRowDto>, CancellationToken, Task<ServerTypeSyncResult>>? OnFetchAndMergeServerTypesAsync { get; set; }
            public Func<LabelTemplateProfile, Action<LabelTemplateType, byte[]>?, CancellationToken, Task<Dictionary<LabelTemplateType, byte[]>>>? OnGetExamplePreviewsAsync { get; set; }
            public Func<string, ITokenRecoveryPrompter?, CancellationToken, Task<SampleListResultDto>>? OnLoadObjectsForTypeAsync { get; set; }
            public Func<string, string, string, string, Action<LabelTemplateType, byte[]>?, ITokenRecoveryPrompter?, CancellationToken, Task<SamplePreviewBundleDto>>? OnLoadSamplePreviewsAsync { get; set; }

            public void WarmUpInBackground()
            {
                WarmUpCalls++;
            }

            public Task<WorkspaceStartupState> InitializeWorkspaceAsync(
                ITokenRecoveryPrompter? recoveryPrompter = null,
                CancellationToken ct = default)
            {
                InitializeWorkspaceCalls++;
                return Task.FromResult(new WorkspaceStartupState
                {
                    MissingWizardStep = SetupWizardStep.None,
                    HasValidConnection = true,
                    CredentialFingerprint = "stub-fp-1",
                    ConfiguredTypes = new[]
                    {
                        new TypeOptionDto { Code = "SUBSTANCE", DisplayText = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new TypeOptionDto { Code = "SUBSTRATE", DisplayText = "Silicon Substrate", TemplateProfile = LabelTemplateProfile.Generic }
                    },
                    Printers = new PrinterDropdownState
                    {
                        AvailablePrinters = new[] { "Brother PT-P700", "Brother PT-P700 (Kopie 1)" },
                        HasInstalledBrotherPrinters = true,
                        SmallBoxPrinter = "Brother PT-P700",
                        MagnetPrinter = "Brother PT-P700 (Kopie 1)",
                        FlaskPrinter = "Brother PT-P700",
                        SmallBoxSelectedIndex = 0,
                        MagnetSelectedIndex = 1,
                        FlaskSelectedIndex = 0
                    }
                });
            }

            public Task<BackgroundTypeRefreshResult> RefreshWorkspaceTypesAsync(
                ITokenRecoveryPrompter? recoveryPrompter = null,
                CancellationToken ct = default)
            {
                RefreshWorkspaceTypesCalls++;
                if (OnRefreshWorkspaceTypesAsync != null)
                {
                    return OnRefreshWorkspaceTypesAsync(recoveryPrompter, ct);
                }

                return Task.FromResult(new BackgroundTypeRefreshResult
                {
                    Success = true,
                    TypesUpdated = false,
                    ConfiguredTypes = new[]
                    {
                        new TypeOptionDto { Code = "SUBSTANCE", DisplayText = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new TypeOptionDto { Code = "SUBSTRATE", DisplayText = "Silicon Substrate", TemplateProfile = LabelTemplateProfile.Generic }
                    }
                });
            }

            public Task<ConnectionCheckResult> TestConnectionAsync(
                string url,
                string token,
                bool persistOnSuccess,
                CancellationToken ct = default)
            {
                return Task.FromResult(new ConnectionCheckResult
                {
                    Success = true,
                    Persisted = persistOnSuccess,
                    NormalizedUrl = url,
                    Message = "Connected"
                });
            }

            public Task<SettingsSnapshotDto> GetSettingsSnapshotAsync(CancellationToken ct = default)
            {
                GetSettingsSnapshotCalls++;
                return Task.FromResult(new SettingsSnapshotDto
                {
                    OpenBisUrl = "https://mock.openbis.example.org",
                    Token = "mock-token-123",
                    ConfiguredTypes = SnapshotConfiguredTypes ?? new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTRATE", DisplayName = "Silicon Substrate", TemplateProfile = LabelTemplateProfile.Generic }
                    },
                    Printers = new PrinterDropdownState
                    {
                        AvailablePrinters = new[] { "Brother PT-P700", "Brother PT-P700 (Kopie 1)" },
                        HasInstalledBrotherPrinters = true,
                        SmallBoxPrinter = "Brother PT-P700",
                        MagnetPrinter = "Brother PT-P700 (Kopie 1)",
                        FlaskPrinter = "Brother PT-P700",
                        SmallBoxSelectedIndex = 0,
                        MagnetSelectedIndex = 1,
                        FlaskSelectedIndex = 0
                    },
                    HasValidOpenBisConfig = true,
                    HasValidConfiguredTypes = true,
                    HasPrinterConfig = true,
                    FirstMissingWizardStep = SetupWizardStep.None
                });
            }

            public Task<ServerTypeSyncResult> FetchAndMergeServerTypesAsync(
                string url,
                string token,
                IReadOnlyList<ObjectTypeRowDto> currentRows,
                CancellationToken ct = default)
            {
                if (OnFetchAndMergeServerTypesAsync != null)
                {
                    return OnFetchAndMergeServerTypesAsync(url, token, currentRows, ct);
                }

                return Task.FromResult(new ServerTypeSyncResult
                {
                    Success = true,
                    ServerTypeCount = currentRows.Count,
                    StatusMessage = $"Loaded {currentRows.Count} object type(s) from openBIS.",
                    MergedRows = currentRows
                });
            }

            public async Task<Dictionary<LabelTemplateType, byte[]>> GetExamplePreviewsAsync(
                LabelTemplateProfile profile,
                Action<LabelTemplateType, byte[]>? onSlotReady = null,
                CancellationToken ct = default)
            {
                lock (_callsLock)
                {
                    GetExamplePreviewsCalls.Add(profile);
                }

                if (OnGetExamplePreviewsAsync != null)
                {
                    return await OnGetExamplePreviewsAsync(profile, onSlotReady, ct).ConfigureAwait(false);
                }

                var result = new Dictionary<LabelTemplateType, byte[]>
                {
                    [LabelTemplateType.SmallBox12mm] = _validPngBytes,
                    [LabelTemplateType.Magnet12mm] = new byte[] { 1, 2, 3 },
                    [LabelTemplateType.Flask24mm] = Array.Empty<byte>()
                };

                await Task.Run(() =>
                {
                    foreach (var kvp in result)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            onSlotReady?.Invoke(kvp.Key, kvp.Value);
                        }
                    }
                }, ct).ConfigureAwait(false);

                return result;
            }

            public Task<OperationResult> SaveWizardStepAsync(WizardStepSaveRequest request, CancellationToken ct = default)
                => Task.FromResult(OperationResult.Ok());

            public Task<OperationResult> SaveSettingsOfflineAsync(SettingsSaveRequest request, CancellationToken ct = default)
            {
                LastSavedOfflineRequest = request;
                return Task.FromResult(OperationResult.Ok());
            }

            public Task<SampleListResultDto> LoadObjectsForTypeAsync(
                string typeCode,
                ITokenRecoveryPrompter? recoveryPrompter = null,
                CancellationToken ct = default)
            {
                lock (_callsLock)
                {
                    LoadObjectsCalls.Add(typeCode);
                }

                if (OnLoadObjectsForTypeAsync != null)
                {
                    return OnLoadObjectsForTypeAsync(typeCode, recoveryPrompter, ct);
                }

                return Task.FromResult(new SampleListResultDto
                {
                    Success = true,
                    Samples = new[]
                    {
                        new SampleSummaryDto
                        {
                            Name = "MockSubstrate-1",
                            PermId = "PERM-MOCK-1",
                            ModificationDate = "2026-11-20"
                        }
                    }
                });
            }

            public void ClearActiveSample()
            {
                ClearActiveSampleCalls++;
            }

            public async Task<SamplePreviewBundleDto> LoadSamplePreviewsAsync(
                string typeCode,
                string permId,
                string magnetFreeText,
                string flaskFreeText,
                Action<LabelTemplateType, byte[]>? onSlotReady = null,
                ITokenRecoveryPrompter? recoveryPrompter = null,
                CancellationToken ct = default)
            {
                lock (_callsLock)
                {
                    LoadSamplePreviewsCalls.Add((typeCode, permId, magnetFreeText, flaskFreeText));
                }

                if (OnLoadSamplePreviewsAsync != null)
                {
                    return await OnLoadSamplePreviewsAsync(
                        typeCode,
                        permId,
                        magnetFreeText,
                        flaskFreeText,
                        onSlotReady,
                        recoveryPrompter,
                        ct).ConfigureAwait(false);
                }

                var slots = new Dictionary<LabelTemplateType, byte[]>
                {
                    [LabelTemplateType.SmallBox12mm] = _validPngBytes,
                    [LabelTemplateType.Magnet12mm] = new byte[] { 1, 2, 3 },
                    [LabelTemplateType.Flask24mm] = Array.Empty<byte>()
                };

                await Task.Run(() =>
                {
                    foreach (var kvp in slots)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            onSlotReady?.Invoke(kvp.Key, kvp.Value);
                        }
                    }
                }, ct).ConfigureAwait(false);

                return new SamplePreviewBundleDto
                {
                    Success = true,
                    Profile = LabelTemplateProfile.Generic,
                    SampleName = "MockSubstrate-1",
                    PermId = permId,
                    SlotPreviews = slots
                };
            }

            public Task<byte[]> RenderSlotWithFreeTextAsync(
                LabelTemplateType slot,
                string freeText,
                CancellationToken ct = default)
            {
                lock (_callsLock)
                {
                    RenderSlotWithFreeTextCalls.Add((slot, freeText));
                }

                return Task.FromResult(GetSlotPayload(slot));
            }

            public Task<OperationResult> UpdatePrinterSelectionsAsync(
                string smallBoxPrinter,
                string magnetPrinter,
                string flaskPrinter)
                => Task.FromResult(OperationResult.Ok());

            public Task<LabelPrintBatchResult> PrintSelectedLabelsAsync(
                IReadOnlyList<PrintSlotSelection> selections,
                CancellationToken ct = default)
                => Task.FromResult(new LabelPrintBatchResult { AllSucceeded = true });

            public byte[] GetLoadingPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile)
                => _validPngBytes;

            public byte[] GetFallbackPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile, string reason)
            {
                lock (_callsLock)
                {
                    GetFallbackPreviewCalls.Add((slot, profile, reason));
                }

                return slot == LabelTemplateType.SmallBox12mm ? _validPngBytes : new byte[] { 9, 8, 7 };
            }

            public IReadOnlyList<SampleSummaryDto> SortSamples(
                IReadOnlyList<SampleSummaryDto>? samples,
                SampleSortField sortBy,
                SampleSortDirection direction)
            {
                SortSamplesCalls++;
                return SampleSorter.Sort(samples, sortBy, direction);
            }

            public void Dispose()
            {
                Disposed = true;
            }

            private byte[] GetSlotPayload(LabelTemplateType slot) => slot switch
            {
                LabelTemplateType.SmallBox12mm => _validPngBytes,
                LabelTemplateType.Magnet12mm => new byte[] { 1, 2, 3 },
                LabelTemplateType.Flask24mm => Array.Empty<byte>(),
                _ => Array.Empty<byte>()
            };
        }

        private static void PumpEventsUntil(Func<bool> predicate, string timeoutMessage, int timeoutMs = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!predicate())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    throw new TimeoutException(timeoutMessage);
                }

                Application.DoEvents();
                Thread.Sleep(10);
            }

            Application.DoEvents();
        }

        private static void Form1AndSetupSettingsFormRenderWithMockBackendAndSurviveCorruptPngBytes()
        {
            byte[] validPng;
            using (var bmp = new Bitmap(1, 1))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                validPng = ms.ToArray();
            }

            // Direct DecodePngBitmap safety verification against valid, corrupt, and empty PNG inputs.
            using (Bitmap validDecoded = Form1.DecodePngBitmap(validPng))
            {
                AssertEqual(1, validDecoded.Width, "Decoded valid 1x1 PNG width mismatch");
                AssertEqual(1, validDecoded.Height, "Decoded valid 1x1 PNG height mismatch");
            }

            using (Bitmap corruptDecoded = Form1.DecodePngBitmap(new byte[] { 1, 2, 3 }))
            {
                AssertTrue(corruptDecoded.Width > 0 && corruptDecoded.Height > 0, "Corrupt PNG fallback bitmap must have positive dimensions");
            }

            using (Bitmap emptyDecoded = Form1.DecodePngBitmap(Array.Empty<byte>(), () => new byte[] { 4, 5 }))
            {
                AssertTrue(emptyDecoded.Width > 0 && emptyDecoded.Height > 0, "Empty PNG with corrupt fallback must still yield valid blank Bitmap");
            }

            using (Bitmap nullDecoded = Form1.DecodePngBitmap(null))
            {
                AssertTrue(nullDecoded.Width > 0 && nullDecoded.Height > 0, "Null PNG bytes must yield valid blank Bitmap");
            }

            var stub = new StubLabelStudioBackend(validPng);

            using (var unshownOwner = new Form())
            {
                var prompter = new FormTokenRecoveryPrompter(unshownOwner, stub);
                AssertFalse(prompter.PromptForNewTokenAsync("https://x", "warn").GetAwaiter().GetResult(),
                    "Prompter must return false safely when owner handle is not created");

                using var preCanceled = new CancellationTokenSource();
                preCanceled.Cancel();
                AssertFalse(prompter.PromptForNewTokenAsync("https://x", "warn", preCanceled.Token).GetAwaiter().GetResult(),
                    "Prompter must return false when CancellationToken is already canceled");
            }

            using (var form = new Form1(stub))
            {
                ShowOffscreen(form);
                AssertTrue(stub.InitializeWorkspaceCalls >= 1, "Form1 must call InitializeWorkspaceAsync on load");

                var cmbTypes = FindControl<ComboBox>(form, "openbisTypeComboBox");
                var cmbObjects = FindControl<ComboBox>(form, "openbisObjectsComboBox");
                var cmbBoxPrinter = FindControl<ComboBox>(form, "smallBoxPrinterComboBox");
                var btnLoad = FindControl<Button>(form, "loadPrintTemplates");
                var txtMagnetFreeText = FindControl<TextBox>(form, "magnetFreeTextBox");
                var picSmallBox = FindControl<PictureBox>(form, "smallBoxLabelPreview");
                var picMagnet = FindControl<PictureBox>(form, "magnetLabelPreview");
                var picFlask = FindControl<PictureBox>(form, "flaskLabelPreview");
                var chkSmallBox = FindControl<CheckBox>(form, "smallBoxLabelCheckBox");
                var chkMagnet = FindControl<CheckBox>(form, "magnetLabelCheckBox");
                var chkFlask = FindControl<CheckBox>(form, "flaskLabelCheckBox");

                AssertEqual(3, cmbTypes.Items.Count, "Expected placeholder plus 2 stub types in openbisTypeComboBox");
                AssertEqual("Brother PT-P700", cmbBoxPrinter.SelectedItem?.ToString(), "Printer combo must populate from stub PrinterDropdownState");

                // Select Silicon Substrate (SUBSTRATE, Generic profile)
                int substrateIdx = -1;
                for (int i = 0; i < cmbTypes.Items.Count; i++)
                {
                    if (cmbTypes.Items[i] is TypeOptionDto opt && opt.Code == "SUBSTRATE")
                    {
                        substrateIdx = i;
                        break;
                    }
                }
                AssertTrue(substrateIdx > 0, "SUBSTRATE TypeOptionDto must be present in openbisTypeComboBox");
                cmbTypes.SelectedIndex = substrateIdx;

                PumpEventsUntil(() => cmbObjects.Items.Count == 1, "Timed out waiting for stub sample dropdown population");
                AssertTrue(stub.LoadObjectsCalls.Contains("SUBSTRATE"), "Selecting SUBSTRATE must invoke LoadObjectsForTypeAsync");
                AssertTrue(cmbObjects.Items[0] is SampleSummaryDto, "openbisObjectsComboBox items must be SampleSummaryDto");
                AssertTrue(txtMagnetFreeText.Enabled, "Generic type must enable magnetFreeTextBox");

                btnLoad.PerformClick();
                PumpEventsUntil(
                    () => chkSmallBox.Checked && chkMagnet.Checked && chkFlask.Checked
                          && picSmallBox.Image != null && picMagnet.Image != null && picFlask.Image != null,
                    "Timed out waiting for LoadSamplePreviewsAsync slots (including corrupt/empty payloads)");

                AssertEqual(1, stub.LoadSamplePreviewsCalls.Count, "Load Templates click must invoke LoadSamplePreviewsAsync once");
                AssertEqual("SUBSTRATE", stub.LoadSamplePreviewsCalls[0].TypeCode, "LoadSamplePreviewsAsync TypeCode mismatch");
                AssertEqual("PERM-MOCK-1", stub.LoadSamplePreviewsCalls[0].PermId, "LoadSamplePreviewsAsync PermId mismatch");
                AssertNotNull(picSmallBox.Image, "SmallBox preview image must be populated");
                AssertNotNull(picMagnet.Image, "Magnet preview image must survive corrupt PNG bytes cleanly");
                AssertNotNull(picFlask.Image, "Flask preview image must survive empty PNG bytes cleanly");

                txtMagnetFreeText.Text = "Custom Magnet";
                MethodInfo? leaveHandler = typeof(Form1).GetMethod(
                    "magnetFreeTextBox_Leave",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                AssertNotNull(leaveHandler, "Expected magnetFreeTextBox_Leave handler on Form1");
                leaveHandler!.Invoke(form, new object[] { txtMagnetFreeText, EventArgs.Empty });
                PumpEventsUntil(
                    () => stub.RenderSlotWithFreeTextCalls.Count == 1,
                    "Timed out waiting for RenderSlotWithFreeTextAsync");
                AssertEqual(LabelTemplateType.Magnet12mm, stub.RenderSlotWithFreeTextCalls[0].Slot, "FreeText slot mismatch");
                AssertEqual("Custom Magnet", stub.RenderSlotWithFreeTextCalls[0].FreeText, "FreeText payload mismatch");
                AssertNotNull(picMagnet.Image, "Magnet preview image must remain valid when FreeText render returns corrupt bytes");
            }

            AssertFalse(stub.Disposed, "Form1(ILabelStudioBackend) must not dispose caller-owned backend");

            using (var settingsForm = new SetupSettingsForm(stub, SetupDialogMode.Settings))
            {
                ShowOffscreen(settingsForm);
                AssertTrue(stub.GetSettingsSnapshotCalls >= 1, "SetupSettingsForm must call GetSettingsSnapshotAsync");

                AssertEqual("https://mock.openbis.example.org", FindControl<TextBox>(settingsForm, "txtInstanceUrl").Text,
                    "SetupSettingsForm URL must populate from stub snapshot");
                AssertEqual("mock-token-123", FindControl<TextBox>(settingsForm, "txtToken").Text,
                    "SetupSettingsForm token must populate from stub snapshot");
                AssertEqual(2, FindControl<DataGridView>(settingsForm, "dgvObjectTypes").Rows.Count,
                    "SetupSettingsForm object type grid must populate from stub snapshot");
                AssertEqual("Brother PT-P700", FindControl<ComboBox>(settingsForm, "cmbSmallBoxPrinter").SelectedItem?.ToString(),
                    "SetupSettingsForm printer dropdown must populate from stub snapshot");

                FindControl<Button>(settingsForm, "btnTabObjectTypes").PerformClick();
                var picExBox = FindControl<PictureBox>(settingsForm, "picPreviewSmallBox");
                var picExMagnet = FindControl<PictureBox>(settingsForm, "picPreviewMagnet");
                var picExFlask = FindControl<PictureBox>(settingsForm, "picPreviewFlask");
                PumpEventsUntil(
                    () => stub.GetExamplePreviewsCalls.Count >= 1
                          && picExBox.Image != null && picExMagnet.Image != null && picExFlask.Image != null,
                    "Timed out waiting for SetupSettingsForm example previews");
            }

            AssertFalse(stub.Disposed, "SetupSettingsForm must not dispose caller-owned backend");
        }

        private static void SetupSettingsFormPreservesInFlightGridEditsDuringBackgroundTypeSync()
        {
            byte[] validPng;
            using (var bmp = new Bitmap(1, 1))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                validPng = ms.ToArray();
            }

            var fetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFetch = new TaskCompletionSource<ServerTypeSyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<ObjectTypeRowDto>? capturedPreFetchRows = null;

            var stub = new StubLabelStudioBackend(validPng)
            {
                SnapshotConfiguredTypes = new[]
                {
                    new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                    new ObjectTypeRowDto { IsSelected = false, Code = "CRYSTAL", DisplayName = "Crystal", TemplateProfile = LabelTemplateProfile.Generic }
                },
                OnFetchAndMergeServerTypesAsync = async (url, token, currentRows, ct) =>
                {
                    capturedPreFetchRows = currentRows;
                    fetchStarted.TrySetResult(true);
                    return await releaseFetch.Task.ConfigureAwait(false);
                }
            };

            using var settingsForm = new SetupSettingsForm(stub, SetupDialogMode.Settings, SetupWizardStep.ObjectTypes);
            ShowOffscreen(settingsForm);

            PumpEventsUntil(() => fetchStarted.Task.IsCompleted, "Timed out waiting for background type fetch to start");
            var grid = FindControl<DataGridView>(settingsForm, "dgvObjectTypes");
            var prgTypesLoading = FindControl<ProgressBar>(settingsForm, "prgTypesLoading");
            AssertTrue(prgTypesLoading.Visible, "Expected prgTypesLoading to be visible while fetch is in flight");
            AssertNotNull(capturedPreFetchRows, "Pre-fetch rows must be passed to FetchAndMergeServerTypesAsync");
            AssertEqual(2, grid.Rows.Count, "Expected initial 2 rows in Object Types grid");

            // User edits both rows in the unlocked grid while FetchAndMergeServerTypesAsync is still paused.
            grid.Rows[0].Cells[0].Value = false;
            grid.Rows[0].Cells[2].Value = "Generic / Free-text";
            grid.Rows[1].Cells[0].Value = true;
            grid.Rows[1].Cells[2].Value = "Molecule / Substance";

            // Background fetch completes with stale pre-edit flags, an updated DisplayName, and a new server type.
            releaseFetch.SetResult(new ServerTypeSyncResult
            {
                Success = true,
                ServerTypeCount = 3,
                StatusMessage = "Loaded 3 object type(s) from openBIS.",
                MergedRows = new[]
                {
                    new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                    new ObjectTypeRowDto { IsSelected = false, Code = "CRYSTAL", DisplayName = "Lab Crystal", TemplateProfile = LabelTemplateProfile.Generic },
                    new ObjectTypeRowDto { IsSelected = false, Code = "WAFER", DisplayName = "Wafer", TemplateProfile = LabelTemplateProfile.Generic }
                }
            });

            PumpEventsUntil(
                () => settingsForm.TypesSyncedFromServer && !prgTypesLoading.Visible,
                "Timed out waiting for background type fetch to apply merged rows");

            AssertEqual(3, grid.Rows.Count, "Expected 3 rows after merging newly synced WAFER type");
            AssertFalse(Convert.ToBoolean(grid.Rows[0].Cells[0].Value), "In-flight uncheck of SUBSTANCE must be preserved");
            AssertEqual("Generic / Free-text", grid.Rows[0].Cells[2].Value?.ToString(), "In-flight profile change on SUBSTANCE must be preserved");

            AssertTrue(Convert.ToBoolean(grid.Rows[1].Cells[0].Value), "In-flight check of CRYSTAL must be preserved");
            AssertEqual("Molecule / Substance", grid.Rows[1].Cells[2].Value?.ToString(), "In-flight profile change on CRYSTAL must be preserved");
            AssertTrue((grid.Rows[1].Cells[1].Value?.ToString() ?? "").Contains("Lab Crystal", StringComparison.Ordinal),
                $"Server DisplayName update must still apply to CRYSTAL, actual: {grid.Rows[1].Cells[1].Value}");

            AssertFalse(Convert.ToBoolean(grid.Rows[2].Cells[0].Value), "Newly discovered WAFER must start unchecked");
            AssertEqual("Generic / Free-text", grid.Rows[2].Cells[2].Value?.ToString(), "Newly discovered WAFER should keep Generic profile");

            AssertTrue(settingsForm.TrySaveSettingsOffline(out string saveError), $"Expected TrySaveSettingsOffline to succeed: {saveError}");
            AssertNotNull(stub.LastSavedOfflineRequest, "Expected SaveSettingsOfflineAsync to capture saved rows");
            ObjectTypeRowDto? savedCrystal = null;
            ObjectTypeRowDto? savedSubstance = null;
            foreach (ObjectTypeRowDto r in stub.LastSavedOfflineRequest!.ObjectTypes)
            {
                if (r.Code == "CRYSTAL") savedCrystal = r;
                if (r.Code == "SUBSTANCE") savedSubstance = r;
            }
            AssertNotNull(savedCrystal, "Expected CRYSTAL row in saved payload");
            AssertTrue(savedCrystal!.IsSelected, "Saved CRYSTAL IsSelected must reflect preserved in-flight edit");
            AssertEqual(LabelTemplateProfile.Molecule, savedCrystal.TemplateProfile, "Saved CRYSTAL profile must reflect preserved in-flight edit");
            AssertNotNull(savedSubstance, "Expected SUBSTANCE row in saved payload");
            AssertFalse(savedSubstance!.IsSelected, "Saved SUBSTANCE IsSelected must reflect preserved in-flight edit");
            AssertEqual(LabelTemplateProfile.Generic, savedSubstance.TemplateProfile, "Saved SUBSTANCE profile must reflect preserved in-flight edit");
        }

        private static byte[] CreateValid1x1PngBytes()
        {
            using var bmp = new Bitmap(1, 1);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private static void Form1CancellationStopsStaleOpenBisCallsAndPreventsRecoveryOverSettings()
        {
            byte[] validPng = CreateValid1x1PngBytes();
            Exception? uiThreadException = null;
            void ThreadExceptionHandler(object? sender, ThreadExceptionEventArgs e)
            {
                uiThreadException ??= e.Exception;
            }

            Application.ThreadException += ThreadExceptionHandler;
            try
            {
                var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var refreshExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken refreshCt = CancellationToken.None;
                int recoveryPromptAttempts = 0;

                var loadObjQueue = new Queue<(string TypeCode, CancellationToken Ct, TaskCompletionSource<bool> Started, TaskCompletionSource<bool> Exited)>();
                bool blockLoadObjects = true;
                bool blockSamplePreviews = false;
                CancellationToken samplePreviewCt = CancellationToken.None;
                var samplePreviewStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var samplePreviewExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var stub = new StubLabelStudioBackend(validPng)
                {
                    OnRefreshWorkspaceTypesAsync = async (prompter, ct) =>
                    {
                        refreshCt = ct;
                        refreshStarted.TrySetResult(true);
                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct);
                            if (prompter != null)
                            {
                                Interlocked.Increment(ref recoveryPromptAttempts);
                                await prompter.PromptForNewTokenAsync("https://mock", "expired", ct);
                            }

                            return new BackgroundTypeRefreshResult { Success = true };
                        }
                        finally
                        {
                            refreshExited.TrySetResult(true);
                        }
                    },
                    OnLoadObjectsForTypeAsync = async (typeCode, prompter, ct) =>
                    {
                        if (!blockLoadObjects)
                        {
                            return new SampleListResultDto
                            {
                                Success = true,
                                Samples = new[]
                                {
                                    new SampleSummaryDto
                                    {
                                        Name = "MockSubstrate-1",
                                        PermId = "PERM-MOCK-1",
                                        ModificationDate = "2026-11-20"
                                    }
                                }
                            };
                        }

                        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (loadObjQueue)
                        {
                            loadObjQueue.Enqueue((typeCode, ct, started, exited));
                        }
                        started.TrySetResult(true);

                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct);
                            if (prompter != null)
                            {
                                Interlocked.Increment(ref recoveryPromptAttempts);
                                await prompter.PromptForNewTokenAsync("https://mock", "expired", ct);
                            }

                            return new SampleListResultDto { Success = true };
                        }
                        finally
                        {
                            exited.TrySetResult(true);
                        }
                    },
                    OnLoadSamplePreviewsAsync = async (typeCode, permId, magnetFreeText, flaskFreeText, onSlotReady, prompter, ct) =>
                    {
                        if (!blockSamplePreviews)
                        {
                            var slots = new Dictionary<LabelTemplateType, byte[]>
                            {
                                [LabelTemplateType.SmallBox12mm] = validPng,
                                [LabelTemplateType.Magnet12mm] = validPng,
                                [LabelTemplateType.Flask24mm] = validPng
                            };
                            foreach (var kvp in slots)
                            {
                                onSlotReady?.Invoke(kvp.Key, kvp.Value);
                            }

                            return new SamplePreviewBundleDto
                            {
                                Success = true,
                                Profile = LabelTemplateProfile.Generic,
                                SampleName = "MockSubstrate-1",
                                PermId = permId,
                                SlotPreviews = slots
                            };
                        }

                        samplePreviewCt = ct;
                        samplePreviewStarted.TrySetResult(true);
                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct);
                            if (prompter != null)
                            {
                                Interlocked.Increment(ref recoveryPromptAttempts);
                                await prompter.PromptForNewTokenAsync("https://mock", "expired", ct);
                            }

                            return new SamplePreviewBundleDto { Success = true };
                        }
                        finally
                        {
                            samplePreviewExited.TrySetResult(true);
                        }
                    }
                };

                FieldInfo? hasLoadedSampleField = typeof(Form1).GetField("_hasLoadedSample", BindingFlags.NonPublic | BindingFlags.Instance);
                AssertNotNull(hasLoadedSampleField, "Expected _hasLoadedSample field on Form1");

                using (var form = new Form1(stub))
                {
                    ShowOffscreen(form);

                    var cmbTypes = FindControl<ComboBox>(form, "openbisTypeComboBox");
                    var cmbObjects = FindControl<ComboBox>(form, "openbisObjectsComboBox");
                    var lblSelectedType = FindControl<Label>(form, "selectedOpenbisTypeLabel");
                    var lblStatus = FindControl<Label>(form, "lblStatusNotice");
                    var prgLoading = FindControl<ProgressBar>(form, "prgLoading");
                    var btnLoad = FindControl<Button>(form, "loadPrintTemplates");
                    var btnPrint = FindControl<Button>(form, "btnPrintLabel");
                    var btnSettings = FindControl<Button>(form, "btnSettings");
                    var picSmallBox = FindControl<PictureBox>(form, "smallBoxLabelPreview");
                    var picMagnet = FindControl<PictureBox>(form, "magnetLabelPreview");
                    var picFlask = FindControl<PictureBox>(form, "flaskLabelPreview");

                    void OpenSettingsAndAssert(Action assertWhileModalOpen)
                    {
                        Exception? modalError = null;
                        int elapsedTicks = 0;
                        using var modalTimer = new System.Windows.Forms.Timer { Interval = 20 };
                        modalTimer.Tick += (_, _) =>
                        {
                            SetupSettingsForm? openSettings = null;
                            foreach (Form openForm in Application.OpenForms)
                            {
                                if (openForm is SetupSettingsForm sf && sf.Visible)
                                {
                                    openSettings = sf;
                                    break;
                                }
                            }

                            if (openSettings == null)
                            {
                                elapsedTicks++;
                                if (elapsedTicks > 150)
                                {
                                    modalTimer.Stop();
                                }
                                return;
                            }

                            modalTimer.Stop();
                            try
                            {
                                assertWhileModalOpen();
                            }
                            catch (Exception ex)
                            {
                                modalError = ex;
                            }
                            finally
                            {
                                openSettings.DialogResult = DialogResult.Cancel;
                                openSettings.Close();
                            }
                        };

                        modalTimer.Start();
                        try
                        {
                            btnSettings.PerformClick();
                        }
                        finally
                        {
                            modalTimer.Stop();
                        }

                        if (modalError != null)
                        {
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(modalError).Throw();
                        }
                    }

                    int FindTypeIndex(string code)
                    {
                        for (int i = 0; i < cmbTypes.Items.Count; i++)
                        {
                            if (cmbTypes.Items[i] is TypeOptionDto opt
                                && string.Equals(opt.Code, code, StringComparison.OrdinalIgnoreCase))
                            {
                                return i;
                            }
                        }

                        throw new InvalidOperationException($"Type '{code}' not found in openbisTypeComboBox.");
                    }

                    // 1. Opening Settings while RefreshWorkspaceTypesAsync is in flight cancels its token immediately
                    //    and prevents expired-token recovery from opening over Settings.
                    PumpEventsUntil(() => refreshStarted.Task.IsCompleted, "Timed out waiting for RefreshWorkspaceTypesAsync to start");
                    AssertFalse(refreshCt.IsCancellationRequested, "Refresh token must not be canceled before Settings is clicked");

                    OpenSettingsAndAssert(() =>
                    {
                        AssertTrue(refreshCt.IsCancellationRequested,
                            "Opening Settings must immediately cancel in-flight RefreshWorkspaceTypesAsync CancellationToken");
                    });

                    PumpEventsUntil(() => refreshExited.Task.IsCompleted, "Timed out waiting for canceled RefreshWorkspaceTypesAsync to unwind");
                    AssertEqual(0, Volatile.Read(ref recoveryPromptAttempts), "Canceled refresh must not invoke recovery prompter");
                    AssertFalse(lblStatus.Visible, "Canceled startup refresh must not display an error notice");

                    // 2. Switching between types and back to placeholder cancels prior LoadObjectsForTypeAsync calls cleanly.
                    int substanceIdx = FindTypeIndex("SUBSTANCE");
                    int substrateIdx = FindTypeIndex("SUBSTRATE");

                    cmbTypes.SelectedIndex = substanceIdx;
                    PumpEventsUntil(() => loadObjQueue.Count >= 1, "Timed out waiting for SUBSTANCE LoadObjectsForTypeAsync");
                    var call1 = loadObjQueue.Dequeue();
                    AssertEqual("SUBSTANCE", call1.TypeCode, "Expected SUBSTANCE load call");
                    AssertFalse(call1.Ct.IsCancellationRequested, "First type selection token should initially be active");
                    AssertTrue(prgLoading.Visible, "Loading bar should be visible while SUBSTANCE loads");
                    AssertFalse(cmbObjects.Enabled, "Objects combo should be disabled while SUBSTANCE loads");

                    cmbTypes.SelectedIndex = substrateIdx;
                    AssertTrue(call1.Ct.IsCancellationRequested,
                        "Switching to another type must immediately cancel prior LoadObjectsForTypeAsync token");
                    PumpEventsUntil(() => loadObjQueue.Count >= 1, "Timed out waiting for SUBSTRATE LoadObjectsForTypeAsync");
                    var call2 = loadObjQueue.Dequeue();
                    AssertEqual("SUBSTRATE", call2.TypeCode, "Expected SUBSTRATE load call");
                    AssertTrue(prgLoading.Visible, "Loading bar must remain visible while SUBSTRATE loads");

                    // Switch back to placeholder (index 0) -> must immediately cancel SUBSTRATE token and reset UI cleanly.
                    cmbTypes.SelectedIndex = 0;
                    AssertTrue(call2.Ct.IsCancellationRequested,
                        "Switching to placeholder must immediately cancel in-flight LoadObjectsForTypeAsync token");
                    PumpEventsUntil(
                        () => call1.Exited.Task.IsCompleted && call2.Exited.Task.IsCompleted,
                        "Timed out waiting for canceled type-selection loads to unwind");

                    AssertFalse(prgLoading.Visible, "Loading bar must hide after placeholder reset");
                    AssertTrue(cmbObjects.Enabled && btnLoad.Enabled && btnPrint.Enabled,
                        "Sample query controls must be re-enabled after placeholder reset");
                    AssertEqual(0, cmbObjects.Items.Count, "Objects combo must remain empty after canceled loads");
                    AssertEqual("Type:", lblSelectedType.Text, "Selected type label must reset to Type:");

                    // 3. Opening Settings while LoadObjectsForTypeAsync is in flight cancels token and re-enables UI.
                    cmbTypes.SelectedIndex = substrateIdx;
                    PumpEventsUntil(() => loadObjQueue.Count >= 1, "Timed out waiting for SUBSTRATE LoadObjectsForTypeAsync before Settings");
                    var call3 = loadObjQueue.Dequeue();

                    OpenSettingsAndAssert(() =>
                    {
                        AssertTrue(call3.Ct.IsCancellationRequested,
                            "Opening Settings must immediately cancel in-flight LoadObjectsForTypeAsync token");
                    });

                    PumpEventsUntil(() => call3.Exited.Task.IsCompleted, "Timed out waiting for canceled LoadObjectsForTypeAsync to unwind");
                    AssertFalse(prgLoading.Visible, "Loading bar must hide after Settings cancels in-flight LoadObjectsForTypeAsync");
                    AssertTrue(cmbObjects.Enabled && btnLoad.Enabled && btnPrint.Enabled,
                        "Sample query controls must be enabled after canceling Settings");

                    // 4. Opening Settings while LoadSamplePreviewsAsync is in flight cancels token, clears loading placeholders,
                    //    and restores controls cleanly.
                    blockLoadObjects = false;
                    blockSamplePreviews = true;
                    cmbTypes.SelectedIndex = 0;
                    cmbTypes.SelectedIndex = substrateIdx;
                    PumpEventsUntil(() => cmbObjects.Items.Count == 1, "Timed out waiting for SUBSTRATE sample item");

                    btnLoad.PerformClick();
                    PumpEventsUntil(() => samplePreviewStarted.Task.IsCompleted, "Timed out waiting for LoadSamplePreviewsAsync to start");
                    AssertNotNull(picSmallBox.Image, "Expected loading placeholder preview image while sample load is in flight");
                    AssertTrue(prgLoading.Visible, "Loading bar must be visible during sample preview load");
                    AssertFalse(btnPrint.Enabled, "Print button must be disabled during sample preview load");

                    OpenSettingsAndAssert(() =>
                    {
                        AssertTrue(samplePreviewCt.IsCancellationRequested,
                            "Opening Settings must immediately cancel in-flight LoadSamplePreviewsAsync token");
                    });

                    PumpEventsUntil(() => samplePreviewExited.Task.IsCompleted, "Timed out waiting for canceled LoadSamplePreviewsAsync to unwind");
                    AssertNull(picSmallBox.Image, "SmallBox loading placeholder image must be cleared when Settings cancels in-flight load");
                    AssertNull(picMagnet.Image, "Magnet loading placeholder image must be cleared when Settings cancels in-flight load");
                    AssertNull(picFlask.Image, "Flask loading placeholder image must be cleared when Settings cancels in-flight load");
                    AssertFalse((bool)hasLoadedSampleField!.GetValue(form)!, "_hasLoadedSample must remain false after canceled load");
                    AssertFalse(prgLoading.Visible, "Loading bar must hide after Settings cancels sample load");
                    AssertTrue(cmbObjects.Enabled && btnLoad.Enabled && btnPrint.Enabled,
                        "Controls must be re-enabled after Settings cancels sample load");

                    // 5. Opening Settings when a sample is ALREADY loaded (no sample load in flight) preserves _hasLoadedSample and cards.
                    blockSamplePreviews = false;
                    btnLoad.PerformClick();
                    PumpEventsUntil(
                        () => (bool)hasLoadedSampleField.GetValue(form)! && picSmallBox.Image != null,
                        "Timed out waiting for sample load completion");

                    OpenSettingsAndAssert(() => { });
                    AssertTrue((bool)hasLoadedSampleField.GetValue(form)!,
                        "_hasLoadedSample must be preserved when opening/canceling Settings with no load in flight");
                    AssertNotNull(picSmallBox.Image, "Existing preview cards must remain visible when no sample load was in flight");
                    AssertEqual(0, Volatile.Read(ref recoveryPromptAttempts), "No recovery prompt should fire when operations are canceled");
                }

                // 6. Disposing Form1 while RefreshWorkspaceTypesAsync & LoadObjectsForTypeAsync are in flight cancels cleanly without throwing.
                refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                refreshExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                refreshCt = CancellationToken.None;
                blockLoadObjects = true;
                loadObjQueue.Clear();

                CancellationToken disposeLoadCt = CancellationToken.None;
                TaskCompletionSource<bool>? disposeLoadExited = null;
                using (var disposingForm = new Form1(stub))
                {
                    ShowOffscreen(disposingForm);
                    PumpEventsUntil(() => refreshStarted.Task.IsCompleted, "Timed out waiting for refresh start before Form1 dispose");

                    var cmbTypes = FindControl<ComboBox>(disposingForm, "openbisTypeComboBox");
                    cmbTypes.SelectedIndex = 1;
                    PumpEventsUntil(() => loadObjQueue.Count == 1, "Timed out waiting for LoadObjectsForTypeAsync start before Form1 dispose");
                    var pending = loadObjQueue.Dequeue();
                    disposeLoadCt = pending.Ct;
                    disposeLoadExited = pending.Exited;
                }

                AssertTrue(refreshCt.IsCancellationRequested, "Disposing Form1 must cancel pending RefreshWorkspaceTypesAsync token");
                AssertTrue(disposeLoadCt.IsCancellationRequested, "Disposing Form1 must cancel pending LoadObjectsForTypeAsync token");
                PumpEventsUntil(
                    () => refreshExited.Task.IsCompleted && disposeLoadExited!.Task.IsCompleted,
                    "Timed out waiting for background tasks to exit after Form1 dispose");
                AssertNull(uiThreadException, $"Unexpected UI thread exception during cancellation test: {uiThreadException}");
            }
            finally
            {
                Application.ThreadException -= ThreadExceptionHandler;
            }
        }

        private static void SetupSettingsFormExamplePreviewExceptionDoesNotLeakUnobservedTask()
        {
            byte[] validPng = CreateValid1x1PngBytes();
            Exception? unobservedEx = null;
            void UnobservedHandler(object? sender, UnobservedTaskExceptionEventArgs e)
            {
                unobservedEx = e.Exception;
                e.SetObserved();
            }

            TaskScheduler.UnobservedTaskException += UnobservedHandler;
            try
            {
                var failedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var blockedMoleculeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var blockedMoleculeExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken blockedMoleculeCt = CancellationToken.None;

                var stub = new StubLabelStudioBackend(validPng)
                {
                    OnGetExamplePreviewsAsync = async (profile, onSlotReady, ct) =>
                    {
                        if (profile == LabelTemplateProfile.Molecule)
                        {
                            blockedMoleculeCt = ct;
                            blockedMoleculeStarted.TrySetResult(true);
                            try
                            {
                                await Task.Delay(Timeout.Infinite, ct);
                                return new Dictionary<LabelTemplateType, byte[]>();
                            }
                            finally
                            {
                                blockedMoleculeExited.TrySetResult(true);
                            }
                        }

                        failedTcs.TrySetResult(true);
                        throw new InvalidOperationException("Simulated example preview failure");
                    }
                };

                using (var form = new SetupSettingsForm(stub, SetupDialogMode.Settings, SetupWizardStep.ObjectTypes))
                {
                    form.ApplyTypeRows(new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule }
                    });
                    ShowOffscreen(form);

                    PumpEventsUntil(() => blockedMoleculeStarted.Task.IsCompleted, "Timed out waiting for Molecule example preview render");

                    // Switching to Generic cancels Molecule's token WITHOUT disposing it prematurely while blocked,
                    // then Generic throws an unexpected exception which must be caught safely inside LoadAndPrewarmExamplePreviewsAsync.
                    FindControl<Button>(form, "btnPreviewGeneric").PerformClick();
                    AssertTrue(blockedMoleculeCt.IsCancellationRequested,
                        "Switching preview profile must cancel prior Molecule preview token");

                    PumpEventsUntil(
                        () => blockedMoleculeExited.Task.IsCompleted && failedTcs.Task.IsCompleted
                              && stub.GetFallbackPreviewCalls.Exists(c =>
                                  c.Profile == LabelTemplateProfile.Generic
                                  && c.Reason.Contains("Simulated example preview failure", StringComparison.Ordinal)),
                        "Timed out waiting for canceled Molecule render and faulted Generic fallback render");

                    AssertNotNull(FindControl<PictureBox>(form, "picPreviewSmallBox").Image,
                        "Fallback preview image must be populated after example preview failure");
                    AssertNotNull(FindControl<PictureBox>(form, "picPreviewMagnet").Image,
                        "Fallback Magnet image must be populated after example preview failure");
                    AssertNotNull(FindControl<PictureBox>(form, "picPreviewFlask").Image,
                        "Fallback Flask image must be populated after example preview failure");
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                AssertNull(unobservedEx, $"Unexpected unobserved task exception: {unobservedEx}");
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= UnobservedHandler;
            }
        }

        private sealed class CapturingLabelPreviewRenderer : LabelPreviewRenderer
        {
            public CapturingLabelPreviewRenderer(
                AppConfigService configService,
                Func<IReadOnlyList<string>> installedPrintersProvider)
                : base(configService, installedPrintersProvider)
            {
            }

            public List<(LabelTemplateType Slot, LabelTemplateProfile Profile, string FreeText)> RenderedCalls { get; }
                = new List<(LabelTemplateType, LabelTemplateProfile, string)>();
            public LabelTemplateProfile? LastPrintProfile { get; private set; }
            public SampleDetailsDto? LastPrintDetails { get; private set; }
            public IReadOnlyList<LabelPrintJobRequest>? LastPrintJobs { get; private set; }

            public override Task<byte[]> RenderTemplatePreviewPngAsync(
                LabelTemplateType slot,
                LabelTemplateProfile profile,
                SampleDetailsDto details,
                string freeText = "",
                CancellationToken cancellationToken = default)
            {
                RenderedCalls.Add((slot, profile, freeText));
                return base.RenderTemplatePreviewPngAsync(slot, profile, details, freeText, cancellationToken);
            }

            public override Task<LabelPrintBatchResult> PrintLabelsAsync(
                LabelTemplateProfile profile,
                SampleDetailsDto details,
                IReadOnlyList<LabelPrintJobRequest> jobs,
                CancellationToken cancellationToken = default)
            {
                LastPrintProfile = profile;
                LastPrintDetails = details;
                LastPrintJobs = jobs;
                return base.PrintLabelsAsync(profile, details, jobs, cancellationToken);
            }
        }

        private static void BackendPrintSelectedLabelsStripsFreeTextForMoleculeAndRejectsMissingPrinter()
        {
            WithTempDir(tempRoot =>
            {
                var configService = new AppConfigService(tempRoot);
                configService.SaveAppConfig(new AppConfig
                {
                    OpenBisUrl = "https://openbis.example.org",
                    Token = "valid-tok"
                });
                configService.SaveConfiguredTypes(new[]
                {
                    new ConfiguredOpenBisType { Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                    new ConfiguredOpenBisType { Code = "SUBSTRATE", DisplayName = "Substrate", TemplateProfile = LabelTemplateProfile.Generic }
                });
                configService.SavePrinterConfig(AppConfigService.CreateDefaultPrinterConfig());

                // Create placeholder .lbx files so PrintLabelsBatch proceeds to installed-printer verification without real b-PAC hardware.
                Directory.CreateDirectory(configService.TemplatesDirectoryPath);
                foreach (LabelTemplateProfile profile in Enum.GetValues<LabelTemplateProfile>())
                {
                    foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
                    {
                        File.WriteAllText(configService.GetTemplateFilePath(slot, profile), "placeholder");
                    }
                }

                string scriptPath = Path.Combine(tempRoot, "openbis_fetch.py");
                File.WriteAllText(scriptPath,
                    "import json, sys\n" +
                    "if '--worker' in sys.argv[1:]:\n" +
                    "    while True:\n" +
                    "        line = sys.stdin.readline()\n" +
                    "        if line == '':\n" +
                    "            break\n" +
                    "        if not line.strip():\n" +
                    "            continue\n" +
                    "        req = json.loads(line)\n" +
                    "        perm_id = req.get('permId', '')\n" +
                    "        sample_type = 'SUBSTANCE' if perm_id == 'PERM-MOL-1' else 'SUBSTRATE'\n" +
                    "        resp = {'success': True, 'details': {'permId': perm_id, 'sampleType': sample_type, 'name': 'Test-' + perm_id}}\n" +
                    "        sys.stdout.write(json.dumps(resp) + '\\n')\n" +
                    "        sys.stdout.flush()\n");

                var client = new OpenBisClient(tempRoot);
                var capturingRenderer = new CapturingLabelPreviewRenderer(
                    configService,
                    () => new[] { "Brother PT-P700 (Installed)" });

                using var backend = new LabelStudioBackend(
                    configService,
                    client,
                    capturingRenderer,
                    () => new[] { "Brother PT-P700 (Installed)" });

                // 1. Molecule sample: FreeText must be stripped across ALL slots for RenderSlotWithFreeTextAsync and PrintSelectedLabelsAsync,
                //    and a nonexistent Brother printer must be rejected cleanly without touching hardware.
                SamplePreviewBundleDto molBundle = backend.LoadSamplePreviewsAsync(
                    "SUBSTANCE",
                    "PERM-MOL-1",
                    "should-strip-magnet",
                    "should-strip-flask").GetAwaiter().GetResult();
                AssertTrue(molBundle.Success, $"Molecule LoadSamplePreviewsAsync failed: {molBundle.ErrorMessage}");
                AssertEqual(LabelTemplateProfile.Molecule, molBundle.Profile, "Expected Molecule profile");

                capturingRenderer.RenderedCalls.Clear();
                backend.RenderSlotWithFreeTextAsync(LabelTemplateType.SmallBox12mm, "box-extra").GetAwaiter().GetResult();
                backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "mag-extra").GetAwaiter().GetResult();
                AssertEqual(2, capturingRenderer.RenderedCalls.Count, "Expected 2 captured RenderSlotWithFreeTextAsync calls");
                AssertEqual("", capturingRenderer.RenderedCalls[0].FreeText, "SmallBox12mm FreeText must always be stripped");
                AssertEqual("", capturingRenderer.RenderedCalls[1].FreeText, "Molecule Magnet12mm FreeText must be stripped");

                LabelPrintBatchResult molPrintResult = backend.PrintSelectedLabelsAsync(new[]
                {
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.SmallBox12mm,
                        PrinterName = "Brother PT-P700 (Missing Box)",
                        FreeText = "strip-box"
                    },
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.Magnet12mm,
                        PrinterName = "Brother PT-P700 (Missing Magnet)",
                        FreeText = "strip-magnet"
                    },
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.Flask24mm,
                        PrinterName = "Brother PT-P700 (Missing Flask)",
                        FreeText = "strip-flask"
                    }
                }).GetAwaiter().GetResult();

                AssertEqual(LabelTemplateProfile.Molecule, capturingRenderer.LastPrintProfile, "Expected Molecule profile passed to PrintLabelsAsync");
                AssertNotNull(capturingRenderer.LastPrintDetails, "Expected sample details passed to PrintLabelsAsync");
                AssertEqual("PERM-MOL-1", capturingRenderer.LastPrintDetails!.PermId, "Expected PERM-MOL-1 details passed to PrintLabelsAsync");
                AssertNotNull(capturingRenderer.LastPrintJobs, "Expected jobs passed to PrintLabelsAsync");
                AssertEqual(3, capturingRenderer.LastPrintJobs!.Count, "Expected 3 print job requests");
                foreach (LabelPrintJobRequest job in capturingRenderer.LastPrintJobs)
                {
                    AssertEqual("", job.FreeText, $"Molecule print job {job.Slot} must strip FreeText cleanly");
                }

                AssertFalse(molPrintResult.AllSucceeded, "Nonexistent printer must fail print batch safely");
                AssertEqual(3, molPrintResult.Jobs.Count, "Expected 3 failed job results");
                foreach (LabelPrintJobResult jobRes in molPrintResult.Jobs)
                {
                    AssertFalse(jobRes.Success, $"Job {jobRes.Slot} targeting missing printer must fail");
                    AssertTrue(jobRes.Error.Contains("not installed", StringComparison.OrdinalIgnoreCase),
                        $"Expected not-installed error for {jobRes.Slot}, actual: {jobRes.Error}");
                }

                // 2. Generic sample: SmallBox12mm must still strip FreeText in both RenderSlotWithFreeTextAsync and PrintSelectedLabelsAsync,
                //    while Magnet12mm / Flask24mm keep FreeText.
                SamplePreviewBundleDto genBundle = backend.LoadSamplePreviewsAsync(
                    "SUBSTRATE",
                    "PERM-GEN-1",
                    "mag-init",
                    "flask-init").GetAwaiter().GetResult();
                AssertTrue(genBundle.Success, $"Generic LoadSamplePreviewsAsync failed: {genBundle.ErrorMessage}");
                AssertEqual(LabelTemplateProfile.Generic, genBundle.Profile, "Expected Generic profile");

                capturingRenderer.RenderedCalls.Clear();
                backend.RenderSlotWithFreeTextAsync(LabelTemplateType.SmallBox12mm, "strip-box").GetAwaiter().GetResult();
                backend.RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "keep-mag").GetAwaiter().GetResult();
                AssertEqual("", capturingRenderer.RenderedCalls[0].FreeText, "Generic SmallBox12mm RenderSlotWithFreeTextAsync must strip FreeText");
                AssertEqual("keep-mag", capturingRenderer.RenderedCalls[1].FreeText, "Generic Magnet12mm RenderSlotWithFreeTextAsync must keep FreeText");

                LabelPrintBatchResult genPrintResult = backend.PrintSelectedLabelsAsync(new[]
                {
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.SmallBox12mm,
                        PrinterName = "Brother PT-P700 (Missing)",
                        FreeText = "strip-box"
                    },
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.Magnet12mm,
                        PrinterName = "Brother PT-P700 (Missing)",
                        FreeText = "keep-mag"
                    },
                    new PrintSlotSelection
                    {
                        Slot = LabelTemplateType.Flask24mm,
                        PrinterName = "Brother PT-P700 (Missing)",
                        FreeText = "keep-flask"
                    }
                }).GetAwaiter().GetResult();

                AssertFalse(genPrintResult.AllSucceeded, "Generic print batch targeting missing printer must fail safely");
                AssertEqual("", capturingRenderer.LastPrintJobs![0].FreeText, "Generic SmallBox12mm print job must strip FreeText");
                AssertEqual("keep-mag", capturingRenderer.LastPrintJobs[1].FreeText, "Generic Magnet12mm print job must preserve FreeText");
                AssertEqual("keep-flask", capturingRenderer.LastPrintJobs[2].FreeText, "Generic Flask24mm print job must preserve FreeText");
            });
        }

        private static void BackendSaveWizardStepRoundTripsPersistEachStepCleanly()
        {
            WithTempDir(tempRoot =>
            {
                using var backend = new LabelStudioBackend(
                    tempRoot,
                    installedPrintersProvider: () => new[] { "Brother PT-P700" });

                AssertEqual(SetupWizardStep.Connection,
                    backend.InitializeWorkspaceAsync().GetAwaiter().GetResult().MissingWizardStep,
                    "Fresh directory must start at Connection wizard step");

                // 1. Connection step success round-trip
                OperationResult connRes = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.Connection,
                    OpenBisUrl = "  https://openbis.example.org/openbis  ",
                    Token = "  wizard-tok-123  "
                }).GetAwaiter().GetResult();
                AssertTrue(connRes.Success, $"Connection wizard save should succeed, error: {connRes.ErrorMessage}");

                SettingsSnapshotDto afterConn = backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
                AssertEqual("https://openbis.example.org/openbis", afterConn.OpenBisUrl, "Connection wizard URL should trim and persist");
                AssertEqual("wizard-tok-123", afterConn.Token, "Connection wizard Token should trim and persist");
                AssertEqual(SetupWizardStep.ObjectTypes, afterConn.FirstMissingWizardStep, "Missing step after Connection should be ObjectTypes");

                // 2. ObjectTypes step success round-trip (filters to IsSelected == true)
                OperationResult typesRes = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.ObjectTypes,
                    ObjectTypes = new[]
                    {
                        new ObjectTypeRowDto { IsSelected = false, Code = "SKIPPED", DisplayName = "Skipped", TemplateProfile = LabelTemplateProfile.Generic },
                        new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule },
                        new ObjectTypeRowDto { IsSelected = true, Code = "CRYSTAL", DisplayName = "Lab Crystal", TemplateProfile = LabelTemplateProfile.Generic }
                    }
                }).GetAwaiter().GetResult();
                AssertTrue(typesRes.Success, $"ObjectTypes wizard save should succeed, error: {typesRes.ErrorMessage}");

                SettingsSnapshotDto afterTypes = backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
                AssertEqual(2, afterTypes.ConfiguredTypes.Count, "Only selected ObjectTypes must persist");
                AssertEqual("CRYSTAL", afterTypes.ConfiguredTypes[0].Code, "Expected sorted CRYSTAL first");
                AssertEqual(LabelTemplateProfile.Generic, afterTypes.ConfiguredTypes[0].TemplateProfile, "Expected CRYSTAL Generic profile");
                AssertEqual("SUBSTANCE", afterTypes.ConfiguredTypes[1].Code, "Expected sorted SUBSTANCE second");
                AssertEqual(LabelTemplateProfile.Molecule, afterTypes.ConfiguredTypes[1].TemplateProfile, "Expected SUBSTANCE Molecule profile");
                AssertEqual(SetupWizardStep.Printers, afterTypes.FirstMissingWizardStep, "Missing step after ObjectTypes should be Printers");

                // 3a. Printers step success round-trip with ObjectTypes == null preserves saved ObjectTypes
                OperationResult printersOnlyRes = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.Printers,
                    ObjectTypes = null,
                    SmallBoxPrinter = "  Brother PT-P700 (Box) ",
                    MagnetPrinter = " Brother PT-P700 (Mag) ",
                    FlaskPrinter = " Brother PT-P700 (Flask) "
                }).GetAwaiter().GetResult();
                AssertTrue(printersOnlyRes.Success, $"Printers-only wizard save should succeed, error: {printersOnlyRes.ErrorMessage}");

                WorkspaceStartupState ready = backend.InitializeWorkspaceAsync().GetAwaiter().GetResult();
                AssertEqual(SetupWizardStep.None, ready.MissingWizardStep, "All wizard steps complete should report SetupWizardStep.None");
                AssertEqual(2, ready.ConfiguredTypes.Count, "Printers step with ObjectTypes == null must preserve existing types");

                var verifyService = new AppConfigService(tempRoot);
                PrinterConfig? savedPrinters = verifyService.LoadPrinterConfig();
                AssertNotNull(savedPrinters, "Saved PrinterConfig should exist");
                AssertEqual("Brother PT-P700 (Box)", savedPrinters!.SmallBoxPrinter, "Saved SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (Mag)", savedPrinters.MagnetPrinter, "Saved MagnetPrinter mismatch");
                AssertEqual("Brother PT-P700 (Flask)", savedPrinters.FlaskPrinter, "Saved FlaskPrinter mismatch");

                // 3b. Printers step with updated ObjectTypes and blank printer normalizes defaults and updates both configs
                OperationResult printersWithTypesRes = backend.SaveWizardStepAsync(new WizardStepSaveRequest
                {
                    Step = SetupWizardStep.Printers,
                    ObjectTypes = new[]
                    {
                        new ObjectTypeRowDto { IsSelected = true, Code = "WAFER", DisplayName = "Silicon Wafer", TemplateProfile = LabelTemplateProfile.Generic }
                    },
                    SmallBoxPrinter = "Brother PT-P700 (Custom)",
                    MagnetPrinter = "",
                    FlaskPrinter = "   "
                }).GetAwaiter().GetResult();
                AssertTrue(printersWithTypesRes.Success, $"Printers+ObjectTypes wizard save should succeed, error: {printersWithTypesRes.ErrorMessage}");

                SettingsSnapshotDto finalSnapshot = backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult();
                AssertEqual(1, finalSnapshot.ConfiguredTypes.Count, "Expected updated single WAFER type");
                AssertEqual("WAFER", finalSnapshot.ConfiguredTypes[0].Code, "Expected WAFER code");
                PrinterConfig? normalizedPrinters = verifyService.LoadPrinterConfig();
                AssertNotNull(normalizedPrinters, "Normalized PrinterConfig should exist");
                AssertEqual("Brother PT-P700 (Custom)", normalizedPrinters!.SmallBoxPrinter, "Custom SmallBoxPrinter mismatch");
                AssertEqual("Brother PT-P700 (Kopie 1)", normalizedPrinters.MagnetPrinter, "Blank MagnetPrinter should normalize to default");
                AssertEqual("Brother PT-P700 (Kopie 2)", normalizedPrinters.FlaskPrinter, "Blank FlaskPrinter should normalize to default");
            });
        }

        private static void FrontendFormsContainNoBackendServiceOrSecretReferences()
        {
            Assembly frontendAssembly = typeof(Form1).Assembly;
            foreach (AssemblyName refAsm in frontendAssembly.GetReferencedAssemblies())
            {
                string? asmName = refAsm.Name;
                AssertFalse(string.Equals(asmName, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase),
                    "Frontend assembly must not directly reference Newtonsoft.Json");
                AssertFalse(asmName != null && asmName.Contains("bpac", StringComparison.OrdinalIgnoreCase),
                    $"Frontend assembly must not reference bpac ({asmName})");
            }

            var forbiddenTypeFullNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "OpenBISLabelStudio.Services.AppConfigService",
                "OpenBISLabelStudio.Services.OpenBisClient",
                "OpenBISLabelStudio.Services.LabelPreviewRenderer",
                "OpenBISLabelStudio.Services.TokenRecoveryCoordinator",
                "OpenBISLabelStudio.Models.AppConfig",
                "OpenBISLabelStudio.Models.ConfiguredOpenBisType",
                "OpenBISLabelStudio.Models.PrinterConfig",
                "OpenBISLabelStudio.Models.SampleDto",
                "OpenBISLabelStudio.Models.SampleDetailsDto",
                "OpenBISLabelStudio.Models.OpenBisTypeInfoDto",
                "OpenBISLabelStudio.Models.OpenBisTypeItem",
                "OpenBISLabelStudio.Models.LabelPrintJobRequest"
            };

            void AssertAllowedType(Type? type, string memberContext)
            {
                if (type == null)
                {
                    return;
                }

                if (type.IsArray || type.IsByRef || type.IsPointer)
                {
                    AssertAllowedType(type.GetElementType(), memberContext);
                    return;
                }

                if (type.IsGenericType)
                {
                    foreach (Type arg in type.GetGenericArguments())
                    {
                        AssertAllowedType(arg, memberContext);
                    }
                }

                string fullName = type.FullName ?? type.Name;
                string ns = type.Namespace ?? "";
                string asmName = type.Assembly.GetName().Name ?? "";

                AssertFalse(forbiddenTypeFullNames.Contains(fullName),
                    $"{memberContext} must not reference forbidden type '{fullName}'");
                AssertFalse(ns.StartsWith("OpenBISLabelStudio.Services", StringComparison.Ordinal),
                    $"{memberContext} must not reference OpenBISLabelStudio.Services type '{fullName}'");
                AssertFalse(ns.StartsWith("Newtonsoft.Json", StringComparison.Ordinal)
                            || string.Equals(asmName, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase),
                    $"{memberContext} must not reference Newtonsoft.Json type '{fullName}'");
                AssertFalse(ns.StartsWith("bpac", StringComparison.OrdinalIgnoreCase)
                            || asmName.Contains("bpac", StringComparison.OrdinalIgnoreCase),
                    $"{memberContext} must not reference bpac type '{fullName}'");
            }

            void InspectTypeTree(Type rootType)
            {
                const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (FieldInfo f in rootType.GetFields(allMembers))
                {
                    AssertAllowedType(f.FieldType, $"Field {rootType.Name}.{f.Name}");
                }

                foreach (PropertyInfo p in rootType.GetProperties(allMembers))
                {
                    AssertAllowedType(p.PropertyType, $"Property {rootType.Name}.{p.Name}");
                }

                foreach (ConstructorInfo c in rootType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    foreach (ParameterInfo param in c.GetParameters())
                    {
                        AssertAllowedType(param.ParameterType, $"Constructor {rootType.Name}({param.Name})");
                    }
                }

                foreach (MethodInfo m in rootType.GetMethods(allMembers))
                {
                    AssertAllowedType(m.ReturnType, $"Method {rootType.Name}.{m.Name} return");
                    foreach (ParameterInfo param in m.GetParameters())
                    {
                        AssertAllowedType(param.ParameterType, $"Method {rootType.Name}.{m.Name}({param.Name})");
                    }
                }

                foreach (Type nested in rootType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    InspectTypeTree(nested);
                }
            }

            InspectTypeTree(typeof(Form1));
            InspectTypeTree(typeof(SetupSettingsForm));
            InspectTypeTree(typeof(FormTokenRecoveryPrompter));
        }

        private static void BackendSampleSorterOrdersByNamePermIdAndModificationDate()
        {
            var nameSamples = new[]
            {
                new SampleSummaryDto { Name = "751a", PermId = "P-7", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "0000000000000000000010b", PermId = "P-9", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "0a", PermId = "P-1", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "x100000000000000000000000", PermId = "P-11", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "100a", PermId = "P-4", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "A10", PermId = "P-6", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "10a", PermId = "P-3", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "a2", PermId = "P-5", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "000000000000000000002b", PermId = "P-8", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "x99999999999999999999999", PermId = "P-10", ModificationDate = "2026-01-01T00:00:00Z" },
                new SampleSummaryDto { Name = "2a", PermId = "P-2", ModificationDate = "2026-01-01T00:00:00Z" }
            };

            IReadOnlyList<SampleSummaryDto> ascNames = SampleSorter.Sort(nameSamples, SampleSortField.Name, SampleSortDirection.Ascending);
            string[] expectedAscNames =
            {
                "0a", "2a", "000000000000000000002b", "10a", "0000000000000000000010b",
                "100a", "751a", "a2", "A10",
                "x99999999999999999999999", "x100000000000000000000000"
            };
            AssertEqual(expectedAscNames.Length, ascNames.Count, "Ascending natural name count mismatch");
            for (int i = 0; i < expectedAscNames.Length; i++)
            {
                AssertEqual(expectedAscNames[i], ascNames[i].Name, $"Ascending name mismatch at index {i}");
            }

            IReadOnlyList<SampleSummaryDto> descNames = SampleSorter.Sort(nameSamples, SampleSortField.Name, SampleSortDirection.Descending);
            for (int i = 0; i < expectedAscNames.Length; i++)
            {
                AssertEqual(expectedAscNames[expectedAscNames.Length - 1 - i], descNames[i].Name, $"Descending name mismatch at index {i}");
            }

            var permSamples = new[]
            {
                new SampleSummaryDto { Name = "x", PermId = "20260924152631220-10170", ModificationDate = "" },
                new SampleSummaryDto { Name = "x", PermId = "ABC-10", ModificationDate = "" },
                new SampleSummaryDto { Name = "x", PermId = "20251113162300874-4172", ModificationDate = "" },
                new SampleSummaryDto { Name = "x", PermId = "ABC-2", ModificationDate = "" },
                new SampleSummaryDto { Name = "x", PermId = "20260924152631220-8238", ModificationDate = "" }
            };

            IReadOnlyList<SampleSummaryDto> ascPerms = SampleSorter.Sort(permSamples, SampleSortField.PermId, SampleSortDirection.Ascending);
            string[] expectedAscPerms =
            {
                "20251113162300874-4172",
                "20260924152631220-8238",
                "20260924152631220-10170",
                "ABC-2",
                "ABC-10"
            };
            for (int i = 0; i < expectedAscPerms.Length; i++)
            {
                AssertEqual(expectedAscPerms[i], ascPerms[i].PermId, $"Ascending PermId mismatch at index {i}");
            }

            IReadOnlyList<SampleSummaryDto> descPerms = SampleSorter.Sort(permSamples, SampleSortField.PermId, SampleSortDirection.Descending);
            for (int i = 0; i < expectedAscPerms.Length; i++)
            {
                AssertEqual(expectedAscPerms[expectedAscPerms.Length - 1 - i], descPerms[i].PermId, $"Descending PermId mismatch at index {i}");
            }

            var modSamples = new[]
            {
                new SampleSummaryDto { Name = "oldest-offset", PermId = "P-OFFSET", ModificationDate = "2026-10-14T10:30:00+02:00" },
                new SampleSummaryDto { Name = "newest-utc", PermId = "P-UTC", ModificationDate = "2026-10-14T09:00:00Z" },
                new SampleSummaryDto { Name = "middle", PermId = "P-MID", ModificationDate = "2026-10-14T08:45:00Z" }
            };

            IReadOnlyList<SampleSummaryDto> descMod = SampleSorter.Sort(modSamples, SampleSortField.ModificationDate, SampleSortDirection.Descending);
            AssertEqual("P-UTC", descMod[0].PermId, "Newest UTC timestamp should sort first in Descending");
            AssertEqual("P-MID", descMod[1].PermId, "Middle timestamp should sort second in Descending");
            AssertEqual("P-OFFSET", descMod[2].PermId, "08:30Z (+02:00) timestamp should sort last in Descending");

            IReadOnlyList<SampleSummaryDto> ascMod = SampleSorter.Sort(modSamples, SampleSortField.ModificationDate, SampleSortDirection.Ascending);
            AssertEqual("P-OFFSET", ascMod[0].PermId, "08:30Z (+02:00) timestamp should sort first in Ascending");
            AssertEqual("P-MID", ascMod[1].PermId, "Middle timestamp should sort second in Ascending");
            AssertEqual("P-UTC", ascMod[2].PermId, "Newest UTC timestamp should sort last in Ascending");
        }

        private static void BackendSampleSorterHandlesEdgeCasesWithoutThrowing()
        {
            IReadOnlyList<SampleSummaryDto> emptyFromNull = SampleSorter.Sort(null, SampleSortField.Name, SampleSortDirection.Ascending);
            AssertEqual(0, emptyFromNull.Count, "Null sample list should yield empty list");

            var trickyDates = new List<SampleSummaryDto>
            {
                null!,
                new SampleSummaryDto { Name = "", PermId = "P-BAD-DATE", ModificationDate = "not-a-date" },
                new SampleSummaryDto { Name = "named", PermId = "P-VALID-OLD", ModificationDate = "2025-11-13 16:23:00" },
                new SampleSummaryDto { Name = "alpha", PermId = "", ModificationDate = "" },
                new SampleSummaryDto { Name = null!, PermId = "P-NULL-FIELDS", ModificationDate = null! },
                new SampleSummaryDto { Name = "valid-new", PermId = "P-VALID-NEW", ModificationDate = "2026-09-24T15:26:31Z" }
            };

            IReadOnlyList<SampleSummaryDto> modDesc = SampleSorter.Sort(trickyDates, SampleSortField.ModificationDate, SampleSortDirection.Descending);
            AssertEqual(5, modDesc.Count, "Null entries should be filtered out");
            AssertEqual("P-VALID-NEW", modDesc[0].PermId, "Newest valid date must come first in Descending");
            AssertEqual("P-VALID-OLD", modDesc[1].PermId, "Older valid date must come second in Descending");

            IReadOnlyList<SampleSummaryDto> modAsc = SampleSorter.Sort(trickyDates, SampleSortField.ModificationDate, SampleSortDirection.Ascending);
            AssertEqual("P-VALID-OLD", modAsc[0].PermId, "Older valid date must come first in Ascending");
            AssertEqual("P-VALID-NEW", modAsc[1].PermId, "Newer valid date must come second in Ascending");

            IReadOnlyList<SampleSummaryDto> permAsc = SampleSorter.Sort(trickyDates, SampleSortField.PermId, SampleSortDirection.Ascending);
            AssertEqual(5, permAsc.Count, "PermId sort must preserve all non-null items");
            AssertEqual("", permAsc[4].PermId, "Blank PermId should sort after non-blank PermIds");

            IReadOnlyList<SampleSummaryDto> nameAsc = SampleSorter.Sort(trickyDates, SampleSortField.Name, SampleSortDirection.Ascending);
            AssertEqual("alpha", nameAsc[0].Name, "First non-empty name should sort first in Ascending");
            AssertEqual("named", nameAsc[1].Name, "Second non-empty name should sort second in Ascending");
            AssertEqual("valid-new", nameAsc[2].Name, "Third non-empty name should sort third in Ascending");
        }

        private static void BackendSortSamplesIsolatesInputAndBreaksDuplicateTies()
        {
            WithTempDir(tempRoot =>
            {
                using var backend = new LabelStudioBackend(tempRoot);
                var original = new List<SampleSummaryDto>
                {
                    new SampleSummaryDto { Name = "0a", PermId = "20260924152631220-8238", ModificationDate = "2026-09-24T15:26:31Z" },
                    new SampleSummaryDto { Name = "0a", PermId = "20251113162300874-4172", ModificationDate = "2025-11-13T16:23:00Z" }
                };

                IReadOnlyList<SampleSummaryDto> sortedAsc = backend.SortSamples(original, SampleSortField.Name, SampleSortDirection.Ascending);
                IReadOnlyList<SampleSummaryDto> sortedDesc = backend.SortSamples(original, SampleSortField.Name, SampleSortDirection.Descending);

                AssertEqual("20260924152631220-8238", original[0].PermId, "Caller list must not be mutated in place");
                AssertEqual("20251113162300874-4172", sortedAsc[0].PermId, "Duplicate Name Ascending tie should break by PermId Ascending");
                AssertEqual("20260924152631220-8238", sortedAsc[1].PermId, "Duplicate Name Ascending second item mismatch");
                AssertEqual("20260924152631220-8238", sortedDesc[0].PermId, "Duplicate Name Descending tie should break by PermId Descending");
                AssertEqual("20251113162300874-4172", sortedDesc[1].PermId, "Duplicate Name Descending second item mismatch");
            });
        }

        private static void ShowOffscreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
        }

        private static T FindControl<T>(Control root, string name) where T : Control
        {
            Control[] matches = root.Controls.Find(name, searchAllChildren: true);
            if (matches.Length == 0 || matches[0] is not T typed)
            {
                throw new InvalidOperationException($"Expected control '{name}' of type {typeof(T).Name} on {root.Name}.");
            }

            return typed;
        }

        private static void WithTempDir(Action<string> action)
        {
            string dir = Path.Combine(Path.GetTempPath(), "OpenBISLabelStudioTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                action(dir);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertFalse(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException(message);
        }

        private static void AssertNull(object? value, string message)
        {
            if (value != null) throw new InvalidOperationException($"{message}. Actual: {value}");
        }

        private static void AssertNotNull(object? value, string message)
        {
            if (value == null) throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{message}. Expected: {expected}, Actual: {actual}");
            }
        }
    }
}
