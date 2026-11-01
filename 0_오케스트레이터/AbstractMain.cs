using System.IO;
using System.ComponentModel.Composition;
using VisualComponents.Create3D;
using VisualComponents.UX.Shared;
using Newtonsoft.Json.Linq;
using System;
using Caliburn.Micro;
using System.Threading;
using System.Timers;

namespace DT_KETI
{
    [Export(typeof(IPlugin))]
    public class AbstractMain : IPlugin
    {
        private const string EXECUTION_MODE = "Initial";
        private const string WORKSPACE_ROOT = @"%PORTFOLIO_ROOT%";
        private const string RUN_ID = "";
        private const string SCENARIO = "scenario";
        private const string REPORT_DIRECTORY = @"artifacts\runs\{ExecutionMode}-{RunId}";
        private const string MODEL_OUTPUT_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\loaded-model.vcmx";
        private const int PLAN_VERSION = 1;
        private const bool OPEN_REPORTS_ON_COMPLETE = true;
        private const string VNS_PROJECT_PATH = @"1_메타휴리스틱\PDP_ASRS_WIP.csproj";
        private const string VNS_EXECUTABLE_PATH = @"1_메타휴리스틱\bin\Debug\net10.0\PDP_ASRS_WIP.exe";
        private const double VNS_TIMEOUT_SECONDS = 600.0;
        private const string PLANNER_MODE = "hyb";
        private const int PLANNER_SEED = 1;
        private const int PLANNER_EVALUATIONS = 5000;
        private const string PYTHON_EXECUTABLE_PATH = @".venv\Scripts\python.exe";
        private const string GNN_DIRECTORY = @"2_규칙모델";
        private const string GNN_VCRAW_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\instance_VCRAW.json";
        private const string GNN_INSTANCE_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\instance_INSTANCE.json";
        private const string WORKSPACE_TOOLS_DIRECTORY = @"0_오케스트레이터\tools";
        private const string INITIAL_P4R_PATH = @"3_시나리오\scenario_04_P4R_INPUT_CLEAN.json";
        private const string INITIAL_SEQUENCE_MODEL_PATH = @"4_VC모델\sequence\base_260827.vcmx";
        private const string GNN_RULE_TEMPLATE_PATH = @"4_VC모델\rule\trained-rule-optimized.vcmx";
        private const string GNN_TRAINING_DATA_DIRECTORY = @"artifacts\runs\{ExecutionMode}-{RunId}\gnn-training-replays";
        private const string GNN_TRAINING_CHECKPOINT_PATH = @"artifacts\trained-rule\latest.pt";
        private const string GNN_WEIGHT_ARTIFACT_DIRECTORY = @"artifacts\trained-rule";
        private const string TRAINED_RULE_MODEL_PATH = @"4_VC모델\rule\trained-rule-optimized.vcmx";
        private const string ABNORMAL_START_VIEW = "View79";
        private const string TRAINED_RULE_OUTPUT_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\trained-rule-optimized.vcmx";
        private const int GNN_TRAINING_EPOCHS = 200;
        private const double GNN_TRAINING_TIMEOUT_SECONDS = 7200.0;
        private const bool TRAIN_RULE_ON_INITIAL = true;
        private const bool REUSE_TRAINED_RULE_ON_INITIAL = false;
        private const double INITIAL_SIMULATION_HORIZON_SECONDS = 2800.0;
        private const double INITIAL_SIMULATION_SPEED_FACTOR = 6.0;
        private const string ABNORMAL_SNAPSHOT_P4R_PATH = @"3_시나리오\scenario_04_P4R_ABNORMAL_INPUT.json";
        private const string VALIDATION_SEQUENCE_MODEL_PATH = @"4_VC모델\sequence\base_260827.vcmx";
        private const string ABNORMAL_SEQUENCE_OUTPUT_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\abnormal-sequence.p4r.json";
        private const string FINAL_SNAPSHOT_OUTPUT_PATH = @"artifacts\runs\{ExecutionMode}-{RunId}\abnormal-final-snapshot.p4r.json";
        private const string ABNORMAL_RECOVERY_SNAPSHOT_PATH = @"3_시나리오\scenario_04_P4R_ABNORMAL_600S_RECONSTRUCTED.json";
        private const string INITIAL_REPORT_PATH = "";
        private const string GNN_SERVICE_PATH = @"0_오케스트레이터\gnn_service.py";
        private const string GNN_CHECKPOINT_PATH = @"2_규칙모델\model.pt";
        private const string EMBEDDED_GNN_ARTIFACT_DIRECTORY = @"artifacts\embedded-gnn";
        private const double GNN_SCALE_MAKESPAN = 139916.0;
        private const double GNN_TIMEOUT_SECONDS = 5.0;
        private const double ABNORMAL_SIMULATION_HORIZON_SECONDS = 601.5;
        private const double ABNORMAL_SIMULATION_SPEED_FACTOR = 30.0;
        private const double RESCHEDULED_SIMULATION_HORIZON_SECONDS = 2000.0;
        private const double RESCHEDULED_SIMULATION_SPEED_FACTOR = 6.0;
        private const bool RUN_GNN_COMMUNICATION_SMOKE_TEST = false;
        private static readonly bool USE_VCMX_PYTHON_GNN_DISPATCH = true;
        private static readonly bool USE_EMBEDDED_GNN_DISPATCH = false;
        private static readonly bool RUN_EMBEDDED_GNN_FORWARD_SELF_TEST = false;
        private static readonly string[] ShuttleConveyorComponents =
        {
            "Terminal Conveyor #1",
            "Terminal Conveyor #5",
            "Terminal Conveyor #2",
            "Terminal Conveyor #6",
            "Terminal Conveyor #3",
            "Terminal Conveyor #7",
            "Terminal Conveyor #4",
            "Terminal Conveyor #8"
        };
        private JObject _json;
        private PipelineRunConfig _runConfig;
        private CompletionTimeLogger _completionTimeLogger;
        private DigitalTwinOrchestrator _orchestrator;
        private GnnProcessAdapter _gnn;
        private GnnLiveCommunicationMonitor _gnnLiveMonitor;
        private EmbeddedGnnDispatchBridge _embeddedGnnBridge;
        private volatile bool _rescheduledValidationRun;
        private System.Timers.Timer timer;
        private static readonly string[] CNC_TYPE =
        {
            "CNC_A",
            "CNC_B",
            "CNC_D",
            "CNC_E"
        };
        private static readonly string[] MHE_TYPE =
        {
            "AMR_A",
            "AMR_B",
            "SHUTTLE_A",
            "LIFTER_A",
            "LIFTER_B"
        };
        [Import]
        IApplication APP = null;
        ISimWorld SIMWORLD = null;
        [ImportingConstructor]
        public AbstractMain([Import(typeof(IApplication))] IApplication app)
        {
            this.APP = app;
            this.SIMWORLD = app.World;
        }

