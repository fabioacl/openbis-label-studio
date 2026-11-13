using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using OpenBISLabelStudio.Models;
using OpenBISLabelStudio.Services;
using ModelTypes = OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio.Tests
{
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
                (nameof(RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing), RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing)
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
