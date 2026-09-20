using UnityEngine;
using ATADTRL.Pipeline;
using ATADTRL.TestFlow;

namespace ATADTRL.Module4
{
    public readonly struct SecurePolicyDistribution
    {
        public readonly float Forward;
        public readonly float Slow;
        public readonly float Wait;
        public readonly float Left;
        public readonly float Right;
        public readonly bool SafetyHold;
        public readonly string MissionStage;
        public readonly float MissionProgress;

        public SecurePolicyDistribution(float forward, float slow, float wait, float left, float right,
            bool safetyHold, string missionStage, float missionProgress)
        {
            Forward = forward;
            Slow = slow;
            Wait = wait;
            Left = left;
            Right = right;
            SafetyHold = safetyHold;
            MissionStage = missionStage;
            MissionProgress = missionProgress;
        }
    }

    /// <summary>
    /// Module 4 secure-policy optimization block. It applies the safety gate
    /// to the selected action and exposes the action distribution used by the
    /// Display 3 policy visualization and robot executor.
    /// </summary>
    public static class OptimizeSecureNavigationPolicy
    {
        public static SecurePolicyDistribution Optimize(
            ATADTRLPipelineManager.SecureAction selectedAction,
            float collisionRisk,
            bool carrying,
            string taskStage,
            float currentProgress,
            PPOPolicyInference ppoInference)
        {
            if (selectedAction == ATADTRLPipelineManager.SecureAction.Wait)
            {
                if (ppoInference.ModelLoaded && ppoInference.Action == NavigationAction.Wait)
                {
                    return new SecurePolicyDistribution(
                        ppoInference.Forward, ppoInference.Slow, ppoInference.Wait,
                        ppoInference.Left, ppoInference.Right, true,
                        "PPO CONTROLLED WAIT AT BLIND CORNER", Mathf.Max(currentProgress, 0.58f));
                }
                return new SecurePolicyDistribution(
                    0.04f, 0.03f, 0.84f, 0.03f, 0.03f, true,
                    "SAFETY-SHIELDED WAIT AT BLIND CORNER", Mathf.Max(currentProgress, 0.58f));
            }

            if (ppoInference.ModelLoaded && carrying)
            {
                return new SecurePolicyDistribution(
                    ppoInference.Forward, ppoInference.Slow, ppoInference.Wait,
                    ppoInference.Left, ppoInference.Right, false,
                    $"PPO {ppoInference.Action.ToString().ToUpperInvariant()} — SAFE TRANSPORT TO D2",
                    Mathf.Max(currentProgress, 0.48f));
            }

            return new SecurePolicyDistribution(
                0.78f, collisionRisk > 0.1f ? 0.13f : 0.07f, 0.03f, 0.03f, 0.03f, false,
                carrying ? "SAFE TRANSPORT TO D2" : taskStage,
                Mathf.Max(currentProgress, carrying ? 0.48f : 0.15f));
        }
    }
}