        public void Exit()
        {
            if (_embeddedGnnBridge != null)
            {
                _embeddedGnnBridge.Dispose();
                _embeddedGnnBridge = null;
            }

            if (_gnnLiveMonitor != null)
            {
                _gnnLiveMonitor.Dispose();
                _gnnLiveMonitor = null;
            }

            if (_gnn != null)
            {
                _gnn.Dispose();
                _gnn = null;
            }
        }

        public void Initialize()
        {
            try
            {
                _runConfig = CreateRunConfig();
                if (string.Equals(_runConfig.ExecutionMode, "ModelOnly", StringComparison.OrdinalIgnoreCase))
                    return;
                _runConfig.ValidateForExecution();
                _orchestrator = new DigitalTwinOrchestrator(_runConfig);
                PipelineConsole.Open(_runConfig.ReportDirectory);
                PipelineConsole.Log("Mode: " + _runConfig.Mode);
                PipelineConsole.Log("Visual Components is ready. Planning runs in the background.");
                Thread planningThread = new Thread(PreparePipelineInBackground);
                planningThread.IsBackground = true;
                planningThread.Name = "DT_KETI Planning";
                planningThread.Start();
            }
            catch (Exception ex)
            {
                HandlePipelineFailure(ex);
                throw;
            }
        }

