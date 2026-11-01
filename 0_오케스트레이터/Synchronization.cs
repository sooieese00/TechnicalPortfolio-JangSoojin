using Caliburn.Micro;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Xml.Linq;
using VisualComponents.Create3D;
using VisualComponents.UX.Shared.Reflection;
using static VisualComponents.Create3D.SubscriptionHelper;

namespace DT_KETI
{
    public class Synchronization
    {
        JsonPreprocessing pro = null;
        TaskDefinition task = null;
        int UNIT_OF_TIME = 1;
        private string[] CNC_TYPE = new string[4]
        {
            "CNC_A",
            "CNC_B",
            "CNC_D",
            "CNC_E"
        };
        private string[] MHE_TYPE = new string[5]
        {
            "AMR_A",
            "AMR_B",
            "SHUTTLE_A",
            "LIFTER_A",
            "LIFTER_B"
        };
        private string AMR_CONTROLLER = "AMR_Transport_Controller";
        private float[] MOTION_RATIO_AB = new float[6]
        {
            0.001f,
            0.001f,
            0.516f,
            0.001f,
            0.001f,
            0.48f
        };
        private float[] MOTION_RATIO_DE = new float[7]
        {
            0.02f,
            0.01f,
            0.16f,
            0.17f,
            0.16f,
            0.16f,
            0.32f
        };
        private int ASRS_ROW_COUNT = 10;
        private int ASRS_COLUMN_COUNT = 4;
        private int ASRS_TIER_COUNT = 4;
        private double[] ROW_POSITION = new double[10]
        {
            1878.986799f,
            3047.318076f,
            4215.649354f,
            5383.980631f,
            6552.311909f,
            7720.643186f,
            8888.974464f,
            10057.305741f,
            11225.637019f,
            12393.9682960079
        };
        private double[] TIER_POSITION = new double[4]
        {
            700,
            1400,
            2100,
            2800
        };
        public const string CurrentDateTimeUtc = "2026-08-25T23:49:40.000Z";
        public void Synchronize(JObject data, ISimWorld simWorld, IApplication app, string modelPath)
        {
            PipelineConsole.Log("Synchronization Start");
            pro = new JsonPreprocessing();
            task = new TaskDefinition();
            SetSourceAndProduct(data, simWorld);
            SetCNCMachine(data, simWorld);
            SetAMRProperty(data, simWorld, app, modelPath);
            SetLifterPropertyandWIP(data, simWorld, app, modelPath);
            SetShuttlePropertyandWIP(data, simWorld, app, modelPath);
            SetBufferWIP(data, simWorld);
            SetAMRControllerandAMRWIPNode(data, simWorld);
        }

        private void SetCNCMachine(JObject data, ISimWorld simWorld)
        {
            string[] _productList = pro.GetProductList(data);
            string[][] _materialList = pro.GetMaterialList(data, _productList);
            string[] _machineList = pro.GetCNCMachineList(data, CNC_TYPE);
            string[][] _flowList = pro.GetFlowList(data, _productList, _materialList, CNC_TYPE);
            double[][] _failureValues = pro.GetFailureValues(data, _machineList, UNIT_OF_TIME);
            double[][] _processingTimes = pro.GetOperationTimeCNC(data, _flowList);
            for (int i = 0; i < _machineList.GetLength(0); i++)
            {
                ISimComponent _CNCMachine = simWorld.FindComponent(_machineList[i]);
                IBehavior _behavior = _CNCMachine.FindBehavior("ProcessExecutor");
                IProcessExecutor _processExecutor = _behavior as IProcessExecutor;
                IRoutine _routine = _processExecutor.ActiveProgram.Routines[0];
                var _machineType = pro.GetCNCType(data, _machineList[i]);
                int machineTypeIndex = Array.IndexOf(CNC_TYPE, _machineType);
                if (_machineList[i] == "CNC_D_001" || _machineList[i] == "CNC_E_001")
                {
                    double minProcessingTime = _processingTimes[machineTypeIndex].Min();
                    var _processTime = _CNCMachine.GetProperty("ProcessTime");
                    _processTime.Value = minProcessingTime.ToString();
                    int processIfStatementIndex = 6;
                    for (int j = 0; j < _flowList[machineTypeIndex].Length; j++)
                    {
                        string _expression = task.GetConditionExpression(_flowList[machineTypeIndex][j]);
                        double materialProcessingTime = _processingTimes[machineTypeIndex][j];
                        if (j != _flowList[machineTypeIndex].Length - 1)
                        {
                            _routine.AddStatement(_routine.Statements[processIfStatementIndex], true);
                        }

                        IProcessIfStatement _ifstatement = _routine.Statements[processIfStatementIndex + j] as IProcessIfStatement;
                        _ifstatement.ConditionExpression = _expression;
                        double[] motionTime = new double[7];
                        for (int k = 0; k < 7; k++)
                        {
                            motionTime[k] = MOTION_RATIO_DE[k] * materialProcessingTime;
                            IMoveJointStatement _moveJoint = _ifstatement.ThenScope.Statements[k] as IMoveJointStatement;
                            _moveJoint.MotionTimeExpression = Convert.ToString(motionTime[k]);
                        }
                    }

                    var _mttr = _CNCMachine.GetProperty("Failure::MTTR");
                    _mttr.Value = Convert.ToString(_failureValues[i][0]);
                    var _mtbf = _CNCMachine.GetProperty("Failure::MTBF");
                    _mtbf.Value = Convert.ToString(_failureValues[i][1]);
                    (string _materialId, double remainingProcessingTime) = pro.GetWorkInProcessProperties(data, _machineList[i], UNIT_OF_TIME);
                    double remainingRepairTime = pro.GetFailureStatus(data, _machineList[i], UNIT_OF_TIME, CurrentDateTimeUtc);
                    IProcessIfStatement _warmUpStatement = _routine.Statements[0] as IProcessIfStatement;
                    if (_materialId != null)
                    {
                        var _workinProcess = _CNCMachine.GetProperty("WorkInProcess");
                        _workinProcess.Value = true;
                        double[] operationTimeWip = pro.GetOperationTimeCNCWip(remainingProcessingTime, MOTION_RATIO_DE, UNIT_OF_TIME);
                        IProcessIfStatement _wipOperationCondition = _warmUpStatement.ElseScope.Statements[1] as IProcessIfStatement;
                        ICreateProductStatement _productCreator = _wipOperationCondition.ThenScope.Statements[1] as ICreateProductStatement;
                        _productCreator.GetProperty("ProductType").Value = _materialId;
                        for (int k = 0; k < operationTimeWip.GetLength(0); k++)
                        {
                            IMoveJointStatement moveJoint = _wipOperationCondition.ThenScope.Statements[k + 4] as IMoveJointStatement;
                            moveJoint.MotionTimeExpression = Convert.ToString(operationTimeWip[k]);
                        }
                    }

                    if (remainingRepairTime != 0)
                    {
                        var _boolFailure = _CNCMachine.GetProperty("MachineFailure");
                        _boolFailure.Value = true;
                        IProcessIfStatement _failureStatement = _warmUpStatement.ElseScope.Statements[0] as IProcessIfStatement;
                        IProcessDelayStatement _remainingOperation = _failureStatement.ThenScope.Statements[1] as IProcessDelayStatement;
                        _remainingOperation.DistributionExpression = Convert.ToString(remainingRepairTime);
                    }
                }
                else
                {
                    int processIfStatementIndex = 9;
                    for (int j = 0; j < _flowList[machineTypeIndex].Length; j++)
                    {
                        string conditionMaterial = _flowList[machineTypeIndex][j];
                        if (_machineList[i] == "CNC_A_001")
                        {
                            if (conditionMaterial == "MT_A" || conditionMaterial == "MT_B" || conditionMaterial == "MT_C")
                                conditionMaterial = conditionMaterial + "_A";
                        }
                        else if (_machineList[i] == "CNC_A_002")
                        {
                            if (conditionMaterial == "MT_A" || conditionMaterial == "MT_B" || conditionMaterial == "MT_C")
                                conditionMaterial = conditionMaterial + "_B";
                        }

                        string _expression = task.GetConditionExpression(conditionMaterial);
                        double materialProcessingTime = _processingTimes[machineTypeIndex][j];
                        if (j != _flowList[machineTypeIndex].Length - 1)
                        {
                            _routine.AddStatement(_routine.Statements[processIfStatementIndex], true);
                        }

                        IProcessIfStatement _ifstatement = _routine.Statements[processIfStatementIndex + j] as IProcessIfStatement;
                        _ifstatement.ConditionExpression = _expression;
                        double[] motionTime = new double[6];
                        for (int k = 0; k < 6; k++)
                        {
                            motionTime[k] = MOTION_RATIO_AB[k] * materialProcessingTime;
                            IMoveJointStatement _moveJoint = _ifstatement.ThenScope.Statements[k] as IMoveJointStatement;
                            _moveJoint.MotionTimeExpression = Convert.ToString(motionTime[k]);
                        }
                    }

                    var _mttr = _CNCMachine.GetProperty("MTTR");
                    _mttr.Value = Convert.ToString(_failureValues[i][0]);
                    var _mtbf = _CNCMachine.GetProperty("MTBF");
                    _mtbf.Value = Convert.ToString(_failureValues[i][1]);
                    (string _materialId, double remainingProcessingTime) = pro.GetWorkInProcessProperties(data, _machineList[i], UNIT_OF_TIME);
                    string convertedMaterialId = task.GetCNCWipProductType(_materialId, _machineList[i]);
                    double remainingRepairTime = pro.GetFailureStatus(data, _machineList[i], UNIT_OF_TIME, CurrentDateTimeUtc);
                    IProcessIfStatement _warmUpStatement = _routine.Statements[0] as IProcessIfStatement;
                    if (convertedMaterialId != null)
                    {
                        var _workinProcess = _CNCMachine.GetProperty("WorkInProcess");
                        _workinProcess.Value = true;
                        double[] operationTimeWip = pro.GetOperationTimeCNCWip(remainingProcessingTime, MOTION_RATIO_AB, UNIT_OF_TIME);
                        IProcessIfStatement _wipOperationCondition = _warmUpStatement.ElseScope.Statements[1] as IProcessIfStatement;
                        ICreateProductStatement _productCreator = _wipOperationCondition.ThenScope.Statements[2] as ICreateProductStatement;
                        _productCreator.GetProperty("ProductType").Value = convertedMaterialId;
                        for (int k = 0; k < operationTimeWip.GetLength(0); k++)
                        {
                            IMoveJointStatement moveJoint = _wipOperationCondition.ThenScope.Statements[k + 4] as IMoveJointStatement;
                            moveJoint.MotionTimeExpression = Convert.ToString(operationTimeWip[k]);
                        }
                    }

                    if (remainingRepairTime != 0)
                    {
                        var _failure = _CNCMachine.GetProperty("MachineFailure");
                        _failure.Value = true;
                        IProcessIfStatement _repairOperationCondition = _warmUpStatement.ElseScope.Statements[0] as IProcessIfStatement;
                        IProcessDelayStatement _remainingOperation = _repairOperationCondition.ThenScope.Statements[2] as IProcessDelayStatement;
                        _remainingOperation.DistributionExpression = Convert.ToString(remainingRepairTime);
                    }
                }

                _CNCMachine.Rebuild();
            }
        }

