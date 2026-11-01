using Caliburn.Micro;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using VisualComponents.Create3D;
using VisualComponents.UX.Shared;

namespace DT_KETI
{
    public class JsonPreprocessing
    {
        public string[] GetProductListForJigFix(JObject data, string JigFixType)
        {
            int count = 0;
            foreach (var item2 in data["PROCESS_INFO"])
            {
                foreach (var element2 in item2["OPERATION_INFO"])
                {
                    var _machineTypeId = (string)element2["MACHINE_TYPE_ID"];
                    if (_machineTypeId == JigFixType)
                    {
                        count++;
                    }
                }
            }

            string[] result = new string[count];
            count = 0;
            foreach (var item2 in data["PROCESS_INFO"])
            {
                foreach (var element2 in item2["OPERATION_INFO"])
                {
                    var _machineTypeId = (string)element2["MACHINE_TYPE_ID"];
                    if (_machineTypeId == JigFixType)
                    {
                        var _productId = (string)item2["PRODUCT_ID"];
                        result[count] = _productId;
                        count++;
                    }
                }
            }

            return result;
        }

        public string[] GetProductList(JObject data)
        {
            int count = 0;
            foreach (var item in data["PRODUCT_INFO"])
            {
                count++;
            }

            string[] result = new string[count];
            count = 0;
            foreach (var item in data["PRODUCT_INFO"])
            {
                var _productId = (string)item["PRODUCT_ID"];
                result[count] = _productId;
                count++;
            }

            return result;
        }

        public string[][] GetWorkInProcessBuffer(JObject data, string bufferId)
        {
            foreach (var item in data["BUFFER_INSTANCE"])
            {
                string bufferInstanceId = Convert.ToString(item["BUFFER_INSTANCE_ID"]);
                if (bufferInstanceId != bufferId)
                {
                    continue;
                }

                JArray bufferStatuses = item["BUFFER_STATUS"] as JArray;
                if (bufferStatuses == null)
                {
                    return new string[0][];
                }

                string bufferTypeId = Convert.ToString(item["BUFFER_TYPE_ID"]);
                string suffix = "";
                if (bufferTypeId == "BT_A_IN" || bufferTypeId == "BT_A_OUT")
                {
                    suffix = "_A";
                }
                else if (bufferTypeId == "BT_C_IN" || bufferTypeId == "BT_C_OUT")
                {
                    suffix = "_B";
                }

                List<string[]> wipList = new List<string[]>();
                for (int i = 0; i < bufferStatuses.Count; i++)
                {
                    string materialId = Convert.ToString(bufferStatuses[i]["MATERIAL_ID"]);
                    int stockNum = Convert.ToInt32(bufferStatuses[i]["STOCK_NUM"]);
                    JArray slotInfos = bufferStatuses[i]["SLOT_INFO"] as JArray;
                    if (slotInfos == null || slotInfos.Count == 0)
                    {
                        continue;
                    }

                    int wipCount = Math.Min(stockNum, slotInfos.Count);
                    for (int j = 0; j < wipCount; j++)
                    {
                        string queueDateTime = Convert.ToString(slotInfos[j]["QUEUE_DATETIME"]);
                        string[] wip = new string[3];
                        wip[0] = materialId + suffix;
                        wip[1] = "1";
                        wip[2] = queueDateTime;
                        wipList.Add(wip);
                    }
                }

                wipList = wipList.OrderBy(x => Convert.ToDateTime(x[2])).ToList();
                string[][] result = new string[wipList.Count][];
                for (int i = 0; i < wipList.Count; i++)
                {
                    result[i] = new string[2];
                    result[i][0] = wipList[i][0];
                    result[i][1] = wipList[i][1];
                }

                return result;
            }

            return new string[0][];
        }

        public string[][] GetMaterialList(JObject data, string[] productList)
        {
            string[][] result = new string[productList.GetLength(0)][];
            for (int i = 0; i < productList.GetLength(0); i++)
            {
                List<string> materialList = new List<string>();
                foreach (var product in data["PRODUCT_INFO"])
                {
                    string productId = Convert.ToString(product["PRODUCT_ID"]);
                    if (productId != productList[i])
                        continue;
                    JArray materialInfo = product["MATERIAL_INFO"] as JArray;
                    if (materialInfo == null)
                        break;
                    foreach (var material in materialInfo)
                    {
                        string materialId = Convert.ToString(material["MATERIAL_ID"]);
                        if (!string.IsNullOrEmpty(materialId))
                            materialList.Add(materialId);
                    }

                    break;
                }

                result[i] = materialList.ToArray();
            }

            return result;
        }

        public Dictionary<string, string> CreateMaterialInstanceNameMap(JObject data)
        {
            Dictionary<string, int> materialCount = new Dictionary<string, int>();
            Dictionary<string, string> locationToInstanceName = new Dictionary<string, string>();
            var locations = new List<(int tier, int row, int column, string locationId, string materialId)>();
            foreach (var warehouse in data["ASRS_INSTANCE"])
            {
                foreach (var inventory in warehouse["INVENTORY_STATUS"])
                {
                    string materialId = (string)inventory["MATERIAL_ID"];
                    foreach (var location in inventory["LOCATION_INFO"])
                    {
                        int tier = int.Parse((string)location["TIER_ID"]);
                        int row = int.Parse((string)location["ROW_ID"]);
                        int column = int.Parse((string)location["COLUMN_ID"]);
                        string locationId = (string)location["LOCATION_INFO_ID"];
                        locations.Add((tier, row, column, locationId, materialId));
                    }
                }
            }

            var sortedLocations = locations.OrderBy(x => x.tier).ThenBy(x => x.row).ThenBy(x => x.column).ThenBy(x => x.locationId).ToList();
            foreach (var item in sortedLocations)
            {
                if (!materialCount.ContainsKey(item.materialId))
                {
                    materialCount[item.materialId] = 0;
                }

                materialCount[item.materialId]++;
                string instanceName = item.materialId + "_" + materialCount[item.materialId].ToString("D3");
                string key = item.tier + "_" + item.row + "_" + item.column + "_" + item.locationId;
                locationToInstanceName[key] = instanceName;
            }

            return locationToInstanceName;
        }

        public string[] GetCNCMachineList(JObject data, string[] CNCTypeList)
        {
            int count = 0;
            for (int i = 0; i < CNCTypeList.GetLength(0); i++)
            {
                foreach (var item in data["MACHINE_INSTANCE"])
                {
                    string machineTypeId = (string)item["MACHINE_TYPE_ID"];
                    if (CNCTypeList[i] == machineTypeId)
                    {
                        count++;
                    }
                }
            }

            string[] result = new string[count];
            int resultIndex = 0;
            foreach (var item in data["MACHINE_INSTANCE"])
            {
                string machineTypeId = (string)item["MACHINE_TYPE_ID"];
                if (CNCTypeList.Contains(machineTypeId))
                {
                    string machineInstanceId = (string)item["MACHINE_INSTANCE_ID"];
                    result[resultIndex++] = machineInstanceId;
                }
            }

            return result;
        }

        public string[] GetInventoryListforASRS(JObject data, Dictionary<string, string> materialInstanceMap, int rowCount, int columnCount, int tier)
        {
            string[] result = new string[rowCount * columnCount];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = "dummy";
            }

            int lastUsedIndex = -1;
            foreach (var warehouse in data["ASRS_INSTANCE"])
            {
                foreach (var inventory in warehouse["INVENTORY_STATUS"])
                {
                    foreach (var location in inventory["LOCATION_INFO"])
                    {
                        int locColumn = int.Parse((string)location["COLUMN_ID"]);
                        int locTier = int.Parse((string)location["TIER_ID"]);
                        int locRow = int.Parse((string)location["ROW_ID"]);
                        string locationId = (string)location["LOCATION_INFO_ID"];
                        if (locTier != tier)
                            continue;
                        int index = locRow * columnCount + locColumn;
                        string productName;
                        if (location["FINISHED_OPERATION_INFO_ID"] != null)
                        {
                            string finishedId = (string)location["FINISHED_OPERATION_INFO_ID"];
                            string middleChar = finishedId.Split('_')[1];
                            productName = "Complete_" + middleChar;
                        }
                        else
                        {
                            string key = locTier + "_" + locRow + "_" + locColumn + "_" + locationId;
                            productName = materialInstanceMap[key];
                        }

                        result[index] = productName;
                        if (index > lastUsedIndex)
                            lastUsedIndex = index;
                    }
                }
            }

            if (lastUsedIndex == -1)
                return new string[0];
            return result.Take(lastUsedIndex + 1).ToArray();
        }

