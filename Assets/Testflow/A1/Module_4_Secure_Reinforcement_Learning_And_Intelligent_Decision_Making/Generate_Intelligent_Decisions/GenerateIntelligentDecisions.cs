using System;
using ATADTRL.Pipeline;
using ATADTRL.TestFlow;

namespace ATADTRL.Module4
{
    public readonly struct IntelligentDecision
    {
        public readonly ATADTRLPipelineManager.SecureAction Action;
        public readonly string Reason;
        public readonly bool ConflictPredicted;
        public readonly bool SafetyHoldRequested;
        public readonly float NextClearTimer;

        public IntelligentDecision(ATADTRLPipelineManager.SecureAction action, string reason,
            bool conflictPredicted, bool safetyHoldRequested, float nextClearTimer)
        {
            Action = action;
            Reason = reason;
            ConflictPredicted = conflictPredicted;
            SafetyHoldRequested = safetyHoldRequested;
            NextClearTimer = nextClearTimer;
        }
    }

    /// <summary>
    /// Module 4 intelligent decision block. It converts the trusted Module 3
    /// state and predicted collision context into an executable robot action.
    /// A trained PPO policy can replace this deterministic A1 pilot without
    /// changing the input/output contract.
    /// </summary>
    public static class GenerateIntelligentDecisions
    {
        public static IntelligentDecision Select(
            bool fullPipelineEnabled,
            int scenarioId,
            string taskStage,
            bool carrying,
            float collisionRisk,
            float predictedMinimumSeparation,
            float safeSeparation,
            float overallTrust,
            bool safetyHoldActive,
            float clearTimer,
            float clearConfirmationSeconds,
            PPOPolicyInference ppoInference)
        {
            ATADTRLPipelineManager.SecureAction normalAction = StageAction(taskStage);
            if (!fullPipelineEnabled)
            {
                return new IntelligentDecision(normalAction,
                    "Module 1 baseline: no Module 4 intervention", false, false, clearTimer);
            }
            if (scenarioId != 1)
            {
                return new IntelligentDecision(normalAction,
                    "Full pipeline pilot is currently validated only for A1", false, false, clearTimer);
            }

            bool transporting = carrying && string.Equals(taskStage, "NavigateToDestination",
                StringComparison.OrdinalIgnoreCase);
            bool unsafePrediction = transporting && collisionRisk >= 0.35f &&
                                    predictedMinimumSeparation < safeSeparation;
            bool lowTrustNearCorner = transporting && overallTrust < 0.55f && collisionRisk > 0.05f;
            if (unsafePrediction || lowTrustNearCorner)
            {
                string reason = unsafePrediction && ppoInference.ModelLoaded
                    ? $"{ppoInference.Action} selected by trained PPO for H1 conflict ({predictedMinimumSeparation:F2} m); safety-verified wait"
                    : unsafePrediction
                    ? $"Predicted H1 conflict at blind corner ({predictedMinimumSeparation:F2} m); controlled wait"
                    : "Observation trust is low near the blind corner; conservative wait";
                // The independent safety shield is deliberately retained even
                // with a trained policy. A learned Forward/Slow action cannot
                // pass the shield when the trusted state predicts collision.
                return new IntelligentDecision(ATADTRLPipelineManager.SecureAction.Wait, reason,
                    true, true, 0f);
            }

            if (safetyHoldActive)
            {
                float nextTimer = clearTimer + 0.1f;
                if (nextTimer < clearConfirmationSeconds)
                {
                    return new IntelligentDecision(ATADTRLPipelineManager.SecureAction.Wait,
                        "Confirming that H1 has cleared the blind corner", false, true, nextTimer);
                }
                return new IntelligentDecision(normalAction,
                    "Predicted conflict cleared; resume the delivery route", false, false, nextTimer);
            }

            if (transporting && ppoInference.ModelLoaded)
            {
                ATADTRLPipelineManager.SecureAction learnedAction = MapPPOAction(ppoInference.Action);
                return new IntelligentDecision(learnedAction,
                    $"Trained PPO selected {ppoInference.Action}; trusted safety shield accepted the action",
                    false, learnedAction == ATADTRLPipelineManager.SecureAction.Wait, clearTimer);
            }

            return new IntelligentDecision(normalAction,
                ppoInference.ModelLoaded
                    ? "Trained PPO is active; task-stage action is outside the navigation action space"
                    : "Guarded fallback policy: trained PPO model is unavailable",
                false, false, clearTimer);
        }

        private static ATADTRLPipelineManager.SecureAction MapPPOAction(NavigationAction action)
        {
            switch (action)
            {
                case NavigationAction.SlowDown: return ATADTRLPipelineManager.SecureAction.SlowDown;
                case NavigationAction.Wait: return ATADTRLPipelineManager.SecureAction.Wait;
                case NavigationAction.TurnLeft: return ATADTRLPipelineManager.SecureAction.Left;
                case NavigationAction.TurnRight: return ATADTRLPipelineManager.SecureAction.Right;
                default: return ATADTRLPipelineManager.SecureAction.Forward;
            }
        }

        private static ATADTRLPipelineManager.SecureAction StageAction(string stage)
        {
            if (string.Equals(stage, "Pick", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stage, "VerifyGrasp", StringComparison.OrdinalIgnoreCase))
                return ATADTRLPipelineManager.SecureAction.Pick;
            if (string.Equals(stage, "Place", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stage, "VerifyRelease", StringComparison.OrdinalIgnoreCase))
                return ATADTRLPipelineManager.SecureAction.Place;
            if (stage != null && stage.StartsWith("Navigate", StringComparison.OrdinalIgnoreCase))
                return ATADTRLPipelineManager.SecureAction.Forward;
            return ATADTRLPipelineManager.SecureAction.Wait;
        }
    }
}