        private void SetSourceAndProduct(JObject data, ISimWorld simWorld)
        {
            IProcessController productTypeManager = simWorld.WorldRoot.Behaviors[0] as IProcessController;
            string shuttleRetrievalSchedule = pro.GetShuttleRetrievalSchedule(data);
            JObject retrievalScheduleObj;
            try
            {
                retrievalScheduleObj = JObject.Parse(shuttleRetrievalSchedule);
            }
            catch
            {
                retrievalScheduleObj = new JObject();
                retrievalScheduleObj["1"] = new JArray();
                retrievalScheduleObj["2"] = new JArray();
                retrievalScheduleObj["3"] = new JArray();
                retrievalScheduleObj["4"] = new JArray();
            }

            Dictionary<string, string> flexibleMaterialMapByLocation = pro.GetFlexibleMaterialMapByLocation(data);
            Dictionary<string, string> handlingMaterialStorageMap = pro.GetHandlingMaterialStorageMap(data);
            Dictionary<string, string> shuttleRetrievalMaterialMap = pro.GetShuttleRetrievalMaterialMap(data, ASRS_ROW_COUNT, ASRS_COLUMN_COUNT, ASRS_TIER_COUNT);
            for (int tierIndex = 0; tierIndex < ASRS_TIER_COUNT; tierIndex++)
            {
                string feederId = "Feeder Process #" + Convert.ToString(tierIndex + 1);
                ISimComponent feeder = simWorld.FindComponent(feederId);
                string[] inventoryList = pro.GetInventoryListforASRS(data, ASRS_ROW_COUNT, ASRS_COLUMN_COUNT, tierIndex, flexibleMaterialMapByLocation, handlingMaterialStorageMap);
                if (inventoryList == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, string> item in shuttleRetrievalMaterialMap)
                {
                    string[] positionParts = item.Key.Split('_');
                    if (positionParts.Length != 3)
                    {
                        continue;
                    }

                    int retrievalTier;
                    int retrievalRow;
                    int retrievalColumn;
                    if (!int.TryParse(positionParts[0], out retrievalTier) || !int.TryParse(positionParts[1], out retrievalRow) || !int.TryParse(positionParts[2], out retrievalColumn))
                    {
                        continue;
                    }

                    if (retrievalTier != tierIndex)
                    {
                        continue;
                    }

                    if (retrievalRow < 0 || retrievalRow >= ASRS_ROW_COUNT || retrievalColumn < 0 || retrievalColumn >= ASRS_COLUMN_COUNT)
                    {
                        continue;
                    }

                    int inventoryIndex = retrievalRow * ASRS_COLUMN_COUNT + retrievalColumn;
                    if (string.IsNullOrWhiteSpace(inventoryList[inventoryIndex]) || string.Equals(inventoryList[inventoryIndex], "dummy", StringComparison.OrdinalIgnoreCase))
                    {
                        inventoryList[inventoryIndex] = item.Value;
                    }
                }

                IBehavior behaviorProductCreator = feeder.FindBehavior("ProductCreator");
                IProductCreator productCreator = behaviorProductCreator as IProductCreator;
                var feedMode = productCreator.GetProperty("FeedMode");
                feedMode.Value = (int)ProductCreatorFeedMode.Batch;
                var batchInterval = productCreator.BatchFeed.GetProperty("BatchInterval");
                batchInterval.Value = 0.001;
                string shelfId = "Shelf #" + Convert.ToString(tierIndex + 1);
                ISimComponent shelf = simWorld.FindComponent(shelfId);
                int realMaterialCount = inventoryList.Count(name => !string.IsNullOrEmpty(name) && (name.StartsWith("MT_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Complete_", StringComparison.OrdinalIgnoreCase)));
                var initialLoadCount = shelf.GetProperty("InitialLoadCount");
                initialLoadCount.Value = realMaterialCount;
                string floorKey = Convert.ToString(tierIndex + 1);
                JToken floorSchedule = retrievalScheduleObj[floorKey];
                if (floorSchedule == null)
                {
                    floorSchedule = new JArray();
                }

                var sequenceProperty = shelf.GetProperty("Sequence");
                sequenceProperty.Value = floorSchedule.ToString(Newtonsoft.Json.Formatting.None);
                int lastMaterialIndex = -1;
                for (int index = 0; index < inventoryList.Length; index++)
                {
                    string materialName = inventoryList[index];
                    if (!string.IsNullOrEmpty(materialName) && (materialName.StartsWith("MT_", StringComparison.OrdinalIgnoreCase) || materialName.StartsWith("Complete_", StringComparison.OrdinalIgnoreCase)))
                    {
                        lastMaterialIndex = index;
                    }
                }

                if (lastMaterialIndex < 0)
                {
                    shelf.Rebuild();
                    feeder.Rebuild();
                    continue;
                }

                int currentIndex = 0;
                while (currentIndex <= lastMaterialIndex)
                {
                    string currentName = inventoryList[currentIndex];
                    int consecutiveCount = 1;
                    while (currentIndex + consecutiveCount <= lastMaterialIndex)
                    {
                        string nextName = inventoryList[currentIndex + consecutiveCount];
                        if (!string.Equals(nextName, currentName, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        consecutiveCount++;
                    }

                    var productType = productTypeManager.ProductTypeManager.ProductTypes.FirstOrDefault(product => string.Equals(product.Name, currentName, StringComparison.OrdinalIgnoreCase));
                    if (productType != null)
                    {
                        var batchItem = productCreator.BatchFeed.CreateBatch();
                        batchItem.Part = productType;
                        batchItem.Limit = consecutiveCount;
                        var intervalValue = batchItem.GetProperty("Interval");
                        intervalValue.Value = "0.001";
                    }

                    currentIndex += consecutiveCount;
                }

                shelf.Rebuild();
                feeder.Rebuild();
            }
        }

        private void SetBufferWIP(JObject data, ISimWorld simWorld)
        {
            string[] bufferList = pro.GetBufferInstanceIdList(data);
            if (bufferList == null || bufferList.Length == 0)
            {
                return;
            }

            for (int bufferIndex = 0; bufferIndex < bufferList.Length; bufferIndex++)
            {
                string bufferId = bufferList[bufferIndex];
                string[][] wipInBuffer = pro.GetWorkInProcessBuffer(data, bufferId);
                if (wipInBuffer == null || wipInBuffer.Length == 0)
                {
                    continue;
                }

                string[] bufferIdParts = bufferId.Split('_');
                if (bufferIdParts.Length < 4)
                {
                    continue;
                }

                string btType = bufferIdParts[1];
                bool isBtInBuffer = string.Equals(bufferIdParts[2], "IN", StringComparison.OrdinalIgnoreCase);
                bool isBtOutBuffer = string.Equals(bufferIdParts[2], "OUT", StringComparison.OrdinalIgnoreCase);
                if (!isBtInBuffer && !isBtOutBuffer)
                {
                    continue;
                }

                if (isBtInBuffer)
                {
                    int inWipNodeNumber = 1;
                    for (int materialIndex = 0; materialIndex < wipInBuffer.Length; materialIndex++)
                    {
                        string[] materialInfo = wipInBuffer[materialIndex];
                        if (materialInfo == null || materialInfo.Length < 2)
                        {
                            continue;
                        }

                        string materialId = materialInfo[0];
                        int materialQuantity;
                        if (!int.TryParse(materialInfo[1], out materialQuantity) || materialQuantity <= 0)
                        {
                            continue;
                        }

                        for (int quantityIndex = 0; quantityIndex < materialQuantity; quantityIndex++)
                        {
                            string wipNodeId = $"BT_{btType}_IN_WIP #{inWipNodeNumber}";
                            ISimComponent targetComponent = simWorld.FindComponent(wipNodeId);
                            var workInProcessProperty = targetComponent.GetProperty("WorkInProcess");
                            workInProcessProperty.Value = true;
                            IBehavior behavior = targetComponent.FindBehavior("ProcessExecutor");
                            IProcessExecutor processExecutor = behavior as IProcessExecutor;
                            IRoutine routine = processExecutor.ActiveProgram.Routines[0];
                            IProcessIfStatement ifStatement = routine.Statements[0] as IProcessIfStatement;
                            ICreateProductStatement createStatement = ifStatement.ThenScope.Statements[0] as ICreateProductStatement;
                            var productTypeProperty = createStatement.GetProperty("ProductType");
                            productTypeProperty.Value = materialId;
                            targetComponent.Rebuild();
                            inWipNodeNumber++;
                        }
                    }

                    continue;
                }

                string outWipNodeId = $"BT_{btType}_OUT_WIP";
                ISimComponent outTargetComponent = simWorld.FindComponent(outWipNodeId);
                int totalWip = 0;
                for (int i = 0; i < wipInBuffer.Length; i++)
                {
                    string[] materialInfo = wipInBuffer[i];
                    int materialQuantity;
                    if (int.TryParse(materialInfo[1], out materialQuantity) && materialQuantity > 0)
                    {
                        totalWip += materialQuantity;
                    }
                }

                if (totalWip <= 0)
                {
                    continue;
                }

                var outWorkInProcessProperty = outTargetComponent.GetProperty("WorkInProcess");
                outWorkInProcessProperty.Value = true;
                IBehavior outBehavior = outTargetComponent.FindBehavior("ProcessExecutor");
                IProcessExecutor outProcessExecutor = outBehavior as IProcessExecutor;
                IRoutine outRoutine = outProcessExecutor.ActiveProgram.Routines[0];
                IProcessIfStatement outIfStatement = outRoutine.Statements[0] as IProcessIfStatement;
                ICreateProductStatement createTemplate = outIfStatement.ThenScope.Statements[0] as ICreateProductStatement;
                ITransportOutStatement transportOutTemplate = outIfStatement.ThenScope.Statements[1] as ITransportOutStatement;
                for (int i = 1; i < totalWip; i++)
                {
                    outIfStatement.ThenScope.AddStatement(createTemplate, true);
                }

                for (int i = 1; i < totalWip; i++)
                {
                    outIfStatement.ThenScope.AddStatement(transportOutTemplate, true);
                }

                int createIndex = 0;
                for (int materialIndex = 0; materialIndex < wipInBuffer.Length; materialIndex++)
                {
                    string[] materialInfo = wipInBuffer[materialIndex];
                    if (materialInfo == null || materialInfo.Length < 2)
                    {
                        continue;
                    }

                    string materialId = materialInfo[0];
                    int materialQuantity;
                    if (string.IsNullOrWhiteSpace(materialId) || !int.TryParse(materialInfo[1], out materialQuantity) || materialQuantity <= 0)
                    {
                        continue;
                    }

                    for (int quantityIndex = 0; quantityIndex < materialQuantity; quantityIndex++)
                    {
                        ICreateProductStatement createStatement = outIfStatement.ThenScope.Statements[createIndex] as ICreateProductStatement;
                        var productTypeProperty = createStatement.GetProperty("ProductType");
                        productTypeProperty.Value = materialId;
                        createIndex++;
                    }
                }

                outTargetComponent.Rebuild();
            }
        }

        private void SetAMRControllerandAMRWIPNode(JObject data, ISimWorld simWorld)
        {
            string AMRSequence = pro.GetAMRSequence(data, CurrentDateTimeUtc);
            ISimComponent _Controller = simWorld.FindComponent(AMR_CONTROLLER);
            EnsureAMRCompletionEventBridge(_Controller);
            var _sequenceProperty = _Controller.GetProperty("Sequence");
            _sequenceProperty.Value = AMRSequence;
            _Controller.Rebuild();
            string[] _MHEList = pro.GetMHEList(data, MHE_TYPE);
            for (int i = 0; i < _MHEList.GetLength(0); i++)
            {
                string _mheType = pro.GetMHEType(data, _MHEList[i]);
                if (_mheType != "AMR_A" && _mheType != "AMR_B")
                {
                    continue;
                }

                string _amrNodeName = "";
                if (_MHEList[i] == "AMR_A_001")
                {
                    _amrNodeName = "AMR_A";
                }
                else if (_MHEList[i] == "AMR_B_001")
                {
                    _amrNodeName = "AMR_B";
                }
                else
                {
                    continue;
                }

                var(x, y) = pro.GetMHEPosition(data, _MHEList[i]);
                double _x = Convert.ToDouble(x);
                double _y = Convert.ToDouble(y);
                string apNodeName1 = _amrNodeName + "_WIP_AP #1";
                string apNodeName2 = _amrNodeName + "_WIP_AP #2";
                string bpNodeName = _amrNodeName + "_WIP_BP";
                string[] amrWipNodeNames =
                {
                    apNodeName1,
                    apNodeName2,
                    bpNodeName
                };
                foreach (string nodeName in amrWipNodeNames)
                {
                    ISimComponent node = simWorld.FindComponent(nodeName);
                    Matrix _mat = node.TransformationInWorld;
                    _mat.SetP(new Vector3(_x, _y, 0));
                    var workInProcessProperty = node.GetProperty("WorkInProcess");
                    workInProcessProperty.Value = false;
                    node.Rebuild();
                }

                List<string[]> _wipList = pro.GetAMRWIPProperties(data, _MHEList[i]);
                List<string> apProductList = new List<string>();
                List<string> bpProductList = new List<string>();
                foreach (string[] wip in _wipList)
                {
                    if (wip == null || wip.Length < 2)
                    {
                        continue;
                    }

                    string productType = wip[0];
                    string wipPosition = wip[1];
                    if (wipPosition == "AP")
                    {
                        apProductList.Add(productType);
                    }
                    else if (wipPosition == "BP")
                    {
                        bpProductList.Add(productType);
                    }
                }

                string[] apNodeNames =
                {
                    apNodeName1,
                    apNodeName2
                };
                int apCreateCount = Math.Min(apProductList.Count, apNodeNames.Length);
                for (int apIndex = 0; apIndex < apCreateCount; apIndex++)
                {
                    string apNodeName = apNodeNames[apIndex];
                    string productType = apProductList[apIndex];
                    ISimComponent apNode = simWorld.FindComponent(apNodeName);
                    Matrix matrix = apNode.TransformationInWorld;
                    matrix.SetP(new Vector3(_x, _y, 0));
                    apNode.TransformationInWorld = matrix;
                    var workInProcessProperty = apNode.GetProperty("WorkInProcess");
                    workInProcessProperty.Value = true;
                    IBehavior behavior = apNode.FindBehavior("ProcessExecutor");
                    IProcessExecutor processExecutor = behavior as IProcessExecutor;
                    IRoutine routine = processExecutor.ActiveProgram.Routines[0];
                    IProcessIfStatement ifStatement = routine.Statements[0] as IProcessIfStatement;
                    ICreateProductStatement createStatement = ifStatement.ThenScope.Statements[0] as ICreateProductStatement;
                    var productTypeProperty = createStatement.GetProperty("ProductType");
                    productTypeProperty.Value = productType;
                    apNode.Rebuild();
                }

                if (bpProductList.Count > 0)
                {
                    ISimComponent bpNode = simWorld.FindComponent(bpNodeName);
                    Matrix matrix = bpNode.TransformationInWorld;
                    matrix.SetP(new Vector3(_x, _y, 0));
                    bpNode.TransformationInWorld = matrix;
                    var workInProcessProperty = bpNode.GetProperty("WorkInProcess");
                    workInProcessProperty.Value = true;
                    IBehavior behavior = bpNode.FindBehavior("ProcessExecutor");
                    IProcessExecutor processExecutor = behavior as IProcessExecutor;
                    IRoutine routine = processExecutor.ActiveProgram.Routines[0];
                    IProcessIfStatement ifStatement = routine.Statements[0] as IProcessIfStatement;
                    for (int productIndex = 1; productIndex < bpProductList.Count; productIndex++)
                    {
                        ifStatement.ThenScope.AddStatement(ifStatement.ThenScope.Statements[0], true);
                    }

                    for (int productIndex = 0; productIndex < bpProductList.Count; productIndex++)
                    {
                        ICreateProductStatement createStatement = ifStatement.ThenScope.Statements[productIndex] as ICreateProductStatement;
                        var productTypeProperty = createStatement.GetProperty("ProductType");
                        productTypeProperty.Value = bpProductList[productIndex];
                    }

                    bpNode.Rebuild();
                }
            }
        }

        private void SetAMRProperty(JObject data, ISimWorld simWorld, IApplication app, string modelPath)
        {
            string[] _MHEList = pro.GetMHEList(data, MHE_TYPE);
            for (int i = 0; i < _MHEList.GetLength(0); i++)
            {
                string _mheType = pro.GetMHEType(data, _MHEList[i]);
                if (_mheType != "AMR_A" && _mheType != "AMR_B")
                {
                    continue;
                }

                ISimComponent _MHE = simWorld.FindComponent(_MHEList[i]);
                EnsureAMRCompletionProperties(_MHE);
                double[] _property = pro.GetPropertiesOfMHE(data, _MHEList[i]);
                if (_property[0] != 0.0)
                {
                    var _moveSpeed = _MHE.GetProperty("MoveSpeed");
                    if (_moveSpeed != null)
                    {
                        _moveSpeed.Value = _property[0];
                    }
                }

                if (_property[1] != 0.0)
                {
                    var _transportSpeed = _MHE.GetProperty("MoveSpeedLoaded");
                    if (_transportSpeed != null)
                    {
                        _transportSpeed.Value = _property[1];
                    }
                }

                if (_property[2] != 0.0)
                {
                    var _acceleration = _MHE.GetProperty("Acceleration");
                    if (_acceleration != null)
                    {
                        _acceleration.Value = _property[2];
                    }
                }

                if (_property[3] != 0.0)
                {
                    var _deceleration = _MHE.GetProperty("Deceleration");
                    if (_deceleration != null)
                    {
                        _deceleration.Value = _property[3];
                    }
                }

                if (_property[4] != 0.0)
                {
                    var _capacity = _MHE.GetProperty("TransportCapacity");
                    if (_capacity != null)
                    {
                        _capacity.Value = Convert.ToInt32(_property[4]);
                    }
                }

                var(x, y) = pro.GetMHEPosition(data, _MHEList[i]);
                double x_AMR = Convert.ToDouble(x);
                double y_AMR = Convert.ToDouble(y);
                Vector3 vec = new Vector3(x_AMR, y_AMR, 0);
                Matrix mat_AMR = _MHE.TransformationInWorld;
                mat_AMR.SetP(vec);
                _MHE.TransformationInWorld = mat_AMR;
                _MHE.Rebuild();
            }
        }

        private void SetShuttlePropertyandWIP(JObject data, ISimWorld simWorld, IApplication app, string modelPath)
        {
            string[] _MHEList = pro.GetMHEList(data, MHE_TYPE);
            for (int i = 0; i < _MHEList.Length; i++)
            {
                string _mheType = pro.GetMHEType(data, _MHEList[i]);
                if (_mheType != "SHUTTLE_A")
                {
                    continue;
                }

                ISimComponent _MHE = simWorld.FindComponent(_MHEList[i]);
                double[] _property = pro.GetPropertiesOfMHE(data, _MHEList[i]);
                if (_property[0] != 0.0)
                {
                    var _moveSpeed = _MHE.GetProperty("Speeds::CarrierSpeed");
                    if (_moveSpeed != null)
                        _moveSpeed.Value = _property[0];
                }

                if (_property[1] != 0.0)
                {
                    var _transportSpeed = _MHE.GetProperty("MoveSpeedLoaded");
                    if (_transportSpeed != null)
                        _transportSpeed.Value = _property[1];
                }

                if (_property[2] != 0.0)
                {
                    var _acceleration = _MHE.GetProperty("Speeds::CarrierAcceleration");
                    if (_acceleration != null)
                        _acceleration.Value = _property[2];
                }

                if (_property[3] != 0.0)
                {
                    var _deceleration = _MHE.GetProperty("Deceleration");
                    if (_deceleration != null)
                        _deceleration.Value = _property[3];
                }

                if (_property[4] != 0.0)
                {
                    var _capacity = _MHE.GetProperty("TransportCapacity");
                    if (_capacity != null)
                        _capacity.Value = Convert.ToInt32(_property[4]);
                }

                string[] _wipProperties = pro.GetShuttleWIPProperties(data, _MHEList[i]);
                if (string.IsNullOrEmpty(_wipProperties[0]))
                {
                    _MHE.Rebuild();
                    continue;
                }

                double _currentPositionX = Convert.ToDouble(_wipProperties[2]);
                double _moveSpeedValue = _property[0];
                int _lastUnderBarIndex = _MHEList[i].LastIndexOf("_");
                string _floorString = _MHEList[i].Substring(_lastUnderBarIndex + 1);
                int _floor = Convert.ToInt32(_floorString);
                if (_wipProperties[0] == "RETRIEVAL")
                {
                    double _travelDistance = Math.Abs(_currentPositionX);
                    double _travelTime = _travelDistance / _moveSpeedValue;
                    ISimComponent asrsOut = simWorld.FindComponent("ASRS Out");
                    var _shuttleRetrievalWIP = asrsOut.GetProperty("ShuttleRetrievalWorkInProcess");
                    _shuttleRetrievalWIP.Value = true;
                    IBehavior _behavior = asrsOut.FindBehavior("ProcessExecutor");
                    IProcessExecutor _processExecutor = _behavior as IProcessExecutor;
                    IRoutine _routine = _processExecutor.ActiveProgram.Routines[0];
                    IProcessIfStatement _shuttleIfStatement = _routine.Statements[1] as IProcessIfStatement;
                    ISendSignalStatement _sendCarriageSignal = _shuttleIfStatement.ThenScope.Statements[0] as ISendSignalStatement;
                    ISendSignalStatement _sendLifterASignal = _shuttleIfStatement.ThenScope.Statements[1] as ISendSignalStatement;
                    ISendSignalStatement _sendLifterBSignal = _shuttleIfStatement.ThenScope.Statements[2] as ISendSignalStatement;
                    ICreateProductStatement _createStatement = _shuttleIfStatement.ThenScope.Statements[3] as ICreateProductStatement;
                    IProcessDelayStatement _delayStatement = _shuttleIfStatement.ThenScope.Statements[4] as IProcessDelayStatement;
                    string _carriageControllerName = "Transport Carriage Controller #" + Convert.ToString(_floor);
                    ISimComponent _carriageController = simWorld.FindComponent(_carriageControllerName);
                    IBehavior _shuttleWIPSignal = _carriageController.FindBehavior("ShuttleWIPDeactiveSignal");
                    var _componentProperty = _sendCarriageSignal.GetProperty("Component");
                    _componentProperty.Value = _carriageController;
                    var _signalProperty = _sendCarriageSignal.GetProperty("Signal");
                    _signalProperty.Value = _shuttleWIPSignal;
                    _sendCarriageSignal.ValueExpression = Convert.ToString(_travelTime);
                    int _tierIndex = Convert.ToInt32(_wipProperties[3]);
                    double _lifterTravelDistance = Math.Abs(TIER_POSITION[_tierIndex] - TIER_POSITION[0]);
                    double[] _lifterAProperties = pro.GetPropertiesOfMHE(data, "LIFTER_A_001");
                    double[] _lifterBProperties = pro.GetPropertiesOfMHE(data, "LIFTER_B_001");
                    double _lifterATravelTime = pro.GetTriangularTime(_lifterTravelDistance, _lifterAProperties[0], _lifterAProperties[2], _lifterAProperties[3]);
                    double _lifterBTravelTime = pro.GetTriangularTime(_lifterTravelDistance, _lifterBProperties[0], _lifterBProperties[2], _lifterBProperties[3]);
                    _sendLifterASignal.ValueExpression = Convert.ToString(_travelTime + _lifterATravelTime);
                    _sendLifterBSignal.ValueExpression = Convert.ToString(_travelTime + _lifterBTravelTime);
                    var _productType = _createStatement.GetProperty("ProductType");
                    _productType.Value = _wipProperties[1];
                    _delayStatement.DistributionExpression = Convert.ToString(_travelTime + _lifterBTravelTime);
                    asrsOut.Rebuild();
                }
                else if (_wipProperties[0] == "STORAGE")
                {
                    int _rowIndex;
                    if (!int.TryParse(_wipProperties[1], out _rowIndex) || _rowIndex < 0 || _rowIndex >= ROW_POSITION.Length)
                        throw new InvalidOperationException("Storage WIP target row is missing or invalid for " + _MHEList[i] + ". Check STORAGE_PLAN.");
                    double _targetPositionX = ROW_POSITION[_rowIndex];
                    double _travelDistance = Math.Abs(_targetPositionX - _currentPositionX);
                    double _travelTime = _travelDistance / _moveSpeedValue;
                    ISimComponent _shelf = simWorld.FindComponent("Shelf #" + Convert.ToString(_floor));
                    var _storageWIP = _shelf.GetProperty("ShuttleStorageWorkInProcess");
                    _storageWIP.Value = true;
                    IBehavior _behavior = _shelf.FindBehavior("ProcessExecutor");
                    IProcessExecutor _processExecutor = _behavior as IProcessExecutor;
                    IRoutine _routine = _processExecutor.ActiveProgram.Routines[0];
                    IProcessIfStatement _ifStatement = _routine.Statements[0] as IProcessIfStatement;
                    ISendSignalStatement _sendSignalStatement = _ifStatement.ThenScope.Statements[0] as ISendSignalStatement;
                    _sendSignalStatement.ValueExpression = Convert.ToString(_travelTime);
                    _shelf.Rebuild();
                }

                _MHE.Rebuild();
            }
        }

        private void SetLifterPropertyandWIP(JObject data, ISimWorld simWorld, IApplication app, string modelPath)
        {
            string[] _MHEList = pro.GetMHEList(data, MHE_TYPE);
            for (int i = 0; i < _MHEList.GetLength(0); i++)
            {
                string _mheType = pro.GetMHEType(data, _MHEList[i]);
                if (_mheType != "LIFTER_A" && _mheType != "LIFTER_B")
                {
                    continue;
                }

                ISimComponent _MHE = simWorld.FindComponent(_MHEList[i]);
                double[] _property = pro.GetPropertiesOfMHE(data, _MHEList[i]);
                double _moveSpeedValue = _property[0];
                double _transportSpeedValue = _property[1];
                double _accelerationValue = _property[2];
                double _decelerationValue = _property[3];
                double _capacityValue = _property[4];
                if (_property[0] != 0.0)
                {
                    var _moveSpeed = _MHE.GetProperty("MoveSpeed");
                    if (_moveSpeed != null)
                    {
                        _moveSpeed.Value = _property[0];
                    }
                }

                if (_property[1] != 0.0)
                {
                    var _transportSpeed = _MHE.GetProperty("MoveSpeedLoaded");
                    if (_transportSpeed != null)
                    {
                        _transportSpeed.Value = _property[1];
                    }
                }

                if (_property[2] != 0.0)
                {
                    var _acceleration = _MHE.GetProperty("Acceleration");
                    if (_acceleration != null)
                    {
                        _acceleration.Value = _property[2];
                    }
                }

                if (_property[3] != 0.0)
                {
                    var _deceleration = _MHE.GetProperty("Deceleration");
                    if (_deceleration != null)
                    {
                        _deceleration.Value = _property[3];
                    }
                }

                if (_property[4] != 0.0)
                {
                    var _capacity = _MHE.GetProperty("TransportCapacity");
                    if (_capacity != null)
                    {
                        _capacity.Value = Convert.ToInt32(_property[4]);
                    }
                }

                var sequenceProp = _MHE.GetProperty("Sequence");
                if (_mheType == "LIFTER_A")
                {
                    string lifterSequence = pro.GetCombinedLifterSequence(data, CurrentDateTimeUtc);
                    sequenceProp.Value = lifterSequence;
                }

                if (_mheType == "LIFTER_A")
                {
                    string[] _wipProperties = pro.GetLifterRetrievalWIPProperties(data, _mheType);
                    if (!string.IsNullOrEmpty(_wipProperties[0]))
                    {
                        double _currentPositionZ = Convert.ToDouble(_wipProperties[0]);
                        string _productType = _wipProperties[1];
                        double _targetPositionZ = 700.0;
                        double _travelDistance = Math.Abs(_currentPositionZ - _targetPositionZ);
                        double _decelerationDistance = (_moveSpeedValue * _moveSpeedValue) / (2.0 * _decelerationValue);
                        double _travelTime;
                        if (_travelDistance >= _decelerationDistance)
                        {
                            double _constantSpeedDistance = _travelDistance - _decelerationDistance;
                            double _constantSpeedTime = _constantSpeedDistance / _moveSpeedValue;
                            double _decelerationTime = _moveSpeedValue / _decelerationValue;
                            _travelTime = _constantSpeedTime + _decelerationTime;
                        }
                        else
                        {
                            _travelTime = Math.Sqrt((2.0 * _travelDistance) / _decelerationValue);
                        }

                        ISimComponent _ASRSOut = simWorld.FindComponent("ASRS Out");
                        var _lifterRetrievalWIP = _ASRSOut.GetProperty("LifterRetrievalWorkInProcess");
                        _lifterRetrievalWIP.Value = true;
                        IBehavior _behavior = _ASRSOut.FindBehavior("ProcessExecutor");
                        IProcessExecutor _processExecutor = _behavior as IProcessExecutor;
                        IRoutine _routine = _processExecutor.ActiveProgram.Routines[0];
                        IProcessIfStatement _ifStatement = _routine.Statements[0] as IProcessIfStatement;
                        ISendSignalStatement _sendSignalLifterA = _ifStatement.ThenScope.Statements[0] as ISendSignalStatement;
                        ISendSignalStatement _sendSignalLifterB = _ifStatement.ThenScope.Statements[1] as ISendSignalStatement;
                        ICreateProductStatement _createStatement = _ifStatement.ThenScope.Statements[2] as ICreateProductStatement;
                        IProcessDelayStatement _delayStatement = _ifStatement.ThenScope.Statements[3] as IProcessDelayStatement;
                        _sendSignalLifterA.ValueExpression = Convert.ToString(_travelTime);
                        _sendSignalLifterB.ValueExpression = Convert.ToString(_travelTime);
                        var _productTypeProperty = _createStatement.GetProperty("ProductType");
                        _productTypeProperty.Value = _productType;
                        _delayStatement.DistributionExpression = Convert.ToString(_travelTime);
                        _ASRSOut.Rebuild();
                    }
                }
                else if (_mheType == "LIFTER_B")
                {
                    string[] _wipProperties = pro.GetLifterStorageWIPProperties(data, _mheType);
                    if (!string.IsNullOrEmpty(_wipProperties[0]))
                    {
                        double _currentPositionZ = Convert.ToDouble(_wipProperties[0]);
                        int _tierId = Convert.ToInt32(_wipProperties[1]);
                        int _columnId = Convert.ToInt32(_wipProperties[2]);
                        int _rowId = Convert.ToInt32(_wipProperties[3]);
                        string _productType = _wipProperties[4];
                        double _targetPositionZ = TIER_POSITION[_tierId];
                        double _travelDistance = Math.Abs(_currentPositionZ - _targetPositionZ);
                        double _decelerationDistance = (_moveSpeedValue * _moveSpeedValue) / (2.0 * _decelerationValue);
                        double _travelTime;
                        if (_travelDistance >= _decelerationDistance)
                        {
                            double _constantSpeedDistance = _travelDistance - _decelerationDistance;
                            double _constantSpeedTime = _constantSpeedDistance / _moveSpeedValue;
                            double _decelerationTime = _moveSpeedValue / _decelerationValue;
                            _travelTime = _constantSpeedTime + _decelerationTime;
                        }
                        else
                        {
                            _travelTime = Math.Sqrt((2.0 * _travelDistance) / _decelerationValue);
                        }

                        int _storagePosition = _rowId * 4 + _columnId + 1;
                        string _terminalConveyorName = "Lifter_WIP #" + Convert.ToString(_tierId + 5);
                        ISimComponent _terminalConveyor = simWorld.FindComponent(_terminalConveyorName);
                        var _lifterStorageWIP = _terminalConveyor.GetProperty("LifterStorageWorkInProcess");
                        _lifterStorageWIP.Value = true;
                        IBehavior _behavior = _terminalConveyor.FindBehavior("ProcessExecutor");
                        IProcessExecutor _processExecutor = _behavior as IProcessExecutor;
                        IRoutine _routine = _processExecutor.ActiveProgram.Routines[0];
                        IProcessWhileStatement _whileStatement = _routine.Statements[0] as IProcessWhileStatement;
                        IProcessIfStatement _ifStatement = _whileStatement.Scope.Statements[1] as IProcessIfStatement;
                        IProcessIfStatement _innerIfStatement = _ifStatement.ThenScope.Statements[0] as IProcessIfStatement;
                        ISendSignalStatement _sendStoragePosition = _innerIfStatement.ThenScope.Statements[1] as ISendSignalStatement;
                        ISendSignalStatement _sendTravelTime1 = _innerIfStatement.ThenScope.Statements[2] as ISendSignalStatement;
                        ISendSignalStatement _sendTravelTime2 = _innerIfStatement.ThenScope.Statements[3] as ISendSignalStatement;
                        ICreateProductStatement _createStatement = _innerIfStatement.ThenScope.Statements[4] as ICreateProductStatement;
                        _sendStoragePosition.ValueExpression = Convert.ToString(_storagePosition);
                        _sendTravelTime1.ValueExpression = Convert.ToString(_travelTime);
                        _sendTravelTime2.ValueExpression = Convert.ToString(_travelTime);
                        var _productTypeProperty = _createStatement.GetProperty("ProductType");
                        _productTypeProperty.Value = _productType;
                        _terminalConveyor.Rebuild();
                    }

                    _MHE.Rebuild();
                }
            }
        }

        private void EnsureAMRCompletionProperties(ISimComponent amr)
        {
            if (amr == null)
                return;
            EnsureProperty(amr, typeof(string), "LastCompletedMaterial", "");
            EnsureProperty(amr, typeof(string), "LastCompletedAction", "");
            EnsureProperty(amr, typeof(string), "LastCompletedLocation", "");
            EnsureProperty(amr, typeof(double), "LastCompletionTime", 0.0);
        }

        private void EnsureProperty(IPropertyContainer container, Type valueType, string name, object defaultValue)
        {
            if (container.GetProperty(name) != null)
                return;
            IProperty property = container.CreateProperty(valueType, PropertyConstraintType.NotSpecified, name);
            property.Value = defaultValue;
        }

        private void EnsureAMRCompletionEventBridge(ISimComponent controller)
        {
            if (controller == null)
                return;
            IPythonScript taskLogic = controller.FindBehavior("TaskLogic") as IPythonScript;
            if (taskLogic == null || string.IsNullOrEmpty(taskLogic.Script))
                return;
            const string marker = "# [CODEX AMR COMPLETION BRIDGE]";
            if (taskLogic.Script.Contains(marker))
                return;
            const string insertBefore = "def consume_sequence_completion_events():";
            const string publishBefore = "  idx_p.Value = current_idx + 1";
            if (!taskLogic.Script.Contains(insertBefore) || !taskLogic.Script.Contains(publishBefore))
                throw new InvalidOperationException("AMR TaskLogic completion hook point was not found.");
            string bridge = marker + "\n" + "def publish_amr_completion(resource, item, action):\n" + "  resource_comp = resource.component\n" + "  material_p = resource_comp.getProperty('LastCompletedMaterial')\n" + "  if not material_p:\n" + "    material_p = resource_comp.createProperty(VC_STRING, 'LastCompletedMaterial')\n" + "  action_p = resource_comp.getProperty('LastCompletedAction')\n" + "  if not action_p:\n" + "    action_p = resource_comp.createProperty(VC_STRING, 'LastCompletedAction')\n" + "  location_p = resource_comp.getProperty('LastCompletedLocation')\n" + "  if not location_p:\n" + "    location_p = resource_comp.createProperty(VC_STRING, 'LastCompletedLocation')\n" + "  time_p = resource_comp.getProperty('LastCompletionTime')\n" + "  if not time_p:\n" + "    time_p = resource_comp.createProperty(VC_REAL, 'LastCompletionTime')\n" + "  # Force a notification even when two different actions finish at the same simulation time.\n" + "  time_p.Value = -1.0\n" + "  material_p.Value = str(item.get('material', ''))\n" + "  action_p.Value = str(action)\n" + "  location_p.Value = str(item.get('location', ''))\n" + "  # Set the trigger property last so C# always reads a complete payload.\n" + "  time_p.Value = sim.SimTime\n" + "\n\n";
            taskLogic.Script = taskLogic.Script.Replace(insertBefore, bridge + insertBefore);
            taskLogic.Script = taskLogic.Script.Replace(publishBefore, "  publish_amr_completion(resource, item, action)\n\n" + publishBefore);
            controller.Rebuild();
        }
    }

    public class TaskDefinition
    {
        public string GetConditionExpression(string materialId)
        {
            string result = "ProductIn.ProductType.Name == " + "\"" + materialId + "\"";
            return result;
        }

        public (int bays, int tiers, double bayHeight, double bayWidth, double frameWidth) GetSizeOfBuffer(int capacity, int existingBays, int existingTiers, double existingBayHeight, double existingBayWidth, double existingFrameWidth)
        {
            var(bays, tiers) = FindClosestSquareDimensions(capacity);
            double bayHeight = existingBayHeight * existingTiers / tiers;
            double bayWidth = existingBayWidth * existingBays / bays;
            double frameWidth = existingFrameWidth * existingTiers / tiers;
            return (bays, tiers, bayHeight, bayWidth, frameWidth);
        }

        static (int width, int height) FindClosestSquareDimensions(int n)
        {
            int closestWidth = 1;
            int closestHeight = n;
            int minDifference = n;
            for (int i = 1; i * i <= n; i++)
            {
                if (n % i == 0)
                {
                    int width = i;
                    int height = n / i;
                    int difference = Math.Abs(width - height);
                    if (difference < minDifference)
                    {
                        minDifference = difference;
                        closestWidth = width;
                        closestHeight = height;
                    }
                }
            }

            return (closestWidth, closestHeight);
        }

        public double GetBatchInterval(string[] operationTimeJigFIx, string[] volume)
        {
            double result = 0;
            for (int i = 0; i < operationTimeJigFIx.GetLength(0); i++)
            {
                result = result + Convert.ToDouble(operationTimeJigFIx[i]) * Convert.ToDouble(volume[i]);
            }

            return result;
        }

        public string GetTaskWipSynchronization(string[][] workInProcess)
        {
            string result = null;
            for (int i = 0; i < workInProcess.GetLength(0); i++)
            {
                result = result + workInProcess[i][0] + "," + workInProcess[i][1];
                if (i != workInProcess.GetLength(0) - 1)
                {
                    result = result + "\n";
                }
            }

            return result;
        }

        public string GetTaskSourceConfiguration(string[] inventoryList)
        {
            string result = null;
            for (int i = 0; i < inventoryList.GetLength(0); i++)
            {
                result = result + inventoryList[i] + ",1";
                if (i != inventoryList.GetLength(0) - 1)
                {
                    result = result + "\n";
                }
            }

            return result;
        }

        public (string mt, string feeder) GetAMRWIPMaterialByOperationId(string operationInfoId)
        {
            if (string.IsNullOrEmpty(operationInfoId))
                return ("", "");
            var parts = operationInfoId.Split('_');
            if (parts.Length < 3)
                return ("", "");
            string firstX = parts[1];
            int secondX = int.Parse(parts[2]);
            string mt = $"AMR_WIP_MT_{firstX}";
            string feeder = "";
            switch (firstX)
            {
                case "A":
                case "B":
                case "C":
                    if (secondX == 3)
                        feeder = "AMR_WIP_FEEDER_A,C";
                    else if (secondX == 5)
                        feeder = "AMR_WIP_FEEDER_ASRS";
                    break;
                case "D":
                case "E":
                    if (secondX == 3)
                        feeder = "AMR_WIP_FEEDER_B";
                    else if (secondX == 5)
                        feeder = "AMR_WIP_FEEDER_ASRS";
                    break;
                case "F":
                case "G":
                    if (secondX == 3)
                        feeder = "AMR_WIP_FEEDER_D";
                    else if (secondX == 6)
                        feeder = "AMR_WIP_FEEDER_ASRS";
                    break;
            }

            if (feeder == "AMR_WIP_FEEDER_ASRS")
            {
                mt = $"ASRS_{firstX}";
            }

            return (mt, feeder);
        }

        public double GetLiftTravelTime(double targetZ, double moveSpeed, double accSpeed, double decSpeed)
        {
            double travelDist = Math.Abs(targetZ - 0.0);
            double moveTime = GetTriangularTime(travelDist, moveSpeed, accSpeed, decSpeed);
            return moveTime;
        }

        public double GetTriangularTime(double travelDist, double moveSpeed, double accSpeed, double decSpeed)
        {
            double dAcc = (moveSpeed * moveSpeed) / (2 * accSpeed);
            double dDec = (moveSpeed * moveSpeed) / (2 * decSpeed);
            double totalTime = 0;
            double tAcc = moveSpeed / accSpeed;
            double tDec = moveSpeed / decSpeed;
            double tCruise = 0;
            double cruiseDist = travelDist - dAcc - dDec;
            if (cruiseDist > 0)
            {
                tCruise = cruiseDist / moveSpeed;
                totalTime = tAcc + tCruise + tDec;
            }
            else
            {
                moveSpeed = Math.Sqrt(2 * travelDist * (accSpeed * decSpeed) / (accSpeed + decSpeed));
                tAcc = moveSpeed / accSpeed;
                tDec = moveSpeed / decSpeed;
                totalTime = tAcc + tDec;
            }

            return totalTime;
        }

        public string GetMaterialType(string operationInfoId)
        {
            if (string.IsNullOrEmpty(operationInfoId))
                return "";
            var parts = operationInfoId.Split('_');
            if (parts.Length < 3)
                return "";
            string firstX = parts[1];
            return $"MT_{firstX}";
        }

        public (double, double) GetAMRWipNodeCoordinate(double x, double y, string feeder)
        {
            double node_x;
            double node_y;
            if (feeder == "AMR_WIP_FEEDER_ASRS")
            {
                if (x <= 8000)
                {
                    node_x = x + 50;
                    node_y = y + 50;
                }
                else
                {
                    node_x = x - 50;
                    node_y = y + 50;
                }
            }
            else
            {
                node_x = x - 50;
                node_y = y - 50;
            }

            return (node_x, node_y);
        }

        public string GetPartsExitedConditionExpression(int partsExitedNum, string materialId)
        {
            string result = "PartsExitedNum == " + Convert.ToString(partsExitedNum);
            return result;
        }

        public string GetPartsExitedProductType(string materialId)
        {
            var parts = materialId.Split('_');
            string X = parts[1];
            string mt = $"AMR_WIP_MT_{X}";
            return mt;
        }

        public string GetWipNodeIdFromOutBuffer(string bufferId)
        {
            int outIndex = bufferId.IndexOf("_OUT_", StringComparison.OrdinalIgnoreCase);
            if (outIndex < 0)
                return "";
            return bufferId.Substring(0, outIndex) + "_WIP";
        }

        public string GetCNCWipProductType(string materialId, string machineInstanceId)
        {
            if (string.IsNullOrEmpty(materialId) || string.IsNullOrEmpty(machineInstanceId))
            {
                return materialId;
            }

            bool isTargetMaterial = materialId.Equals("MT_A", StringComparison.OrdinalIgnoreCase) || materialId.Equals("MT_B", StringComparison.OrdinalIgnoreCase) || materialId.Equals("MT_C", StringComparison.OrdinalIgnoreCase);
            if (!isTargetMaterial)
            {
                return materialId;
            }

            if (machineInstanceId.Equals("CNC_A_001", StringComparison.OrdinalIgnoreCase))
            {
                return materialId.ToUpperInvariant() + "_A";
            }

            if (machineInstanceId.Equals("CNC_A_002", StringComparison.OrdinalIgnoreCase))
            {
                return materialId.ToUpperInvariant() + "_B";
            }

            return materialId;
        }
    }
}