        public int[] GetInventoryNum(JObject data, string[] productList)
        {
            int[] result = new int[productList.GetLength(0)];
            for (int i = 0; i < productList.GetLength(0); i++)
            {
                foreach (var item in data["BUFFER_INSTANCE"])
                {
                    foreach (var element in item["WORK_IN_PROCESS_STATUS"])
                    {
                        var _finishedOperationInfoId = (string)element["FINISHED_OPERATION_INFO_ID"];
                        foreach (var item2 in data["PROCESS_INFO"])
                        {
                            foreach (var element2 in item2["OPERATION_INFO"])
                            {
                                var _operationInfoId = (string)element2["OPERATION_INFO_ID"];
                                if (_finishedOperationInfoId == _operationInfoId)
                                {
                                    var _productId = (string)item2["PRODUCT_ID"];
                                    if (productList[i] == _productId)
                                    {
                                        var _workInProcessNum = (int)element["WORK_IN_PROCESS_NUM"];
                                        result[i] = Math.Max(_workInProcessNum, 0);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        public (string materialId, double remainingProcessingTime) GetWorkInProcessProperties(JObject data, string machineId, int unitOfTime)
        {
            string materialId = null;
            double remainingProcessingTime = 0;
            bool _wip = false;
            foreach (var item in data["MACHINE_INSTANCE"])
            {
                var _machineId = (string)item["MACHINE_INSTANCE_ID"];
                if (_machineId == machineId)
                {
                    foreach (JProperty element in item)
                    {
                        string _workInProcessCheck = element.Name;
                        if (_workInProcessCheck == "WORK_IN_PROCESS_STATUS")
                        {
                            _wip = true;
                        }
                    }
                }
            }

            if (_wip)
            {
                foreach (var item in data["MACHINE_INSTANCE"])
                {
                    var _machineId = (string)item["MACHINE_INSTANCE_ID"];
                    if (_machineId == machineId)
                    {
                        foreach (var element in item["WORK_IN_PROCESS_STATUS"])
                        {
                            var _operationInfoIdFK = (string)element["OPERATION_INFO_ID"];
                            var _processedTime = (double)element["PROCESSED_TIME"];
                            foreach (var item2 in data["PROCESS_INFO"])
                            {
                                foreach (var element2 in item2["OPERATION_INFO"])
                                {
                                    var _operationInfoId = (string)element2["OPERATION_INFO_ID"];
                                    if (_operationInfoId == _operationInfoIdFK)
                                    {
                                        var _processingTime = (double)element2["PROCESSING_TIME"];
                                        remainingProcessingTime = (_processingTime - _processedTime) * unitOfTime;
                                        foreach (var element3 in item2["CONSUMED_MATERIAL_INFO"])
                                        {
                                            var _materialId = (string)element3["MATERIAL_ID"];
                                            materialId = _materialId;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return (materialId, remainingProcessingTime);
        }

        public double GetFailureStatus(JObject data, string machineId, double unitOfTime, string currentDateTime)
        {
            double result = 0.0;
            foreach (var item in data["MACHINE_INSTANCE"])
            {
                string _machineId = Convert.ToString(item["MACHINE_INSTANCE_ID"]);
                if (_machineId != machineId)
                {
                    continue;
                }

                JArray failureStatusArray = item["FAILURE_STATUS"] as JArray;
                if (failureStatusArray == null || !failureStatusArray.HasValues)
                {
                    return result;
                }

                string _machineTypeIdFK = Convert.ToString(item["MACHINE_TYPE_ID"]);
                foreach (var _failureStatus in failureStatusArray)
                {
                    JToken eventToken = _failureStatus["EVENT_DATETIME"];
                    DateTimeOffset eventDateTime;
                    if (eventToken != null && eventToken.Type == JTokenType.Date)
                        eventDateTime = eventToken.ToObject<DateTimeOffset>();
                    else if (!DateTimeOffset.TryParse((string)eventToken, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out eventDateTime))
                    {
                        continue;
                    }

                    foreach (var item2 in data["MACHINE_TYPE"])
                    {
                        string _machineTypeId = Convert.ToString(item2["MACHINE_TYPE_ID"]);
                        if (_machineTypeId != _machineTypeIdFK)
                        {
                            continue;
                        }

                        double _meanTimeToRepair = Convert.ToDouble(item2["MEAN_TIME_TO_REPAIR"]);
                        DateTimeOffset snapshotTime = DateTimeOffset.Parse(currentDateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
                        double elapsedTime = (snapshotTime - eventDateTime).TotalSeconds;
                        double remainingRepairTime = _meanTimeToRepair - elapsedTime;
                        if (remainingRepairTime > 0)
                        {
                            result = remainingRepairTime * unitOfTime;
                        }
                        else
                        {
                            result = 0.0;
                        }

                        break;
                    }
                }

                break;
            }

            return result;
        }

        public string[] GetProductionVolume(JObject data, string[] productList, int[] inventoryNumList)
        {
            string[] result = new string[productList.GetLength(0)];
            for (int i = 0; i < productList.GetLength(0); i++)
            {
                foreach (var item in data["PRODUCTION_PLAN"])
                {
                    var _productId = (string)item["PRODUCT_ID"];
                    if (_productId == productList[i])
                    {
                        var _productionVolume = (int)item["PRODUCTION_VOLUME"];
                        var _currentVolume = (int)item["CURRENT_VOLUME"];
                        int _finalVolume = _productionVolume - _currentVolume - inventoryNumList[i];
                        result[i] = Convert.ToString(Math.Max(_finalVolume, 0));
                    }
                }
            }

            return result;
        }

        public string[][] GetFlowList(JObject data, string[] productList, string[][] materialList, string[] CNCTypeList)
        {
            int NUM_FLOW = CNCTypeList.GetLength(0);
            int processCount = 0;
            foreach (var item in data["PROCESS_INFO"])
            {
                processCount++;
            }

            string[][] result = new string[NUM_FLOW][];
            for (int j = 0; j < NUM_FLOW; j++)
            {
                int count = 0;
                string[] temp = new string[processCount];
                for (int i = 0; i < productList.GetLength(0); i++)
                {
                    foreach (var item in data["PROCESS_INFO"])
                    {
                        var _productId = (string)item["PRODUCT_ID"];
                        var _processInfoId = (string)item["PROCESS_INFO_ID"];
                        if (_productId == productList[i])
                        {
                            foreach (var element in item["OPERATION_INFO"])
                            {
                                var _operationInfoId = (string)element["OPERATION_INFO_ID"];
                                var _machineTypeId = (string)element["MACHINE_TYPE_ID"];
                                if (_machineTypeId == CNCTypeList[j])
                                {
                                    temp[count] = materialList[i][0];
                                    count++;
                                }
                            }
                        }
                    }
                }

                result[j] = new string[count];
                for (int i = 0; i < result[j].GetLength(0); i++)
                {
                    result[j][i] = temp[i];
                }
            }

            return result;
        }

        public string[] GetMobileManipulatorList(JObject data, string MMType)
        {
            int count = 0;
            foreach (var item2 in data["MHV_INSTANCE"])
            {
                var _mhvTypeIdFK = (string)item2["MHV_TYPE_ID"];
                foreach (var item in data["MHV_TYPE"])
                {
                    var _resourceType = (string)item["RESOURCE_TYPE"];
                    var _mhvTypeId = (string)item["MHV_TYPE_ID"];
                    if (_resourceType == MMType && _mhvTypeId == _mhvTypeIdFK)
                    {
                        count++;
                    }
                }
            }

            string[] result = new string[count];
            int i = 0;
            foreach (var item2 in data["MHV_INSTANCE"])
            {
                var _mhvTypeIdFK = (string)item2["MHV_TYPE_ID"];
                foreach (var item in data["MHV_TYPE"])
                {
                    var _resourceType = (string)item["RESOURCE_TYPE"];
                    var _mhvTypeId = (string)item["MHV_TYPE_ID"];
                    if (_resourceType == MMType && _mhvTypeId == _mhvTypeIdFK)
                    {
                        var _mhvInstanceId = (string)item2["MHV_INSTANCE_ID"];
                        result[i] = _mhvInstanceId;
                        i++;
                    }
                }
            }

            return result;
        }

        public double[] GetPropertiesOfMHE(JObject data, string mheId)
        {
            double[] result = new double[5];
            foreach (var instance in data["MHE_INSTANCE"])
            {
                string instanceId = (string)instance["MHE_INSTANCE_ID"];
                if (instanceId == mheId)
                {
                    string typeIdFK = (string)instance["MHE_TYPE_ID"];
                    foreach (var type in data["MHE_TYPE"])
                    {
                        string typeId = (string)type["MHE_TYPE_ID"];
                        if (typeId == typeIdFK)
                        {
                            result[0] = type["MOVE_SPEED"] != null ? (double)type["MOVE_SPEED"] : 0.0;
                            result[1] = type["TRANSPORTATION_SPEED"] != null ? (double)type["TRANSPORTATION_SPEED"] : 0.0;
                            result[2] = type["ACC_SPEED"] != null ? (double)type["ACC_SPEED"] : 0.0;
                            result[3] = type["DEC_SPEED"] != null ? (double)type["DEC_SPEED"] : 0.0;
                            result[4] = type["CAPACITY"] != null ? (double)type["CAPACITY"] : 0.0;
                            break;
                        }
                    }

                    break;
                }
            }

            return result;
        }

        public string[] GetOperationTimeJigFix(JObject data, string[] productList, string JigFixType, int unitOfTime)
        {
            string[] result = new string[productList.GetLength(0)];
            for (int i = 0; i < productList.GetLength(0); i++)
            {
                foreach (var item in data["PROCESS_INFO"])
                {
                    var _productId = (string)item["PRODUCT_ID"];
                    if (_productId == productList[i])
                    {
                        foreach (var element in item["OPERATION_INFO"])
                        {
                            var _machineTypeId = (string)element["MACHINE_TYPE_ID"];
                            if (_machineTypeId == JigFixType)
                            {
                                var _processingTime = (double)element["PROCESSING_TIME"];
                                result[i] = Convert.ToString(_processingTime * unitOfTime);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public string GetJigFixId(JObject data, string JigFixType)
        {
            string result = null;
            foreach (var item in data["MACHINE_INSTANCE"])
            {
                var _machineTypeId = (string)item["MACHINE_TYPE_ID"];
                if (_machineTypeId == JigFixType)
                {
                    var _machineInstanceId = (string)item["MACHINE_INSTANCE_ID"];
                    result = _machineInstanceId;
                }
            }

            return result;
        }

        public string[] GetMHEList(JObject data, string[] mheTypes)
        {
            int count = 0;
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string mheTypeId = (string)mhe["MHE_TYPE_ID"];
                if (mheTypes.Contains(mheTypeId))
                {
                    count++;
                }
            }

            string[] result = new string[count];
            int i = 0;
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string mheTypeId = (string)mhe["MHE_TYPE_ID"];
                if (mheTypes.Contains(mheTypeId))
                {
                    result[i] = (string)mhe["MHE_INSTANCE_ID"];
                    i++;
                }
            }

            return result;
        }

        public string GetMHEType(JObject data, string mheID)
        {
            string result = null;
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string mheInstanceId = (string)mhe["MHE_INSTANCE_ID"];
                if (mheInstanceId == mheID)
                {
                    result = (string)mhe["MHE_TYPE_ID"];
                    break;
                }
            }

            return result;
        }

        public string[] GetBufferInstanceIdList(JObject data)
        {
            int count = 0;
            foreach (var item in data["BUFFER_INSTANCE"])
            {
                count++;
            }

            string[] result = new string[count];
            int i = 0;
            foreach (var item in data["BUFFER_INSTANCE"])
            {
                var _bufferId = (string)item["BUFFER_INSTANCE_ID"];
                result[i] = _bufferId;
                i++;
            }

            return result;
        }

        public int GetBufferCapacity(JObject data, string bufferId)
        {
            int result = 0;
            foreach (var item in data["BUFFER_INSTANCE"])
            {
                var _bufferInstanceId = (string)item["BUFFER_INSTANCE_ID"];
                if (_bufferInstanceId == bufferId)
                {
                    var _bufferTypeIdFK = (string)item["BUFFER_TYPE_ID"];
                    foreach (var item2 in data["BUFFER_TYPE"])
                    {
                        var _bufferTypeId = (string)item2["BUFFER_TYPE_ID"];
                        if (_bufferTypeId == _bufferTypeIdFK)
                        {
                            var _capacity = (int)item2["CAPACITY"];
                            result = _capacity;
                        }
                    }
                }
            }

            return result;
        }

        public double[][] GetFailureValues(JObject data, string[] machineList, int unitOfTime)
        {
            double[][] result = new double[machineList.GetLength(0)][];
            for (int i = 0; i < result.GetLength(0); i++)
            {
                result[i] = new double[2];
            }

            for (int i = 0; i < machineList.GetLength(0); i++)
            {
                foreach (var item in data["MACHINE_INSTANCE"])
                {
                    var _machineInstanceId = (string)item["MACHINE_INSTANCE_ID"];
                    if (_machineInstanceId == machineList[i])
                    {
                        var _machineTypeIdFK = (string)item["MACHINE_TYPE_ID"];
                        foreach (var item2 in data["MACHINE_TYPE"])
                        {
                            var _machineTypeId = (string)item2["MACHINE_TYPE_ID"];
                            if (_machineTypeId == _machineTypeIdFK)
                            {
                                var _mttr = (double)item2["MEAN_TIME_TO_REPAIR"];
                                var _mtbf = (double)item2["MEAN_TIME_BETWEEN_FAILURE"];
                                result[i][0] = _mttr * unitOfTime;
                                result[i][1] = _mtbf * unitOfTime;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public string GetFlowTypeforFeeder(JObject data, string productId, string[] CNCType)
        {
            string result = null;
            foreach (var item in data["PROCESS_INFO"])
            {
                var _productId = (string)item["PRODUCT_ID"];
                if (_productId == productId)
                {
                    foreach (var element in item["OPERATION_INFO"])
                    {
                        var _machineTypeId = (string)element["MACHINE_TYPE_ID"];
                        for (int i = 0; i < CNCType.GetLength(0); i++)
                        {
                            if (_machineTypeId == CNCType[i])
                            {
                                result = _machineTypeId;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public double[] GetOperationTimeCNCWip(double remainingProcessingTime, float[] motionRatio, int unitOfTime)
        {
            double[] result = new double[motionRatio.GetLength(0)];
            for (int i = 0; i < motionRatio.GetLength(0); i++)
            {
                result[i] = Math.Round(remainingProcessingTime * motionRatio[i] * unitOfTime, 5);
            }

            return result;
        }

        public double[][] GetOperationTimeCNC(JObject data, string[][] flowList)
        {
            double[][] result = new double[flowList.Length][];
            for (int i = 0; i < flowList.Length; i++)
            {
                result[i] = new double[flowList[i].Length];
                for (int j = 0; j < flowList[i].Length; j++)
                {
                    string flowMaterial = flowList[i][j];
                    foreach (var processInfo in data["PROCESS_INFO"])
                    {
                        bool matchesMaterial = false;
                        foreach (var consumed in processInfo["CONSUMED_MATERIAL_INFO"])
                        {
                            if ((string)consumed["MATERIAL_ID"] == flowMaterial)
                            {
                                matchesMaterial = true;
                                break;
                            }
                        }

                        if (!matchesMaterial)
                            continue;
                        foreach (var operation in processInfo["OPERATION_INFO"])
                        {
                            result[i][j] = double.Parse((string)operation["PROCESSING_TIME"]);
                            break;
                        }
                    }
                }
            }

            return result;
        }

        public string GetCNCType(JObject data, string machineInstanceId)
        {
            string result = "";
            foreach (var machine in data["MACHINE_INSTANCE"])
            {
                string id = (string)machine["MACHINE_INSTANCE_ID"];
                if (id == machineInstanceId)
                {
                    result = (string)machine["MACHINE_TYPE_ID"];
                }
            }

            return result;
        }

        public List<string[]> GetAMRWIPProperties(JObject data, string mheInstanceId)
        {
            List<string[]> result = new List<string[]>();
            JArray mheInstanceArray = data["MHE_INSTANCE"] as JArray;
            foreach (JToken mhe in mheInstanceArray)
            {
                string currentMheInstanceId = Convert.ToString(mhe["MHE_INSTANCE_ID"]);
                if (currentMheInstanceId != mheInstanceId)
                {
                    continue;
                }

                JArray statusArray = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                if (statusArray == null || !statusArray.HasValues)
                {
                    return result;
                }

                foreach (JToken handlingStatus in statusArray)
                {
                    JArray materialInfoArray = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                    if (materialInfoArray == null || !materialInfoArray.HasValues)
                    {
                        continue;
                    }

                    foreach (JToken materialInfo in materialInfoArray)
                    {
                        string materialId = Convert.ToString(materialInfo["MATERIAL_ID"]);
                        string handlingMaterialInfoId = Convert.ToString(materialInfo["HANDLING_MATERIAL_INFO_ID"]);
                        string completedOperationId = Convert.ToString(materialInfo["COMPLETED_HANDLING_OPERATION_INFO_ID"]);
                        string[] operationParts = completedOperationId.Split('_');
                        int operationNumber = Convert.ToInt32(operationParts[operationParts.Length - 1]);
                        string wipPosition = operationNumber < 3 ? "BP" : "AP";
                        string machineInstanceId = GetAMRWIPMachineInstance(data, materialInfo, handlingMaterialInfoId, wipPosition);
                        string productType = materialId;
                        if (materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C")
                        {
                            if (machineInstanceId == "CNC_A_001")
                            {
                                productType = materialId + "_A";
                            }
                            else if (machineInstanceId == "CNC_A_002")
                            {
                                productType = materialId + "_B";
                            }
                        }

                        result.Add(new string[] { productType, wipPosition, materialId, handlingMaterialInfoId, machineInstanceId });
                    }
                }

                return result;
            }

            return result;
        }

        public string GetAMRWIPMachineInstance(JObject data, JToken materialInfo, string handlingMaterialInfoId, string wipPosition)
        {
            if (wipPosition == "BP")
            {
                foreach (var remainingPlan in data["HANDLING_REMAINING_PLAN"])
                {
                    string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                    if (remainingMaterialInfoId != handlingMaterialInfoId)
                    {
                        continue;
                    }

                    JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                    if (productionSchedules != null && productionSchedules.HasValues)
                    {
                        return Convert.ToString(productionSchedules[0]["MACHINE_INSTANCE_ID"]);
                    }

                    break;
                }

                return "";
            }

            string materialId = Convert.ToString(materialInfo["MATERIAL_ID"]);
            if (materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C")
            {
                return "CNC_A_001";
            }

            if (materialId == "MT_D" || materialId == "MT_E")
            {
                return "CNC_B_001";
            }

            if (materialId == "MT_F" || materialId == "MT_G")
            {
                return "CNC_D_001";
            }

            return "";
        }

        private string GetAMRWIPProductType(string materialId, string machineId)
        {
            if (materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C")
            {
                if (machineId == "CNC_A_001")
                    return materialId + "_A";
                if (machineId == "CNC_A_002")
                    return materialId + "_B";
            }

            return materialId;
        }

        public string GetAMRWIPMachineNodeName(string machineInstanceId)
        {
            if (machineInstanceId == "CNC_A_001")
            {
                return "CNCA";
            }
            else if (machineInstanceId == "CNC_B_001")
            {
                return "CNCB";
            }
            else if (machineInstanceId == "CNC_A_002")
            {
                return "CNCC";
            }
            else if (machineInstanceId == "CNC_D_001")
            {
                return "CNCD";
            }
            else if (machineInstanceId == "CNC_E_001")
            {
                return "CNCE";
            }

            return "";
        }

        public (double? x, double? y) GetMHEPosition(JObject data, string mheInstanceId)
        {
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string id = Convert.ToString(mhe["MHE_INSTANCE_ID"]);
                if (id != mheInstanceId)
                    continue;
                var currentPos = mhe["CURRENT_POSITION"];
                if (currentPos != null)
                {
                    string xStr = Convert.ToString(currentPos["CURRENT_POSITION_X"]);
                    string yStr = Convert.ToString(currentPos["CURRENT_POSITION_Y"]);
                    string zStr = Convert.ToString(currentPos["CURRENT_POSITION_Z"]);
                    if (!string.IsNullOrEmpty(xStr) && !string.IsNullOrEmpty(yStr))
                        return (Convert.ToDouble(xStr), Convert.ToDouble(yStr));
                    if (!string.IsNullOrEmpty(zStr))
                        return (Convert.ToDouble(zStr), null);
                }

                break;
            }

            return (null, null);
        }

        public List<string> GetShuttleIds(JObject data)
        {
            List<string> shuttleIds = new List<string>();
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string type = (string)mhe["MHE_TYPE_ID"];
                if (!string.IsNullOrEmpty(type) && type.StartsWith("SHUTTLE"))
                {
                    string id = (string)mhe["MHE_INSTANCE_ID"];
                    if (!string.IsNullOrEmpty(id))
                    {
                        shuttleIds.Add(id);
                    }
                }
            }

            return shuttleIds;
        }

        public (double time, string opId) GetShuttleWipInfo(JObject data, string shuttleID, double idleY, double moveSpeed, double accSpeed, double decSpeed)
        {
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                if ((string)mhe["MHE_INSTANCE_ID"] == shuttleID && mhe["MATERIAL_HANDLING_STATUS"] != null)
                {
                    foreach (var status in mhe["MATERIAL_HANDLING_STATUS"])
                    {
                        string opId = (string)status["OPERATION_INFO_ID"];
                        if (!opId.EndsWith("1"))
                        {
                            continue;
                        }

                        double currentY = double.Parse((string)status["CURRENT_POSITION"]["CURRENT_POSITION_Y"]);
                        double travelDist = Math.Abs(currentY - idleY);
                        double moveTime = GetTriangularTime(travelDist, moveSpeed, accSpeed, decSpeed);
                        return (moveTime, opId);
                    }
                }
            }

            return (double.MaxValue, null);
        }

        public (double loadTime, double unloadTime) GetLiftLoadUnloadTime(JObject data, string mheID)
        {
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                if ((string)mhe["MHE_INSTANCE_ID"] == mheID && mhe["MATERIAL_HANDLING_STATUS"] != null)
                {
                    foreach (var status in mhe["MATERIAL_HANDLING_STATUS"])
                    {
                        string opId = (string)status["OPERATION_INFO_ID"];
                        if (!string.IsNullOrEmpty(opId))
                        {
                            foreach (var mhi in data["MATERIAL_HANDLING_INFO"])
                            {
                                foreach (var op in mhi["OPERATION_INFO"])
                                {
                                    if ((string)op["OPERATION_INFO_ID"] == opId)
                                    {
                                        double loadTime = double.Parse(op["LOADING_TIME"].ToString());
                                        double unloadTime = double.Parse(op["UNLOADING_TIME"].ToString());
                                        return (loadTime, unloadTime);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return (0.0, 0.0);
        }

        public double GetLifterWipTime(JObject data, string mheID, double targetZ, double moveSpeed, double accSpeed, double decSpeed)
        {
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                if ((string)mhe["MHE_INSTANCE_ID"] == mheID && mhe["MATERIAL_HANDLING_STATUS"] != null)
                {
                    foreach (var status in mhe["MATERIAL_HANDLING_STATUS"])
                    {
                        double processedTime = double.Parse((string)status["PROCESSED_TIME"]);
                        double currentZ = double.Parse((string)status["CURRENT_POSITION"]["CURRENT_POSITION_Z"]);
                        double travelDist = Math.Abs(currentZ - targetZ);
                        double moveTime = GetTriangularTime(travelDist, moveSpeed, accSpeed, decSpeed);
                        string opId = (string)status["OPERATION_INFO_ID"];
                        var(loadStr, unloadStr) = FindOperationTimes(data, opId);
                        double loadTime = double.Parse(loadStr);
                        double unloadTime = double.Parse(unloadStr);
                        return moveTime + unloadTime;
                    }
                }
            }

            return 0.0;
        }

        private (string load, string unload) FindOperationTimes(JObject data, string operationId)
        {
            foreach (var mhi in data["MATERIAL_HANDLING_INFO"])
            {
                foreach (var op in mhi["OPERATION_INFO"])
                {
                    if ((string)op["OPERATION_INFO_ID"] == operationId)
                    {
                        string load = (string)op["LOADING_TIME"];
                        string unload = (string)op["UNLOADING_TIME"];
                        return (load, unload);
                    }
                }
            }

            return ("0", "0");
        }

        public double GetTriangularTime(double travelDist, double moveSpeed, double accSpeed, double decSpeed)
        {
            if (accSpeed <= 0 || decSpeed <= 0)
            {
                return (moveSpeed > 0) ? travelDist / moveSpeed : double.MaxValue;
            }

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

        public string[] GetShuttleWIPProperties(JObject data, string shuttleId)
        {
            string[] result = new string[5]
            {
                "",
                "",
                "",
                "",
                ""
            };
            if (data == null || string.IsNullOrWhiteSpace(shuttleId))
            {
                return result;
            }

            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            if (mheInstances == null)
            {
                return result;
            }

            JToken targetMhe = null;
            JToken handlingStatus = null;
            foreach (JToken mhe in mheInstances)
            {
                string mheInstanceId = Convert.ToString(mhe["MHE_INSTANCE_ID"]);
                if (!string.Equals(mheInstanceId, shuttleId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                targetMhe = mhe;
                JToken currentPosition = mhe["CURRENT_POSITION"];
                if (currentPosition != null)
                {
                    result[2] = Convert.ToString(currentPosition["CURRENT_POSITION_X"]);
                }

                JArray statusArray = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                if (statusArray != null && statusArray.HasValues)
                {
                    handlingStatus = statusArray[0];
                }

                break;
            }

            if (targetMhe == null || handlingStatus == null)
            {
                return result;
            }

            int lastSeparatorIndex = shuttleId.LastIndexOf('_');
            int shuttleNumber = 0;
            if (lastSeparatorIndex >= 0)
            {
                string shuttleNumberText = shuttleId.Substring(lastSeparatorIndex + 1);
                int.TryParse(shuttleNumberText, out shuttleNumber);
            }

            if (shuttleNumber >= 1 && shuttleNumber <= 4)
            {
                result[3] = Convert.ToString(shuttleNumber - 1);
                result[4] = Convert.ToString(shuttleNumber);
            }

            JArray retrievingMaterialInfos = handlingStatus["RETRIEVING_MATERIAL_INFO"] as JArray;
            if (retrievingMaterialInfos != null && retrievingMaterialInfos.HasValues)
            {
                JToken retrievingInfo = retrievingMaterialInfos[0];
                string retrievingMaterialInfoId = Convert.ToString(retrievingInfo["RETRIEVING_MATERIAL_INFO_ID"]);
                string materialId = Convert.ToString(retrievingInfo["MATERIAL_ID"]);
                string retrievalLocationInfoId = Convert.ToString(retrievingInfo["RETRIEVAL_LOCATION_INFO_ID"]);
                string productType = materialId;
                bool tierFound = false;
                JArray asrsInstances = data["ASRS_INSTANCE"] as JArray;
                if (asrsInstances != null)
                {
                    foreach (JToken asrs in asrsInstances)
                    {
                        JArray inventoryStatuses = asrs["INVENTORY_STATUS"] as JArray;
                        if (inventoryStatuses == null)
                        {
                            continue;
                        }

                        foreach (JToken inventoryStatus in inventoryStatuses)
                        {
                            JArray locationInfos = inventoryStatus["LOCATION_INFO"] as JArray;
                            if (locationInfos == null)
                            {
                                continue;
                            }

                            foreach (JToken locationInfo in locationInfos)
                            {
                                string candidateLocationId = Convert.ToString(locationInfo["LOCATION_INFO_ID"]);
                                if (!string.Equals(candidateLocationId, retrievalLocationInfoId, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                int tier;
                                if (int.TryParse(Convert.ToString(locationInfo["TIER_ID"]), out tier))
                                {
                                    result[3] = Convert.ToString(tier);
                                    result[4] = Convert.ToString(tier + 1);
                                    tierFound = true;
                                }

                                break;
                            }

                            if (tierFound)
                            {
                                break;
                            }
                        }

                        if (tierFound)
                        {
                            break;
                        }
                    }
                }

                if (!tierFound && !string.IsNullOrWhiteSpace(retrievalLocationInfoId))
                {
                    string locationNumberText = retrievalLocationInfoId;
                    if (locationNumberText.StartsWith("LOC", StringComparison.OrdinalIgnoreCase))
                    {
                        locationNumberText = locationNumberText.Substring(3);
                    }

                    int locationNumber;
                    if (int.TryParse(locationNumberText, out locationNumber) && locationNumber > 0)
                    {
                        int tier = (locationNumber - 1) / 10;
                        result[3] = Convert.ToString(tier);
                        result[4] = Convert.ToString(tier + 1);
                    }
                }

                bool isFlexibleMaterial = materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C";
                if (isFlexibleMaterial)
                {
                    JArray remainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
                    if (remainingPlans != null)
                    {
                        foreach (JToken remainingPlan in remainingPlans)
                        {
                            string remainingOperationInfoId = Convert.ToString(remainingPlan["HANDLING_OPERATION_INFO_ID"]);
                            string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                            bool matched = string.Equals(remainingOperationInfoId, retrievingMaterialInfoId, StringComparison.OrdinalIgnoreCase) || string.Equals(remainingMaterialInfoId, retrievingMaterialInfoId, StringComparison.OrdinalIgnoreCase);
                            if (!matched)
                            {
                                continue;
                            }

                            JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                            if (productionSchedules == null)
                            {
                                continue;
                            }

                            foreach (JToken productionSchedule in productionSchedules)
                            {
                                string machineInstanceId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                                if (machineInstanceId == "CNC_A_001")
                                {
                                    productType = materialId + "_A";
                                    break;
                                }

                                if (machineInstanceId == "CNC_A_002")
                                {
                                    productType = materialId + "_B";
                                    break;
                                }
                            }

                            break;
                        }
                    }
                }

                result[0] = "RETRIEVAL";
                result[1] = productType;
                return result;
            }

            JArray handlingMaterialInfos = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
            if (handlingMaterialInfos == null || !handlingMaterialInfos.HasValues)
            {
                return result;
            }

            JToken handlingMaterialInfo = handlingMaterialInfos[0];
            string handlingMaterialInfoId = Convert.ToString(handlingMaterialInfo["HANDLING_MATERIAL_INFO_ID"]);
            string handlingMaterialId = Convert.ToString(handlingMaterialInfo["MATERIAL_ID"]);
            if (string.IsNullOrWhiteSpace(handlingMaterialInfoId))
            {
                return result;
            }

            result[0] = "STORAGE";
            string handlingScheduleId = "";
            JArray storageRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            if (storageRemainingPlans != null)
            {
                foreach (JToken remainingPlan in storageRemainingPlans)
                {
                    string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                    if (!string.Equals(remainingMaterialInfoId, handlingMaterialInfoId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JArray handlingSchedules = remainingPlan["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken handlingSchedule in handlingSchedules)
                    {
                        string remainingMheInstanceId = Convert.ToString(handlingSchedule["MHE_INSTANCE_ID"]);
                        if (!string.Equals(remainingMheInstanceId, shuttleId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        handlingScheduleId = Convert.ToString(handlingSchedule["HANDLING_SCHEDULE_ID"]);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(handlingScheduleId))
                    {
                        break;
                    }
                }
            }

            JArray storagePlans = data["STORAGE_PLAN"] as JArray;
            if (storagePlans == null)
            {
                return result;
            }

            foreach (JToken storagePlan in storagePlans)
            {
                JArray storageSchedules = storagePlan["STORAGE_SCHEDULE"] as JArray;
                if (storageSchedules == null)
                {
                    continue;
                }

                foreach (JToken storageSchedule in storageSchedules)
                {
                    string materialId = Convert.ToString(storageSchedule["MATERIAL_ID"]);
                    if (!string.Equals(materialId, handlingMaterialId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JArray storageLocationInfos = storageSchedule["STORAGE_LOCATION_INFO"] as JArray;
                    if (storageLocationInfos == null)
                    {
                        continue;
                    }

                    foreach (JToken storageLocationInfo in storageLocationInfos)
                    {
                        string completedScheduleId = Convert.ToString(storageLocationInfo["COMPLETED_HANDLING_SCHEDULE_ID"]);
                        bool matchesSchedule = !string.IsNullOrWhiteSpace(handlingScheduleId) && string.Equals(completedScheduleId, handlingScheduleId, StringComparison.OrdinalIgnoreCase);
                        bool matchesPinnedJob = string.IsNullOrWhiteSpace(handlingScheduleId) && string.Equals(Convert.ToString(storageLocationInfo["STORAGE_LOCATION_INFO_ID"]), "SLOC_" + handlingMaterialInfoId.Replace("#", "_"), StringComparison.OrdinalIgnoreCase);
                        if (!matchesSchedule && !matchesPinnedJob)
                        {
                            continue;
                        }

                        result[1] = Convert.ToString(storageLocationInfo["ROW_ID"]);
                        result[3] = Convert.ToString(storageLocationInfo["TIER_ID"]);
                        int tier;
                        if (int.TryParse(result[3], out tier))
                        {
                            result[4] = Convert.ToString(tier + 1);
                        }

                        return result;
                    }
                }
            }

            return result;
        }

        public string GetAMRSequence(JObject data, string current_datetime)
        {
            JArray result = new JArray();
            List<JObject> tempList = new List<JObject>();
            HashSet<string> addedScheduleIds = new HashSet<string>();
            DateTime currentTime = DateTime.Parse(current_datetime).ToUniversalTime();
            Dictionary<string, string> operationToMaterial = new Dictionary<string, string>();
            Dictionary<string, string[]> amrWipScheduleInfo = GetAMRWIPScheduleInfo(data);
            Dictionary<string, string[]> bufferRemainingWipInfo = GetBufferRemainingWipInfo(data);
            JArray materialHandlingInfos = data["MATERIAL_HANDLING_INFO"] as JArray;
            if (materialHandlingInfos != null)
            {
                foreach (var mhi in materialHandlingInfos)
                {
                    string materialId = Convert.ToString(mhi["MATERIAL_ID"]);
                    JArray opArray = mhi["MATERIAL_HANDLING_OPERATION_INFO"] as JArray;
                    if (opArray == null)
                    {
                        continue;
                    }

                    foreach (var op in opArray)
                    {
                        string operationInfoId = Convert.ToString(op["OPERATION_INFO_ID"]);
                        if (!string.IsNullOrEmpty(operationInfoId))
                        {
                            operationToMaterial[operationInfoId] = materialId;
                        }
                    }
                }
            }

            Dictionary<string, string> cncActionToBuffer = new Dictionary<string, string>();
            cncActionToBuffer["CNC_A_001_unloading"] = "BT_A_IN_001";
            cncActionToBuffer["CNC_A_002_unloading"] = "BT_C_IN_001";
            cncActionToBuffer["CNC_B_001_unloading"] = "BT_B_IN_001";
            cncActionToBuffer["CNC_D_001_unloading"] = "BT_D_IN_001";
            cncActionToBuffer["CNC_E_001_unloading"] = "BT_D_IN_001";
            cncActionToBuffer["CNC_A_001_loading"] = "BT_A_OUT_001";
            cncActionToBuffer["CNC_A_002_loading"] = "BT_C_OUT_001";
            cncActionToBuffer["CNC_B_001_loading"] = "BT_B_OUT_001";
            cncActionToBuffer["CNC_D_001_loading"] = "BT_D_OUT_001";
            cncActionToBuffer["CNC_E_001_loading"] = "BT_D_OUT_001";
            Dictionary<string, string> cncActionToInitialWipBuffer = new Dictionary<string, string>(cncActionToBuffer);
            cncActionToInitialWipBuffer["CNC_A_001_loading"] = "BT_A_OUT_WIP";
            cncActionToInitialWipBuffer["CNC_A_002_loading"] = "BT_C_OUT_WIP";
            cncActionToInitialWipBuffer["CNC_B_001_loading"] = "BT_B_OUT_WIP";
            cncActionToInitialWipBuffer["CNC_D_001_loading"] = "BT_D_OUT_WIP";
            cncActionToInitialWipBuffer["CNC_E_001_loading"] = "BT_D_OUT_WIP";
            List<JArray> planGroups = new List<JArray>();
            JArray productionPlans = data["PRODUCTION_PLAN"] as JArray;
            JArray handlingRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            JArray productionRemainingPlans = data["PRODUCTION_REMAINING_PLAN"] as JArray;
            JArray bufferRemainingPlans = data["BUFFER_REMAINING_PLAN"] as JArray;
            if (productionPlans != null)
                planGroups.Add(productionPlans);
            if (handlingRemainingPlans != null)
                planGroups.Add(handlingRemainingPlans);
            if (productionRemainingPlans != null)
                planGroups.Add(productionRemainingPlans);
            if (bufferRemainingPlans != null)
                planGroups.Add(bufferRemainingPlans);
            foreach (JArray planGroup in planGroups)
            {
                bool isInitialBufferWipPlan = object.ReferenceEquals(planGroup, bufferRemainingPlans);
                Dictionary<string, string> locationMap = cncActionToBuffer;
                foreach (var plan in planGroup)
                {
                    JArray productionSchedules = plan["PRODUCTION_SCHEDULE"] as JArray;
                    JArray handlingSchedules = plan["HANDLING_SCHEDULE"] as JArray;
                    string[] initialBufferWipInfo = null;
                    if (productionSchedules == null)
                        productionSchedules = new JArray();
                    if (isInitialBufferWipPlan)
                    {
                        string slotInfoId = Convert.ToString(plan["SLOT_INFO_ID"]);
                        string[] matchedBufferWipInfo;
                        if (bufferRemainingWipInfo.TryGetValue(slotInfoId, out matchedBufferWipInfo) && matchedBufferWipInfo.Length > 2 && matchedBufferWipInfo[2].EndsWith("_OUT", StringComparison.OrdinalIgnoreCase))
                        {
                            initialBufferWipInfo = matchedBufferWipInfo;
                        }
                    }

                    if (handlingSchedules == null || !handlingSchedules.HasValues)
                        continue;
                    bool isTwoStepProcess = productionSchedules.Count > 1;
                    int returnToAsrsSuffix = isTwoStepProcess ? 8 : 7;
                    foreach (var schedule in handlingSchedules)
                    {
                        string resourceId = Convert.ToString(schedule["MHE_INSTANCE_ID"]);
                        if (string.IsNullOrEmpty(resourceId) || !resourceId.StartsWith("AMR_"))
                            continue;
                        string handlingScheduleId = Convert.ToString(schedule["HANDLING_SCHEDULE_ID"]);
                        if (!string.IsNullOrEmpty(handlingScheduleId) && addedScheduleIds.Contains(handlingScheduleId))
                            continue;
                        DateTime startTime = DateTime.Parse(Convert.ToString(schedule["START_DATETIME"])).ToUniversalTime();
                        DateTime endTime = DateTime.Parse(Convert.ToString(schedule["END_DATETIME"])).ToUniversalTime();
                        string operationInfoId = Convert.ToString(schedule["HANDLING_OPERATION_INFO_ID"]);
                        if (string.IsNullOrEmpty(operationInfoId))
                            continue;
                        string operationDefinitionId = operationInfoId;
                        if (!operationToMaterial.ContainsKey(operationDefinitionId))
                        {
                            if (resourceId.StartsWith("AMR_A_"))
                                operationDefinitionId = operationInfoId + "_1";
                            else if (resourceId.StartsWith("AMR_B_"))
                                operationDefinitionId = operationInfoId + "_2";
                        }

                        if (!operationToMaterial.ContainsKey(operationDefinitionId) && initialBufferWipInfo == null)
                            continue;
                        string materialId = initialBufferWipInfo != null ? initialBufferWipInfo[1] : operationToMaterial[operationDefinitionId];
                        string[] opParts = operationInfoId.Split('_');
                        int opSuffix;
                        int opSuffixIndex = opParts.Length >= 4 ? opParts.Length - 2 : opParts.Length - 1;
                        if (opParts.Length == 0 || !int.TryParse(opParts[opSuffixIndex], out opSuffix))
                            continue;
                        bool isAMRWipSchedule = amrWipScheduleInfo.ContainsKey(handlingScheduleId);
                        if (!isAMRWipSchedule && (materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C"))
                        {
                            JArray matchedProductionSchedules = GetMatchedProductionSchedules(schedule, productionSchedules);
                            foreach (var ps in matchedProductionSchedules)
                            {
                                string machineId = Convert.ToString(ps["MACHINE_INSTANCE_ID"]);
                                if (machineId == "CNC_A_001")
                                {
                                    materialId += "_A";
                                    break;
                                }

                                if (machineId == "CNC_A_002")
                                {
                                    materialId += "_B";
                                    break;
                                }
                            }
                        }

                        string loadingLocation = "";
                        string unloadingLocation = "";
                        if (isAMRWipSchedule)
                        {
                            loadingLocation = amrWipScheduleInfo[handlingScheduleId][0];
                            materialId = amrWipScheduleInfo[handlingScheduleId][1];
                            unloadingLocation = amrWipScheduleInfo[handlingScheduleId][2];
                        }
                        else if (initialBufferWipInfo != null)
                        {
                            loadingLocation = initialBufferWipInfo[0];
                            unloadingLocation = GetBufferWipDestination(loadingLocation);
                        }
                        else
                        {
                            loadingLocation = ResolveAMRLocation("loading", opSuffix, returnToAsrsSuffix, schedule, productionSchedules, locationMap);
                            unloadingLocation = ResolveAMRLocation("unloading", opSuffix, returnToAsrsSuffix, schedule, productionSchedules, locationMap);
                        }

                        if (!string.IsNullOrEmpty(loadingLocation))
                        {
                            JObject loadingItem = new JObject();
                            loadingItem["material"] = materialId;
                            loadingItem["location"] = loadingLocation;
                            loadingItem["action"] = "loading";
                            loadingItem["resource"] = resourceId;
                            loadingItem["_EVENT_TIME"] = (isAMRWipSchedule ? currentTime : startTime).ToString("yyyy-MM-ddTHH:mm:ssZ");
                            loadingItem["_ORDER"] = 1;
                            tempList.Add(loadingItem);
                        }

                        if (!string.IsNullOrEmpty(unloadingLocation))
                        {
                            JObject unloadingItem = new JObject();
                            unloadingItem["material"] = materialId;
                            unloadingItem["location"] = unloadingLocation;
                            unloadingItem["action"] = "unloading";
                            unloadingItem["resource"] = resourceId;
                            unloadingItem["_EVENT_TIME"] = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                            unloadingItem["_ORDER"] = 0;
                            tempList.Add(unloadingItem);
                        }

                        if (!string.IsNullOrEmpty(handlingScheduleId))
                            addedScheduleIds.Add(handlingScheduleId);
                    }
                }
            }

            tempList.Sort(delegate (JObject a, JObject b)
            {
                DateTime aTime = DateTime.Parse(Convert.ToString(a["_EVENT_TIME"])).ToUniversalTime();
                DateTime bTime = DateTime.Parse(Convert.ToString(b["_EVENT_TIME"])).ToUniversalTime();
                int timeCompare = DateTime.Compare(aTime, bTime);
                if (timeCompare != 0)
                    return timeCompare;
                int aOrder = Convert.ToInt32(a["_ORDER"]);
                int bOrder = Convert.ToInt32(b["_ORDER"]);
                return aOrder.CompareTo(bOrder);
            });
            foreach (JObject item in tempList)
            {
                item.Remove("_EVENT_TIME");
                item.Remove("_ORDER");
                result.Add(item);
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private JArray GetMatchedProductionSchedules(JToken handlingSchedule, JArray productionSchedules)
        {
            JArray result = new JArray();
            if (productionSchedules == null || !productionSchedules.HasValues)
                return result;
            string retrievalLocationInfoId = Convert.ToString(handlingSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
            if (!string.IsNullOrEmpty(retrievalLocationInfoId))
            {
                foreach (var productionSchedule in productionSchedules)
                {
                    string productionRetrievalLocationInfoId = Convert.ToString(productionSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                    if (productionRetrievalLocationInfoId == retrievalLocationInfoId)
                        result.Add(productionSchedule);
                }
            }

            if (!result.HasValues)
            {
                foreach (var productionSchedule in productionSchedules)
                    result.Add(productionSchedule);
            }

            return result;
        }

        private Dictionary<string, string[]> GetAMRWIPScheduleInfo(JObject data)
        {
            Dictionary<string, string[]> result = new Dictionary<string, string[]>();
            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            JArray remainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            if (mheInstances == null || remainingPlans == null)
            {
                return result;
            }

            foreach (var mhe in mheInstances)
            {
                string mheTypeId = Convert.ToString(mhe["MHE_TYPE_ID"]);
                string resourceId = Convert.ToString(mhe["MHE_INSTANCE_ID"]);
                bool isSupportedAMR = (mheTypeId == "AMR_A" && resourceId == "AMR_A_001") || (mheTypeId == "AMR_B" && resourceId == "AMR_B_001");
                if (!isSupportedAMR)
                {
                    continue;
                }

                string amrNodePrefix;
                string amrAPDestination;
                if (resourceId == "AMR_A_001")
                {
                    amrNodePrefix = "AMR_A";
                    amrAPDestination = "ChangeType #AMR_A";
                }
                else if (resourceId == "AMR_B_001")
                {
                    amrNodePrefix = "AMR_B";
                    amrAPDestination = "ChangeType #AMR_B";
                }
                else
                {
                    continue;
                }

                JArray statusArray = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                if (statusArray == null || !statusArray.HasValues)
                {
                    continue;
                }

                int apNodeIndex = 1;
                foreach (var handlingStatus in statusArray)
                {
                    JArray materialInfoArray = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                    if (materialInfoArray == null || !materialInfoArray.HasValues)
                    {
                        continue;
                    }

                    foreach (var materialInfo in materialInfoArray)
                    {
                        string handlingMaterialInfoId = Convert.ToString(materialInfo["HANDLING_MATERIAL_INFO_ID"]);
                        string materialId = Convert.ToString(materialInfo["MATERIAL_ID"]);
                        string completedOperationId = Convert.ToString(materialInfo["COMPLETED_HANDLING_OPERATION_INFO_ID"]);
                        if (string.IsNullOrEmpty(handlingMaterialInfoId) || string.IsNullOrEmpty(materialId) || string.IsNullOrEmpty(completedOperationId))
                        {
                            continue;
                        }

                        string[] completedOperationParts = completedOperationId.Split('_');
                        int completedOperationSuffix;
                        if (completedOperationParts.Length == 0 || !int.TryParse(completedOperationParts[completedOperationParts.Length - 1], out completedOperationSuffix))
                        {
                            continue;
                        }

                        string wipPosition = completedOperationSuffix < 3 ? "BP" : "AP";
                        foreach (var remainingPlan in remainingPlans)
                        {
                            string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                            if (remainingMaterialInfoId != handlingMaterialInfoId)
                            {
                                continue;
                            }

                            JArray handlingSchedules = remainingPlan["HANDLING_SCHEDULE"] as JArray;
                            if (handlingSchedules == null || !handlingSchedules.HasValues)
                            {
                                break;
                            }

                            JToken firstAMRSchedule = null;
                            DateTime earliestStartTime = DateTime.MaxValue;
                            foreach (var schedule in handlingSchedules)
                            {
                                string scheduleResourceId = Convert.ToString(schedule["MHE_INSTANCE_ID"]);
                                if (scheduleResourceId != resourceId)
                                {
                                    continue;
                                }

                                DateTime startTime;
                                if (!DateTime.TryParse(Convert.ToString(schedule["START_DATETIME"]), out startTime))
                                {
                                    continue;
                                }

                                if (startTime < earliestStartTime)
                                {
                                    earliestStartTime = startTime;
                                    firstAMRSchedule = schedule;
                                }
                            }

                            if (firstAMRSchedule == null)
                            {
                                break;
                            }

                            string handlingScheduleId = Convert.ToString(firstAMRSchedule["HANDLING_SCHEDULE_ID"]);
                            string operationInfoId = Convert.ToString(firstAMRSchedule["HANDLING_OPERATION_INFO_ID"]);
                            if (string.IsNullOrEmpty(handlingScheduleId) || string.IsNullOrEmpty(operationInfoId))
                            {
                                break;
                            }

                            string[] opParts = operationInfoId.Split('_');
                            int opSuffix;
                            if (opParts.Length == 0 || !int.TryParse(opParts[opParts.Length - 1], out opSuffix))
                            {
                                break;
                            }

                            string machineId = Convert.ToString(materialInfo["MACHINE_INSTANCE_ID"]);
                            if (string.IsNullOrEmpty(machineId))
                            {
                                JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                                if (productionSchedules != null && productionSchedules.HasValues)
                                {
                                    if (opSuffix < 4)
                                    {
                                        machineId = Convert.ToString(productionSchedules[0]["MACHINE_INSTANCE_ID"]);
                                    }
                                    else if (opSuffix > 4)
                                    {
                                        machineId = Convert.ToString(productionSchedules[productionSchedules.Count - 1]["MACHINE_INSTANCE_ID"]);
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(machineId) && wipPosition == "AP")
                            {
                                machineId = GetAMRWIPMachineInstance(data, materialInfo, handlingMaterialInfoId, wipPosition);
                            }

                            if (string.IsNullOrEmpty(machineId))
                            {
                                PipelineConsole.Log("[AMR WIP MACHINE NOT FOUND]" + " RESOURCE=" + resourceId + " HANDLING_MATERIAL_INFO_ID=" + handlingMaterialInfoId);
                                break;
                            }

                            string loadingLocation;
                            string unloadingLocation;
                            if (wipPosition == "BP")
                            {
                                loadingLocation = amrNodePrefix + "_WIP_BP";
                                unloadingLocation = GetAMRWIPBPDestination(machineId);
                            }
                            else
                            {
                                if (apNodeIndex > 2)
                                {
                                    PipelineConsole.Log("[AMR AP WIP WARNING]" + " RESOURCE=" + resourceId + " AP_COUNT_EXCEEDED");
                                    break;
                                }

                                loadingLocation = amrNodePrefix + "_WIP_AP #" + apNodeIndex;
                                unloadingLocation = amrAPDestination;
                                apNodeIndex++;
                            }

                            string productType = GetAMRWIPProductType(materialId, machineId);
                            if (string.IsNullOrEmpty(loadingLocation) || string.IsNullOrEmpty(unloadingLocation) || string.IsNullOrEmpty(productType))
                            {
                                PipelineConsole.Log("[AMR WIP LOCATION NOT FOUND]" + " RESOURCE=" + resourceId + " MACHINE=" + machineId);
                                break;
                            }

                            result[handlingScheduleId] = new string[]
                            {
                                loadingLocation,
                                productType,
                                unloadingLocation,
                                machineId
                            };
                            break;
                        }
                    }
                }
            }

            return result;
        }

        private Dictionary<string, string[]> GetBufferRemainingWipInfo(JObject data)
        {
            Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            JArray bufferInstances = data["BUFFER_INSTANCE"] as JArray;
            if (bufferInstances == null)
                return result;
            foreach (JToken bufferToken in bufferInstances)
            {
                string bufferInstanceId = Convert.ToString(bufferToken["BUFFER_INSTANCE_ID"]);
                string bufferTypeId = Convert.ToString(bufferToken["BUFFER_TYPE_ID"]);
                JArray bufferStatuses = bufferToken["BUFFER_STATUS"] as JArray;
                if (string.IsNullOrWhiteSpace(bufferInstanceId) || bufferStatuses == null)
                    continue;
                foreach (JToken statusToken in bufferStatuses)
                {
                    string materialId = Convert.ToString(statusToken["MATERIAL_ID"]);
                    int stockNum;
                    if (string.IsNullOrWhiteSpace(materialId) || !int.TryParse(Convert.ToString(statusToken["STOCK_NUM"]), out stockNum) || stockNum <= 0)
                        continue;
                    string productType = materialId;
                    bool isFlexibleMaterial = string.Equals(materialId, "MT_A", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_B", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_C", StringComparison.OrdinalIgnoreCase);
                    if (isFlexibleMaterial)
                    {
                        if (string.Equals(bufferTypeId, "BT_A_OUT", StringComparison.OrdinalIgnoreCase))
                        {
                            productType = materialId + "_A";
                        }
                        else if (string.Equals(bufferTypeId, "BT_C_OUT", StringComparison.OrdinalIgnoreCase))
                        {
                            productType = materialId + "_B";
                        }
                    }

                    JArray slotInfos = statusToken["SLOT_INFO"] as JArray;
                    if (slotInfos == null)
                        continue;
                    foreach (JToken slotToken in slotInfos)
                    {
                        string slotInfoId = Convert.ToString(slotToken["SLOT_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(slotInfoId))
                            continue;
                        result[slotInfoId] = new string[]
                        {
                            GetBufferWipLoadingLocation(bufferInstanceId),
                            productType,
                            bufferTypeId
                        };
                    }
                }
            }

            return result;
        }

        private string GetBufferWipLoadingLocation(string bufferInstanceId)
        {
            Dictionary<string, string> locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            locations["BT_A_OUT_001"] = "BT_A_OUT_WIP";
            locations["BT_C_OUT_001"] = "BT_C_OUT_WIP";
            locations["BT_B_OUT_001"] = "BT_B_OUT_WIP";
            locations["BT_D_OUT_001"] = "BT_D_OUT_WIP";
            string location;
            if (locations.TryGetValue(bufferInstanceId, out location))
                return location;
            return bufferInstanceId;
        }

        private string GetBufferWipDestination(string bufferInstanceId)
        {
            Dictionary<string, string> destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            destinations["BT_A_OUT_WIP"] = "ChangeTypeWIP #A";
            destinations["BT_C_OUT_WIP"] = "ChangeTypeWIP #C";
            destinations["BT_B_OUT_WIP"] = "ChangeTypeWIP #B";
            destinations["BT_D_OUT_WIP"] = "ChangeTypeWIP #D";
            string destination;
            if (destinations.TryGetValue(bufferInstanceId, out destination))
                return destination;
            return "";
        }

        private string GetAMRWIPBPDestination(string machineId)
        {
            if (machineId == "CNC_A_001")
            {
                return "BT_A_IN_AMR_WIP";
            }

            if (machineId == "CNC_A_002")
            {
                return "BT_C_IN_AMR_WIP";
            }

            if (machineId == "CNC_B_001")
            {
                return "BT_B_IN_AMR_WIP";
            }

            if (machineId == "CNC_D_001" || machineId == "CNC_E_001")
            {
                return "BT_D_IN_AMR_WIP";
            }

            return "";
        }

        private string ResolveAMRLocation(string action, int opSuffix, int returnToAsrsSuffix, JToken handlingSchedule, JArray productionSchedules, Dictionary<string, string> cncActionToBuffer)
        {
            JArray matchedProductionSchedules = GetMatchedProductionSchedules(handlingSchedule, productionSchedules);
            if (opSuffix == 3)
            {
                if (action == "loading")
                {
                    return "ASRS Out";
                }

                if (action == "unloading")
                {
                    if (!matchedProductionSchedules.HasValues)
                    {
                        return "";
                    }

                    string machineId = Convert.ToString(matchedProductionSchedules[0]["MACHINE_INSTANCE_ID"]);
                    string key = machineId + "_unloading";
                    if (cncActionToBuffer.ContainsKey(key))
                    {
                        return cncActionToBuffer[key];
                    }
                }
            }

            if (opSuffix == 5 || opSuffix == 6 || opSuffix == returnToAsrsSuffix)
            {
                if (!matchedProductionSchedules.HasValues)
                {
                    return "";
                }

                string machineId = Convert.ToString(matchedProductionSchedules[matchedProductionSchedules.Count - 1]["MACHINE_INSTANCE_ID"]);
                if (action == "loading")
                {
                    string key = machineId + "_loading";
                    if (cncActionToBuffer.ContainsKey(key))
                    {
                        return cncActionToBuffer[key];
                    }
                }

                if (action == "unloading")
                {
                    return GetBTChangeTypeDestination(machineId);
                }
            }

            return "";
        }

        private string GetBTChangeTypeDestination(string machineId)
        {
            if (machineId == "CNC_A_001")
            {
                return "ChangeType #A";
            }

            if (machineId == "CNC_A_002")
            {
                return "ChangeType #C";
            }

            if (machineId == "CNC_B_001")
            {
                return "ChangeType #B";
            }

            if (machineId == "CNC_D_001" || machineId == "CNC_E_001")
            {
                return "ChangeType #D";
            }

            return "";
        }

        public string GetShuttleRetrievalSchedule(JObject data)
        {
            JObject result = new JObject();
            result["1"] = new JArray();
            result["2"] = new JArray();
            result["3"] = new JArray();
            result["4"] = new JArray();
            if (data == null)
            {
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }

            Dictionary<string, JObject> locationMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            JArray asrsInstances = data["ASRS_INSTANCE"] as JArray;
            if (asrsInstances != null)
            {
                foreach (JToken asrsToken in asrsInstances)
                {
                    JObject asrs = asrsToken as JObject;
                    if (asrs == null)
                    {
                        continue;
                    }

                    JArray inventoryStatuses = asrs["INVENTORY_STATUS"] as JArray;
                    if (inventoryStatuses == null)
                    {
                        continue;
                    }

                    foreach (JToken inventoryToken in inventoryStatuses)
                    {
                        JObject inventoryStatus = inventoryToken as JObject;
                        if (inventoryStatus == null)
                        {
                            continue;
                        }

                        JArray locationInfos = inventoryStatus["LOCATION_INFO"] as JArray;
                        if (locationInfos == null)
                        {
                            continue;
                        }

                        foreach (JToken locationToken in locationInfos)
                        {
                            JObject locationInfo = locationToken as JObject;
                            if (locationInfo == null)
                            {
                                continue;
                            }

                            string locationInfoId = Convert.ToString(locationInfo["LOCATION_INFO_ID"]);
                            if (!string.IsNullOrWhiteSpace(locationInfoId))
                            {
                                locationMap[locationInfoId] = locationInfo;
                            }
                        }
                    }
                }
            }

            Dictionary<string, List<JObject>> floorTemp = new Dictionary<string, List<JObject>>();
            floorTemp["1"] = new List<JObject>();
            floorTemp["2"] = new List<JObject>();
            floorTemp["3"] = new List<JObject>();
            floorTemp["4"] = new List<JObject>();
            int insertOrder = 0;
            Dictionary<string, JObject> retrievingMaterialMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> shuttleStorageMaterialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            if (mheInstances != null)
            {
                foreach (JToken mheToken in mheInstances)
                {
                    JObject mheInstance = mheToken as JObject;
                    if (mheInstance == null)
                    {
                        continue;
                    }

                    string mheInstanceId = Convert.ToString(mheInstance["MHE_INSTANCE_ID"]);
                    if (string.IsNullOrWhiteSpace(mheInstanceId) || !mheInstanceId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JArray handlingStatuses = mheInstance["MATERIAL_HANDLING_STATUS"] as JArray;
                    if (handlingStatuses == null)
                    {
                        continue;
                    }

                    foreach (JToken statusToken in handlingStatuses)
                    {
                        JObject handlingStatus = statusToken as JObject;
                        if (handlingStatus == null)
                        {
                            continue;
                        }

                        JArray handlingMaterialInfos = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                        if (handlingMaterialInfos != null)
                        {
                            foreach (JToken handlingMaterialToken in handlingMaterialInfos)
                            {
                                string handlingMaterialInfoId = Convert.ToString(handlingMaterialToken["HANDLING_MATERIAL_INFO_ID"]);
                                if (!string.IsNullOrWhiteSpace(handlingMaterialInfoId))
                                {
                                    shuttleStorageMaterialIds.Add(handlingMaterialInfoId);
                                }
                            }
                        }

                        JArray retrievingMaterialInfos = handlingStatus["RETRIEVING_MATERIAL_INFO"] as JArray;
                        if (retrievingMaterialInfos == null)
                        {
                            continue;
                        }

                        foreach (JToken retrievingToken in retrievingMaterialInfos)
                        {
                            JObject retrievingInfo = retrievingToken as JObject;
                            if (retrievingInfo == null)
                            {
                                continue;
                            }

                            string retrievingMaterialInfoId = Convert.ToString(retrievingInfo["RETRIEVING_MATERIAL_INFO_ID"]);
                            if (string.IsNullOrWhiteSpace(retrievingMaterialInfoId))
                            {
                                continue;
                            }

                            JObject retrievingInfoCopy = new JObject(retrievingInfo);
                            retrievingInfoCopy["_MHE_INSTANCE_ID"] = mheInstanceId;
                            retrievingMaterialMap[retrievingMaterialInfoId] = retrievingInfoCopy;
                        }
                    }
                }
            }

            JArray handlingRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            if (handlingRemainingPlans != null)
            {
                foreach (JToken remainingPlanToken in handlingRemainingPlans)
                {
                    JObject remainingPlan = remainingPlanToken as JObject;
                    if (remainingPlan == null)
                    {
                        continue;
                    }

                    string retrievingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_OPERATION_INFO_ID"]);
                    JObject retrievingInfo;
                    if (string.IsNullOrWhiteSpace(retrievingMaterialInfoId) || !retrievingMaterialMap.TryGetValue(retrievingMaterialInfoId, out retrievingInfo))
                    {
                        continue;
                    }

                    string materialId = Convert.ToString(retrievingInfo["MATERIAL_ID"]);
                    string retrievalLocationInfoId = Convert.ToString(retrievingInfo["RETRIEVAL_LOCATION_INFO_ID"]);
                    string currentShuttleId = Convert.ToString(retrievingInfo["_MHE_INSTANCE_ID"]);
                    if (string.IsNullOrWhiteSpace(materialId) || string.IsNullOrWhiteSpace(retrievalLocationInfoId))
                    {
                        continue;
                    }

                    string convertedMaterialId = materialId;
                    if (string.Equals(materialId, "MT_A", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_B", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_C", StringComparison.OrdinalIgnoreCase))
                    {
                        JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                        if (productionSchedules != null)
                        {
                            foreach (JToken productionScheduleToken in productionSchedules)
                            {
                                JObject productionSchedule = productionScheduleToken as JObject;
                                if (productionSchedule == null)
                                {
                                    continue;
                                }

                                string machineInstanceId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                                if (string.Equals(machineInstanceId, "CNC_A_001", StringComparison.OrdinalIgnoreCase))
                                {
                                    convertedMaterialId = materialId + "_A";
                                    break;
                                }

                                if (string.Equals(machineInstanceId, "CNC_A_002", StringComparison.OrdinalIgnoreCase))
                                {
                                    convertedMaterialId = materialId + "_B";
                                    break;
                                }
                            }
                        }
                    }

                    JArray handlingSchedules = remainingPlan["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken scheduleToken in handlingSchedules)
                    {
                        JObject handlingSchedule = scheduleToken as JObject;
                        if (handlingSchedule == null)
                        {
                            continue;
                        }

                        string operationInfoId = Convert.ToString(handlingSchedule["HANDLING_OPERATION_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(operationInfoId))
                        {
                            continue;
                        }

                        string[] operationParts = operationInfoId.Split('_');
                        int operationSuffix;
                        if (operationParts.Length == 0 || !int.TryParse(operationParts[operationParts.Length - 1], out operationSuffix) || operationSuffix != 1)
                        {
                            continue;
                        }

                        string scheduleShuttleId = Convert.ToString(handlingSchedule["MHE_INSTANCE_ID"]);
                        if (string.IsNullOrWhiteSpace(scheduleShuttleId) || !scheduleShuttleId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(currentShuttleId) && !string.Equals(currentShuttleId, scheduleShuttleId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string scheduleRetrievalLocationId = Convert.ToString(handlingSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                        if (!string.IsNullOrWhiteSpace(scheduleRetrievalLocationId))
                        {
                            retrievalLocationInfoId = scheduleRetrievalLocationId;
                        }

                        int row;
                        int column;
                        int tier;
                        JObject locationInfo;
                        if (locationMap.TryGetValue(retrievalLocationInfoId, out locationInfo))
                        {
                            if (!int.TryParse(Convert.ToString(locationInfo["ROW_ID"]), out row) || !int.TryParse(Convert.ToString(locationInfo["COLUMN_ID"]), out column) || !int.TryParse(Convert.ToString(locationInfo["TIER_ID"]), out tier))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            string locationNumberText = retrievalLocationInfoId;
                            if (locationNumberText.StartsWith("LOC", StringComparison.OrdinalIgnoreCase))
                            {
                                locationNumberText = locationNumberText.Substring(3);
                            }

                            int locationNumber;
                            if (!int.TryParse(locationNumberText, out locationNumber) || locationNumber <= 0)
                            {
                                continue;
                            }

                            int zeroBasedLocationNumber = locationNumber - 1;
                            row = zeroBasedLocationNumber % 10;
                            column = zeroBasedLocationNumber % 4;
                            tier = zeroBasedLocationNumber / 10;
                        }

                        string floor = Convert.ToString(tier + 1);
                        if (!floorTemp.ContainsKey(floor))
                        {
                            continue;
                        }

                        JObject item = new JObject();
                        item["material"] = convertedMaterialId;
                        item["row"] = Convert.ToString(row);
                        item["column"] = Convert.ToString(column);
                        item["action"] = "retrieval";
                        item["_MHS_ID"] = Convert.ToString(handlingSchedule["HANDLING_SCHEDULE_ID"]);
                        item["_START_DATETIME"] = Convert.ToString(handlingSchedule["START_DATETIME"]);
                        item["_SOURCE_PRIORITY"] = 0;
                        item["_INSERT_ORDER"] = insertOrder++;
                        floorTemp[floor].Add(item);
                    }
                }
            }

            JArray productionPlans = data["PRODUCTION_PLAN"] as JArray;
            if (productionPlans != null)
            {
                foreach (JToken planToken in productionPlans)
                {
                    JObject productionPlan = planToken as JObject;
                    if (productionPlan == null)
                    {
                        continue;
                    }

                    JArray handlingSchedules = productionPlan["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken scheduleToken in handlingSchedules)
                    {
                        JObject handlingSchedule = scheduleToken as JObject;
                        if (handlingSchedule == null)
                        {
                            continue;
                        }

                        string mheInstanceId = Convert.ToString(handlingSchedule["MHE_INSTANCE_ID"]);
                        if (string.IsNullOrWhiteSpace(mheInstanceId) || !mheInstanceId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string operationInfoId = Convert.ToString(handlingSchedule["HANDLING_OPERATION_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(operationInfoId))
                        {
                            continue;
                        }

                        string[] operationParts = operationInfoId.Split('_');
                        int operationSuffix;
                        if (operationParts.Length == 0 || !int.TryParse(operationParts[operationParts.Length - 1], out operationSuffix) || operationSuffix != 1)
                        {
                            continue;
                        }

                        string locationInfoId = Convert.ToString(handlingSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(locationInfoId))
                        {
                            continue;
                        }

                        string materialId = GetFlexibleProductNameByLocation(productionPlan, locationInfoId);
                        JObject locationInfo;
                        if (!locationMap.TryGetValue(locationInfoId, out locationInfo))
                        {
                            continue;
                        }

                        int tier;
                        if (!int.TryParse(Convert.ToString(locationInfo["TIER_ID"]), out tier))
                        {
                            continue;
                        }

                        string floor = Convert.ToString(tier + 1);
                        if (!floorTemp.ContainsKey(floor))
                        {
                            continue;
                        }

                        JObject item = new JObject();
                        item["material"] = materialId;
                        item["row"] = Convert.ToString(locationInfo["ROW_ID"]);
                        item["column"] = Convert.ToString(locationInfo["COLUMN_ID"]);
                        item["action"] = "retrieval";
                        item["_MHS_ID"] = Convert.ToString(handlingSchedule["HANDLING_SCHEDULE_ID"]);
                        item["_START_DATETIME"] = Convert.ToString(handlingSchedule["START_DATETIME"]);
                        item["_SOURCE_PRIORITY"] = 1;
                        item["_INSERT_ORDER"] = insertOrder++;
                        floorTemp[floor].Add(item);
                    }
                }
            }

            Dictionary<string, string[]> storageByScheduleId = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            JArray storagePlans = data["STORAGE_PLAN"] as JArray;
            if (storagePlans != null)
            {
                foreach (JToken storagePlanToken in storagePlans)
                {
                    JArray storageSchedules = storagePlanToken["STORAGE_SCHEDULE"] as JArray;
                    if (storageSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken storageSchedule in storageSchedules)
                    {
                        string materialId = Convert.ToString(storageSchedule["MATERIAL_ID"]);
                        string completeMaterialId = materialId;
                        if (materialId.StartsWith("MT_", StringComparison.OrdinalIgnoreCase) && materialId.Length > 3)
                        {
                            completeMaterialId = "Complete_" + materialId.Substring(3);
                        }

                        JArray storageLocations = storageSchedule["STORAGE_LOCATION_INFO"] as JArray;
                        if (storageLocations == null)
                        {
                            continue;
                        }

                        foreach (JToken storageLocation in storageLocations)
                        {
                            string completedScheduleId = Convert.ToString(storageLocation["COMPLETED_HANDLING_SCHEDULE_ID"]);
                            if (string.IsNullOrWhiteSpace(completedScheduleId))
                                continue;
                            storageByScheduleId[completedScheduleId] = new string[]
                            {
                                completeMaterialId,
                                Convert.ToString(storageLocation["TIER_ID"]),
                                Convert.ToString(storageLocation["ROW_ID"]),
                                Convert.ToString(storageLocation["COLUMN_ID"])
                            };
                        }
                    }
                }
            }

            List<JArray> storagePlanGroups = new List<JArray>();
            JArray productionRemainingPlans = data["PRODUCTION_REMAINING_PLAN"] as JArray;
            JArray bufferRemainingPlans = data["BUFFER_REMAINING_PLAN"] as JArray;
            if (productionPlans != null)
                storagePlanGroups.Add(productionPlans);
            if (handlingRemainingPlans != null)
                storagePlanGroups.Add(handlingRemainingPlans);
            if (productionRemainingPlans != null)
                storagePlanGroups.Add(productionRemainingPlans);
            if (bufferRemainingPlans != null)
                storagePlanGroups.Add(bufferRemainingPlans);
            HashSet<string> addedStorageScheduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JArray storagePlanGroup in storagePlanGroups)
            {
                foreach (JToken planToken in storagePlanGroup)
                {
                    string handlingMaterialInfoId = Convert.ToString(planToken["HANDLING_MATERIAL_INFO_ID"]);
                    if (!string.IsNullOrWhiteSpace(handlingMaterialInfoId) && shuttleStorageMaterialIds.Contains(handlingMaterialInfoId))
                    {
                        continue;
                    }

                    JArray handlingSchedules = planToken["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken handlingSchedule in handlingSchedules)
                    {
                        string resourceId = Convert.ToString(handlingSchedule["MHE_INSTANCE_ID"]);
                        if (string.IsNullOrWhiteSpace(resourceId) || !resourceId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string scheduleId = Convert.ToString(handlingSchedule["HANDLING_SCHEDULE_ID"]);
                        string[] storageInfo;
                        if (string.IsNullOrWhiteSpace(scheduleId) || addedStorageScheduleIds.Contains(scheduleId) || !storageByScheduleId.TryGetValue(scheduleId, out storageInfo))
                            continue;
                        int tier;
                        if (!int.TryParse(storageInfo[1], out tier))
                            continue;
                        string floor = Convert.ToString(tier + 1);
                        if (!floorTemp.ContainsKey(floor))
                            continue;
                        JObject item = new JObject();
                        item["material"] = storageInfo[0];
                        item["row"] = storageInfo[2];
                        item["column"] = storageInfo[3];
                        item["action"] = "storage";
                        item["_MHS_ID"] = scheduleId;
                        item["_START_DATETIME"] = Convert.ToString(handlingSchedule["START_DATETIME"]);
                        item["_SOURCE_PRIORITY"] = 1;
                        item["_INSERT_ORDER"] = insertOrder++;
                        floorTemp[floor].Add(item);
                        addedStorageScheduleIds.Add(scheduleId);
                    }
                }
            }

            foreach (KeyValuePair<string, List<JObject>> pair in floorTemp)
            {
                pair.Value.Sort(delegate (JObject a, JObject b)
                {
                    DateTime aTime;
                    DateTime bTime;
                    bool aParsed = DateTime.TryParse(Convert.ToString(a["_START_DATETIME"]), out aTime);
                    bool bParsed = DateTime.TryParse(Convert.ToString(b["_START_DATETIME"]), out bTime);
                    if (aParsed && bParsed)
                    {
                        int timeCompare = DateTime.Compare(aTime.ToUniversalTime(), bTime.ToUniversalTime());
                        if (timeCompare != 0)
                        {
                            return timeCompare;
                        }
                    }
                    else if (aParsed)
                    {
                        return -1;
                    }
                    else if (bParsed)
                    {
                        return 1;
                    }

                    int aPriority = Convert.ToInt32(a["_SOURCE_PRIORITY"]);
                    int bPriority = Convert.ToInt32(b["_SOURCE_PRIORITY"]);
                    int priorityCompare = aPriority.CompareTo(bPriority);
                    if (priorityCompare != 0)
                    {
                        return priorityCompare;
                    }

                    int aInsertOrder = Convert.ToInt32(a["_INSERT_ORDER"]);
                    int bInsertOrder = Convert.ToInt32(b["_INSERT_ORDER"]);
                    return aInsertOrder.CompareTo(bInsertOrder);
                });
                JArray floorSchedule = new JArray();
                foreach (JObject item in pair.Value)
                {
                    item.Remove("_MHS_ID");
                    item.Remove("_START_DATETIME");
                    item.Remove("_SOURCE_PRIORITY");
                    item.Remove("_INSERT_ORDER");
                    floorSchedule.Add(item);
                }

                result[pair.Key] = floorSchedule;
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public string GetFlexibleProductName(JObject plan)
        {
            string materialId = Convert.ToString(plan["MATERIAL_ID"]);
            if (materialId != "MT_A" && materialId != "MT_B" && materialId != "MT_C")
                return materialId;
            JArray productionSchedules = plan["PRODUCTION_SCHEDULE"] as JArray;
            if (productionSchedules == null)
                return materialId;
            foreach (var ps in productionSchedules)
            {
                string machineId = Convert.ToString(ps["MACHINE_INSTANCE_ID"]);
                if (machineId == "CNC_A_001")
                    return materialId + "_A";
                if (machineId == "CNC_A_002")
                    return materialId + "_B";
            }

            return materialId;
        }

        private string GetFlexibleProductNameByLocation(JObject plan, string retrievalLocationInfoId)
        {
            if (plan == null)
            {
                return "";
            }

            string materialId = Convert.ToString(plan["MATERIAL_ID"]);
            bool isFlexibleMaterial = string.Equals(materialId, "MT_A", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_B", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_C", StringComparison.OrdinalIgnoreCase);
            if (!isFlexibleMaterial)
            {
                return materialId;
            }

            JArray productionSchedules = plan["PRODUCTION_SCHEDULE"] as JArray;
            if (productionSchedules == null)
            {
                return materialId;
            }

            foreach (JToken scheduleToken in productionSchedules)
            {
                JObject productionSchedule = scheduleToken as JObject;
                if (productionSchedule == null)
                {
                    continue;
                }

                string productionLocationInfoId = Convert.ToString(productionSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                if (!string.Equals(productionLocationInfoId, retrievalLocationInfoId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string machineInstanceId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                if (string.Equals(machineInstanceId, "CNC_A_001", StringComparison.OrdinalIgnoreCase))
                {
                    return materialId + "_A";
                }

                if (string.Equals(machineInstanceId, "CNC_A_002", StringComparison.OrdinalIgnoreCase))
                {
                    return materialId + "_B";
                }
            }

            return materialId;
        }

        public string GetCombinedLifterSequence(JObject data, string currentDateTime)
        {
            List<JObject> sequenceTasks = new List<JObject>();
            HashSet<string> addedScheduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data == null)
            {
                return new JArray().ToString(Newtonsoft.Json.Formatting.None);
            }

            Dictionary<string, string> operationToMaterial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JArray materialHandlingInfos = data["MATERIAL_HANDLING_INFO"] as JArray;
            if (materialHandlingInfos != null)
            {
                foreach (JToken materialHandlingToken in materialHandlingInfos)
                {
                    JObject materialHandlingInfo = materialHandlingToken as JObject;
                    if (materialHandlingInfo == null)
                    {
                        continue;
                    }

                    string materialId = Convert.ToString(materialHandlingInfo["MATERIAL_ID"]);
                    JArray operationInfos = materialHandlingInfo["MATERIAL_HANDLING_OPERATION_INFO"] as JArray;
                    if (operationInfos == null)
                    {
                        continue;
                    }

                    foreach (JToken operationToken in operationInfos)
                    {
                        JObject operationInfo = operationToken as JObject;
                        if (operationInfo == null)
                        {
                            continue;
                        }

                        string operationInfoId = Convert.ToString(operationInfo["OPERATION_INFO_ID"]);
                        if (!string.IsNullOrWhiteSpace(operationInfoId))
                        {
                            operationToMaterial[operationInfoId] = materialId;
                        }
                    }
                }
            }

            Dictionary<string, JObject> asrsLocationMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            JArray asrsInstances = data["ASRS_INSTANCE"] as JArray;
            if (asrsInstances != null)
            {
                foreach (JToken asrsToken in asrsInstances)
                {
                    JObject asrs = asrsToken as JObject;
                    if (asrs == null)
                    {
                        continue;
                    }

                    JArray inventoryStatuses = asrs["INVENTORY_STATUS"] as JArray;
                    if (inventoryStatuses == null)
                    {
                        continue;
                    }

                    foreach (JToken inventoryToken in inventoryStatuses)
                    {
                        JObject inventoryStatus = inventoryToken as JObject;
                        if (inventoryStatus == null)
                        {
                            continue;
                        }

                        JArray locationInfos = inventoryStatus["LOCATION_INFO"] as JArray;
                        if (locationInfos == null)
                        {
                            continue;
                        }

                        foreach (JToken locationToken in locationInfos)
                        {
                            JObject locationInfo = locationToken as JObject;
                            if (locationInfo == null)
                            {
                                continue;
                            }

                            string locationInfoId = Convert.ToString(locationInfo["LOCATION_INFO_ID"]);
                            if (!string.IsNullOrWhiteSpace(locationInfoId))
                            {
                                asrsLocationMap[locationInfoId] = locationInfo;
                            }
                        }
                    }
                }
            }

            Dictionary<string, JObject> retrievingMaterialMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            HashSet<string> lifterBWorkInProcessMaterialInfoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> retrievalWorkInProcessInfoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mheInstances != null)
            {
                foreach (JToken mheToken in mheInstances)
                {
                    JObject mheInstance = mheToken as JObject;
                    if (mheInstance == null)
                    {
                        continue;
                    }

                    string mheInstanceId = Convert.ToString(mheInstance["MHE_INSTANCE_ID"]);
                    string mheTypeId = Convert.ToString(mheInstance["MHE_TYPE_ID"]);
                    JArray handlingStatuses = mheInstance["MATERIAL_HANDLING_STATUS"] as JArray;
                    if (handlingStatuses == null)
                    {
                        continue;
                    }

                    foreach (JToken statusToken in handlingStatuses)
                    {
                        JObject handlingStatus = statusToken as JObject;
                        if (handlingStatus == null)
                        {
                            continue;
                        }

                        if (string.Equals(mheInstanceId, "LIFTER_B_001", StringComparison.OrdinalIgnoreCase) || string.Equals(mheTypeId, "LIFTER_B", StringComparison.OrdinalIgnoreCase))
                        {
                            JArray handlingMaterialInfos = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                            if (handlingMaterialInfos != null)
                            {
                                foreach (JToken materialInfoToken in handlingMaterialInfos)
                                {
                                    string handlingMaterialInfoId = Convert.ToString(materialInfoToken["HANDLING_MATERIAL_INFO_ID"]);
                                    if (!string.IsNullOrWhiteSpace(handlingMaterialInfoId))
                                    {
                                        lifterBWorkInProcessMaterialInfoIds.Add(handlingMaterialInfoId);
                                    }
                                }
                            }
                        }

                        JArray retrievingInfos = handlingStatus["RETRIEVING_MATERIAL_INFO"] as JArray;
                        if (retrievingInfos == null)
                        {
                            continue;
                        }

                        foreach (JToken retrievingToken in retrievingInfos)
                        {
                            JObject retrievingInfo = retrievingToken as JObject;
                            if (retrievingInfo == null)
                            {
                                continue;
                            }

                            string retrievingInfoId = Convert.ToString(retrievingInfo["RETRIEVING_MATERIAL_INFO_ID"]);
                            if (!string.IsNullOrWhiteSpace(retrievingInfoId))
                            {
                                retrievingMaterialMap[retrievingInfoId] = retrievingInfo;
                                if (string.Equals(mheInstanceId, "LIFTER_A_001", StringComparison.OrdinalIgnoreCase) || string.Equals(mheTypeId, "LIFTER_A", StringComparison.OrdinalIgnoreCase) || mheInstanceId.StartsWith("SHUTTLE_", StringComparison.OrdinalIgnoreCase) || mheTypeId.StartsWith("SHUTTLE_", StringComparison.OrdinalIgnoreCase))
                                {
                                    retrievalWorkInProcessInfoIds.Add(retrievingInfoId);
                                }
                            }
                        }
                    }
                }
            }

            Dictionary<string, JObject> storageLocationByScheduleFamily = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            JArray storagePlans = data["STORAGE_PLAN"] as JArray;
            if (storagePlans != null)
            {
                foreach (JToken storagePlanToken in storagePlans)
                {
                    JObject storagePlan = storagePlanToken as JObject;
                    if (storagePlan == null)
                    {
                        continue;
                    }

                    JArray storageSchedules = storagePlan["STORAGE_SCHEDULE"] as JArray;
                    if (storageSchedules == null)
                    {
                        continue;
                    }

                    foreach (JToken storageScheduleToken in storageSchedules)
                    {
                        JObject storageSchedule = storageScheduleToken as JObject;
                        if (storageSchedule == null)
                        {
                            continue;
                        }

                        JArray storageLocationInfos = storageSchedule["STORAGE_LOCATION_INFO"] as JArray;
                        if (storageLocationInfos == null)
                        {
                            continue;
                        }

                        foreach (JToken locationToken in storageLocationInfos)
                        {
                            JObject storageLocation = locationToken as JObject;
                            if (storageLocation == null)
                            {
                                continue;
                            }

                            string completedScheduleId = Convert.ToString(storageLocation["COMPLETED_HANDLING_SCHEDULE_ID"]);
                            if (string.IsNullOrWhiteSpace(completedScheduleId))
                            {
                                continue;
                            }

                            string scheduleFamilyId = completedScheduleId;
                            int separatorIndex = scheduleFamilyId.LastIndexOf('_');
                            if (separatorIndex > 0)
                            {
                                scheduleFamilyId = scheduleFamilyId.Substring(0, separatorIndex);
                            }

                            storageLocationByScheduleFamily[scheduleFamilyId] = storageLocation;
                        }
                    }
                }
            }

            List<JArray> planGroups = new List<JArray>();
            JArray handlingRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            JArray productionRemainingPlans = data["PRODUCTION_REMAINING_PLAN"] as JArray;
            JArray bufferRemainingPlans = data["BUFFER_REMAINING_PLAN"] as JArray;
            JArray productionPlans = data["PRODUCTION_PLAN"] as JArray;
            if (handlingRemainingPlans != null)
            {
                planGroups.Add(handlingRemainingPlans);
            }

            if (productionRemainingPlans != null)
            {
                planGroups.Add(productionRemainingPlans);
            }

            if (bufferRemainingPlans != null)
            {
                planGroups.Add(bufferRemainingPlans);
            }

            if (productionPlans != null)
            {
                planGroups.Add(productionPlans);
            }

            int insertOrder = 0;
            foreach (JArray planGroup in planGroups)
            {
                foreach (JToken planToken in planGroup)
                {
                    JObject plan = planToken as JObject;
                    if (plan == null)
                    {
                        continue;
                    }

                    JArray handlingSchedules = plan["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null || !handlingSchedules.HasValues)
                    {
                        continue;
                    }

                    JArray productionSchedules = plan["PRODUCTION_SCHEDULE"] as JArray;
                    string handlingMaterialInfoId = Convert.ToString(plan["HANDLING_MATERIAL_INFO_ID"]);
                    bool isLifterBWorkInProcess = !string.IsNullOrWhiteSpace(handlingMaterialInfoId) && lifterBWorkInProcessMaterialInfoIds.Contains(handlingMaterialInfoId);
                    string handlingOperationInfoId = Convert.ToString(plan["HANDLING_OPERATION_INFO_ID"]);
                    string retrievingMaterialInfoId = Convert.ToString(plan["RETRIEVING_MATERIAL_INFO_ID"]);
                    bool isRetrievalWorkInProcess = (!string.IsNullOrWhiteSpace(retrievingMaterialInfoId) && retrievalWorkInProcessInfoIds.Contains(retrievingMaterialInfoId)) || (!string.IsNullOrWhiteSpace(handlingMaterialInfoId) && retrievalWorkInProcessInfoIds.Contains(handlingMaterialInfoId)) || (!string.IsNullOrWhiteSpace(handlingOperationInfoId) && retrievalWorkInProcessInfoIds.Contains(handlingOperationInfoId));
                    string originalMaterial = Convert.ToString(plan["MATERIAL_ID"]);
                    if (string.IsNullOrWhiteSpace(originalMaterial))
                    {
                        string remainingOperationInfoId = Convert.ToString(plan["HANDLING_OPERATION_INFO_ID"]);
                        JObject retrievingInfo;
                        if (!string.IsNullOrWhiteSpace(remainingOperationInfoId) && retrievingMaterialMap.TryGetValue(remainingOperationInfoId, out retrievingInfo))
                        {
                            originalMaterial = Convert.ToString(retrievingInfo["MATERIAL_ID"]);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(originalMaterial))
                    {
                        foreach (JToken scheduleToken in handlingSchedules)
                        {
                            string operationInfoId = Convert.ToString(scheduleToken["HANDLING_OPERATION_INFO_ID"]);
                            if (!string.IsNullOrWhiteSpace(operationInfoId) && operationToMaterial.ContainsKey(operationInfoId))
                            {
                                originalMaterial = operationToMaterial[operationInfoId];
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(originalMaterial))
                    {
                        continue;
                    }

                    foreach (JToken scheduleToken in handlingSchedules)
                    {
                        JObject schedule = scheduleToken as JObject;
                        if (schedule == null)
                        {
                            continue;
                        }

                        string resourceId = Convert.ToString(schedule["MHE_INSTANCE_ID"]);
                        bool isRetrievalLifter = string.Equals(resourceId, "LIFTER_A_001", StringComparison.OrdinalIgnoreCase);
                        bool isStorageLifter = string.Equals(resourceId, "LIFTER_B_001", StringComparison.OrdinalIgnoreCase);
                        if (!isRetrievalLifter && !isStorageLifter)
                        {
                            continue;
                        }

                        string scheduleId = Convert.ToString(schedule["HANDLING_SCHEDULE_ID"]);
                        if (!string.IsNullOrWhiteSpace(scheduleId) && addedScheduleIds.Contains(scheduleId))
                        {
                            continue;
                        }

                        string operationInfoId = Convert.ToString(schedule["HANDLING_OPERATION_INFO_ID"]);
                        string[] operationParts = operationInfoId.Split('_');
                        int operationSuffix;
                        if (operationParts.Length == 0 || !int.TryParse(operationParts[operationParts.Length - 1], out operationSuffix))
                        {
                            continue;
                        }

                        if (isRetrievalLifter && operationSuffix != 2)
                        {
                            continue;
                        }

                        if (isRetrievalLifter && isRetrievalWorkInProcess)
                        {
                            continue;
                        }

                        string startDateTime = Convert.ToString(schedule["START_DATETIME"]);
                        DateTime parsedStartDateTime;
                        if (!DateTime.TryParse(startDateTime, out parsedStartDateTime))
                        {
                            continue;
                        }

                        if (isRetrievalLifter)
                        {
                            string retrievalLocationInfoId = Convert.ToString(schedule["RETRIEVAL_LOCATION_INFO_ID"]);
                            int tier;
                            JObject asrsLocation;
                            if (!string.IsNullOrWhiteSpace(retrievalLocationInfoId) && asrsLocationMap.TryGetValue(retrievalLocationInfoId, out asrsLocation))
                            {
                                if (!int.TryParse(Convert.ToString(asrsLocation["TIER_ID"]), out tier))
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                string locationNumberText = retrievalLocationInfoId;
                                if (locationNumberText.StartsWith("LOC", StringComparison.OrdinalIgnoreCase))
                                {
                                    locationNumberText = locationNumberText.Substring(3);
                                }

                                int locationNumber;
                                if (!int.TryParse(locationNumberText, out locationNumber) || locationNumber <= 0)
                                {
                                    continue;
                                }

                                tier = (locationNumber - 1) / 10;
                            }

                            string productType = originalMaterial;
                            if (originalMaterial == "MT_A" || originalMaterial == "MT_B" || originalMaterial == "MT_C")
                            {
                                string machineId = "";
                                if (productionSchedules != null)
                                {
                                    foreach (JToken productionSchedule in productionSchedules)
                                    {
                                        string productionLocationId = Convert.ToString(productionSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                                        if (!string.IsNullOrWhiteSpace(retrievalLocationInfoId) && !string.IsNullOrWhiteSpace(productionLocationId) && !string.Equals(retrievalLocationInfoId, productionLocationId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }

                                        string candidateMachineId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                                        if (candidateMachineId == "CNC_A_001" || candidateMachineId == "CNC_A_002")
                                        {
                                            machineId = candidateMachineId;
                                            break;
                                        }
                                    }
                                }

                                if (machineId == "CNC_A_001")
                                {
                                    productType = originalMaterial + "_A";
                                }
                                else if (machineId == "CNC_A_002")
                                {
                                    productType = originalMaterial + "_B";
                                }
                            }

                            JObject retrievalTask = new JObject();
                            retrievalTask["action"] = "retrieval";
                            retrievalTask["tier"] = tier;
                            retrievalTask["material"] = productType;
                            retrievalTask["_EVENT_TIME"] = parsedStartDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                            retrievalTask["_INSERT_ORDER"] = insertOrder++;
                            sequenceTasks.Add(retrievalTask);
                        }

                        if (isStorageLifter)
                        {
                            string scheduleFamilyId = scheduleId;
                            int separatorIndex = scheduleFamilyId.LastIndexOf('_');
                            if (separatorIndex > 0)
                            {
                                scheduleFamilyId = scheduleFamilyId.Substring(0, separatorIndex);
                            }

                            JObject storageLocation;
                            if (!storageLocationByScheduleFamily.TryGetValue(scheduleFamilyId, out storageLocation))
                            {
                                continue;
                            }

                            int tier;
                            int row;
                            int column;
                            if (!int.TryParse(Convert.ToString(storageLocation["TIER_ID"]), out tier) || !int.TryParse(Convert.ToString(storageLocation["ROW_ID"]), out row) || !int.TryParse(Convert.ToString(storageLocation["COLUMN_ID"]), out column))
                            {
                                continue;
                            }

                            string completeProductType;
                            if (originalMaterial.StartsWith("Complete_", StringComparison.OrdinalIgnoreCase))
                            {
                                completeProductType = originalMaterial;
                            }
                            else if (originalMaterial.StartsWith("MT_", StringComparison.OrdinalIgnoreCase))
                            {
                                completeProductType = "Complete_" + originalMaterial.Substring(3);
                            }
                            else
                            {
                                completeProductType = originalMaterial;
                            }

                            JObject storageTask = new JObject();
                            storageTask["action"] = isLifterBWorkInProcess ? "wip" : "storage";
                            storageTask["tier"] = tier;
                            storageTask["row"] = row;
                            storageTask["column"] = column;
                            storageTask["material"] = completeProductType;
                            storageTask["_EVENT_TIME"] = parsedStartDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                            storageTask["_INSERT_ORDER"] = insertOrder++;
                            sequenceTasks.Add(storageTask);
                        }

                        if (!string.IsNullOrWhiteSpace(scheduleId))
                        {
                            addedScheduleIds.Add(scheduleId);
                        }
                    }
                }
            }

            sequenceTasks.Sort(delegate (JObject a, JObject b)
            {
                DateTime aTime;
                DateTime bTime;
                bool aParsed = DateTime.TryParse(Convert.ToString(a["_EVENT_TIME"]), out aTime);
                bool bParsed = DateTime.TryParse(Convert.ToString(b["_EVENT_TIME"]), out bTime);
                if (aParsed && bParsed)
                {
                    int timeCompare = DateTime.Compare(aTime.ToUniversalTime(), bTime.ToUniversalTime());
                    if (timeCompare != 0)
                    {
                        return timeCompare;
                    }
                }
                else if (aParsed)
                {
                    return -1;
                }
                else if (bParsed)
                {
                    return 1;
                }

                int aOrder = Convert.ToInt32(a["_INSERT_ORDER"]);
                int bOrder = Convert.ToInt32(b["_INSERT_ORDER"]);
                return aOrder.CompareTo(bOrder);
            });
            JArray result = new JArray();
            foreach (JObject task in sequenceTasks)
            {
                string action = Convert.ToString(task["action"]);
                JObject resultItem = new JObject();
                resultItem["action"] = action;
                resultItem["tier"] = Convert.ToInt32(task["tier"]);
                if (action == "storage" || action == "wip")
                {
                    resultItem["row"] = Convert.ToInt32(task["row"]);
                    resultItem["column"] = Convert.ToInt32(task["column"]);
                }

                resultItem["material"] = Convert.ToString(task["material"]);
                result.Add(resultItem);
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public string[] GetLifterRetrievalWIPProperties(JObject data, string lifterTypeId)
        {
            string[] result = new string[4]
            {
                "",
                "",
                "",
                ""
            };
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string mheTypeId = Convert.ToString(mhe["MHE_TYPE_ID"]);
                if (mheTypeId != lifterTypeId)
                    continue;
                JArray statusArray = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                if (statusArray == null || !statusArray.HasValues)
                    return result;
                JToken handlingMaterialInfo = null;
                foreach (JToken handlingStatus in statusArray)
                {
                    JArray retrieving = handlingStatus["RETRIEVING_MATERIAL_INFO"] as JArray;
                    if (retrieving != null && retrieving.HasValues)
                    {
                        handlingMaterialInfo = retrieving[0];
                        break;
                    }

                    JArray handling = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                    if (handling != null && handling.HasValues)
                    {
                        handlingMaterialInfo = handling[0];
                        break;
                    }
                }

                if (handlingMaterialInfo == null)
                    return result;
                string currentPositionZ = Convert.ToString(mhe.SelectToken("CURRENT_POSITION.CURRENT_POSITION_Z"));
                string materialId = Convert.ToString(handlingMaterialInfo["MATERIAL_ID"]);
                string handlingMaterialInfoId = Convert.ToString(handlingMaterialInfo["HANDLING_MATERIAL_INFO_ID"] ?? handlingMaterialInfo["RETRIEVING_MATERIAL_INFO_ID"]);
                result[0] = currentPositionZ;
                result[1] = materialId;
                if (materialId == "MT_A" || materialId == "MT_B" || materialId == "MT_C")
                {
                    foreach (var remainingPlan in data["HANDLING_REMAINING_PLAN"])
                    {
                        string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                        if (remainingMaterialInfoId != handlingMaterialInfoId)
                            continue;
                        JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                        if (productionSchedules != null && productionSchedules.HasValues)
                        {
                            string machineInstanceId = Convert.ToString(productionSchedules[0]["MACHINE_INSTANCE_ID"]);
                            if (machineInstanceId == "CNC_A_001")
                                result[1] = materialId + "_A";
                            else if (machineInstanceId == "CNC_A_002")
                                result[1] = materialId + "_B";
                        }

                        break;
                    }
                }

                return result;
            }

            return result;
        }

        public string[] GetLifterStorageWIPProperties(JObject data, string lifterTypeId)
        {
            string[] result = new string[8]
            {
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                ""
            };
            foreach (var mhe in data["MHE_INSTANCE"])
            {
                string mheTypeId = Convert.ToString(mhe["MHE_TYPE_ID"]);
                if (mheTypeId != lifterTypeId)
                    continue;
                JArray statusArray = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                if (statusArray == null || !statusArray.HasValues)
                    return result;
                JToken handlingStatus = statusArray[0];
                JArray handlingMaterialInfoArray = handlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                if (handlingMaterialInfoArray == null || !handlingMaterialInfoArray.HasValues)
                    return result;
                JToken handlingMaterialInfo = handlingMaterialInfoArray[0];
                string currentPositionZ = Convert.ToString(mhe["CURRENT_POSITION"]["CURRENT_POSITION_Z"]);
                string materialId = Convert.ToString(handlingMaterialInfo["MATERIAL_ID"]);
                string handlingMaterialInfoId = Convert.ToString(handlingMaterialInfo["HANDLING_MATERIAL_INFO_ID"]);
                result[0] = currentPositionZ;
                string lastHandlingScheduleId = "";
                DateTime latestEndDatetime = DateTime.MinValue;
                foreach (var remainingPlan in data["HANDLING_REMAINING_PLAN"])
                {
                    string remainingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                    if (remainingMaterialInfoId != handlingMaterialInfoId)
                        continue;
                    JArray handlingSchedules = remainingPlan["HANDLING_SCHEDULE"] as JArray;
                    if (handlingSchedules == null || !handlingSchedules.HasValues)
                        continue;
                    foreach (var schedule in handlingSchedules)
                    {
                        string endDatetimeString = Convert.ToString(schedule["END_DATETIME"]);
                        DateTime endDatetime;
                        if (DateTime.TryParse(endDatetimeString, out endDatetime) && endDatetime > latestEndDatetime)
                        {
                            latestEndDatetime = endDatetime;
                            lastHandlingScheduleId = Convert.ToString(schedule["HANDLING_SCHEDULE_ID"]);
                        }
                    }
                }

                result[3] = lastHandlingScheduleId;
                if (string.IsNullOrEmpty(lastHandlingScheduleId))
                    return result;
                foreach (var storagePlan in data["STORAGE_PLAN"])
                {
                    JArray storageSchedules = storagePlan["STORAGE_SCHEDULE"] as JArray;
                    if (storageSchedules == null || !storageSchedules.HasValues)
                        continue;
                    foreach (var storageSchedule in storageSchedules)
                    {
                        string storageMaterialId = Convert.ToString(storageSchedule["MATERIAL_ID"]);
                        if (storageMaterialId != materialId)
                            continue;
                        JArray locationInfos = storageSchedule["STORAGE_LOCATION_INFO"] as JArray;
                        if (locationInfos == null || !locationInfos.HasValues)
                            continue;
                        foreach (var locationInfo in locationInfos)
                        {
                            string completedHandlingScheduleId = Convert.ToString(locationInfo["COMPLETED_HANDLING_SCHEDULE_ID"]);
                            if (completedHandlingScheduleId != lastHandlingScheduleId)
                                continue;
                            result[1] = Convert.ToString(locationInfo["TIER_ID"]);
                            result[2] = Convert.ToString(locationInfo["COLUMN_ID"]);
                            result[3] = Convert.ToString(locationInfo["ROW_ID"]);
                            break;
                        }

                        if (!string.IsNullOrEmpty(result[4]))
                            break;
                    }

                    if (!string.IsNullOrEmpty(result[4]))
                        break;
                }

                string productType = GetCompleteProductType(materialId);
                result[4] = productType;
                return result;
            }

            return result;
        }

        public Dictionary<string, string> GetShuttleRetrievalMaterialMap(JObject data, int rowCount, int columnCount, int tierCount)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (data == null || rowCount <= 0 || columnCount <= 0 || tierCount <= 0)
            {
                return result;
            }

            Dictionary<string, JObject> remainingPlanByRmiId = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            JArray handlingRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            if (handlingRemainingPlans != null)
            {
                foreach (JToken remainingPlanToken in handlingRemainingPlans)
                {
                    JObject remainingPlan = remainingPlanToken as JObject;
                    if (remainingPlan == null)
                    {
                        continue;
                    }

                    string rmiId = Convert.ToString(remainingPlan["HANDLING_OPERATION_INFO_ID"]);
                    if (!string.IsNullOrWhiteSpace(rmiId))
                    {
                        remainingPlanByRmiId[rmiId] = remainingPlan;
                    }
                }
            }

            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            if (mheInstances == null)
            {
                return result;
            }

            foreach (JToken mheToken in mheInstances)
            {
                JObject mheInstance = mheToken as JObject;
                if (mheInstance == null)
                {
                    continue;
                }

                string mheInstanceId = Convert.ToString(mheInstance["MHE_INSTANCE_ID"]);
                if (string.IsNullOrWhiteSpace(mheInstanceId) || !mheInstanceId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JArray handlingStatuses = mheInstance["MATERIAL_HANDLING_STATUS"] as JArray;
                if (handlingStatuses == null)
                {
                    continue;
                }

                foreach (JToken statusToken in handlingStatuses)
                {
                    JObject handlingStatus = statusToken as JObject;
                    if (handlingStatus == null)
                    {
                        continue;
                    }

                    JArray retrievingMaterialInfos = handlingStatus["RETRIEVING_MATERIAL_INFO"] as JArray;
                    if (retrievingMaterialInfos == null)
                    {
                        continue;
                    }

                    foreach (JToken retrievingInfoToken in retrievingMaterialInfos)
                    {
                        JObject retrievingInfo = retrievingInfoToken as JObject;
                        if (retrievingInfo == null)
                        {
                            continue;
                        }

                        string rmiId = Convert.ToString(retrievingInfo["RETRIEVING_MATERIAL_INFO_ID"]);
                        string materialId = Convert.ToString(retrievingInfo["MATERIAL_ID"]);
                        string locationInfoId = Convert.ToString(retrievingInfo["RETRIEVAL_LOCATION_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(rmiId) || string.IsNullOrWhiteSpace(materialId) || string.IsNullOrWhiteSpace(locationInfoId))
                        {
                            continue;
                        }

                        JObject remainingPlan;
                        if (!remainingPlanByRmiId.TryGetValue(rmiId, out remainingPlan))
                        {
                            continue;
                        }

                        JArray handlingSchedules = remainingPlan["HANDLING_SCHEDULE"] as JArray;
                        if (handlingSchedules == null)
                        {
                            continue;
                        }

                        bool hasRetrievalSchedule = false;
                        foreach (JToken scheduleToken in handlingSchedules)
                        {
                            JObject schedule = scheduleToken as JObject;
                            if (schedule == null)
                            {
                                continue;
                            }

                            string operationInfoId = Convert.ToString(schedule["HANDLING_OPERATION_INFO_ID"]);
                            string scheduleMheId = Convert.ToString(schedule["MHE_INSTANCE_ID"]);
                            if (string.IsNullOrWhiteSpace(operationInfoId) || string.IsNullOrWhiteSpace(scheduleMheId))
                            {
                                continue;
                            }

                            string[] operationParts = operationInfoId.Split('_');
                            int operationSuffix;
                            if (operationParts.Length == 0 || !int.TryParse(operationParts[operationParts.Length - 1], out operationSuffix))
                            {
                                continue;
                            }

                            if (operationSuffix == 1 && scheduleMheId.StartsWith("SHUTTLE_A_", StringComparison.OrdinalIgnoreCase))
                            {
                                hasRetrievalSchedule = true;
                                break;
                            }
                        }

                        if (!hasRetrievalSchedule)
                        {
                            continue;
                        }

                        string productTypeName = materialId;
                        bool isFlexibleMaterial = string.Equals(materialId, "MT_A", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_B", StringComparison.OrdinalIgnoreCase) || string.Equals(materialId, "MT_C", StringComparison.OrdinalIgnoreCase);
                        if (isFlexibleMaterial)
                        {
                            JArray productionSchedules = remainingPlan["PRODUCTION_SCHEDULE"] as JArray;
                            if (productionSchedules != null)
                            {
                                foreach (JToken productionScheduleToken in productionSchedules)
                                {
                                    JObject productionSchedule = productionScheduleToken as JObject;
                                    if (productionSchedule == null)
                                    {
                                        continue;
                                    }

                                    string machineInstanceId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                                    if (string.Equals(machineInstanceId, "CNC_A_001", StringComparison.OrdinalIgnoreCase))
                                    {
                                        productTypeName = materialId + "_A";
                                        break;
                                    }

                                    if (string.Equals(machineInstanceId, "CNC_A_002", StringComparison.OrdinalIgnoreCase))
                                    {
                                        productTypeName = materialId + "_B";
                                        break;
                                    }
                                }
                            }
                        }

                        string locationNumberText = locationInfoId;
                        if (locationNumberText.StartsWith("LOC", StringComparison.OrdinalIgnoreCase))
                        {
                            locationNumberText = locationNumberText.Substring(3);
                        }

                        int locationNumber;
                        if (!int.TryParse(locationNumberText, out locationNumber) || locationNumber <= 0)
                        {
                            continue;
                        }

                        int zeroBasedLocationNumber = locationNumber - 1;
                        int tier = zeroBasedLocationNumber / rowCount;
                        int row = zeroBasedLocationNumber % rowCount;
                        int column = zeroBasedLocationNumber % columnCount;
                        if (tier < 0 || tier >= tierCount || row < 0 || row >= rowCount || column < 0 || column >= columnCount)
                        {
                            continue;
                        }

                        string positionKey = tier + "_" + row + "_" + column;
                        result[positionKey] = productTypeName;
                    }
                }
            }

            return result;
        }

        public string[] GetInventoryListforASRS(JObject data, int rowCount, int columnCount, int targetTier, Dictionary<string, string> flexibleMaterialMapByLocation, Dictionary<string, string> handlingMaterialStorageMap)
        {
            if (rowCount < 0)
            {
                rowCount = 0;
            }

            if (columnCount < 0)
            {
                columnCount = 0;
            }

            string[] result = new string[rowCount * columnCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = "dummy";
            }

            if (data != null)
            {
                JArray asrsInstances = data["ASRS_INSTANCE"] as JArray;
                if (asrsInstances != null)
                {
                    foreach (JToken asrsToken in asrsInstances)
                    {
                        JObject asrsInstance = asrsToken as JObject;
                        if (asrsInstance == null)
                        {
                            continue;
                        }

                        JArray inventoryStatuses = asrsInstance["INVENTORY_STATUS"] as JArray;
                        if (inventoryStatuses == null)
                        {
                            continue;
                        }

                        foreach (JToken inventoryToken in inventoryStatuses)
                        {
                            JObject inventoryStatus = inventoryToken as JObject;
                            if (inventoryStatus == null)
                            {
                                continue;
                            }

                            string materialId = Convert.ToString(inventoryStatus["MATERIAL_ID"]);
                            if (string.IsNullOrWhiteSpace(materialId))
                            {
                                continue;
                            }

                            JArray locationInfos = inventoryStatus["LOCATION_INFO"] as JArray;
                            if (locationInfos == null)
                            {
                                continue;
                            }

                            foreach (JToken locationToken in locationInfos)
                            {
                                JObject locationInfo = locationToken as JObject;
                                if (locationInfo == null)
                                {
                                    continue;
                                }

                                int tier;
                                if (!int.TryParse(Convert.ToString(locationInfo["TIER_ID"]), out tier))
                                {
                                    continue;
                                }

                                if (tier != targetTier)
                                {
                                    continue;
                                }

                                int row;
                                int column;
                                if (!int.TryParse(Convert.ToString(locationInfo["ROW_ID"]), out row))
                                {
                                    continue;
                                }

                                if (!int.TryParse(Convert.ToString(locationInfo["COLUMN_ID"]), out column))
                                {
                                    continue;
                                }

                                if (row < 0 || row >= rowCount || column < 0 || column >= columnCount)
                                {
                                    continue;
                                }

                                int resultIndex = row * columnCount + column;
                                string locationInfoId = Convert.ToString(locationInfo["LOCATION_INFO_ID"]);
                                string completedHandlingOperationInfoId = Convert.ToString(locationInfo["COMPLETED_HANDLING_OPERATION_INFO_ID"]);
                                string productTypeName;
                                if (!string.IsNullOrWhiteSpace(completedHandlingOperationInfoId))
                                {
                                    productTypeName = GetCompleteProductType(materialId);
                                }
                                else
                                {
                                    productTypeName = materialId;
                                    if (!string.IsNullOrWhiteSpace(locationInfoId) && flexibleMaterialMapByLocation != null)
                                    {
                                        string flexibleKey = GetFlexibleMaterialKey(materialId, locationInfoId);
                                        string flexibleMaterialId;
                                        if (flexibleMaterialMapByLocation.TryGetValue(flexibleKey, out flexibleMaterialId))
                                        {
                                            productTypeName = flexibleMaterialId;
                                        }
                                    }
                                }

                                result[resultIndex] = productTypeName;
                            }
                        }
                    }
                }
            }

            if (handlingMaterialStorageMap != null)
            {
                foreach (KeyValuePair<string, string> item in handlingMaterialStorageMap)
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        continue;
                    }

                    string[] positionParts = item.Key.Split('_');
                    if (positionParts.Length != 3)
                    {
                        continue;
                    }

                    int tier;
                    int row;
                    int column;
                    if (!int.TryParse(positionParts[0], out tier) || !int.TryParse(positionParts[1], out row) || !int.TryParse(positionParts[2], out column))
                    {
                        continue;
                    }

                    if (tier != targetTier)
                    {
                        continue;
                    }

                    if (row < 0 || row >= rowCount || column < 0 || column >= columnCount)
                    {
                        continue;
                    }

                    int resultIndex = row * columnCount + column;
                    result[resultIndex] = item.Value;
                }
            }

            return result;
        }

        public Dictionary<string, string> GetHandlingMaterialStorageMap(JObject data)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (data == null)
            {
                return result;
            }

            JArray mheInstances = data["MHE_INSTANCE"] as JArray;
            JArray handlingRemainingPlans = data["HANDLING_REMAINING_PLAN"] as JArray;
            JArray storagePlans = data["STORAGE_PLAN"] as JArray;
            if (mheInstances == null)
            {
                return result;
            }

            if (handlingRemainingPlans == null)
            {
                return result;
            }

            if (storagePlans == null)
            {
                return result;
            }

            foreach (JToken mheToken in mheInstances)
            {
                JObject mheInstance = mheToken as JObject;
                if (mheInstance == null)
                {
                    continue;
                }

                string mheTypeId = Convert.ToString(mheInstance["MHE_TYPE_ID"]);
                if (!string.Equals(mheTypeId, "SHUTTLE_A", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string mheInstanceId = Convert.ToString(mheInstance["MHE_INSTANCE_ID"]);
                if (string.IsNullOrWhiteSpace(mheInstanceId))
                {
                    continue;
                }

                JArray materialHandlingStatuses = mheInstance["MATERIAL_HANDLING_STATUS"] as JArray;
                if (materialHandlingStatuses == null || !materialHandlingStatuses.HasValues)
                {
                    continue;
                }

                foreach (JToken statusToken in materialHandlingStatuses)
                {
                    JObject materialHandlingStatus = statusToken as JObject;
                    if (materialHandlingStatus == null)
                    {
                        continue;
                    }

                    JArray handlingMaterialInfos = materialHandlingStatus["HANDLING_MATERIAL_INFO"] as JArray;
                    if (handlingMaterialInfos == null || !handlingMaterialInfos.HasValues)
                    {
                        continue;
                    }

                    foreach (JToken materialInfoToken in handlingMaterialInfos)
                    {
                        JObject handlingMaterialInfo = materialInfoToken as JObject;
                        if (handlingMaterialInfo == null)
                        {
                            continue;
                        }

                        string materialId = Convert.ToString(handlingMaterialInfo["MATERIAL_ID"]);
                        string handlingMaterialInfoId = Convert.ToString(handlingMaterialInfo["HANDLING_MATERIAL_INFO_ID"]);
                        if (string.IsNullOrWhiteSpace(materialId))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(handlingMaterialInfoId))
                        {
                            continue;
                        }

                        JObject matchedRemainingPlan = null;
                        foreach (JToken remainingPlanToken in handlingRemainingPlans)
                        {
                            JObject remainingPlan = remainingPlanToken as JObject;
                            if (remainingPlan == null)
                            {
                                continue;
                            }

                            string remainingHandlingMaterialInfoId = Convert.ToString(remainingPlan["HANDLING_MATERIAL_INFO_ID"]);
                            if (!string.Equals(remainingHandlingMaterialInfoId, handlingMaterialInfoId, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            matchedRemainingPlan = remainingPlan;
                            break;
                        }

                        if (matchedRemainingPlan == null)
                        {
                            continue;
                        }

                        JArray handlingSchedules = matchedRemainingPlan["HANDLING_SCHEDULE"] as JArray;
                        if (handlingSchedules == null || !handlingSchedules.HasValues)
                        {
                            continue;
                        }

                        JObject matchedHandlingSchedule = null;
                        foreach (JToken scheduleToken in handlingSchedules)
                        {
                            JObject handlingSchedule = scheduleToken as JObject;
                            if (handlingSchedule == null)
                            {
                                continue;
                            }

                            string scheduleMheInstanceId = Convert.ToString(handlingSchedule["MHE_INSTANCE_ID"]);
                            if (!string.Equals(scheduleMheInstanceId, mheInstanceId, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            matchedHandlingSchedule = handlingSchedule;
                            break;
                        }

                        if (matchedHandlingSchedule == null)
                        {
                            continue;
                        }

                        string handlingScheduleId = Convert.ToString(matchedHandlingSchedule["HANDLING_SCHEDULE_ID"]);
                        if (string.IsNullOrWhiteSpace(handlingScheduleId))
                        {
                            continue;
                        }

                        JObject matchedStorageLocation = null;
                        foreach (JToken storagePlanToken in storagePlans)
                        {
                            if (matchedStorageLocation != null)
                            {
                                break;
                            }

                            JObject storagePlan = storagePlanToken as JObject;
                            if (storagePlan == null)
                            {
                                continue;
                            }

                            JArray storageSchedules = storagePlan["STORAGE_SCHEDULE"] as JArray;
                            if (storageSchedules == null)
                            {
                                continue;
                            }

                            foreach (JToken storageScheduleToken in storageSchedules)
                            {
                                if (matchedStorageLocation != null)
                                {
                                    break;
                                }

                                JObject storageSchedule = storageScheduleToken as JObject;
                                if (storageSchedule == null)
                                {
                                    continue;
                                }

                                string storageMaterialId = Convert.ToString(storageSchedule["MATERIAL_ID"]);
                                if (!string.Equals(storageMaterialId, materialId, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                JArray storageLocationInfos = storageSchedule["STORAGE_LOCATION_INFO"] as JArray;
                                if (storageLocationInfos == null)
                                {
                                    continue;
                                }

                                foreach (JToken locationToken in storageLocationInfos)
                                {
                                    JObject storageLocation = locationToken as JObject;
                                    if (storageLocation == null)
                                    {
                                        continue;
                                    }

                                    string completedHandlingScheduleId = Convert.ToString(storageLocation["COMPLETED_HANDLING_SCHEDULE_ID"]);
                                    if (!string.Equals(completedHandlingScheduleId, handlingScheduleId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    matchedStorageLocation = storageLocation;
                                    break;
                                }
                            }
                        }

                        if (matchedStorageLocation == null)
                        {
                            continue;
                        }

                        int row;
                        int column;
                        int tier;
                        if (!int.TryParse(Convert.ToString(matchedStorageLocation["ROW_ID"]), out row))
                        {
                            continue;
                        }

                        if (!int.TryParse(Convert.ToString(matchedStorageLocation["COLUMN_ID"]), out column))
                        {
                            continue;
                        }

                        if (!int.TryParse(Convert.ToString(matchedStorageLocation["TIER_ID"]), out tier))
                        {
                            continue;
                        }

                        string positionKey = tier + "_" + row + "_" + column;
                        string completeProductType = GetCompleteProductType(materialId);
                        result[positionKey] = completeProductType;
                    }
                }
            }

            return result;
        }

        private string GetCompleteProductType(string materialId)
        {
            if (string.IsNullOrWhiteSpace(materialId))
            {
                return "dummy";
            }

            if (!materialId.StartsWith("MT_", StringComparison.OrdinalIgnoreCase) || materialId.Length <= 3)
            {
                return materialId;
            }

            return "Complete_" + materialId.Substring(3);
        }

        private string GetFlexibleMaterialKey(string materialId, string locationInfoId)
        {
            return materialId + "|" + locationInfoId;
        }

        public Dictionary<string, string> GetFlexibleMaterialMapByLocation(JObject data)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (data == null)
            {
                return result;
            }

            JArray productionPlans = data["PRODUCTION_PLAN"] as JArray;
            if (productionPlans == null)
            {
                return result;
            }

            foreach (JToken planToken in productionPlans)
            {
                JObject productionPlan = planToken as JObject;
                if (productionPlan == null)
                {
                    continue;
                }

                string originalMaterialId = Convert.ToString(productionPlan["MATERIAL_ID"]);
                bool isFlexibleMaterial = string.Equals(originalMaterialId, "MT_A", StringComparison.OrdinalIgnoreCase) || string.Equals(originalMaterialId, "MT_B", StringComparison.OrdinalIgnoreCase) || string.Equals(originalMaterialId, "MT_C", StringComparison.OrdinalIgnoreCase);
                if (!isFlexibleMaterial)
                {
                    continue;
                }

                JArray productionSchedules = productionPlan["PRODUCTION_SCHEDULE"] as JArray;
                if (productionSchedules == null)
                {
                    continue;
                }

                foreach (JToken scheduleToken in productionSchedules)
                {
                    JObject productionSchedule = scheduleToken as JObject;
                    if (productionSchedule == null)
                    {
                        continue;
                    }

                    string locationInfoId = Convert.ToString(productionSchedule["RETRIEVAL_LOCATION_INFO_ID"]);
                    if (string.IsNullOrWhiteSpace(locationInfoId))
                    {
                        continue;
                    }

                    string machineInstanceId = Convert.ToString(productionSchedule["MACHINE_INSTANCE_ID"]);
                    string convertedMaterialId;
                    if (string.Equals(machineInstanceId, "CNC_A_001", StringComparison.OrdinalIgnoreCase))
                    {
                        convertedMaterialId = originalMaterialId + "_A";
                    }
                    else if (string.Equals(machineInstanceId, "CNC_A_002", StringComparison.OrdinalIgnoreCase))
                    {
                        convertedMaterialId = originalMaterialId + "_B";
                    }
                    else
                    {
                        continue;
                    }

                    string key = GetFlexibleMaterialKey(originalMaterialId, locationInfoId);
                    result[key] = convertedMaterialId;
                }
            }

            return result;
        }
    }
}
