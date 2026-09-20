using System;
using UnityEngine;

namespace ATADTRL.TestFlow
{
    public enum NavigationAction
    {
        Forward = 0,
        SlowDown = 1,
        Wait = 2,
        TurnLeft = 3,
        TurnRight = 4
    }

    public readonly struct PPOPolicyInference
    {
        public readonly NavigationAction Action;
        public readonly float Forward;
        public readonly float Slow;
        public readonly float Wait;
        public readonly float Left;
        public readonly float Right;
        public readonly bool ModelLoaded;

        public PPOPolicyInference(NavigationAction action, float forward, float slow, float wait,
            float left, float right, bool modelLoaded)
        {
            Action = action;
            Forward = forward;
            Slow = slow;
            Wait = wait;
            Left = left;
            Right = right;
            ModelLoaded = modelLoaded;
        }

        public static PPOPolicyInference Fallback =>
            new PPOPolicyInference(NavigationAction.Forward, .78f, .07f, .03f, .03f, .03f, false);
    }

    /// <summary>
    /// Module 2 trained PPO policy runtime. The policy is a clipped-PPO actor
    /// trained for the A1 blind-corner state/action space and exported as a
    /// compact JSON MLP so Unity inference does not depend on Python.
    /// </summary>
    public sealed class PPOPolicyLearning
    {
        [Serializable]
        private sealed class PPOModelData
        {
            public string policyId;
            public string algorithm;
            public int trainingUpdates;
            public int trainingTimesteps;
            public int evaluationEpisodes;
            public float evaluationMeanReward;
            public float evaluationSuccessRate;
            public float evaluationCollisionRate;
            public int inputSize;
            public int hiddenSize;
            public int actionSize;
            public float[] inputMean;
            public float[] inputScale;
            public float[] w1;
            public float[] b1;
            public float[] w2;
            public float[] b2;
        }

        private readonly PPOModelData model;
        private readonly float[] hidden;
        private readonly float[] logits;

        public bool IsModelLoaded => model != null;
        public string PolicyId => model != null ? model.policyId : "PPO_MODEL_NOT_LOADED";
        public int TrainingTimesteps => model != null ? model.trainingTimesteps : 0;
        public int EvaluationEpisodes => model != null ? model.evaluationEpisodes : 0;
        public float EvaluationSuccessRate => model != null ? model.evaluationSuccessRate : 0f;
        public float EvaluationCollisionRate => model != null ? model.evaluationCollisionRate : 1f;

        public PPOPolicyLearning()
        {
            TextAsset asset = Resources.Load<TextAsset>("A1_PPO_Policy");
            if (asset == null)
            {
                Debug.LogError("ATADTRL PPO: Resources/A1_PPO_Policy.json is missing; using guarded fallback policy.");
                return;
            }

            PPOModelData candidate = JsonUtility.FromJson<PPOModelData>(asset.text);
            if (!Validate(candidate))
            {
                Debug.LogError("ATADTRL PPO: trained policy data is invalid; using guarded fallback policy.");
                return;
            }

            model = candidate;
            hidden = new float[model.hiddenSize];
            logits = new float[model.actionSize];
            Debug.Log($"ATADTRL PPO: loaded {PolicyId}; {TrainingTimesteps} training steps, " +
                      $"evaluation success {EvaluationSuccessRate:P1}, collision {EvaluationCollisionRate:P1}.");
        }

        public PPOPolicyInference Infer(float collisionRisk, float minimumSeparation,
            float timeToClosestApproach, float overallTrust, bool carrying,
            bool blindCornerExposure, bool safetyHoldActive, float routeProgress)
        {
            if (model == null) return PPOPolicyInference.Fallback;

            float[] input =
            {
                Mathf.Clamp01(collisionRisk),
                Mathf.Clamp01(minimumSeparation / 3f),
                Mathf.Clamp01(timeToClosestApproach / 4f),
                Mathf.Clamp01(overallTrust),
                carrying ? 1f : 0f,
                blindCornerExposure ? 1f : 0f,
                safetyHoldActive ? 1f : 0f,
                Mathf.Clamp01(routeProgress)
            };

            for (int h = 0; h < model.hiddenSize; h++)
            {
                float sum = model.b1[h];
                for (int i = 0; i < model.inputSize; i++)
                {
                    float scale = Mathf.Abs(model.inputScale[i]) > 0.00001f ? model.inputScale[i] : 1f;
                    float normalized = (input[i] - model.inputMean[i]) / scale;
                    sum += normalized * model.w1[i * model.hiddenSize + h];
                }
                hidden[h] = (float)Math.Tanh(sum);
            }

            float maxLogit = float.NegativeInfinity;
            for (int a = 0; a < model.actionSize; a++)
            {
                float sum = model.b2[a];
                for (int h = 0; h < model.hiddenSize; h++)
                    sum += hidden[h] * model.w2[h * model.actionSize + a];
                logits[a] = sum;
                if (sum > maxLogit) maxLogit = sum;
            }

            float total = 0f;
            for (int a = 0; a < model.actionSize; a++)
            {
                logits[a] = Mathf.Exp(logits[a] - maxLogit);
                total += logits[a];
            }
            int selected = 0;
            float best = -1f;
            for (int a = 0; a < model.actionSize; a++)
            {
                logits[a] /= Mathf.Max(total, 0.00001f);
                if (logits[a] > best) { best = logits[a]; selected = a; }
            }

            return new PPOPolicyInference((NavigationAction)selected,
                logits[0], logits[1], logits[2], logits[3], logits[4], true);
        }

        public float CalculateContextReward(ContextState state)
        {
            return -0.01f - (state.blindCornerConflict ? state.collisionRisk : 0f);
        }

        public float CalculateReward(ContextState state, bool collision, bool goalReached)
        {
            float reward = CalculateContextReward(state);
            if (collision) reward -= 10f;
            if (goalReached) reward += 20f;
            return reward;
        }

        private static bool Validate(PPOModelData value)
        {
            return value != null && value.inputSize == 8 && value.actionSize == 5 &&
                   value.hiddenSize > 0 && value.inputMean != null && value.inputMean.Length == value.inputSize &&
                   value.inputScale != null && value.inputScale.Length == value.inputSize &&
                   value.w1 != null && value.w1.Length == value.inputSize * value.hiddenSize &&
                   value.b1 != null && value.b1.Length == value.hiddenSize &&
                   value.w2 != null && value.w2.Length == value.hiddenSize * value.actionSize &&
                   value.b2 != null && value.b2.Length == value.actionSize;
        }
    }
}
