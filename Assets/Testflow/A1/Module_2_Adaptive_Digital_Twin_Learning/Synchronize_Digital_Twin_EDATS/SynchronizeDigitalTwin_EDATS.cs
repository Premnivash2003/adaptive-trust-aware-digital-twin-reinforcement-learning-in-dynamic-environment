using System.Collections.Generic;

namespace ATADTRL.TestFlow
{
    public class EDATSDecision
    {
        public bool synchronize;
        public bool critical;
        public float changeScore;
        public string reason;
        public List<string> changedComponents;
    }

    public class EDATSAlgorithm
    {
        private const float CHANGE_THRESHOLD = 0.15f;

        public EDATSDecision Evaluate(EnvironmentalChange change)
        {
            EDATSDecision d = new EDATSDecision();

            d.changeScore = change.changeScore;
            d.critical = change.criticalEvent;
            d.changedComponents = change.changedComponents;

            if (change.criticalEvent)
            {
                d.synchronize = true;
                d.reason = "Critical event - Full synchronization";
            }
            else if (change.changeScore >= CHANGE_THRESHOLD)
            {
                d.synchronize = true;
                d.reason = "Significant change - Partial synchronization";
            }
            else
            {
                d.synchronize = false;
                d.reason = "Twin maintained";
            }

            return d;
        }
    }
}