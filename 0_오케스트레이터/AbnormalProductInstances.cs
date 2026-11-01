using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public static class AbnormalProductInstances
    {
        private const string QueueProperty = "Abnormal::InstanceQueueJSON";
        private const string ScriptName = "AbnormalInstanceQueue";
        private const string Staging = "AbnormalInstanceStaging";
        public static JObject BuildPlan(JObject data, GnnJobRegistry registry)
        {
            var queues = new JObject();
            var assigned = new HashSet<int>();
            foreach (var shelf in new RuleJsonPreprocessing().GetShelfSnapshots(data))
            {
                int last = Array.FindLastIndex(shelf.Inventory, IsPhysical);
                var rows = new JArray();
                for (int index = 0; index <= last; index++)
                {
                    int row = index / RuleJsonPreprocessing.ColumnCount;
                    int column = index % RuleJsonPreprocessing.ColumnCount;
                    string material = IsPhysical(shelf.Inventory[index]) ? shelf.Inventory[index] : "dummy";
                    var jobs = registry.Jobs.Where(j => j.Component == "ASRS" && j.Row == row && j.Column == column && j.Tier == shelf.TierIndex).ToArray();
                    if (jobs.Length > 1)
                        throw new InvalidDataException("Multiple jobs at one ASRS position.");
                    var job = jobs.SingleOrDefault();
                    if (job != null && job.Material != material)
                        throw new InvalidDataException("ASRS material/identity mismatch: " + job.SourceId);
                    string locationId = "ASRS_R" + row + "C" + column + "T" + shelf.TierIndex;
                    string source = job == null ? locationId : job.SourceId;
                    var item = Entry(source, material, job, "ASRS");
                    item["row"] = row;
                    item["column"] = column;
                    item["tier"] = shelf.TierIndex;
                    item["placeholder"] = material == "dummy";
                    rows.Add(item);
                    if (job != null)
                        assigned.Add(job.Job);
                }

                queues["Feeder Process #" + (shelf.TierIndex + 1)] = rows;
            }

            foreach (var job in registry.Jobs.Where(j => !assigned.Contains(j.Job)))
            {
                string feeder;
                if (job.SourceType == "MACHINE_WIP")
                    feeder = job.Component;
                else if (job.SourceType == "AMR_WIP")
                {
                    var wips = new JsonPreprocessing().GetAMRWIPProperties(data, job.Component);
                    var wip = wips.Single(item => item[3] == job.SourceId);
                    string prefix = job.Component.Substring(0, job.Component.LastIndexOf('_'));
                    feeder = prefix + "_WIP_" + wip[1];
                    if (wip[1] == "AP")
                    {
                        int index = wips.Where(item => item[1] == "AP").ToList().IndexOf(wip);
                        if (index >= 2)
                            throw new InvalidDataException("AMR AP WIP exceeds available restore nodes.");
                        feeder += " #" + (index + 1);
                    }
                }
                else if (job.SourceType == "BUFFER_WIP")
                {
                    var parts = job.Component.Split('_');
                    if (parts.Length != 4 || !job.Slot.HasValue || job.Slot.Value < 0)
                        throw new InvalidDataException("Invalid buffer slot: " + job.SourceId);
                    feeder = "BT_" + parts[1] + "_" + parts[2] + "_WIP";
                    if (parts[2] == "IN")
                    {
                        if (job.Slot.Value >= 3)
                            throw new InvalidDataException("Buffer slot exceeds model capacity.");
                        feeder += " #" + (job.Slot.Value + 1);
                    }
                }
                else
                    throw new InvalidDataException("Physical instance restoration is not defined for " + job.SourceType + ": " + job.SourceId + ". No anonymous product will be substituted.");
                var items = queues[feeder] as JArray;
                if (items != null && job.SourceType == "BUFFER_WIP" && job.Component.Contains("_IN_"))
                    throw new InvalidDataException("Multiple products occupy one input buffer slot: " + feeder);
                if (items == null)
                    queues[feeder] = items = new JArray();
                var entry = Entry(job.SourceId, job.Material, job, job.Component);
                if (job.Slot.HasValue)
                    entry["slot"] = job.Slot.Value;
                items.Add(entry);
                assigned.Add(job.Job);
            }

            var all = queues.Properties().SelectMany(p => ((JArray)p.Value).OfType<JObject>()).ToArray();
            if (all.Select(r => (string)r["instanceId"]).Distinct().Count() != all.Length)
                throw new InvalidDataException("Duplicate abnormal product instance ID.");
            return queues;
        }

        private static bool IsPhysical(string material)
        {
            return !string.IsNullOrWhiteSpace(material) && (material.StartsWith("MT_", StringComparison.Ordinal) || material.StartsWith("Complete_", StringComparison.Ordinal));
        }

        private static JObject Entry(string source, string material, GnnJobRegistryEntry job, string component)
        {
            string id = "P4R_" + BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(source)).Replace("-", "");
            return new JObject
            {
                ["instanceId"] = id,
                ["sourceId"] = source,
                ["material"] = material,
                ["job"] = job == null ? -1 : job.Job,
                ["due"] = job == null ? 1.0e30 : job.Due,
                ["logicalComponent"] = component
            };
        }

        public static void Install(JObject data, GnnJobRegistry registry, ISimWorld world, IApplication app, string reportDirectory)
        {
            JObject plan = BuildPlan(data, registry);
            var central = world.FindComponent("Central Controller");
            var dispatch = central.Behaviors.OfType<IPythonInstance>().Single(s => s.Script.Contains("def choose_and_execute(state,event_name):"));
            Set(central, "Abnormal::InstanceOwners", JsonConvert.SerializeObject(plan.Properties().Select(p => p.Name).ToArray()));
            Set(central, "Abnormal::SnapshotTime", RuleModelSynchronization.GetSnapshotDateTime(data));
            Set(central, "Abnormal::InitializationStatus", "WAITING");
            dispatch.Script += "\n" + ReadRuntime("AbnormalDispatchPolicy");
            const string unloadMarker = "# ABNORMAL STRICT UNLOAD FIRST";
            if (!dispatch.Script.Contains(unloadMarker))
            {
                const string anchor = "  candidates=generate_candidates(state)";
                if (!dispatch.Script.Contains(anchor))
                    throw new InvalidDataException("Central dispatch candidate hook missing.");
                dispatch.Script = dispatch.Script.Replace(anchor, "  initialization_ready = abnormal_initialization_ready()\n" + "  if initialization_ready:\n" + "      comp.getProperty('Abnormal::InitializationStatus').Value = 'READY'\n" + anchor + "\n" + "  if not initialization_ready:\n" + "      candidates = [c for c in candidates if c.get('type') in ('A3_UNLOADING','A6_STORAGE')]\n" + "  # ABNORMAL STRICT UNLOAD FIRST\n" + "  candidates = abnormal_priority_candidates(state,candidates)\n" + "  if not candidates:\n" + "      candidate_count_p.Value = 0\n" + "      candidate_json_p.Value = '[]'\n" + "      last_action_p.Value = 'WAITING FOR UNLOAD OR ASRS'\n" + "      return\n");
                const string fallback = "  print('[CENTRAL VCMX GNN FALLBACK]";
                int fallbackIndex = dispatch.Script.IndexOf(fallback, StringComparison.Ordinal);
                if (fallbackIndex < 0)
                    throw new InvalidDataException("GNN storage fallback hook missing.");
                dispatch.Script = dispatch.Script.Insert(fallbackIndex, "  if candidates and all(c.get('type')=='A6_STORAGE' for c in candidates):\n" + "      CURRENT_ACTION = candidates[0]\n" + "      print('[ASRS AUTO STORAGE] product={} time={}'.format(CURRENT_ACTION.get('product',''),sim.SimTime))\n" + "      execute(CURRENT_ACTION)\n" + "      return\n");
            }

            foreach (var cnc in new RuleJsonPreprocessing().GetCncSnapshots(data, 1, RuleModelSynchronization.GetSnapshotDateTime(data)))
            {
                var machine = world.FindComponent(cnc.MachineId);
                Set(machine, "Abnormal::RepairSeconds", cnc.RemainingRepairTime);
                Set(machine, "Abnormal::FailureStatus", cnc.RemainingRepairTime > 0 ? "DOWN" : "RUNNING");
                var display = machine.FindBehavior("AbnormalFailureDisplay") as IPythonScript ?? machine.RootNode.CreateBehavior<IPythonScript>("AbnormalFailureDisplay");
                display.Script = ReadRuntime("AbnormalFailureDisplay");
            }

            var controller = world.WorldRoot.Behaviors.OfType<IProcessController>().First();
            foreach (var type in controller.ProductTypeManager.ProductTypes)
            {
                foreach (string name in new[]
                {
                    "P4RInstanceId",
                    "P4RSourceId",
                    "ProductID",
                    "LotID",
                    "MaterialId",
                    "P4RInitialComponent"
                }

                )
                {
                    Ensure(type.ProductProperties, name, "");
                    Ensure(type.ComponentProperties, name, "");
                }

                foreach (string name in new[]
                {
                    "GnnJobId",
                    "P4RInitialRow",
                    "P4RInitialColumn",
                    "P4RInitialTier",
                    "P4RInitialSlot"
                }

                )
                {
                    Ensure(type.ProductProperties, name, -1);
                    Ensure(type.ComponentProperties, name, -1);
                }

                Ensure(type.ProductProperties, "DueDate", 1.0e30);
                Ensure(type.ComponentProperties, "DueDate", 1.0e30);
                Ensure(type.ProductProperties, "P4RInitialPlaced", false);
                Ensure(type.ComponentProperties, "P4RInitialPlaced", false);
            }

            string runtime;
            using (var stream = typeof(AbnormalProductInstances).Assembly.GetManifestResourceStream("DT_KETI.AbnormalInstanceQueue.py"))
            using (var reader = new StreamReader(stream))
                runtime = reader.ReadToEnd();
            for (int tier = 1; tier <= 4; tier++)
            {
                var shelf = world.FindComponent("Shelf #" + tier);
                var script = shelf.Behaviors.OfType<IPythonInstance>().Single(s => s.Script.Contains("def transport_node_product_arriving("));
                if (!script.Script.Contains("# ABNORMAL INSTANCE SLOT CHECK"))
                {
                    const string target = "  arriving_slot.product_time = sim.SimTime";
                    if (!script.Script.Contains(target))
                        throw new InvalidDataException("Shelf arrival hook missing.");
                    script.Script = script.Script.Replace(target, "  # ABNORMAL INSTANCE SLOT CHECK\n" + "  initial_id = product.Component.getProperty('P4RInstanceId')\n" + "  placed = product.Component.getProperty('P4RInitialPlaced')\n" + "  if initial_id and initial_id.Value and placed and not placed.Value:\n" + "    expected = tuple(int(product.Component.getProperty(n).Value) for n in ('P4RInitialRow','P4RInitialColumn','P4RInitialTier'))\n" + "    if expected[0] >= 0:\n" + "      actual = (stack_manager.stacks.index(arriving_stack), CENTRAL_COLUMN_TO_SLOT.index(arriving_slot.index), central_shelf_tier())\n" + "      if actual != expected:\n" + "        raise RuntimeError('Abnormal instance position mismatch: {} expected={} actual={}'.format(initial_id.Value,expected,actual))\n" + "      placed.Value = True\n" + target);
                }
            }

            foreach (string family in new[]
            {
                "A",
                "B",
                "C",
                "D"
            }

            )
                for (int slot = 1; slot <= 3; slot++)
                {
                    var node = world.FindComponent("BT_" + family + "_IN_WIP #" + slot);
                    if (node != null)
                        node.GetProperty("WorkInProcess").Value = false;
                }

            foreach (var property in plan.Properties())
            {
                var node = world.FindComponent(property.Name);
                if (node == null)
                    throw new InvalidDataException("Instance feeder missing: " + property.Name);
                var rows = (JArray)property.Value;
                var stage = node.FindBehavior(Staging) as IComponentContainer ?? node.RootNode.CreateBehavior<IComponentContainer>(Staging);
                stage.Capacity = Math.Max(1, rows.Count);
                stage.IsContentVisible = false;
                var executor = node.FindBehavior("ProcessExecutor") as IProcessExecutor;
                if (executor == null)
                    throw new InvalidDataException("Instance feeder has no executor: " + node.Name);
                var creator = node.FindBehavior("ProductCreator") as IProductCreator;
                if (creator != null)
                {
                    creator.UsePartPooling = false;
                    foreach (var batch in creator.BatchFeed.ProductBatches.ToArray())
                        creator.BatchFeed.DeleteBatch(batch);
                    creator.BatchFeed.IsLooping = false;
                    creator.FeedMode = ProductCreatorFeedMode.Batch;
                }
                else if (rows.Count > 0)
                {
                    var wip = node.GetProperty("WorkInProcess");
                    if (wip != null)
                        wip.Value = true;
                    var creates = new List<Tuple<IStatementScope, ICreateProductStatement>>();
                    foreach (var routine in executor.ActiveProgram.Routines)
                        FindCreates(routine, creates);
                    if (creates.Count != rows.Count)
                        throw new InvalidDataException("WIP queue/CreateProduct count mismatch: " + node.Name);
                    for (int i = 0; i < creates.Count; i++)
                    {
                        var scope = creates[i].Item1;
                        var old = creates[i].Item2;
                        int index = scope.Statements.IndexOf(old);
                        if (node.Name.StartsWith("CNC_", StringComparison.Ordinal))
                        {
                            var warmup = (IProcessIfStatement)executor.ActiveProgram.Routines[0].Statements[0];
                            var initial = warmup.ElseScope.AddStatement<IProcessIfStatement>(0);
                            initial.Name = "Abnormal: restore CNC WIP before repair";
                            initial.ConditionExpression = "WorkInProcess";
                            var restored = (ICreateProductStatement)initial.ThenScope.AddStatement(old, true);
                            restored.Name = "Abnormal: create WIP " + i;
                            var registerDelay = initial.ThenScope.AddStatement<IProcessDelayStatement>();
                            registerDelay.Name = "Abnormal: register instance identity";
                            registerDelay.DistributionExpression = "0.01";
                            rows[i]["creationStatement"] = restored.Name;
                            rows[i]["creationContainer"] = restored.Container.Name;
                            SetPosition((JObject)rows[i], node, old.ProductPositionFrame);
                            scope.DeleteStatement(old);
                            continue;
                        }

                        var incoming = scope.AddStatement<ITransportInStatement>(index);
                        incoming.Name = "Abnormal: receive prepared instance";
                        incoming.Source = TransportInType.FromContainer;
                        incoming.SetFilter(ProductFilterType.AcceptAll);
                        incoming.ProductVariableName = old.ProductVariableName;
                        incoming.ProductPositionFrame = old.ProductPositionFrame;
                        SetPosition((JObject)rows[i], node, old.ProductPositionFrame);
                        scope.DeleteStatement(old);
                    }
                }

                foreach (JObject row in rows)
                    if (row["worldPosition"] == null)
                        SetPosition(row, node, node.FindFeature("MainFrame") as IFrameFeature);
                Set(node, QueueProperty, rows.ToString(Formatting.None));
                Set(node, "Abnormal::PreparedCount", 0);
                Set(node, "Abnormal::ReleasedCount", 0);
                Set(node, "Abnormal::InstanceError", "");
                var script = node.FindBehavior(ScriptName) as IPythonScript ?? node.RootNode.CreateBehavior<IPythonScript>(ScriptName);
                script.Script = runtime;
                node.Rebuild();
            }

            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, "abnormal-instances.json"), plan.ToString(Formatting.Indented));
            string commandPath = Path.Combine(reportDirectory, "prepare-abnormal-instances.py");
            string names = JsonConvert.SerializeObject(plan.Properties().Select(p => p.Name).ToArray());
            File.WriteAllText(commandPath, "from vcCommand import *\nimport json\n" + "def prepare(arguments=None):\n  app=getApplication()\n  sim=getSimulation()\n" + "  for name in json.loads(" + JsonConvert.SerializeObject(names) + "):\n" + "    owner=app.findComponent(name)\n    namespace={}\n" + "    exec(compile(owner.findBehaviour('" + ScriptName + "').Script, '<abnormal queue>', 'exec'), namespace)\n" + "    namespace['prepare_instances'](owner,sim)\n  return 'OK'\n");
            string commandName = "DT_PrepareAbnormalInstances";
            app.LoadPythonCommand(commandName, commandPath);
            object result;
            if (!app.ExecutePythonCommandMethod(commandName, "prepare", new string[0], out result) || Convert.ToString(result) != "OK")
                throw new InvalidOperationException("VC abnormal physical instance preparation failed: " + Convert.ToString(result));
            foreach (var item in plan.Properties())
            {
                var node = world.FindComponent(item.Name);
                int expected = node.Name.StartsWith("CNC_", StringComparison.Ordinal) ? 0 : ((JArray)item.Value).Count;
                if (Convert.ToInt32(node.GetProperty("Abnormal::PreparedCount").Value) != expected)
                    throw new InvalidOperationException("VC instance preparation count mismatch: " + item.Name);
            }

            PipelineConsole.Progress("[ABNORMAL INSTANCES] Snapshot " + RuleModelSynchronization.GetSnapshotDateTime(data) + ": shelf/buffer/AMR instances prepared; CNC WIP will be created before repair.");
        }

        private static string ReadRuntime(string name)
        {
            using (var stream = typeof(AbnormalProductInstances).Assembly.GetManifestResourceStream("DT_KETI." + name + ".py"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        private static void SetPosition(JObject row, ISimComponent node, IFrameFeature frame)
        {
            Matrix matrix = frame == null ? node.TransformationInWorld : node.RootNode.GetFeatureTransformationInWorld(frame);
            row["worldPosition"] = new JArray(new[] { matrix.GetN(), matrix.GetO(), matrix.GetA(), matrix.GetP() }.Select(v => new JArray(v.X, v.Y, v.Z)));
        }

        private static void FindCreates(IStatementScope scope, List<Tuple<IStatementScope, ICreateProductStatement>> result)
        {
            foreach (var statement in scope.Statements.ToArray())
            {
                var create = statement as ICreateProductStatement;
                if (create != null)
                    result.Add(Tuple.Create(scope, create));
                var branch = statement as IProcessIfStatement;
                if (branch != null)
                {
                    FindCreates(branch.ThenScope, result);
                    FindCreates(branch.ElseScope, result);
                }

                var loop = statement as IProcessWhileStatement;
                if (loop != null)
                    FindCreates(loop.Scope, result);
            }
        }

        private static void Ensure(IPropertyContainer owner, string name, object value)
        {
            if (owner.GetProperty(name) == null)
                owner.CreateProperty(value.GetType(), PropertyConstraintType.NotSpecified, name).Value = value;
        }

        private static void Set(IPropertyContainer owner, string name, object value)
        {
            Ensure(owner, name, value);
            owner.GetProperty(name).Value = value;
        }
    }
}
