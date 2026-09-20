using System.Collections.Generic;

namespace ATADTRL.Module2
{
    public class EDATSDecision
    {
        public bool divergenceTriggered;
        public bool staleStateTriggered;

        public float twinAge;
        public float robotPositionDivergence;
        public float robotYawDivergence;
        public float robotVelocityDivergence;
        public float actorPositionDivergence;
        public bool synchronize;
        public bool criticalEvent;

        public float changeScore;
        public float threshold;

        public string reason;

        public List<string> changedComponents =
            new List<string>();
    }

    public class EDATSAlgorithm
    {
        private readonly EDATSConfig config;

        public EDATSAlgorithm(EDATSConfig config)
        {
            this.config = config;
        }

        public EDATSDecision Evaluate(
    ChangeDetectionResult change,
    TwinDivergenceResult divergence,
    float twinAge)
        {
            var decision =
                new EDATSDecision
                {
                    criticalEvent =
                        change.criticalEvent,

                    changeScore =
                        change.changeScore,

                    threshold =
                        config.overallChangeThreshold,

                    twinAge =
                        twinAge,

                    robotPositionDivergence =
                        divergence?.robotPositionError ?? 0f,

                    robotYawDivergence =
                        divergence?.robotYawError ?? 0f,

                    robotVelocityDivergence =
                        divergence?.robotVelocityError ?? 0f,

                    actorPositionDivergence =
                        divergence?.maximumActorPositionError ?? 0f,

                    changedComponents =
                        new List<string>(
                            change.changedComponents)
                };

            // =============================================================
            // RULE 1 — CRITICAL EVENT
            // =============================================================

            if (change.criticalEvent)
            {
                decision.synchronize = true;

                decision.reason =
                    string.IsNullOrEmpty(
                        change.criticalReason)
                        ? "Critical event synchronization"
                        : change.criticalReason;

                return decision;
            }

            // =============================================================
            // RULE 2 — ENVIRONMENTAL CHANGE
            // =============================================================

            if (change.changeScore >
                config.overallChangeThreshold)
            {
                decision.synchronize = true;

                decision.reason =
                    "Environmental change threshold exceeded";

                return decision;
            }

            // =============================================================
            // RULE 3 — TWIN DIVERGENCE
            // =============================================================

            if (divergence != null &&
                divergence.exceeded)
            {
                decision.synchronize = true;

                decision.divergenceTriggered = true;

                foreach (
                    string component in
                    divergence.divergentComponents)
                {
                    if (!decision.changedComponents.Contains(
                            component))
                    {
                        decision.changedComponents.Add(
                            component);
                    }
                }

                decision.reason =
                    "Twin divergence threshold exceeded";

                return decision;
            }

            // =============================================================
            // RULE 4 — STALE TWIN PROTECTION
            // =============================================================

            if (twinAge >
                config.maximumTwinAgeSeconds)
            {
                decision.synchronize = true;

                decision.staleStateTriggered = true;

                decision.reason =
                    "Maximum Twin age exceeded";

                return decision;
            }

            // =============================================================
            // RULE 5 — MAINTAIN TWIN
            // =============================================================

            decision.synchronize = false;

            decision.reason =
                "Twin maintained";

            return decision;
        }
    }
}