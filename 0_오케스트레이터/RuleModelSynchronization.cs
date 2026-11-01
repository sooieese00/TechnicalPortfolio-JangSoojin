using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public sealed class RuleModelSynchronization
    {
        private const int UnitOfTime = 1;
        private const string CurrentDateTime = "2024-06-04T09:00:00Z";
        public static string GetSnapshotDateTime(JObject data)
        {
            JToken token = data["CURRENT_DATETIME"];
            if (token != null && token.Type == JTokenType.Date)
                return token.ToObject<DateTimeOffset>().UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            string value = (string)token;
            if (string.IsNullOrWhiteSpace(value))
                return CurrentDateTime;
            DateTimeOffset timestamp;
            if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out timestamp))
                throw new InvalidOperationException("Invalid snapshot CURRENT_DATETIME: " + value);
            return timestamp.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static readonly float[] MotionRatioAb =
        {
            0.001f,
            0.001f,
            0.516f,
            0.001f,
            0.001f,
            0.48f
        };
        private static readonly float[] MotionRatioDe =
        {
            0.02f,
            0.01f,
            0.16f,
            0.17f,
            0.16f,
            0.16f,
            0.32f
        };
        private static readonly double[] RowPositions =
        {
            1878.986799,
            3047.318076,
            4215.649354,
            5383.980631,
            6552.311909,
            7720.643186,
            8888.974464,
            10057.305741,
            11225.637019,
            12393.9682960079
        };
        private static readonly double[] TierPositions =
        {
            700,
            1400,
            2100,
            2800
        };
        public void SynchronizeSnapshot(JObject data, ISimWorld simWorld, IApplication app)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (simWorld == null)
                throw new ArgumentNullException("simWorld");
            PipelineConsole.Log("[RULE SYNC] Restoring abnormal P4R snapshot.");
            ResetRuntimeWipState(simWorld);
            var preprocessing = new RuleJsonPreprocessing();
            IList<RuleShelfSnapshot> shelves = preprocessing.GetShelfSnapshots(data);
            JObject pendingStorage = preprocessing.GetPendingStorageWip(data);
            RestoreCncRuntimeState(data, simWorld);
            RestoreBufferRuntimeState(data, simWorld);
            RestoreAmrProperties(data, simWorld);
            RestoreAmrWipNodes(data, simWorld);
            RestoreShuttlePropertiesAndWip(data, simWorld);
            RestoreLifterPropertiesAndWip(data, simWorld);
            RestoreAsrsInventory(shelves, simWorld);
            PublishPendingStorageWip(pendingStorage, simWorld);
            data["_DT_RULE_INITIALIZATION"] = new JObject
            {
                ["pendingStorage"] = pendingStorage == null ? JValue.CreateNull() : pendingStorage.DeepClone(),
                ["asrsRetrievalWipRestore"] = "disabled"
            };
            PipelineConsole.Log("[RULE SYNC] Rule snapshot synchronization completed.");
        }

        private static void ResetRuntimeWipState(ISimWorld simWorld)
        {
            string[] bufferTypes =
            {
                "A",
                "B",
                "C",
                "D"
            };
            foreach (string bufferType in bufferTypes)
            {
                SetBooleanIfPresent(simWorld.FindComponent("BT_" + bufferType + "_OUT_WIP"), "WorkInProcess", false);
                for (int slot = 1; slot <= 3; slot++)
                    SetBooleanIfPresent(simWorld.FindComponent("BT_" + bufferType + "_IN_WIP #" + slot), "WorkInProcess", false);
            }

            string[] machines =
            {
                "CNC_A_001",
                "CNC_A_002",
                "CNC_B_001",
                "CNC_D_001",
                "CNC_E_001"
            };
            foreach (string machineName in machines)
            {
                ISimComponent machine = simWorld.FindComponent(machineName);
                SetBooleanIfPresent(machine, "WorkInProcess", false);
                SetBooleanIfPresent(machine, "MachineFailure", false);
            }

            PipelineConsole.Log("[RULE SYNC] Buffer/CNC/AMR WIP flags reset; ASRS WIP restore is disabled.");
        }

        private static void SetBooleanIfPresent(ISimComponent component, string propertyName, bool value)
        {
            if (component == null)
                return;
            var property = component.GetProperty(propertyName);
            if (property != null)
                property.Value = value;
            component.Rebuild();
        }

        private static void RestoreAsrsInventory(IEnumerable<RuleShelfSnapshot> shelves, ISimWorld simWorld)
        {
            IProcessController productTypes = simWorld.WorldRoot.Behaviors[0] as IProcessController;
            foreach (RuleShelfSnapshot snapshot in shelves)
            {
                string suffix = (snapshot.TierIndex + 1).ToString();
                ISimComponent shelf = simWorld.FindComponent("Shelf #" + suffix);
                ISimComponent feeder = simWorld.FindComponent("Feeder Process #" + suffix);
                var initialCount = shelf.GetProperty("InitialCount");
                var sequence = shelf.GetProperty("Sequence");
                if (initialCount == null)
                    throw new InvalidOperationException("Shelf #" + suffix + " has no InitialCount property.");
                initialCount.Value = snapshot.Inventory.Count(IsPhysicalMaterial);
                if (sequence != null)
                    sequence.Value = snapshot.RetrievalSequence.ToString(Formatting.None);
                IProductCreator creator = feeder.FindBehavior("ProductCreator") as IProductCreator;
                creator.GetProperty("FeedMode").Value = (int)ProductCreatorFeedMode.Batch;
                creator.BatchFeed.GetProperty("BatchInterval").Value = 0.001;
                AddInventoryBatches(snapshot.Inventory, creator, productTypes);
                shelf.Rebuild();
                feeder.Rebuild();
            }
        }

        private static void AddInventoryBatches(string[] inventory, IProductCreator creator, IProcessController productTypes)
        {
            int last = -1;
            for (int i = 0; i < inventory.Length; i++)
                if (IsPhysicalMaterial(inventory[i]))
                    last = i;
            int current = 0;
            while (current <= last)
            {
                string name = RuleJsonPreprocessing.NormalizeRuleProductType(inventory[current]);
                int count = 1;
                while (current + count <= last && string.Equals(RuleJsonPreprocessing.NormalizeRuleProductType(inventory[current + count]), name, StringComparison.OrdinalIgnoreCase))
                    count++;
                var productType = productTypes.ProductTypeManager.ProductTypes.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                if (productType != null)
                {
                    var batch = creator.BatchFeed.CreateBatch();
                    batch.Part = productType;
                    batch.Limit = count;
                    batch.GetProperty("Interval").Value = "0.001";
                }

                current += count;
            }
        }

        private static bool IsPhysicalMaterial(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && (name.StartsWith("MT_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Complete_", StringComparison.OrdinalIgnoreCase));
        }

        private static void RestoreCncRuntimeState(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            var task = new TaskDefinition();
            foreach (RuleCncSnapshot snapshot in preprocessing.GetCncSnapshots(data, UnitOfTime, GetSnapshotDateTime(data)))
            {
                ISimComponent machine = simWorld.FindComponent(snapshot.MachineId);
                IProcessExecutor executor = machine.FindBehavior("ProcessExecutor") as IProcessExecutor;
                IRoutine routine = executor.ActiveProgram.Routines[0];
                bool isDe = string.Equals(snapshot.MachineId, "CNC_D_001", StringComparison.OrdinalIgnoreCase) || string.Equals(snapshot.MachineId, "CNC_E_001", StringComparison.OrdinalIgnoreCase);
                if (isDe)
                    ApplyDeCnc(machine, routine, snapshot, task, simWorld);
                else
                    ApplyAbCnc(machine, routine, snapshot, task, simWorld);
                machine.Rebuild();
                PipelineConsole.Log("[RULE SYNC] CNC restored: " + snapshot.MachineId + ", WIP=" + (snapshot.WorkInProcessMaterial ?? "none"));
            }
        }

        private static void ApplyDeCnc(ISimComponent machine, IRoutine routine, RuleCncSnapshot snapshot, TaskDefinition task, ISimWorld simWorld)
        {
            if (snapshot.ProcessingTimes.Length > 0)
                machine.GetProperty("ProcessTime").Value = snapshot.ProcessingTimes.Min().ToString();
            ConfigureMaterialOperations(routine, 6, snapshot, task, MotionRatioDe);
            machine.GetProperty("Failure::MTTR").Value = snapshot.Mttr.ToString();
            machine.GetProperty("Failure::MTBF").Value = snapshot.Mtbf.ToString();
            IProcessIfStatement warmUp = routine.Statements[0] as IProcessIfStatement;
            if (IsPhysicalMaterial(snapshot.WorkInProcessMaterial))
            {
                machine.GetProperty("WorkInProcess").Value = true;
                IProcessIfStatement wip = warmUp.ElseScope.Statements[1] as IProcessIfStatement;
                ICreateProductStatement creator = wip.ThenScope.Statements[1] as ICreateProductStatement;
                SetCreatorProductType(creator, snapshot.WorkInProcessMaterial, simWorld);
                ApplyRemainingMotion(wip, snapshot.RemainingProcessingTime, MotionRatioDe);
            }

            ApplyFailure(machine, warmUp, snapshot.RemainingRepairTime, 1);
        }

        private static void ApplyAbCnc(ISimComponent machine, IRoutine routine, RuleCncSnapshot snapshot, TaskDefinition task, ISimWorld simWorld)
        {
            ConfigureMaterialOperations(routine, 9, snapshot, task, MotionRatioAb);
            machine.GetProperty("MTTR").Value = snapshot.Mttr.ToString();
            machine.GetProperty("MTBF").Value = snapshot.Mtbf.ToString();
            IProcessIfStatement warmUp = routine.Statements[0] as IProcessIfStatement;
            if (IsPhysicalMaterial(snapshot.WorkInProcessMaterial))
            {
                machine.GetProperty("WorkInProcess").Value = true;
                IProcessIfStatement wip = warmUp.ElseScope.Statements[1] as IProcessIfStatement;
                ICreateProductStatement creator = wip.ThenScope.Statements[2] as ICreateProductStatement;
                SetCreatorProductType(creator, snapshot.WorkInProcessMaterial, simWorld);
                ApplyRemainingMotion(wip, snapshot.RemainingProcessingTime, MotionRatioAb);
            }

            ApplyFailure(machine, warmUp, snapshot.RemainingRepairTime, 2);
        }

        private static void ConfigureMaterialOperations(IRoutine routine, int firstIndex, RuleCncSnapshot snapshot, TaskDefinition task, float[] motionRatios)
        {
            for (int j = 0; j < snapshot.FlowMaterials.Length; j++)
            {
                if (j != snapshot.FlowMaterials.Length - 1)
                    routine.AddStatement(routine.Statements[firstIndex], true);
                IProcessIfStatement condition = routine.Statements[firstIndex + j] as IProcessIfStatement;
                condition.ConditionExpression = task.GetConditionExpression(snapshot.FlowMaterials[j]);
                for (int k = 0; k < motionRatios.Length; k++)
                {
                    IMoveJointStatement move = condition.ThenScope.Statements[k] as IMoveJointStatement;
                    move.MotionTimeExpression = Convert.ToString(motionRatios[k] * snapshot.ProcessingTimes[j]);
                }
            }
        }

        private static void ApplyRemainingMotion(IProcessIfStatement wip, double remainingTime, float[] motionRatios)
        {
            for (int k = 0; k < motionRatios.Length; k++)
            {
                IMoveJointStatement move = wip.ThenScope.Statements[k + 4] as IMoveJointStatement;
                move.MotionTimeExpression = Convert.ToString(Math.Round(remainingTime * motionRatios[k] * UnitOfTime, 5));
            }
        }

        private static void ApplyFailure(ISimComponent machine, IProcessIfStatement warmUp, double remainingRepairTime, int delayStatementIndex)
        {
            if (remainingRepairTime == 0)
                return;
            machine.GetProperty("MachineFailure").Value = true;
            IProcessIfStatement failure = warmUp.ElseScope.Statements[0] as IProcessIfStatement;
            IProcessDelayStatement delay = failure.ThenScope.Statements[delayStatementIndex] as IProcessDelayStatement;
            delay.DistributionExpression = Convert.ToString(remainingRepairTime);
            var recovered = failure.ThenScope.AddStatement<ISetPropertyStatement>(delayStatementIndex + 1);
            recovered.Name = "Abnormal: repair completed";
            recovered.TargetPropertyName = "MachineFailure";
            recovered.TargetValueExpression = "False";
        }

        private static void RestoreBufferRuntimeState(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            foreach (RuleBufferSnapshot snapshot in preprocessing.GetBufferSnapshots(data))
            {
                if (snapshot.IsInput)
                    RestoreInputBuffer(snapshot, simWorld);
                else
                    RestoreOutputBuffer(snapshot, simWorld);
            }
        }

        private static void RestoreInputBuffer(RuleBufferSnapshot snapshot, ISimWorld simWorld)
        {
            for (int i = 0; i < snapshot.Products.Count; i++)
            {
                string nodeId = "BT_" + snapshot.BufferType + "_IN_WIP #" + (i + 1);
                ISimComponent node = RequireComponent(simWorld, nodeId);
                node.GetProperty("WorkInProcess").Value = true;
                IProcessIfStatement condition = GetFirstIfStatement(node);
                ICreateProductStatement creator = condition.ThenScope.Statements[0] as ICreateProductStatement;
                SetCreatorProductType(creator, snapshot.Products[i], simWorld);
                node.Rebuild();
            }
        }

        private static void RestoreOutputBuffer(RuleBufferSnapshot snapshot, ISimWorld simWorld)
        {
            string nodeId = "BT_" + snapshot.BufferType + "_OUT_WIP";
            ISimComponent node = RequireComponent(simWorld, nodeId);
            node.GetProperty("WorkInProcess").Value = true;
            IProcessIfStatement condition = GetFirstIfStatement(node);
            ICreateProductStatement createTemplate = condition.ThenScope.Statements[0] as ICreateProductStatement;
            ITransportOutStatement transportTemplate = condition.ThenScope.Statements[1] as ITransportOutStatement;
            for (int i = 1; i < snapshot.Products.Count; i++)
                condition.ThenScope.AddStatement(createTemplate, true);
            for (int i = 1; i < snapshot.Products.Count; i++)
                condition.ThenScope.AddStatement(transportTemplate, true);
            var incoming = condition.ThenScope.Statements.OfType<ICreateProductStatement>().ToArray();
            var outgoing = condition.ThenScope.Statements.OfType<ITransportOutStatement>().ToArray();
            for (int i = 0; i < snapshot.Products.Count; i++)
            {
                ICreateProductStatement creator = incoming[i];
                SetCreatorProductType(creator, snapshot.Products[i], simWorld);
            }

            for (int i = 0; i < outgoing.Length; i++)
            {
                condition.ThenScope.MoveStatement(incoming[i], 2 * i);
                condition.ThenScope.MoveStatement(outgoing[i], 2 * i + 1);
            }

            node.Rebuild();
        }

        private static void SetCreatorProductType(ICreateProductStatement creator, string productTypeName, ISimWorld simWorld)
        {
            if (creator == null)
                throw new InvalidOperationException("Rule WIP CreateProduct statement is missing.");
            IProcessController controller = simWorld.WorldRoot.Behaviors[0] as IProcessController;
            var productType = controller.ProductTypeManager.ProductTypes.FirstOrDefault(item => string.Equals(item.Name, productTypeName, StringComparison.OrdinalIgnoreCase));
            if (productType == null)
                throw new InvalidOperationException("Rule ProductType was not found: " + productTypeName);
            creator.GetProperty("ProductType").Value = productType;
        }

        private static void RestoreAmrProperties(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            foreach (RuleAmrSnapshot snapshot in preprocessing.GetAmrSnapshots(data))
            {
                ISimComponent amr = RequireComponent(simWorld, snapshot.ComponentId);
                EnsureAmrCompletionProperties(amr);
                SetNonZeroProperty(amr, "MoveSpeed", snapshot.MoveSpeed);
                SetNonZeroProperty(amr, "MoveSpeedLoaded", snapshot.LoadedMoveSpeed);
                SetNonZeroProperty(amr, "Acceleration", snapshot.Acceleration);
                SetNonZeroProperty(amr, "Deceleration", snapshot.Deceleration);
                if (snapshot.TransportCapacity != 0)
                {
                    var capacity = amr.GetProperty("TransportCapacity");
                    if (capacity != null)
                        capacity.Value = snapshot.TransportCapacity;
                }

                Matrix matrix = amr.TransformationInWorld;
                matrix.SetP(new Vector3(snapshot.X, snapshot.Y, 0));
                amr.TransformationInWorld = matrix;
                amr.Rebuild();
                PipelineConsole.Log("[RULE SYNC] AMR restored: " + snapshot.ComponentId);
            }
        }

        private static void EnsureAmrCompletionProperties(ISimComponent amr)
        {
            EnsureProperty(amr, typeof(string), "LastCompletedMaterial", "");
            EnsureProperty(amr, typeof(string), "LastCompletedAction", "");
            EnsureProperty(amr, typeof(string), "LastCompletedLocation", "");
            EnsureProperty(amr, typeof(double), "LastCompletionTime", 0.0);
        }

        private static void EnsureProperty(IPropertyContainer container, Type valueType, string name, object defaultValue)
        {
            if (container.GetProperty(name) != null)
                return;
            IProperty property = container.CreateProperty(valueType, PropertyConstraintType.NotSpecified, name);
            property.Value = defaultValue;
        }

        private static void RestoreAmrWipNodes(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            foreach (RuleAmrWipSnapshot snapshot in preprocessing.GetAmrWipSnapshots(data))
            {
                string nodePrefix = string.Equals(snapshot.AmrId, "AMR_A_001", StringComparison.OrdinalIgnoreCase) ? "AMR_A" : "AMR_B";
                string[] apNodeIds =
                {
                    nodePrefix + "_WIP_AP #1",
                    nodePrefix + "_WIP_AP #2"
                };
                string bpNodeId = nodePrefix + "_WIP_BP";
                foreach (string nodeId in apNodeIds.Concat(new[] { bpNodeId }))
                    ResetAmrWipNode(RequireComponent(simWorld, nodeId), snapshot.X, snapshot.Y);
                int apCount = Math.Min(snapshot.ApProducts.Count, apNodeIds.Length);
                for (int i = 0; i < apCount; i++)
                {
                    ISimComponent node = RequireComponent(simWorld, apNodeIds[i]);
                    node.GetProperty("WorkInProcess").Value = true;
                    ICreateProductStatement creator = GetFirstIfStatement(node).ThenScope.Statements[0] as ICreateProductStatement;
                    SetCreatorProductType(creator, snapshot.ApProducts[i], simWorld);
                    node.Rebuild();
                }

                if (snapshot.BpProducts.Count > 0)
                {
                    ISimComponent node = RequireComponent(simWorld, bpNodeId);
                    node.GetProperty("WorkInProcess").Value = true;
                    IProcessIfStatement condition = GetFirstIfStatement(node);
                    for (int i = 1; i < snapshot.BpProducts.Count; i++)
                        condition.ThenScope.AddStatement(condition.ThenScope.Statements[0], true);
                    var transport = condition.ThenScope.Statements.OfType<ITransportOutStatement>().Single();
                    for (int i = 1; i < snapshot.BpProducts.Count; i++)
                        condition.ThenScope.AddStatement(transport, true);
                    var incoming = condition.ThenScope.Statements.OfType<ICreateProductStatement>().ToArray();
                    var outgoing = condition.ThenScope.Statements.OfType<ITransportOutStatement>().ToArray();
                    for (int i = 0; i < snapshot.BpProducts.Count; i++)
                    {
                        ICreateProductStatement creator = incoming[i];
                        SetCreatorProductType(creator, snapshot.BpProducts[i], simWorld);
                        condition.ThenScope.MoveStatement(incoming[i], 2 * i);
                        condition.ThenScope.MoveStatement(outgoing[i], 2 * i + 1);
                    }

                    node.Rebuild();
                }

                PipelineConsole.Log("[RULE SYNC] AMR WIP nodes restored: " + snapshot.AmrId);
            }
        }

        private static void ResetAmrWipNode(ISimComponent node, double x, double y)
        {
            Matrix matrix = node.TransformationInWorld;
            matrix.SetP(new Vector3(x, y, 0));
            node.TransformationInWorld = matrix;
            node.GetProperty("WorkInProcess").Value = false;
            node.Rebuild();
        }

        private static void RestoreShuttlePropertiesAndWip(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            foreach (RuleMheSnapshot snapshot in preprocessing.GetShuttleSnapshots(data))
            {
                ISimComponent shuttle = RequireComponent(simWorld, snapshot.ComponentId);
                SetNonZeroProperty(shuttle, "Speeds::CarrierSpeed", snapshot.Properties[0]);
                SetNonZeroProperty(shuttle, "MoveSpeedLoaded", snapshot.Properties[1]);
                SetNonZeroProperty(shuttle, "Speeds::CarrierAcceleration", snapshot.Properties[2]);
                SetNonZeroProperty(shuttle, "Deceleration", snapshot.Properties[3]);
                SetCapacity(shuttle, snapshot.Properties[4]);
                string[] wip = snapshot.WipProperties;
                shuttle.Rebuild();
                PipelineConsole.Log("[RULE SYNC] Shuttle restored: " + snapshot.ComponentId);
            }
        }

        private static void RestoreShuttleRetrievalWip(ISimWorld simWorld, RuleMheSnapshot snapshot, string[] wip, int floor, double currentX)
        {
            double shuttleTime = Math.Abs(currentX) / snapshot.Properties[0];
            ISimComponent asrsOut = RequireComponent(simWorld, "ASRS Out");
            asrsOut.GetProperty("ShuttleRetrievalWorkInProcess").Value = true;
            IProcessExecutor executor = asrsOut.FindBehavior("ProcessExecutor") as IProcessExecutor;
            IProcessIfStatement condition = executor.ActiveProgram.Routines[0].Statements[1] as IProcessIfStatement;
            ISendSignalStatement carriageSignal = condition.ThenScope.Statements[0] as ISendSignalStatement;
            ISendSignalStatement lifterASignal = condition.ThenScope.Statements[1] as ISendSignalStatement;
            ISendSignalStatement lifterBSignal = condition.ThenScope.Statements[2] as ISendSignalStatement;
            ICreateProductStatement creator = condition.ThenScope.Statements[3] as ICreateProductStatement;
            IProcessDelayStatement delay = condition.ThenScope.Statements[4] as IProcessDelayStatement;
            ISimComponent controller = RequireComponent(simWorld, "Transport Carriage Controller #" + floor);
            carriageSignal.GetProperty("Component").Value = controller;
            carriageSignal.GetProperty("Signal").Value = controller.FindBehavior("ShuttleWIPDeactiveSignal");
            carriageSignal.ValueExpression = Convert.ToString(shuttleTime);
            int tier = Convert.ToInt32(wip[3]);
            double distance = Math.Abs(TierPositions[tier] - TierPositions[0]);
            double lifterATime = CalculateTriangularTime(distance, snapshot.LifterAProperties);
            double lifterBTime = CalculateTriangularTime(distance, snapshot.LifterBProperties);
            lifterASignal.ValueExpression = Convert.ToString(shuttleTime + lifterATime);
            lifterBSignal.ValueExpression = Convert.ToString(shuttleTime + lifterBTime);
            creator.GetProperty("ProductType").Value = wip[1];
            delay.DistributionExpression = Convert.ToString(shuttleTime + lifterBTime);
            asrsOut.Rebuild();
        }

        private static void RestoreLifterPropertiesAndWip(JObject data, ISimWorld simWorld)
        {
            var preprocessing = new RuleJsonPreprocessing();
            foreach (RuleMheSnapshot snapshot in preprocessing.GetLifterSnapshots(data, GetSnapshotDateTime(data)))
            {
                ISimComponent lifter = RequireComponent(simWorld, snapshot.ComponentId);
                SetNonZeroProperty(lifter, "MoveSpeed", snapshot.Properties[0]);
                SetNonZeroProperty(lifter, "MoveSpeedLoaded", snapshot.Properties[1]);
                SetNonZeroProperty(lifter, "Acceleration", snapshot.Properties[2]);
                SetNonZeroProperty(lifter, "Deceleration", snapshot.Properties[3]);
                SetCapacity(lifter, snapshot.Properties[4]);
                if (string.Equals(snapshot.MheType, "LIFTER_A", StringComparison.OrdinalIgnoreCase))
                {
                    var sequence = lifter.GetProperty("Sequence");
                    if (sequence != null)
                        sequence.Value = snapshot.Sequence;
                }

                lifter.Rebuild();
                PipelineConsole.Log("[RULE SYNC] Lifter restored: " + snapshot.ComponentId);
            }
        }

        private static void RestoreLifterRetrievalWip(ISimWorld simWorld, RuleMheSnapshot snapshot)
        {
            string[] wip = snapshot.WipProperties;
            if (wip.Length < 2 || string.IsNullOrEmpty(wip[0]))
                return;
            double travelTime = CalculateLifterRemainingTime(Math.Abs(Convert.ToDouble(wip[0]) - 700.0), snapshot.Properties[0], snapshot.Properties[3]);
            ISimComponent asrsOut = RequireComponent(simWorld, "ASRS Out");
            asrsOut.GetProperty("LifterRetrievalWorkInProcess").Value = true;
            IProcessExecutor executor = asrsOut.FindBehavior("ProcessExecutor") as IProcessExecutor;
            IProcessIfStatement condition = executor.ActiveProgram.Routines[0].Statements[0] as IProcessIfStatement;
            (condition.ThenScope.Statements[0] as ISendSignalStatement).ValueExpression = Convert.ToString(travelTime);
            (condition.ThenScope.Statements[1] as ISendSignalStatement).ValueExpression = Convert.ToString(travelTime);
            (condition.ThenScope.Statements[2] as ICreateProductStatement).GetProperty("ProductType").Value = wip[1];
            (condition.ThenScope.Statements[3] as IProcessDelayStatement).DistributionExpression = Convert.ToString(travelTime);
            asrsOut.Rebuild();
        }

        private static void PublishPendingStorageWip(JObject pending, ISimWorld simWorld)
        {
            for (int floor = 1; floor <= 4; floor++)
            {
                ISimComponent shelf = RequireComponent(simWorld, "Shelf #" + floor);
                var shuttleWip = shelf.GetProperty("ShuttleStorageWorkInProcess");
                if (shuttleWip != null)
                    shuttleWip.Value = false;
                shelf.Rebuild();
                ISimComponent terminal = RequireComponent(simWorld, "Lifter_WIP #" + (floor + 4));
                var lifterWip = terminal.GetProperty("LifterStorageWorkInProcess");
                if (lifterWip != null)
                    lifterWip.Value = false;
                terminal.Rebuild();
            }

            ISimComponent controller = RequireComponent(simWorld, "Central Controller");
            var property = controller.GetProperty("DispatchRule::PendingStorageWipJSON");
            if (property == null)
                throw new InvalidOperationException("Central Controller is missing DispatchRule::PendingStorageWipJSON.");
            property.Value = pending == null ? string.Empty : pending.ToString(Formatting.None);
            controller.Rebuild();
            PipelineConsole.Log(pending == null ? "[RULE SYNC] No pending Shuttle/Lifter storage WIP." : "[RULE SYNC] Pending storage WIP published: " + Convert.ToString(pending["type"]) + " / " + Convert.ToString(pending["productId"]));
        }

        private sealed class PendingStorageChoice
        {
            public int Tier;
            public int Row;
            public int Column;
            public double LifterDelay;
            public double ShuttleDelay;
            public double RemainingTime;
        }

        private static void PreparePendingStoragePlacement(JObject pending, IList<RuleShelfSnapshot> shelves, ISimWorld simWorld)
        {
            if (pending == null)
                return;
            string storageType = Convert.ToString(pending["type"]);
            string productType = RuleJsonPreprocessing.NormalizeRuleProductType(Convert.ToString(pending["productType"]));
            if (!IsPhysicalMaterial(productType))
                throw new InvalidOperationException("Pending Storage WIP has no physical ProductType.");
            int tierFilter = -1;
            if (string.Equals(storageType, "SHUTTLE_STORAGE", StringComparison.OrdinalIgnoreCase))
            {
                tierFilter = (int? )pending["currentTier"] ?? -1;
                if (tierFilter < 0 || tierFilter >= shelves.Count)
                    throw new InvalidOperationException("Pending Shuttle Storage WIP has an invalid current tier.");
            }

            PendingStorageChoice best = null;
            for (int tier = 0; tier < shelves.Count; tier++)
            {
                if (tierFilter >= 0 && tier != tierFilter)
                    continue;
                RuleShelfSnapshot shelf = shelves[tier];
                for (int row = 0; row < RowPositions.Length; row++)
                    for (int column = 0; column < 4; column++)
                    {
                        int index = row * 4 + column;
                        if (index >= shelf.Inventory.Length || IsPhysicalMaterial(shelf.Inventory[index]))
                            continue;
                        PendingStorageChoice candidate = CalculatePendingStorageChoice(pending, storageType, tier, row, column, simWorld);
                        if (IsBetterStorageChoice(candidate, best))
                            best = candidate;
                    }
            }

            if (best == null)
                throw new InvalidOperationException("No empty ASRS slot is available for pending Storage WIP.");
            RuleShelfSnapshot targetShelf = shelves[best.Tier];
            targetShelf.Inventory[best.Row * 4 + best.Column] = productType;
            pending["productType"] = productType;
            pending["targetRow"] = best.Row;
            pending["targetColumn"] = best.Column;
            pending["targetTier"] = best.Tier;
            pending["lifterDelay"] = best.LifterDelay;
            pending["shuttleDelay"] = best.ShuttleDelay;
            pending["remainingTime"] = best.RemainingTime;
            pending["preplacedInShelf"] = true;
            pending["storageRestorePending"] = true;
            PipelineConsole.Log("[RULE SYNC] Pending storage preplaced: product=" + Convert.ToString(pending["productId"]) + " material=" + productType + " location=(" + best.Row + "," + best.Column + "," + best.Tier + ")" + " remaining=" + best.RemainingTime.ToString("0.###"));
        }

        private static PendingStorageChoice CalculatePendingStorageChoice(JObject pending, string storageType, int tier, int row, int column, ISimWorld simWorld)
        {
            ISimComponent shuttle = RequireComponent(simWorld, "SHUTTLE_A_" + (tier + 1).ToString("000"));
            double shuttleX;
            if (string.Equals(storageType, "SHUTTLE_STORAGE", StringComparison.OrdinalIgnoreCase))
            {
                shuttleX = ReadJsonDouble(pending, "currentX", 0.0);
            }
            else
            {
                JObject positions = pending["shuttlePositions"] as JObject;
                shuttleX = positions == null ? 0.0 : ReadJsonDouble(positions, tier.ToString(), 0.0);
            }

            double shuttleDelay = ComponentMotionTime(shuttle, RowPositions[row] - shuttleX, true);
            double lifterDelay = 0.0;
            double handoff = 0.0;
            if (string.Equals(storageType, "LIFTER_STORAGE", StringComparison.OrdinalIgnoreCase))
            {
                ISimComponent lifterA = RequireComponent(simWorld, "LIFTER_A_001");
                ISimComponent lifterB = RequireComponent(simWorld, "LIFTER_B_001");
                double targetZ = TierPositions[tier];
                double currentZ = ReadJsonDouble(pending, "currentZ", 0.0);
                double currentA = ReadJsonDouble(pending, "lifterACurrentZ", currentZ);
                double currentB = ReadJsonDouble(pending, "lifterBCurrentZ", currentZ);
                lifterDelay = Math.Max(ComponentMotionTime(lifterA, targetZ - currentA, true), ComponentMotionTime(lifterB, targetZ - currentB, true));
                handoff = 15.0;
            }

            return new PendingStorageChoice
            {
                Tier = tier,
                Row = row,
                Column = column,
                LifterDelay = lifterDelay,
                ShuttleDelay = shuttleDelay,
                RemainingTime = lifterDelay + handoff + shuttleDelay + 10.0
            };
        }

        private static bool IsBetterStorageChoice(PendingStorageChoice candidate, PendingStorageChoice current)
        {
            if (current == null)
                return true;
            int time = candidate.RemainingTime.CompareTo(current.RemainingTime);
            if (time != 0)
                return time < 0;
            if (candidate.Tier != current.Tier)
                return candidate.Tier < current.Tier;
            if (candidate.Row != current.Row)
                return candidate.Row < current.Row;
            return candidate.Column < current.Column;
        }

        private static double ComponentMotionTime(ISimComponent component, double distance, bool loaded)
        {
            double speed = ReadComponentNumber(component, loaded ? new[] { "MoveSpeedLoaded", "Speeds::CarrierSpeed", "MoveSpeed" } : new[] { "Speeds::CarrierSpeed", "MoveSpeed", "MoveSpeedLoaded" }, 0.0);
            double acceleration = ReadComponentNumber(component, new[] { "Speeds::CarrierAcceleration", "Acceleration" }, 0.0);
            double deceleration = ReadComponentNumber(component, new[] { "Deceleration", "Speeds::CarrierDeceleration" }, 0.0);
            return MotionTime(distance, speed, acceleration, deceleration);
        }

        private static double ReadComponentNumber(ISimComponent component, IEnumerable<string> names, double fallback)
        {
            foreach (string name in names)
            {
                var property = component.GetProperty(name);
                if (property == null || property.Value == null)
                    continue;
                double value;
                if (double.TryParse(Convert.ToString(property.Value), out value) && value > 0.0)
                    return value;
            }

            return fallback;
        }

        private static double ReadJsonDouble(JObject value, string name, double fallback)
        {
            double result;
            return value != null && double.TryParse(Convert.ToString(value[name]), out result) ? result : fallback;
        }

        private static double MotionTime(double distance, double speed, double acceleration, double deceleration)
        {
            distance = Math.Abs(distance);
            if (distance <= 0.0)
                return 0.0;
            if (speed <= 0.0)
                return double.MaxValue / 4.0;
            if (acceleration <= 0.0 || deceleration <= 0.0)
                return distance / speed;
            double accelerationDistance = speed * speed / (2.0 * acceleration);
            double decelerationDistance = speed * speed / (2.0 * deceleration);
            if (accelerationDistance + decelerationDistance <= distance)
                return speed / acceleration + (distance - accelerationDistance - decelerationDistance) / speed + speed / deceleration;
            double peak = Math.Sqrt(2.0 * distance * acceleration * deceleration / (acceleration + deceleration));
            return peak / acceleration + peak / deceleration;
        }

        private static void SetCapacity(ISimComponent component, double value)
        {
            if (value == 0.0)
                return;
            var capacity = component.GetProperty("TransportCapacity");
            if (capacity != null)
                capacity.Value = Convert.ToInt32(value);
        }

        private static double CalculateLifterRemainingTime(double distance, double moveSpeed, double deceleration)
        {
            double decelerationDistance = moveSpeed * moveSpeed / (2.0 * deceleration);
            if (distance >= decelerationDistance)
                return (distance - decelerationDistance) / moveSpeed + moveSpeed / deceleration;
            return Math.Sqrt(2.0 * distance / deceleration);
        }

        private static double CalculateTriangularTime(double distance, double[] properties)
        {
            double speed = properties[0];
            double acceleration = properties[2];
            double deceleration = properties[3];
            if (acceleration <= 0 || deceleration <= 0)
                return speed > 0 ? distance / speed : double.MaxValue;
            double cruiseDistance = distance - speed * speed / (2 * acceleration) - speed * speed / (2 * deceleration);
            if (cruiseDistance > 0)
                return speed / acceleration + cruiseDistance / speed + speed / deceleration;
            double peakSpeed = Math.Sqrt(2 * distance * acceleration * deceleration / (acceleration + deceleration));
            return peakSpeed / acceleration + peakSpeed / deceleration;
        }

        private static void SetNonZeroProperty(ISimComponent component, string propertyName, double value)
        {
            if (value == 0.0)
                return;
            var property = component.GetProperty(propertyName);
            if (property != null)
                property.Value = value;
        }

        private static ISimComponent RequireComponent(ISimWorld simWorld, string componentId)
        {
            ISimComponent component = simWorld.FindComponent(componentId);
            if (component == null)
                throw new InvalidOperationException("Rule component was not found: " + componentId);
            return component;
        }

        private static IProcessIfStatement GetFirstIfStatement(ISimComponent component)
        {
            IProcessExecutor executor = component.FindBehavior("ProcessExecutor") as IProcessExecutor;
            if (executor == null || executor.ActiveProgram == null || executor.ActiveProgram.Routines.Count == 0)
                throw new InvalidOperationException("Rule ProcessExecutor is missing: " + component.Name);
            IProcessIfStatement condition = executor.ActiveProgram.Routines[0].Statements[0] as IProcessIfStatement;
            if (condition == null)
                throw new InvalidOperationException("Rule WIP condition is missing: " + component.Name);
            return condition;
        }
    }
}
