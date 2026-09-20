using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Logging;
using ATADTRL.Scenarios;

namespace ATADTRL.TestFlow
{
    [DisallowMultipleComponent]
    public class Module2TestFlowManager : MonoBehaviour
    {
        private EnvironmentChangeDetector detector;
        private EDATSAlgorithm edats;
        private AdaptiveDigitalTwin twin;
        private ContextAwareWorldModel worldModel;
        private PPOPolicyLearning ppo;
        private ObservationRecord previous;
        private int episodeId = -1;

        public EnvironmentalChange LastChange { get; private set; }
        public EDATSDecision LastDecision { get; private set; }
        public A1TwinState Twin { get; private set; }
        public ContextState Context { get; private set; }
        public long ProcessedObservationCount => processedObservationCount;
        public float LastContextReward => lastContextReward;
        public bool HasContextReward => hasContextReward;

        [Header("A1 Blind Corner (world X/Z)")]
        public Vector2 blindCorner;
        public bool useScenarioBlindCorner = true;

        [Header("Live A1 diagnostics (PPO reward only; no trained policy)")]
        [SerializeField] private long processedObservationCount;
        [SerializeField] private int synchronizationCount;
        [SerializeField] private float lastChangeScore;
        [SerializeField] private float twinStateAge;
        [SerializeField] private string syncReason;
        [SerializeField] private bool hasContextReward;
        [SerializeField] private float lastContextReward;
        [SerializeField] private bool blindCornerConflict;
        [SerializeField] private float collisionRisk;

        private void Awake() => ResetFlow();

        private void OnEnable()
        {
            ResetFlow();
            ObservationLogger.ObservationLogged += Process;
            ObservationLogger.ScenarioStarted += BeginScenario;
            ObservationLogger.ScenarioEnded += EndScenario;
        }

        private void OnDisable()
        {
            ObservationLogger.ObservationLogged -= Process;
            ObservationLogger.ScenarioStarted -= BeginScenario;
            ObservationLogger.ScenarioEnded -= EndScenario;
            ResetFlow();
        }

        private void ResetFlow()
        {
            detector = new EnvironmentChangeDetector();
            edats = new EDATSAlgorithm();
            twin = new AdaptiveDigitalTwin();
            worldModel = new ContextAwareWorldModel(blindCorner);
            ppo = new PPOPolicyLearning();
            previous = null;
            episodeId = -1;
            LastChange = null;
            LastDecision = null;
            Twin = null;
            Context = null;
            processedObservationCount = 0;
            synchronizationCount = 0;
            lastChangeScore = twinStateAge = lastContextReward = collisionRisk = 0f;
            hasContextReward = blindCornerConflict = false;
            syncReason = "Waiting for A1 observations";
        }

        private void BeginScenario(int scenarioId) => ResetFlow();

        private void EndScenario()
        {
            // Retain final diagnostics without comparing across episodes.
            previous = null;
            episodeId = -1;
        }

        private void ConfigureCorner()
        {
            if (useScenarioBlindCorner)
            {
                var scenarios = FindAnyObjectByType<ScenarioManager>();
                var a1 = scenarios?.Scenarios.Find(s => s.scenarioId == 1 && s.scenarioCode == "A1");
                if (a1?.firstTaskDeliveryWaypoints == null || a1.firstTaskDeliveryWaypoints.Count < 2)
                {
                    Debug.LogWarning("[A1 Module 2] A1 route unavailable; set the blind corner and disable Use Scenario Blind Corner.", this);
                    worldModel = null;
                    return;
                }
                // Fixed route geometry, not live actor transforms or Ground Truth.
                Vector3 corner = a1.firstTaskDeliveryWaypoints[1];
                blindCorner = new Vector2(corner.x, corner.z);
            }
            worldModel = new ContextAwareWorldModel(blindCorner);
        }

        public void Process(ObservationRecord observation)
        {
            if (!isActiveAndEnabled || observation == null || observation.scenarioId != 1)
                return;

            // Also supports enabling this component in the middle of A1.
            if (previous == null || episodeId != observation.episodeId)
            {
                ResetFlow();
                episodeId = observation.episodeId;
                ConfigureCorner();
            }

            LastChange = detector.Detect(previous, observation);
            LastDecision = edats.Evaluate(LastChange);
            if (Twin == null)
            {
                LastDecision.synchronize = true;
                LastDecision.reason = "Initial A1 observation - Full synchronization";
            }
            Twin = twin.Update(observation, LastDecision);
            Context = worldModel?.Build(Twin);

            // ObservationRecord has no collision/goal outcome flags. Do not
            // invent them or import Ground Truth into the operational flow.
            hasContextReward = Context != null && Context.valid;
            lastContextReward = hasContextReward ? ppo.CalculateContextReward(Context) : 0f;
            blindCornerConflict = hasContextReward && Context.blindCornerConflict;
            collisionRisk = hasContextReward ? Context.collisionRisk : 0f;
            processedObservationCount++;
            if (LastDecision.synchronize) synchronizationCount++;
            lastChangeScore = LastChange.changeScore;
            twinStateAge = Twin.stateAge;
            syncReason = LastDecision.reason;
            previous = observation;
        }
    }
}