        private static PipelineRunConfig CreateRunConfig()
        {
            var config = new PipelineRunConfig
            {
                WorkspaceRoot = WORKSPACE_ROOT,
                RunId = RUN_ID,
                Scenario = SCENARIO,
                ReportDirectory = REPORT_DIRECTORY,
                ModelOutputPath = MODEL_OUTPUT_PATH,
                PlanVersion = PLAN_VERSION,
                VnsProjectPath = VNS_PROJECT_PATH,
                VnsExecutablePath = VNS_EXECUTABLE_PATH,
                VnsTimeoutSeconds = VNS_TIMEOUT_SECONDS,
                PlannerMode = PLANNER_MODE,
                PlannerSeed = PLANNER_SEED,
                PlannerEvaluations = PLANNER_EVALUATIONS,
                PythonExecutablePath = PYTHON_EXECUTABLE_PATH,
                GnnDirectory = GNN_DIRECTORY,
                GnnVcRawPath = GNN_VCRAW_PATH,
                GnnInstancePath = GNN_INSTANCE_PATH,
                WorkspaceToolsDirectory = WORKSPACE_TOOLS_DIRECTORY,
                InitialP4rPath = INITIAL_P4R_PATH,
                InitialSequenceModelPath = INITIAL_SEQUENCE_MODEL_PATH,
                GnnRuleTemplatePath = GNN_RULE_TEMPLATE_PATH,
                GnnTrainingDataDirectory = GNN_TRAINING_DATA_DIRECTORY,
                GnnTrainingCheckpointPath = GNN_TRAINING_CHECKPOINT_PATH,
                GnnWeightArtifactDirectory = GNN_WEIGHT_ARTIFACT_DIRECTORY,
                TrainedRuleModelPath = TRAINED_RULE_MODEL_PATH,
                TrainedRuleOutputPath = TRAINED_RULE_OUTPUT_PATH,
                GnnTrainingEpochs = GNN_TRAINING_EPOCHS,
                GnnTrainingTimeoutSeconds = GNN_TRAINING_TIMEOUT_SECONDS,
                TrainRuleOnInitial = TRAIN_RULE_ON_INITIAL,
                ReuseTrainedRuleOnInitial = REUSE_TRAINED_RULE_ON_INITIAL,
                InitialSimulationHorizonSeconds = INITIAL_SIMULATION_HORIZON_SECONDS,
                InitialSimulationSpeedFactor = INITIAL_SIMULATION_SPEED_FACTOR,
                AbnormalSnapshotP4rPath = ABNORMAL_SNAPSHOT_P4R_PATH,
                ValidationSequenceModelPath = VALIDATION_SEQUENCE_MODEL_PATH,
                AbnormalSequenceOutputPath = ABNORMAL_SEQUENCE_OUTPUT_PATH,
                FinalSnapshotOutputPath = FINAL_SNAPSHOT_OUTPUT_PATH,
                RecoverySnapshotP4rPath = ABNORMAL_RECOVERY_SNAPSHOT_PATH,
                InitialReportPath = INITIAL_REPORT_PATH,
                GnnServicePath = GNN_SERVICE_PATH,
                GnnCheckpointPath = GNN_CHECKPOINT_PATH,
                EmbeddedGnnArtifactDirectory = EMBEDDED_GNN_ARTIFACT_DIRECTORY,
                GnnScaleMakespan = GNN_SCALE_MAKESPAN,
                GnnTimeoutSeconds = GNN_TIMEOUT_SECONDS,
                AbnormalSimulationHorizonSeconds = ABNORMAL_SIMULATION_HORIZON_SECONDS,
                AbnormalSimulationSpeedFactor = ABNORMAL_SIMULATION_SPEED_FACTOR,
                RescheduledSimulationHorizonSeconds = RESCHEDULED_SIMULATION_HORIZON_SECONDS,
                RescheduledSimulationSpeedFactor = RESCHEDULED_SIMULATION_SPEED_FACTOR
            };
            config.Prepare(EXECUTION_MODE);
            return config;
        }

