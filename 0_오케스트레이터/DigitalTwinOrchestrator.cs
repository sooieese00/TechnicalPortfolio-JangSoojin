using System;
using System.Collections.Generic;

namespace DT_KETI
{
    public enum OrchestratorState
    {
        Idle,
        InitialPlanning,
        PreparingAbnormal,
        LoadingPlan,
        Validating,
        NormalRunning,
        AbnormalDetected,
        GnnDispatching,
        AbnormalRunning,
        ExportingAbnormalOutputs,
        Snapshotting,
        Rescheduling,
        ValidatingNewPlan,
        RestoringSnapshot,
        Completed,
        Failed
    }

    public sealed class DigitalTwinOrchestrator
    {
        private readonly object _gate = new object ();
        private readonly PipelineRunConfig _config;
        private OrchestratorState _state = OrchestratorState.Idle;
        private static readonly IDictionary<OrchestratorState, OrchestratorState[]> AllowedTransitions = new Dictionary<OrchestratorState, OrchestratorState[]>
        {
            {
                OrchestratorState.Idle,
                new[]
                {
                    OrchestratorState.InitialPlanning,
                    OrchestratorState.PreparingAbnormal
                }
            },
            {
                OrchestratorState.InitialPlanning,
                new[]
                {
                    OrchestratorState.LoadingPlan
                }
            },
            {
                OrchestratorState.PreparingAbnormal,
                new[]
                {
                    OrchestratorState.LoadingPlan
                }
            },
            {
                OrchestratorState.LoadingPlan,
                new[]
                {
                    OrchestratorState.Validating,
                    OrchestratorState.RestoringSnapshot
                }
            },
            {
                OrchestratorState.RestoringSnapshot,
                new[]
                {
                    OrchestratorState.ValidatingNewPlan,
                    OrchestratorState.Validating
                }
            },
            {
                OrchestratorState.Validating,
                new[]
                {
                    OrchestratorState.NormalRunning,
                    OrchestratorState.GnnDispatching
                }
            },
            {
                OrchestratorState.NormalRunning,
                new[]
                {
                    OrchestratorState.AbnormalDetected,
                    OrchestratorState.Snapshotting,
                    OrchestratorState.Completed
                }
            },
            {
                OrchestratorState.AbnormalDetected,
                new[]
                {
                    OrchestratorState.GnnDispatching,
                    OrchestratorState.Snapshotting
                }
            },
            {
                OrchestratorState.GnnDispatching,
                new[]
                {
                    OrchestratorState.AbnormalRunning
                }
            },
            {
                OrchestratorState.AbnormalRunning,
                new[]
                {
                    OrchestratorState.ExportingAbnormalOutputs
                }
            },
            {
                OrchestratorState.ExportingAbnormalOutputs,
                new[]
                {
                    OrchestratorState.Rescheduling
                }
            },
            {
                OrchestratorState.Snapshotting,
                new[]
                {
                    OrchestratorState.Rescheduling
                }
            },
            {
                OrchestratorState.Rescheduling,
                new[]
                {
                    OrchestratorState.ValidatingNewPlan,
                    OrchestratorState.LoadingPlan
                }
            },
            {
                OrchestratorState.ValidatingNewPlan,
                new[]
                {
                    OrchestratorState.LoadingPlan,
                    OrchestratorState.RestoringSnapshot,
                    OrchestratorState.NormalRunning
                }
            }
        };
        public DigitalTwinOrchestrator(PipelineRunConfig config)
        {
            _config = config;
        }

        public OrchestratorState State
        {
            get
            {
                lock (_gate)
                    return _state;
            }
        }

        public event Action<OrchestratorState, OrchestratorState, string> StateChanged;
        public void BeginInitialPlan()
        {
            if (_config != null)
                _config.UseInitialSimulationSettings();
            Transition(OrchestratorState.InitialPlanning, "Initial mode: starting P4R/VNS planning.");
        }

        public void BeginAbnormalPlan()
        {
            if (_config != null)
                _config.UseAbnormalSimulationSettings();
            Transition(OrchestratorState.PreparingAbnormal, "Abnormal mode: preparing the abnormal P4R snapshot and rule model.");
        }

        public void BeginPlanLoading()
        {
            Transition(OrchestratorState.LoadingPlan, "Loading P4R into the selected VC base model.");
        }

        public void MarkPlanLoaded()
        {
            Transition(OrchestratorState.Validating, "The schedule has been loaded into Visual Components.");
        }

        public void MarkSimulationRunning()
        {
            Transition(OrchestratorState.NormalRunning, "Visual Components simulation is running.");
        }

        public void NotifyAbnormal(string reason)
        {
            Transition(OrchestratorState.AbnormalDetected, reason);
        }

        public void BeginGnnDispatch()
        {
            Transition(OrchestratorState.GnnDispatching, "GNN event dispatch mode started.");
        }

        public void MarkAbnormalSimulationRunning()
        {
            Transition(OrchestratorState.AbnormalRunning, "Abnormal VC simulation is running with GNN dispatch.");
        }

        public void BeginAbnormalOutputExport()
        {
            Transition(OrchestratorState.ExportingAbnormalOutputs, "Exporting the abnormal event sequence P4R and final snapshot P4R.");
        }

        public void BeginSnapshot()
        {
            Transition(OrchestratorState.Snapshotting, "Creating a P4R snapshot from the VC runtime state.");
        }

        public void BeginRescheduling()
        {
            Transition(OrchestratorState.Rescheduling, "Starting VNS rescheduling from the P4R snapshot.");
        }

        public void BeginNewPlanValidation()
        {
            if (_config != null)
                _config.UseRescheduledValidationSettings();
            Transition(OrchestratorState.ValidatingNewPlan, "Validating the rescheduled plan.");
        }

        public void BeginSnapshotRestore()
        {
            Transition(OrchestratorState.RestoringSnapshot, "Resetting VC and restoring the P4R snapshot state.");
        }

        public void Complete()
        {
            Transition(OrchestratorState.Completed, "The digital-twin run completed.");
        }

        public void Fail(Exception error)
        {
            lock (_gate)
            {
                OrchestratorState previous = _state;
                _state = OrchestratorState.Failed;
                Publish(previous, _state, error == null ? "Unknown failure." : error.Message);
            }
        }

        private void Transition(OrchestratorState next, string message)
        {
            lock (_gate)
            {
                if (_state == next)
                    return;
                OrchestratorState[] allowed;
                if (!AllowedTransitions.TryGetValue(_state, out allowed) || Array.IndexOf(allowed, next) < 0)
                    throw new InvalidOperationException("Invalid orchestrator transition: " + _state + " -> " + next);
                OrchestratorState previous = _state;
                _state = next;
                Publish(previous, next, message);
            }
        }

        private void Publish(OrchestratorState previous, OrchestratorState current, string message)
        {
            if (_config != null)
                _config.WriteOrchestratorStatus(previous, current, message);
            Action<OrchestratorState, OrchestratorState, string> handler = StateChanged;
            if (handler != null)
                handler(previous, current, message);
        }
    }
}
