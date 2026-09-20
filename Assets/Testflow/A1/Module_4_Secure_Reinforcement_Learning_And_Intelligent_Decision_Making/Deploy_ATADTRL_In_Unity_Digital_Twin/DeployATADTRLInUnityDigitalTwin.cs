using UnityEngine;
using ATADTRL.Module2;
using ATADTRL.Pipeline;

namespace ATADTRL.Module4
{
    /// <summary>
    /// Runtime deployment verifier for the complete A1 Unity digital-twin
    /// pipeline. It reports readiness but never creates duplicate managers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeployATADTRLInUnityDigitalTwin : MonoBehaviour
    {
        public bool Module2Ready { get; private set; }
        public bool Module3Ready { get; private set; }
        public bool Module4DashboardReady { get; private set; }
        public bool Module4ExecutorReady { get; private set; }
        public bool DeploymentReady => Module2Ready && Module3Ready && Module4DashboardReady && Module4ExecutorReady;

        private void Start()
        {
            Module2Ready = FindAnyObjectByType<Module2Manager>() != null;
            Module3Ready = FindAnyObjectByType<ATADTRLPipelineManager>() != null;
            Module4DashboardReady = FindAnyObjectByType<ATADTRL.UI.ATADTRLOutcomeDashboard>() != null;
            Module4ExecutorReady = FindAnyObjectByType<ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay>() != null;
            Debug.Log(DeploymentReady
                ? "ATADTRL DEPLOYMENT: Modules 1-4 are connected in the Unity digital twin."
                : "ATADTRL DEPLOYMENT: one or more A1 runtime modules are unavailable.");
        }
    }
}