        private void PreparePipelineInBackground()
        {
            try
            {
                if (RUN_EMBEDDED_GNN_FORWARD_SELF_TEST)
                {
                    EmbeddedGnnSelfTest.RunAndLog(Path.Combine(_runConfig.EmbeddedGnnArtifactDirectory, "gnn-weights.manifest.json"), Path.Combine(_runConfig.EmbeddedGnnArtifactDirectory, "forward-fixture.json"));
                }

                string jsonPath;
                string modelPath;
                var p4r = new VnsProcessAdapter(_runConfig);
                p4r.OutputReceived += PipelineConsole.Log;
                if (_runConfig.Mode == PipelineExecutionMode.Initial)
                {
                    _orchestrator.BeginInitialPlan();
                    PipelineConsole.Log("Initial P4R: " + _runConfig.InitialP4rPath);
                    PipelineConsole.Log("Starting VNS planning. This can take several minutes.");
                    _runConfig.WriteStatus("InitialPlanning", 10, "Running VNS from the initial P4R input.");
                    jsonPath = p4r.Run(_runConfig.InitialP4rPath, _runConfig.GnnTrainingDataDirectory);
                    _runConfig.SchedulePath = jsonPath;
                    PipelineConsole.Progress("Preparing simulation data...");
                    _runConfig.GnnVcRawPath = p4r.ExportVc(_runConfig.InitialP4rPath);
                    _runConfig.GnnInstancePath = p4r.ExportInstance(_runConfig.InitialP4rPath);
                    if (_runConfig.TrainRuleOnInitial)
                    {
                        _runConfig.WriteStatus("TrainingEmbeddedGnn", 25, "Training the GNN and creating the self-contained Rule VCMX.");
                        _runConfig.TrainedRuleModelPath = _runConfig.TrainedRuleOutputPath;
                        var trainedRule = new TrainedRuleModelPipeline(_runConfig);
                        trainedRule.OutputReceived += PipelineConsole.Log;
                        _runConfig.TrainedRuleModelPath = trainedRule.Build(_runConfig.GnnVcRawPath);
                        PipelineConsole.Progress("GNN-embedded Rule VC model deployed: " + Path.GetFileName(_runConfig.TrainedRuleModelPath));
                    }
                    else
                        PipelineConsole.Log("Sequence-only validation: GNN training and rule packaging explicitly disabled.");
                    modelPath = _runConfig.InitialSequenceModelPath;
                }
                else
                {
                    _orchestrator.BeginAbnormalPlan();
                    PipelineConsole.Log("Abnormal P4R: " + _runConfig.AbnormalSnapshotP4rPath);
                    PipelineConsole.Log("Creating VCRAW for the GNN.");
                    _runConfig.WriteStatus("PreparingAbnormal", 10, "Loading the abnormal P4R snapshot into the rule-based VC model.");
                    jsonPath = _runConfig.AbnormalSnapshotP4rPath;
                    modelPath = _runConfig.TrainedRuleModelPath;
                    _runConfig.GnnVcRawPath = p4r.ExportVc(jsonPath);
                    _runConfig.GnnInstancePath = p4r.ExportInstance(jsonPath);
                }

                _orchestrator.BeginPlanLoading();
                string modelOutputPath = string.IsNullOrWhiteSpace(_runConfig.ModelOutputPath) ? Path.Combine(_runConfig.ReportDirectory, _runConfig.Mode == PipelineExecutionMode.Initial ? "initial-loaded.vcmx" : "abnormal-loaded.vcmx") : _runConfig.ModelOutputPath;
                _runConfig.WriteStatus("Synchronizing", 35, "Loading the mode-specific VC base model and applying P4R.");
                PipelineConsole.Log("Planning completed. Applying P4R to the VC model.");
                Execute.OnUIThread(delegate
                {
                    try
                    {
                        StartVisualComponentsPipeline(jsonPath, modelPath, modelOutputPath);
                    }
                    catch (Exception ex)
                    {
                        HandlePipelineFailure(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                HandlePipelineFailure(ex);
            }
        }

        private void StartVisualComponentsPipeline(string jsonPath, string modelPath, string modelOutputPath)
        {
            PipelineConsole.Log("Loading VC model: " + modelPath);
            Interface intf = new Interface();
            Simulation sim = new Simulation();
            AppControl appCtrl = new AppControl();
            _json = intf.readJson(jsonPath);
            ISimComponent[] _model = appCtrl.LoadModel(APP, SIMWORLD, modelPath);
            PipelineConsole.Log("VC model loaded. Synchronizing the P4R schedule.");
            PipelineConsole.Progress("Applying the schedule and restoring work in progress...");
            if (_runConfig.Mode == PipelineExecutionMode.Abnormal && !_rescheduledValidationRun)
            {
                PipelineConsole.Log("Using RuleModelSynchronization for abnormal snapshot restore.");
                var ruleSync = new RuleModelSynchronization();
                ruleSync.SynchronizeSnapshot(_json, SIMWORLD, APP);
            }
            else
            {
                PipelineConsole.Log("Using Synchronization for Sequence-model schedule loading.");
                var sequenceSync = new Synchronization();
                sequenceSync.Synchronize(_json, SIMWORLD, APP, modelPath);
            }

            _orchestrator.MarkPlanLoaded();
            PipelineConsole.Log("P4R synchronization completed.");
            if (_runConfig.Mode == PipelineExecutionMode.Abnormal && !_rescheduledValidationRun)
            {
                PipelineConsole.Log("[GNN JOB MAP] Building from abnormal P4R and VCRAW.");
                GnnJobRegistry jobRegistry = GnnJobRegistryBuilder.Build(_json, _runConfig.GnnVcRawPath);
                foreach (GnnJobRegistryEntry job in jobRegistry.Jobs)
                {
                    string location = job.Row.HasValue ? " ASRS(" + job.Row.Value + "," + job.Column.Value + "," + job.Tier.Value + ")" : "";
                    PipelineConsole.Log("[GNN JOB MAP] job=" + job.Job + " " + job.SourceId + " " + job.Material + " " + job.Component + location);
                }

                string instanceJson = File.ReadAllText(_runConfig.GnnInstancePath);
                VcGnnJobRegistryInjector.Inject(SIMWORLD, jobRegistry, instanceJson);
                AbnormalProductInstances.Install(_json, jobRegistry, SIMWORLD, APP, _runConfig.ReportDirectory);
                PipelineConsole.Progress("[GNN JOB MAP] " + jobRegistry.Jobs.Count + " jobs injected into Central Controller. Version=" + jobRegistry.Version);
                if (USE_VCMX_PYTHON_GNN_DISPATCH)
                {
                    PipelineConsole.Progress("VCMX Python GNN selected. Current INSTANCE was injected; " + "forward/choice/commit run inside the Rule model Python Behavior.");
                }
                else if (USE_EMBEDDED_GNN_DISPATCH)
                {
                    EmbeddedGnnSelfTest.RunSessionAndLog(Path.Combine(_runConfig.EmbeddedGnnArtifactDirectory, "gnn-weights.manifest.json"), _runConfig.GnnVcRawPath, Path.Combine(_runConfig.EmbeddedGnnArtifactDirectory, "forward-fixture.json"), _runConfig.GnnScaleMakespan);
                    _embeddedGnnBridge = new EmbeddedGnnDispatchBridge();
                    _embeddedGnnBridge.Start(SIMWORLD, Path.Combine(_runConfig.EmbeddedGnnArtifactDirectory, "gnn-weights.manifest.json"), _runConfig.GnnVcRawPath, _runConfig.GnnScaleMakespan);
                    PipelineConsole.Log("Embedded C# GNN loaded. Rule VC decisions are routed to embedded inference.");
                }
                else
                {
                    _gnn = new GnnProcessAdapter(_runConfig);
                    _gnn.ErrorOutput += delegate (string line)
                    {
                        PipelineConsole.Log("[GNN STDERR] " + line);
                    };
                    _gnn.Start();
                    PipelineConsole.Log("External Python GNN model loaded and ready.");
                    if (RUN_GNN_COMMUNICATION_SMOKE_TEST)
                        RunGnnCommunicationSmokeTest();
                    _gnnLiveMonitor = new GnnLiveCommunicationMonitor();
                    _gnnLiveMonitor.Start(SIMWORLD, _gnn);
                }

                _orchestrator.BeginGnnDispatch();
                _runConfig.WriteStatus("GnnReady", 50, "The GNN model is loaded and ready for VC event requests.");
            }

            VcConsoleLogging.Install(SIMWORLD);
            var proForCompletionLog = new JsonPreprocessing();
            string[] completionCncList = proForCompletionLog.GetCNCMachineList(_json, CNC_TYPE);
            string[] completionMheList = proForCompletionLog.GetMHEList(_json, MHE_TYPE);
            _completionTimeLogger = new CompletionTimeLogger();
            _completionTimeLogger.Start(SIMWORLD, completionCncList, completionMheList, ShuttleConveyorComponents, _runConfig.ReportDirectory);
            appCtrl.SaveModel(APP, modelOutputPath);
            PipelineConsole.Log("Loaded VC model saved: " + modelOutputPath);
            if (_runConfig.Mode == PipelineExecutionMode.Initial || _rescheduledValidationRun)
            {
                _completionTimeLogger.Dispose();
                appCtrl.LoadModel(APP, SIMWORLD, modelOutputPath);
                _completionTimeLogger = new CompletionTimeLogger();
                _completionTimeLogger.Start(SIMWORLD, completionCncList, completionMheList, ShuttleConveyorComponents, _runConfig.ReportDirectory, install: false);
                PipelineConsole.Log("Reloaded the saved VC model before starting the simulation.");
            }

            if (_runConfig.Mode == PipelineExecutionMode.Abnormal && !_rescheduledValidationRun)
                ApplySavedView(ABNORMAL_START_VIEW);
            sim.ExecuteSimulation(APP, _runConfig.SimulationHorizonSeconds, _runConfig.SpeedFactor);
            PipelineConsole.Log("VC simulation started. Horizon=" + _runConfig.SimulationHorizonSeconds + ", SpeedFactor=" + _runConfig.SpeedFactor);
            if (_runConfig.Mode == PipelineExecutionMode.Initial || _rescheduledValidationRun)
                _orchestrator.MarkSimulationRunning();
            else
                _orchestrator.MarkAbnormalSimulationRunning();
            _runConfig.WriteStatus("SimulationRunning", 60, _runConfig.Mode == PipelineExecutionMode.Initial || _rescheduledValidationRun ? "Initial-mode VC validation simulation is running." : "Abnormal-mode VC/GNN simulation is running.");
            timer = new System.Timers.Timer(1000);
            timer.Elapsed += new ElapsedEventHandler(callBackFunction);
            timer.AutoReset = true;
            timer.Start();
            if (APP.Simulation.IsRunning)
            {
                Thread threadReporting = new Thread(threadReportingFunction);
                threadReporting.IsBackground = true;
                threadReporting.Start();
            }
        }

        private void RunGnnCommunicationSmokeTest()
        {
            PipelineConsole.Log("[GNN TEST] Starting health -> choose -> commit -> reset.");
            try
            {
                bool healthy = _gnn.Health();
                PipelineConsole.Log("[GNN TEST] health=" + healthy);
                if (!healthy)
                    throw new InvalidOperationException("GNN health check returned false.");
                var candidates = new[]
                {
                    new JObject
                    {
                        ["kind"] = "ret",
                        ["job"] = 34,
                        ["res"] = "crane"
                    }
                };
                PipelineConsole.Log("[GNN TEST] choose request=" + new JArray(candidates).ToString(Newtonsoft.Json.Formatting.None));
                GnnDecision decision = _gnn.Choose(candidates);
                if (decision.Index < 0 || decision.Index >= candidates.Length)
                    throw new InvalidOperationException("GNN returned an invalid candidate index: " + decision.Index);
                string scores = decision.Scores == null ? "[]" : JArray.FromObject(decision.Scores).ToString(Newtonsoft.Json.Formatting.None);
                PipelineConsole.Log("[GNN TEST] choose response index=" + decision.Index + " scores=" + scores);
                JObject selected = candidates[decision.Index];
                _gnn.Commit((string)selected["kind"], (int)selected["job"], (string)selected["res"]);
                PipelineConsole.Log("[GNN TEST] commit accepted kind=" + selected["kind"] + " job=" + selected["job"] + " res=" + selected["res"]);
                _gnn.Reset();
                PipelineConsole.Log("[GNN TEST] reset accepted.");
                PipelineConsole.Log("[GNN TEST] PASS - C# <-> Python GNN communication works.");
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("[GNN TEST] FAIL - " + ex);
                try
                {
                    if (_gnn != null && _gnn.IsRunning)
                    {
                        _gnn.Reset();
                        PipelineConsole.Log("[GNN TEST] session reset after failure.");
                    }
                }
                catch (Exception resetException)
                {
                    PipelineConsole.Log("[GNN TEST] reset after failure also failed: " + resetException.Message);
                }
            }
        }

        private void HandlePipelineFailure(Exception ex)
        {
            PipelineConsole.Log("ERROR: " + ex);
            if (_orchestrator != null)
                _orchestrator.Fail(ex);
            if (_runConfig != null)
                _runConfig.WriteStatus("Failed", 0, ex.Message);
        }

        void threadReportingFunction()
        {
            try
            {
                while (true)
                {
                    if (APP.Simulation.IsRunning == false)
                    {
                        timer.Close();
                        if (_runConfig != null)
                            _runConfig.WriteStatus("GeneratingReport", 90, "Generating JSON and HTML reports.");
                        bool sequenceValidation = _runConfig.Mode == PipelineExecutionMode.Initial || _rescheduledValidationRun;
                        if (sequenceValidation)
                        {
                            var proForReport = new JsonPreprocessing();
                            var simReport = new SimulationReporting();
                            string[] cnc = proForReport.GetCNCMachineList(_json, CNC_TYPE);
                            string[] mhe = proForReport.GetMHEList(_json, MHE_TYPE);
                            simReport.CreateReport(SIMWORLD, cnc, mhe, ShuttleConveyorComponents, _runConfig.Scenario, _runConfig.SimulationHorizonSeconds, _runConfig.SpeedFactor, _runConfig.ReportDirectory, _completionTimeLogger == null ? new JArray() : _completionTimeLogger.Snapshot(), _rescheduledValidationRun ? RecoveryReportContext.Build(_runConfig) : null);
                            PipelineConsole.Progress("Report ready: " + Path.Combine(_runConfig.ReportDirectory, "SimulationReport.html"));
                        }
                        else
                        {
                            new AbnormalSimulationReporting().CreateReport(SIMWORLD, _runConfig);
                            PipelineConsole.Log("Abnormal Rule-model report created.");
                        }

                        if (_completionTimeLogger != null)
                        {
                            _completionTimeLogger.Dispose();
                            _completionTimeLogger = null;
                        }

                        if (sequenceValidation)
                        {
                            _runConfig.WriteStatus("Completed", 100, _rescheduledValidationRun ? "Rescheduled Sequence-model validation completed." : "Initial VNS plan simulation and reporting completed.");
                            _orchestrator.Complete();
                            if (OPEN_REPORTS_ON_COMPLETE)
                                OpenReportFiles();
                        }
                        else
                        {
                            _orchestrator.BeginAbnormalOutputExport();
                            _runConfig.WriteStatus("ExportingAbnormalP4R", 70, "The abnormal simulation ended. Event-sequence P4R and final-snapshot P4R export is required next.");
                            if (_gnnLiveMonitor != null)
                            {
                                _gnnLiveMonitor.Dispose();
                                _gnnLiveMonitor = null;
                            }

                            if (_embeddedGnnBridge != null)
                            {
                                _embeddedGnnBridge.Dispose();
                                _embeddedGnnBridge = null;
                            }

                            if (_gnn != null)
                            {
                                _gnn.Dispose();
                                _gnn = null;
                            }

                            AbnormalExportResult export = new AbnormalP4rExporter().Export(_json, SIMWORLD, _runConfig);
                            PipelineConsole.Log("Abnormal operation sequence draft: " + export.SequencePath);
                            PipelineConsole.Log("Abnormal final snapshot draft: " + export.SnapshotPath);
                            string recoverySnapshot = export.SnapshotReadyForVns ? export.SnapshotPath : _runConfig.RecoverySnapshotP4rPath;
                            string mismatch = RecoverySnapshotMismatch(recoverySnapshot);
                            if (mismatch != null)
                            {
                                PipelineConsole.Log("Recovery snapshot rejected: " + mismatch);
                                PipelineConsole.Progress("Paused: recovery snapshot does not match this abnormal run.");
                                _runConfig.WriteStatus("AwaitingRecoverySnapshot", 75, mismatch);
                            }
                            else if (!string.IsNullOrWhiteSpace(recoverySnapshot) && File.Exists(recoverySnapshot))
                            {
                                PipelineConsole.Log("Recovery state snapshot: " + recoverySnapshot);
                                StartRescheduledValidationInBackground(recoverySnapshot);
                            }
                            else
                            {
                                _runConfig.WriteStatus("AwaitingRuleSnapshotExporter", 75, "Draft P4R files were created. Complete the Rule runtime-state exporters before VNS is allowed to run.");
                                PipelineConsole.Log("VNS not started: final Rule snapshot is intentionally marked incomplete.");
                            }
                        }

                        break;
                    }

                    Thread.Sleep(250);
                }
            }
            catch (Exception ex)
            {
                HandlePipelineFailure(ex);
            }
        }

        private string RecoverySnapshotMismatch(string recoveryPath)
        {
            if (string.IsNullOrWhiteSpace(recoveryPath) || !File.Exists(recoveryPath))
                return null;
            var settings = new Newtonsoft.Json.JsonSerializerSettings
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None
            };
            string abnormalAt = (string)Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(File.ReadAllText(_runConfig.AbnormalSnapshotP4rPath), settings)["CURRENT_DATETIME"];
            JObject recovery = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(File.ReadAllText(recoveryPath), settings);
            string recoveryAt = (string)recovery["CURRENT_DATETIME"];
            if (recovery.SelectToken("_DT_RECONSTRUCTION.SnapshotReadyForVns")?.Value<bool>() == false || recovery.SelectToken("_DT_RULE_EXPORT.SnapshotReadyForVns")?.Value<bool>() == false)
                return "Recovery snapshot contains unresolved runtime state.";
            DateTimeOffset start, end;
            if (!DateTimeOffset.TryParse(abnormalAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out start) || !DateTimeOffset.TryParse(recoveryAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out end))
                return "Recovery or abnormal snapshot timestamp is missing or invalid.";
            double elapsed = (end - start).TotalSeconds;
            return Math.Abs(elapsed - Math.Floor(_runConfig.AbnormalSimulationHorizonSeconds)) <= 1.0 ? null : Path.GetFileName(recoveryPath) + " is at " + recoveryAt + ", " + elapsed + " s after the abnormal input (" + abnormalAt + "); expected " + Math.Floor(_runConfig.AbnormalSimulationHorizonSeconds) + " s. Rebuild it from this run's VC log.";
        }

        private void ApplySavedView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                return;
            try
            {
                IView3D view = System.Linq.Enumerable.FirstOrDefault(SIMWORLD.Views, v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
                if (view == null || APP.ActiveWindow == null)
                {
                    PipelineConsole.Log("Saved view not applied (not found or no active window): " + viewName);
                    return;
                }

                APP.ActiveWindow.ApplyView(view);
                PipelineConsole.Log("Saved view applied: " + viewName);
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("Saved view could not be applied: " + viewName + " (" + ex.Message + ")");
            }
        }

        private void OpenReportFiles()
        {
            foreach (string name in new[]
            {
                "SimulationReport.html",
                "SimulationReport.json"
            }

            )
            {
                string path = Path.Combine(_runConfig.ReportDirectory, name);
                try
                {
                    if (File.Exists(path))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    PipelineConsole.Log("Could not open " + path + ": " + ex.Message);
                }
            }
        }

        private void StartRescheduledValidationInBackground(string snapshotPath)
        {
            Thread continuation = new Thread(delegate ()
            {
                try
                {
                    string pinnedSnapshot = Path.Combine(_runConfig.ReportDirectory, "recovery-input.p4r.json");
                    if (!string.Equals(Path.GetFullPath(snapshotPath), Path.GetFullPath(pinnedSnapshot), StringComparison.OrdinalIgnoreCase))
                        File.Copy(snapshotPath, pinnedSnapshot, true);
                    snapshotPath = pinnedSnapshot;
                    _runConfig.RecoverySnapshotP4rPath = pinnedSnapshot;
                    PipelineConsole.Progress("Recovery state loaded (end of the abnormal window).");
                    _orchestrator.BeginRescheduling();
                    _runConfig.WriteStatus("Rescheduling", 80, "Running VNS from the final abnormal P4R snapshot.");
                    var vns = new VnsProcessAdapter(_runConfig);
                    vns.OutputReceived += PipelineConsole.Log;
                    string schedulePath = vns.Run(snapshotPath);
                    _runConfig.SchedulePath = schedulePath;
                    _orchestrator.BeginNewPlanValidation();
                    Execute.OnUIThread(delegate
                    {
                        try
                        {
                            _rescheduledValidationRun = true;
                            _orchestrator.BeginPlanLoading();
                            _runConfig.WriteStatus("Synchronizing", 85, "Loading the Sequence VC model and applying the rescheduled P4R.");
                            string output = Path.Combine(_runConfig.ReportDirectory, "rescheduled-validation-loaded.vcmx");
                            StartVisualComponentsPipeline(schedulePath, _runConfig.ValidationSequenceModelPath, output);
                        }
                        catch (Exception ex)
                        {
                            HandlePipelineFailure(ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    HandlePipelineFailure(ex);
                }
            });
            continuation.IsBackground = true;
            continuation.Name = "DT_KETI Abnormal Rescheduling";
            continuation.Start();
        }

        void threadTimerCallbackFunction()
        {
        }

        void callBackFunction(object senter, ElapsedEventArgs eventArgs)
        {
        }
    }
}
