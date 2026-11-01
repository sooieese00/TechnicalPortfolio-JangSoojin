using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public static class VcGnnJobRegistryInjector
    {
        private const string ControllerName = "Central Controller";
        private static readonly string[] ShelfNames =
        {
            "Shelf #1",
            "Shelf #2",
            "Shelf #3",
            "Shelf #4"
        };
        public static void Inject(ISimWorld simWorld, GnnJobRegistry registry, string instanceJson = null)
        {
            if (simWorld == null)
                throw new ArgumentNullException(nameof(simWorld));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            ISimComponent controller = simWorld.FindComponent(ControllerName);
            if (controller == null)
                throw new InvalidOperationException("VC component was not found: " + ControllerName);
            string jobMasterJson = registry.ToJson();
            SetString(controller, "DispatchRule::GnnJobMasterJSON", jobMasterJson);
            SetString(controller, "DispatchRule::GnnResourceMapJSON", BuildResourceMap().ToString(Formatting.None));
            SetString(controller, "DispatchRule::GnnJobRegistryJSON", "{}");
            SetString(controller, "DispatchRule::GnnRegistryVersion", registry.Version ?? "");
            if (!string.IsNullOrWhiteSpace(instanceJson))
                SetString(controller, "DispatchRule::EmbeddedGnnInstanceJSON", instanceJson);
            foreach (string shelfName in ShelfNames)
            {
                ISimComponent shelf = simWorld.FindComponent(shelfName);
                if (shelf == null)
                    throw new InvalidOperationException("VC component was not found: " + shelfName);
                SetString(shelf, "DispatchRule::GnnJobMasterJSON", jobMasterJson);
                SetString(shelf, "DispatchRule::GnnRegistryVersion", registry.Version ?? "");
            }
        }

        private static JObject BuildResourceMap()
        {
            return new JObject
            {
                ["AMR_A_001"] = "amr0",
                ["AMR_B_001"] = "amr1",
                ["CNC_A_001"] = "ws0",
                ["CNC_B_001"] = "ws1",
                ["CNC_A_002"] = "ws2",
                ["CNC_D_001"] = "ws3",
                ["CNC_E_001"] = "ws3",
                ["ASRS"] = "crane"
            };
        }

        private static void SetString(IPropertyContainer owner, string name, string value)
        {
            IProperty property = owner.GetProperty(name);
            if (property == null)
            {
                property = owner.CreateProperty(typeof(string), PropertyConstraintType.NotSpecified, name);
            }

            property.Value = value ?? "";
        }
    }
}
