using UnityEngine;
using ATADTRL.Environment;
using ATADTRL.Scenarios;

namespace ATADTRL.TestFlow.A1.Module1
{
    /// <summary>
    /// Runtime state contract for "Unity Warehouse Environment Simulation".
    /// The existing ScenarioManager remains the single owner of A1 execution.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnityWarehouseEnvironmentSimulation : MonoBehaviour
    {
        public WarehouseManager Warehouse { get; private set; }
        public ScenarioManager Scenarios { get; private set; }
        public bool IsReady => UnityWarehouseEnvironmentModelling.IsModelReady(Warehouse) && Scenarios != null;
        public bool IsScenarioRunning => Scenarios != null && Scenarios.IsRunning;

        private void Awake()
        {
            Warehouse = FindAnyObjectByType<WarehouseManager>();
            Scenarios = FindAnyObjectByType<ScenarioManager>();
        }

        public string RuntimeStatus()
        {
            if (!IsReady) return "WAREHOUSE_SIMULATION_NOT_READY";
            return IsScenarioRunning ? "A1_SCENARIO_RUNNING" : "WAREHOUSE_IDLE_LIVE";
        }
    }
}
