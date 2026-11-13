using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio.Services
{
    /// <summary>
    /// Manages configuration files under config/, template path resolution under templates/,
    /// wizard readiness checks, and printer matching.
    /// </summary>
    public class AppConfigService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly string _baseDirectory;

        /// <summary>
        /// Initializes the service rooted at <paramref name="baseDirectory"/> or the application base directory.
        /// </summary>
        public AppConfigService(string? baseDirectory = null)
        {
            _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetFullPath(baseDirectory);

            ConfigDirectoryPath = Path.Combine(_baseDirectory, "config");
            TemplatesDirectoryPath = Path.Combine(_baseDirectory, "templates");
            OpenBisConfigPath = Path.Combine(ConfigDirectoryPath, "openbis_config.json");
            OpenBisTypesPath = Path.Combine(ConfigDirectoryPath, "openbis_types.json");
            PrintersConfigPath = Path.Combine(ConfigDirectoryPath, "printers_config.json");
        }

        public string ConfigDirectoryPath { get; }
        public string TemplatesDirectoryPath { get; }
        public string OpenBisConfigPath { get; }
        public string OpenBisTypesPath { get; }
        public string PrintersConfigPath { get; }

        /// <summary>
        /// Returns true when config/openbis_config.json exists and contains non-empty URL and token.
        /// </summary>
        public bool HasValidOpenBisConfig()
        {
            AppConfig? config = LoadAppConfig();
            return config != null
                   && !string.IsNullOrWhiteSpace(config.OpenBisUrl)
                   && !string.IsNullOrWhiteSpace(config.Token);
        }

        /// <summary>
        /// Returns true when config/openbis_types.json contains at least one non-empty object type code.
        /// </summary>
        public bool HasValidConfiguredTypes()
        {
            List<ConfiguredOpenBisType> types = LoadConfiguredTypes();
            return types.Any(t => !string.IsNullOrWhiteSpace(t.Code));
        }

        /// <summary>
        /// Returns true when config/printers_config.json exists and deserializes cleanly.
        /// </summary>
        public bool HasPrinterConfig()
        {
            return LoadPrinterConfig() != null;
        }

        /// <summary>
        /// Determines the earliest incomplete setup wizard step based on config/ files.
        /// </summary>
        public SetupWizardStep GetFirstMissingWizardStep()
        {
            if (!HasValidOpenBisConfig())
            {
                return SetupWizardStep.Connection;
            }

            if (!HasValidConfiguredTypes())
            {
                return SetupWizardStep.ObjectTypes;
            }

            if (!HasPrinterConfig())
            {
                return SetupWizardStep.Printers;
            }

            return SetupWizardStep.None;
        }

        /// <summary>
        /// Loads config/openbis_config.json, returning null if missing, empty, or corrupt.
        /// </summary>
        public AppConfig? LoadAppConfig()
        {
            AppConfig? config = ReadJsonObjectSafe<AppConfig>(OpenBisConfigPath);
            if (config == null)
            {
                return null;
            }

            config.OpenBisUrl ??= "";
            config.Token ??= "";
            return config;
        }

        /// <summary>
        /// Atomically persists openBIS connection settings to config/openbis_config.json.
        /// </summary>
        public void SaveAppConfig(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var normalized = new AppConfig
            {
                OpenBisUrl = config.OpenBisUrl ?? "",
                Token = config.Token ?? ""
            };

            WriteJsonAtomically(OpenBisConfigPath, normalized);
        }

        /// <summary>
        /// Loads configured object types from config/openbis_types.json, supporting both object arrays
        /// and legacy string arrays while ignoring corrupt files safely.
        /// </summary>
        public List<ConfiguredOpenBisType> LoadConfiguredTypes()
        {
            JToken? token = ReadJsonTokenSafe(OpenBisTypesPath);
            if (token is not JArray array)
            {
                return new List<ConfiguredOpenBisType>();
            }

            var result = new List<ConfiguredOpenBisType>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (JToken item in array)
                {
                    ConfiguredOpenBisType? normalized;
                    if (item.Type == JTokenType.String)
                    {
                        string rawCode = item.Value<string>() ?? "";
                        LabelTemplateProfile defaultProfile = rawCode.Trim().Equals("SUBSTANCE", StringComparison.OrdinalIgnoreCase)
                            ? LabelTemplateProfile.Molecule
                            : LabelTemplateProfile.Generic;
                        normalized = NormalizeConfiguredType(rawCode, null, defaultProfile);
                    }
                    else if (item.Type == JTokenType.Object)
                    {
                        ConfiguredOpenBisType? parsed = item.ToObject<ConfiguredOpenBisType>();
                        if (parsed == null || !Enum.IsDefined(typeof(LabelTemplateProfile), parsed.TemplateProfile))
                        {
                            return new List<ConfiguredOpenBisType>();
                        }

                        normalized = NormalizeConfiguredType(parsed.Code, parsed.DisplayName, parsed.TemplateProfile);
                    }
                    else
                    {
                        return new List<ConfiguredOpenBisType>();
                    }

                    if (normalized != null && seenCodes.Add(normalized.Code))
                    {
                        result.Add(normalized);
                    }
                }
            }
            catch
            {
                return new List<ConfiguredOpenBisType>();
            }

            return result;
        }

        /// <summary>
        /// Atomically persists enabled openBIS object types and template profiles to config/openbis_types.json.
        /// </summary>
        public void SaveConfiguredTypes(IEnumerable<ConfiguredOpenBisType> types)
        {
            ArgumentNullException.ThrowIfNull(types);

            var normalized = new List<ConfiguredOpenBisType>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ConfiguredOpenBisType item in types)
            {
                if (item == null)
                {
                    continue;
                }

                LabelTemplateProfile profile = Enum.IsDefined(typeof(LabelTemplateProfile), item.TemplateProfile)
                    ? item.TemplateProfile
                    : LabelTemplateProfile.Generic;

                ConfiguredOpenBisType? entry = NormalizeConfiguredType(item.Code, item.DisplayName, profile);
                if (entry != null && seenCodes.Add(entry.Code))
                {
                    normalized.Add(entry);
                }
            }

            WriteJsonAtomically(OpenBisTypesPath, normalized);
        }

        /// <summary>
        /// Loads config/printers_config.json, returning null if missing, empty, or corrupt.
        /// </summary>
        public PrinterConfig? LoadPrinterConfig()
        {
            PrinterConfig? config = ReadJsonObjectSafe<PrinterConfig>(PrintersConfigPath);
            if (config == null)
            {
                return null;
            }

            config.SmallBoxPrinter ??= "";
            config.MagnetPrinter ??= "";
            config.FlaskPrinter ??= "";
            return config;
        }

        /// <summary>
        /// Atomically persists printer mappings to config/printers_config.json.
        /// </summary>
        public void SavePrinterConfig(PrinterConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var normalized = new PrinterConfig
            {
                SmallBoxPrinter = config.SmallBoxPrinter ?? "",
                MagnetPrinter = config.MagnetPrinter ?? "",
                FlaskPrinter = config.FlaskPrinter ?? ""
            };

            WriteJsonAtomically(PrintersConfigPath, normalized);
        }

        /// <summary>
        /// Creates a default Brother PT-P700 printer routing configuration.
        /// </summary>
        public static PrinterConfig CreateDefaultPrinterConfig()
        {
            return new PrinterConfig();
        }

        /// <summary>
        /// Resolves the .lbx template file path under templates/ for a given label slot and profile.
        /// </summary>
        public string GetTemplateFilePath(LabelTemplateType slot, LabelTemplateProfile profile)
        {
            string fileName = (slot, profile) switch
            {
                (LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Molecule) => "template_molecule_box_12mm.lbx",
                (LabelTemplateType.SmallBox12mm, LabelTemplateProfile.Generic) => "template_small_box_12mm.lbx",
                (LabelTemplateType.Magnet12mm, LabelTemplateProfile.Molecule) => "template_molecule_magnet_12mm.lbx",
                (LabelTemplateType.Magnet12mm, LabelTemplateProfile.Generic) => "template_magnet_12mm.lbx",
                (LabelTemplateType.Flask24mm, LabelTemplateProfile.Molecule) => "template_molecule_flask_24mm.lbx",
                (LabelTemplateType.Flask24mm, LabelTemplateProfile.Generic) => "template_flask_24mm.lbx",
                _ => throw new ArgumentOutOfRangeException(nameof(slot), $"Unsupported template mapping: {slot}, {profile}")
            };

            return Path.Combine(TemplatesDirectoryPath, fileName);
        }

        /// <summary>
        /// Finds the best matching printer index (exact match, substring match, or fallback 0; -1 when empty).
        /// </summary>
        public static int FindBestPrinterIndex(IReadOnlyList<string> availablePrinters, string preferredName)
        {
            if (availablePrinters == null || availablePrinters.Count == 0)
            {
                return -1;
            }

            if (string.IsNullOrWhiteSpace(preferredName))
            {
                return 0;
            }

            for (int i = 0; i < availablePrinters.Count; i++)
            {
                if (string.Equals(availablePrinters[i] ?? "", preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (int i = 0; i < availablePrinters.Count; i++)
            {
                if ((availablePrinters[i] ?? "").Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Converts an underscore/hyphen-delimited openBIS type code into a title-cased fallback label.
        /// </summary>
        public static string FormatFriendlyName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "";
            }

            string[] parts = code.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                }
            }

            return string.Join(" ", parts);
        }

        private static ConfiguredOpenBisType? NormalizeConfiguredType(string? rawCode, string? rawDisplayName, LabelTemplateProfile profile)
        {
            string code = (rawCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return new ConfiguredOpenBisType
            {
                Code = code,
                DisplayName = string.IsNullOrWhiteSpace(rawDisplayName)
                    ? FormatFriendlyName(code)
                    : rawDisplayName.Trim(),
                TemplateProfile = profile
            };
        }

        private static T? ReadJsonObjectSafe<T>(string path) where T : class
        {
            JToken? token = ReadJsonTokenSafe(path);
            if (token == null || token.Type != JTokenType.Object)
            {
                return null;
            }

            try
            {
                return token.ToObject<T>();
            }
            catch
            {
                return null;
            }
        }

        private static JToken? ReadJsonTokenSafe(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JToken.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private void WriteJsonAtomically(string targetPath, object payload)
        {
            Directory.CreateDirectory(ConfigDirectoryPath);

            string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            string tempPath = Path.Combine(ConfigDirectoryPath, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(tempPath, json, Utf8NoBom);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore cleanup failure for transient temp file.
                    }
                }
            }
        }
    }
}
