using System.IO;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public class Interface
    {
        public JObject readJson(string path)
        {
            JObject result = JObject.Parse(File.ReadAllText(path));
            foreach (JToken op in result.SelectTokens("MATERIAL_HANDLING_INFO[*].MATERIAL_HANDLING_OPERATION_INFO[*]"))
            {
                string canonical = (string)op["MATERIAL_HANDLING_OPERATION_INFO_ID"];
                string alias = (string)op["OPERATION_INFO_ID"];
                if (!string.IsNullOrEmpty(canonical) && !string.IsNullOrEmpty(alias) && canonical != alias)
                    throw new InvalidDataException("Conflicting material handling operation IDs.");
                if (string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(canonical))
                    op["OPERATION_INFO_ID"] = canonical;
            }

            return result;
        }
    }
}
