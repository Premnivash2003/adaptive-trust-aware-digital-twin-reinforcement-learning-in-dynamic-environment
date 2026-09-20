namespace ATADTRL.Module4
{
    public readonly struct ATADTRLPerformanceEvaluation
    {
        public readonly bool BaselineCollisionReproduced;
        public readonly bool SafeReplayCompleted;
        public readonly bool CollisionAvoided;
        public readonly bool ParcelDelivered;

        public ATADTRLPerformanceEvaluation(bool baselineCollisionReproduced,
            bool safeReplayCompleted, bool collisionAvoided, bool parcelDelivered)
        {
            BaselineCollisionReproduced = baselineCollisionReproduced;
            SafeReplayCompleted = safeReplayCompleted;
            CollisionAvoided = collisionAvoided;
            ParcelDelivered = parcelDelivered;
        }

        public bool EndToEndPass => BaselineCollisionReproduced && SafeReplayCompleted &&
                                    CollisionAvoided && ParcelDelivered;
    }

    /// <summary>Module 4 baseline-versus-safe-replay performance evaluator.</summary>
    public static class EvaluateATADTRLPerformance
    {
        public static ATADTRLPerformanceEvaluation Evaluate(string baselineOutcome,
            int baselineCollisions, string replayOutcome, bool replayCompleted)
        {
            bool baselineCollision = baselineCollisions > 0 ||
                                     (!string.IsNullOrWhiteSpace(baselineOutcome) &&
                                      baselineOutcome.StartsWith("COLLISION"));
            bool avoided = replayCompleted && replayOutcome != null &&
                           replayOutcome.Contains("COLLISION AVOIDED");
            bool delivered = replayCompleted && replayOutcome != null &&
                             replayOutcome.Contains("DELIVERED TO D2");
            return new ATADTRLPerformanceEvaluation(baselineCollision, replayCompleted, avoided, delivered);
        }
    }
}
