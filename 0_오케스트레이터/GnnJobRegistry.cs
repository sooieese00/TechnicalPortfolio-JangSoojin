using System.Collections.Generic;
using Newtonsoft.Json;

namespace DT_KETI
{
    public sealed class GnnJobRegistry
    {
        [JsonProperty("schema")]
        public string Schema { get; set; } = "p4r-gnn-job-registry/v1";

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("jobs")]
        public List<GnnJobRegistryEntry> Jobs { get; set; } = new List<GnnJobRegistryEntry>();

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }
    }

    public sealed class GnnJobRegistryEntry
    {
        [JsonProperty("job")]
        public int Job { get; set; }

        [JsonProperty("sourceId")]
        public string SourceId { get; set; }

        [JsonProperty("sourceType")]
        public string SourceType { get; set; }

        [JsonProperty("material")]
        public string Material { get; set; }

        [JsonProperty("component")]
        public string Component { get; set; }

        [JsonProperty("slot", NullValueHandling = NullValueHandling.Ignore)]
        public int? Slot { get; set; }

        [JsonProperty("row", NullValueHandling = NullValueHandling.Ignore)]
        public int? Row { get; set; }

        [JsonProperty("column", NullValueHandling = NullValueHandling.Ignore)]
        public int? Column { get; set; }

        [JsonProperty("tier", NullValueHandling = NullValueHandling.Ignore)]
        public int? Tier { get; set; }

        [JsonProperty("due")]
        public double Due { get; set; }
    }
}
