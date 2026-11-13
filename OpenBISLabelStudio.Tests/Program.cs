using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OpenBISLabelStudio.Tests
{
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
                (nameof(BuildExampleSampleDetailsMatchesProfile), BuildExampleSampleDetailsMatchesProfile),
                (nameof(RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing), RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing),
                (nameof(BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved), BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved),
                (nameof(SetupSettingsFormInitializesSafelyWithZeroPrinters), SetupSettingsFormInitializesSafelyWithZeroPrinters),
                (nameof(SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken), SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken),
                (nameof(SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards), SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards),
                (nameof(SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails), SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails),
                (nameof(Form1CredentialsChangedSinceRequestDetectsUrlAndTokenChanges), Form1CredentialsChangedSinceRequestDetectsUrlAndTokenChanges),
                (nameof(Form1ResolveTemplateProfileUsesConfiguredMappingAndFallbacks), Form1ResolveTemplateProfileUsesConfiguredMappingAndFallbacks),
                (nameof(Form1HasSettingsButtonWithoutOverlaps), Form1HasSettingsButtonWithoutOverlaps),
                (nameof(Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges), Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges),
                (nameof(TokenRecoveryCoordinatorCoalescesConcurrentCallsAndResetsAfterCompletion), TokenRecoveryCoordinatorCoalescesConcurrentCallsAndResetsAfterCompletion)
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
                    "if '--types' in args:\n" +
                    "    payload = {'success': False, 'errorType': 'token_expired', 'error': 'Expired'}\n" +
                    "elif '--details' in args:\n" +
                    "    payload = {'success': True, 'details': {'permId': '2026-1', 'name': 'CuPc', 'sampleType': 'SUBSTANCE'}}\n" +
                    "else:\n" +
                    "    payload = {'success': True, 'data': [{'name': 'Sample-A', 'permId': '2026-9', 'modificationDate': '2026-11-13'}]}\n" +
                    "with open(out_path, 'w', encoding='utf-8') as f:\n" +
                    "    json.dump(payload, f)\n");

                var client = new OpenBisClient(tempRoot);
                string tokenWithSpecialChars = "tok&1 \"quoted\" %TEMP%";
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

                ModelTypes.OpenBisListResult listResult = client.QuerySamplesByTypeAsync("https://openbis.example.org", tokenWithSpecialChars, "SUBSTANCE").GetAwaiter().GetResult();
                AssertTrue(listResult.Success, "Sample list stub should succeed");
                AssertEqual(1, listResult.Data.Count, "Sample list count mismatch");
                AssertEqual("Sample-A", listResult.Data[0].Name, "Sample list item Name mismatch");

                string thirdRecordedOutPath = File.ReadAllText(recordFile).Trim();
                AssertFalse(File.Exists(thirdRecordedOutPath), $"Third temp JSON output file must be deleted: {thirdRecordedOutPath}");
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

        private static void BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved()
        {
            var serverTypes = new[]
            {
                new OpenBisTypeInfoDto { Code = "SUBSTANCE", Description = "Substance" },
                new OpenBisTypeInfoDto { Code = "CRYSTAL", Description = "Crystal" },
                new OpenBisTypeInfoDto { Code = "CUSTOM_PLATE", Description = "" }
            };

            List<ConfiguredOpenBisType> initialRows = SetupSettingsForm.BuildInitialTypeRows(
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

            List<ConfiguredOpenBisType> mergedRows = SetupSettingsForm.BuildInitialTypeRows(serverTypes, saved);
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
                using var form = new SetupSettingsForm(
                    configService,
                    client,
                    renderer,
                    SetupDialogMode.Settings,
                    installedPrintersProvider: () => new[] { "Brother PT-P700" });

                form.ApplyServerTypes(serverTypes);
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

                using (var wizardForm = new SetupSettingsForm(
                    configService,
                    client,
                    renderer,
                    SetupDialogMode.Wizard,
                    SetupWizardStep.Printers,
                    installedPrintersProvider: () => Array.Empty<string>()))
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
                    SmallBoxPrinter = "Custom Box Queue",
                    MagnetPrinter = "Custom Magnet Queue",
                    FlaskPrinter = "Custom Flask Queue"
                });

                using (var settingsForm = new SetupSettingsForm(
                    configService,
                    client,
                    renderer,
                    SetupDialogMode.Settings,
                    SetupWizardStep.Printers,
                    installedPrintersProvider: () => Array.Empty<string>()))
                {
                    AssertEqual(SetupDialogMode.Settings, settingsForm.Mode, "Expected Settings mode");
                    ShowOffscreen(settingsForm);
                    var warningLabel = FindControl<Label>(settingsForm, "lblNoPrintersWarning");
                    AssertTrue(warningLabel.Visible, "Zero-printer warning label must be visible in Settings Printer Routing tab");

                    var cmbBox = FindControl<ComboBox>(settingsForm, "cmbSmallBoxPrinter");
                    var cmbMagnet = FindControl<ComboBox>(settingsForm, "cmbMagnetPrinter");
                    var cmbFlask = FindControl<ComboBox>(settingsForm, "cmbFlaskPrinter");
                    AssertEqual("Custom Box Queue", cmbBox.Text, "SmallBox printer should keep configured name");
                    AssertEqual("Custom Magnet Queue", cmbMagnet.Text, "Magnet printer should keep configured name");
                    AssertEqual("Custom Flask Queue", cmbFlask.Text, "Flask printer should keep configured name");
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

                using var form = new SetupSettingsForm(
                    configService,
                    client,
                    renderer,
                    SetupDialogMode.ExpiredTokenRecovery,
                    SetupWizardStep.Connection,
                    initialWarning: "Token expired",
                    installedPrintersProvider: () => Array.Empty<string>());

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
                    SmallBoxPrinter = "Printer A",
                    MagnetPrinter = "Printer B",
                    FlaskPrinter = "Printer C"
                });

                string origAppJson = File.ReadAllText(configService.OpenBisConfigPath);
                string origTypesJson = File.ReadAllText(configService.OpenBisTypesPath);
                string origPrintersJson = File.ReadAllText(configService.PrintersConfigPath);

                var offlineClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);
                string[] printers = { "Printer A", "Printer B", "Printer C", "Printer X", "Printer Y", "Printer Z" };

                // 1. Modify state and Cancel -> disk files must remain untouched.
                using (var cancelForm = new SetupSettingsForm(
                    configService,
                    offlineClient,
                    renderer,
                    SetupDialogMode.Settings,
                    installedPrintersProvider: () => printers))
                {
                    FindControl<TextBox>(cancelForm, "txtInstanceUrl").Text = "https://changed.example.org";
                    FindControl<TextBox>(cancelForm, "txtToken").Text = "changed-token";
                    FindControl<ComboBox>(cancelForm, "cmbSmallBoxPrinter").SelectedItem = "Printer X";
                    FindControl<DataGridView>(cancelForm, "dgvObjectTypes").Rows[1].Cells[2].Value = "Molecule / Substance";

                    ShowOffscreen(cancelForm);
                    FindControl<Button>(cancelForm, "btnCancel").PerformClick();
                }

                AssertEqual(origAppJson, File.ReadAllText(configService.OpenBisConfigPath), "Cancel must leave openbis_config.json unchanged");
                AssertEqual(origTypesJson, File.ReadAllText(configService.OpenBisTypesPath), "Cancel must leave openbis_types.json unchanged");
                AssertEqual(origPrintersJson, File.ReadAllText(configService.PrintersConfigPath), "Cancel must leave printers_config.json unchanged");

                // 2. Verify live example preview rendering on Page 2 + invalid save guard + valid TrySaveSettingsOffline.
                using (var saveForm = new SetupSettingsForm(
                    configService,
                    offlineClient,
                    renderer,
                    SetupDialogMode.Settings,
                    SetupWizardStep.ObjectTypes,
                    installedPrintersProvider: () => printers))
                {
                    // Pre-seed CachedServerTypes so OnShown does not spawn offlineClient during UI preview verification.
                    saveForm.ApplyServerTypes(new[]
                    {
                        new OpenBisTypeInfoDto { Code = "SUBSTANCE", Description = "Substance" },
                        new OpenBisTypeInfoDto { Code = "CRYSTAL", Description = "Crystal" }
                    });
                    ShowOffscreen(saveForm);

                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewSmallBox").Image, "SmallBox preview image should be populated");
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewMagnet").Image, "Magnet preview image should be populated");
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewFlask").Image, "Flask preview image should be populated");

                    FindControl<Button>(saveForm, "btnPreviewGeneric").PerformClick();
                    AssertNotNull(FindControl<PictureBox>(saveForm, "picPreviewMagnet").Image, "Generic Magnet preview image should be populated");

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

                    FindControl<ComboBox>(saveForm, "cmbSmallBoxPrinter").SelectedItem = "Printer X";
                    FindControl<ComboBox>(saveForm, "cmbMagnetPrinter").SelectedItem = "Printer Y";
                    FindControl<ComboBox>(saveForm, "cmbFlaskPrinter").SelectedItem = "Printer Z";

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
                AssertEqual("Printer X", savedPrinters!.SmallBoxPrinter, "Updated SmallBoxPrinter mismatch");
                AssertEqual("Printer Y", savedPrinters.MagnetPrinter, "Updated MagnetPrinter mismatch");
                AssertEqual("Printer Z", savedPrinters.FlaskPrinter, "Updated FlaskPrinter mismatch");
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

                using var form = new SetupSettingsForm(
                    configService,
                    offlineClient,
                    renderer,
                    SetupDialogMode.Settings,
                    installedPrintersProvider: () => new[] { "Printer A" });

                var txtUrl = FindControl<TextBox>(form, "txtInstanceUrl");
                var txtToken = FindControl<TextBox>(form, "txtToken");
                AssertFalse(txtUrl.ReadOnly, "Instance URL textbox must start writable");
                AssertFalse(txtToken.ReadOnly, "Token textbox must start writable");

                txtUrl.Text = "https://openbis.example.org";
                txtToken.Text = "tok-123";
                form.ApplyServerTypes(new[]
                {
                    new OpenBisTypeInfoDto { Code = "SUBSTANCE", Description = "Substance" }
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
                Form1.CredentialsChangedSinceRequest("https://openbis.example.org", "tok-1", null),
                "Null currentConfig must return false");

            AssertFalse(
                Form1.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://other.example.org", Token = "" }),
                "Empty token in currentConfig must return false");

            AssertFalse(
                Form1.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://other.example.org", Token = "   " }),
                "Whitespace token in currentConfig must return false");

            AssertFalse(
                Form1.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok-1" }),
                "Matching URL and token must return false");

            AssertTrue(
                Form1.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://openbis.example.org", Token = "tok-2" }),
                "Updated token must return true");

            AssertTrue(
                Form1.CredentialsChangedSinceRequest(
                    "https://openbis.example.org",
                    "tok-1",
                    new AppConfig { OpenBisUrl = "https://updated.example.org", Token = "tok-1" }),
                "Updated URL with non-empty token must return true");

            AssertTrue(
                Form1.CredentialsChangedSinceRequest(
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
                    Form1.ResolveTemplateProfile("CRYSTAL", configuredTypes, null),
                    "Mapped non-SUBSTANCE type should resolve to Molecule");

                // Lookup must be case-insensitive and trim whitespace.
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    Form1.ResolveTemplateProfile("  crystal  ", configuredTypes, null),
                    "ResolveTemplateProfile should be case-insensitive and trim whitespace");

                // Explicit configured mapping overrides legacy SUBSTANCE assumption.
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    Form1.ResolveTemplateProfile("SUBSTANCE", configuredTypes, null),
                    "Configured Generic profile for SUBSTANCE must override legacy hardcoded assumption");

                // Fallback via sampleDetailsType when selectedTypeCode is empty, null, or unconfigured.
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    Form1.ResolveTemplateProfile("", configuredTypes, "CUSTOM_2D"),
                    "Empty selectedTypeCode should fall back to sampleDetailsType");
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    Form1.ResolveTemplateProfile(null, configuredTypes, "custom_2d"),
                    "Null selectedTypeCode should fall back to case-insensitive sampleDetailsType");
                AssertEqual(
                    LabelTemplateProfile.Molecule,
                    Form1.ResolveTemplateProfile("UNCONFIGURED", configuredTypes, "CRYSTAL"),
                    "Unconfigured selectedTypeCode should fall back to sampleDetailsType");

                // Unmapped types default to Generic regardless of hardcoded SUBSTANCE name.
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    Form1.ResolveTemplateProfile("UNCONFIGURED", configuredTypes, "ALSO_UNCONFIGURED"),
                    "Unmapped type should resolve to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    Form1.ResolveTemplateProfile(null, configuredTypes, null),
                    "Null inputs should resolve to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    Form1.ResolveTemplateProfile("SUBSTANCE", Array.Empty<ConfiguredOpenBisType>(), "SUBSTANCE"),
                    "Empty configured types should default to Generic");
                AssertEqual(
                    LabelTemplateProfile.Generic,
                    Form1.ResolveTemplateProfile("SUBSTANCE", null, "SUBSTANCE"),
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
            ComboBox cmbObjects = FindControl<ComboBox>(form, "openbisObjectsComboBox");
            GroupBox grpBox = FindControl<GroupBox>(form, "smallBoxLabelGroupBox");
            GroupBox grpMagnet = FindControl<GroupBox>(form, "magnetLabelGroupBox");
            GroupBox grpFlask = FindControl<GroupBox>(form, "flaskLabelGroupBox");

            AssertEqual("Settings...", btnSettings.Text, "Settings button text mismatch");
            AssertTrue(btnSettings.Enabled, "Settings button must be enabled");

            Rectangle clientBounds = new Rectangle(Point.Empty, form.ClientSize);
            AssertTrue(clientBounds.Contains(btnLoad.Bounds), $"Load button outside ClientSize: {btnLoad.Bounds}");
            AssertTrue(clientBounds.Contains(btnPrint.Bounds), $"Print button outside ClientSize: {btnPrint.Bounds}");
            AssertTrue(clientBounds.Contains(btnSettings.Bounds), $"Settings button outside ClientSize: {btnSettings.Bounds}");
            AssertTrue(clientBounds.Contains(lblStatus.Bounds), $"Status notice label outside ClientSize: {lblStatus.Bounds}");

            AssertFalse(btnLoad.Bounds.IntersectsWith(btnPrint.Bounds), "Load and Print buttons must not overlap");
            AssertFalse(btnLoad.Bounds.IntersectsWith(btnSettings.Bounds), "Load and Settings buttons must not overlap");
            AssertFalse(btnPrint.Bounds.IntersectsWith(btnSettings.Bounds), "Print and Settings buttons must not overlap");
            AssertFalse(lblStatus.Bounds.IntersectsWith(cmbObjects.Bounds), "Status notice must not overlap objects dropdown");
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
                    SmallBoxPrinter = "Printer A",
                    MagnetPrinter = "Printer B",
                    FlaskPrinter = "Printer C"
                });

                var offlineClient = new OpenBisClient(tempRoot, "nonexistent-python-binary-xyz");
                var renderer = new LabelPreviewRenderer(configService);

                // 1. Verify safe startup with zero installed printers and cached types shown alongside non-fatal notice.
                using (var zeroPrinterForm = new Form1(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => Array.Empty<string>()))
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
                    AssertEqual("Printer A", cmbBox.SelectedItem?.ToString(), "Zero-printer fallback should preserve SmallBoxPrinter");
                    AssertEqual("Printer B", cmbMagnet.SelectedItem?.ToString(), "Zero-printer fallback should preserve MagnetPrinter");
                    AssertEqual("Printer C", cmbFlask.SelectedItem?.ToString(), "Zero-printer fallback should preserve FlaskPrinter");
                }

                // 2. Verify initial mismatch does not overwrite disk during startup population, and manual selection immediately persists.
                configService.SavePrinterConfig(new PrinterConfig
                {
                    SmallBoxPrinter = "Missing Printer",
                    MagnetPrinter = "Printer B",
                    FlaskPrinter = "Printer C"
                });

                using (var form = new Form1(
                    configService,
                    offlineClient,
                    renderer,
                    installedPrintersProvider: () => new[] { "Printer A", "Printer B", "Printer C" }))
                {
                    ShowOffscreen(form);

                    var cmbBox = FindControl<ComboBox>(form, "smallBoxPrinterComboBox");
                    var cmbMagnet = FindControl<ComboBox>(form, "magnetPrinterComboBox");
                    var cmbFlask = FindControl<ComboBox>(form, "flaskPrinterComboBox");

                    AssertEqual("Printer A", cmbBox.SelectedItem?.ToString(), "Initial SmallBoxPrinter fallback selection mismatch");
                    AssertEqual("Printer B", cmbMagnet.SelectedItem?.ToString(), "Initial MagnetPrinter selection mismatch");
                    AssertEqual("Printer C", cmbFlask.SelectedItem?.ToString(), "Initial FlaskPrinter selection mismatch");
                    AssertEqual("Missing Printer", configService.LoadPrinterConfig()!.SmallBoxPrinter,
                        "Startup printer fallback must not overwrite config/printers_config.json before manual selection");

                    cmbBox.SelectedIndex = 1;    // Printer B
                    cmbMagnet.SelectedIndex = 2; // Printer C
                    cmbFlask.SelectedIndex = 0;  // Printer A
                }

                PrinterConfig? persisted = configService.LoadPrinterConfig();
                AssertNotNull(persisted, "PrinterConfig should exist after auto-save");
                AssertEqual("Printer B", persisted!.SmallBoxPrinter, "Auto-saved SmallBoxPrinter mismatch");
                AssertEqual("Printer C", persisted.MagnetPrinter, "Auto-saved MagnetPrinter mismatch");
                AssertEqual("Printer A", persisted.FlaskPrinter, "Auto-saved FlaskPrinter mismatch");
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
