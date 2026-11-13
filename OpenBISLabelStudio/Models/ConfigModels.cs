using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace OpenBISLabelStudio.Models
{
    /// <summary>
    /// Selects which label layout family and field population behavior an openBIS object type uses.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LabelTemplateProfile
    {
        Generic,
        Molecule
    }

    /// <summary>
    /// Identifies one of the three physical label template slots in the main UI.
    /// </summary>
    public enum LabelTemplateType
    {
        SmallBox12mm,
        Magnet12mm,
        Flask24mm
    }

    /// <summary>
    /// Identifies the first-run setup step required to complete local configuration.
    /// </summary>
    public enum SetupWizardStep
    {
        None,
        Connection,
        ObjectTypes,
        Printers
    }

    /// <summary>
    /// Stores openBIS connection credentials persisted in config/openbis_config.json.
    /// </summary>
    public class AppConfig
    {
        [JsonProperty("openbisUrl")]
        public string OpenBisUrl { get; set; } = "";

        [JsonProperty("token")]
        public string Token { get; set; } = "";
    }

    /// <summary>
    /// Maps an enabled openBIS object type to its cached display name and label template profile.
    /// </summary>
    public class ConfiguredOpenBisType
    {
        [JsonProperty("code")]
        public string Code { get; set; } = "";

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonProperty("templateProfile")]
        [JsonConverter(typeof(StringEnumConverter))]
        public LabelTemplateProfile TemplateProfile { get; set; } = LabelTemplateProfile.Generic;
    }

    /// <summary>
    /// Stores Windows printer queue mappings for the three label slots.
    /// </summary>
    public class PrinterConfig
    {
        [JsonProperty("smallBoxPrinter")]
        public string SmallBoxPrinter { get; set; } = "Brother PT-P700";

        [JsonProperty("magnetPrinter")]
        public string MagnetPrinter { get; set; } = "Brother PT-P700 (Kopie 1)";

        [JsonProperty("flaskPrinter")]
        public string FlaskPrinter { get; set; } = "Brother PT-P700 (Kopie 2)";
    }

    /// <summary>
    /// Represents a selectable openBIS type entry in the main window dropdown.
    /// </summary>
    public class OpenBisTypeItem
    {
        public string Code { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public LabelTemplateProfile TemplateProfile { get; set; } = LabelTemplateProfile.Generic;

        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Result payload returned by openbis_fetch.py --types.
    /// </summary>
    public class OpenBisTypesResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("errorType")]
        public string ErrorType { get; set; } = "";

        [JsonProperty("types")]
        public List<OpenBisTypeInfoDto> Types { get; set; } = new List<OpenBisTypeInfoDto>();
    }

    /// <summary>
    /// Describes a single openBIS object type returned from the server.
    /// </summary>
    public class OpenBisTypeInfoDto
    {
        [JsonProperty("code")]
        public string Code { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Result payload returned by openbis_fetch.py for sample list queries.
    /// </summary>
    public class OpenBisListResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("errorType")]
        public string ErrorType { get; set; } = "";

        [JsonProperty("data")]
        public List<SampleDto> Data { get; set; } = new List<SampleDto>();
    }

    /// <summary>
    /// Summary item for an openBIS sample in the selection list.
    /// </summary>
    public class SampleDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("permId")]
        public string PermId { get; set; } = "";

        [JsonProperty("modificationDate")]
        public string ModificationDate { get; set; } = "";
    }

    /// <summary>
    /// Result payload returned by openbis_fetch.py --details.
    /// </summary>
    public class OpenBisDetailsResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("errorType")]
        public string ErrorType { get; set; } = "";

        [JsonProperty("details")]
        public SampleDetailsDto? Details { get; set; }
    }

    /// <summary>
    /// Detailed metadata and icon flags for a selected openBIS sample.
    /// </summary>
    public class SampleDetailsDto
    {
        [JsonProperty("permId")]
        public string PermId { get; set; } = "";

        [JsonProperty("sampleType")]
        public string SampleType { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("studiedBy")]
        public string StudiedBy { get; set; } = "";

        [JsonProperty("studiedByName")]
        public string StudiedByName { get; set; } = "";

        [JsonProperty("studiedByUsername")]
        public string StudiedByUsername { get; set; } = "";

        [JsonProperty("storageLocation")]
        public string StorageLocation { get; set; } = "";

        [JsonProperty("supplierOwnName")]
        public string SupplierOwnName { get; set; } = "";

        [JsonProperty("evaporationTemperature")]
        public string EvaporationTemperature { get; set; } = "";

        [JsonProperty("specialStorageConditions")]
        public List<string> SpecialStorageConditions { get; set; } = new List<string>();

        [JsonProperty("iconFlags")]
        public IconFlagsDto IconFlags { get; set; } = new IconFlagsDto();

        [JsonProperty("elnPreviewPath")]
        public string ElnPreviewPath { get; set; } = "";

        [JsonProperty("properties")]
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Hazard and storage icon visibility flags for molecule label rendering.
    /// </summary>
    public class IconFlagsDto
    {
        [JsonProperty("poisonous")]
        public bool Poisonous { get; set; }

        [JsonProperty("flammable")]
        public bool Flammable { get; set; }

        [JsonProperty("dark")]
        public bool Dark { get; set; }

        [JsonProperty("dry")]
        public bool Dry { get; set; }

        [JsonProperty("freezer")]
        public bool Freezer { get; set; }

        [JsonProperty("fridge")]
        public bool Fridge { get; set; }

        [JsonProperty("noOxygen")]
        public bool NoOxygen { get; set; }

        [JsonProperty("tightlyClosed")]
        public bool TightlyClosed { get; set; }
    }
}
