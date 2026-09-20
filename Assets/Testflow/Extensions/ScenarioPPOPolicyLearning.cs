using System;
using UnityEngine;

namespace ATADTRL.TestFlow.Extensions
{
    public readonly struct ScenarioPPOInference
    {
        public readonly int ActionIndex;
        public readonly float[] Probabilities;
        public readonly bool ModelLoaded;
        public ScenarioPPOInference(int actionIndex, float[] probabilities, bool loaded)
        { ActionIndex = actionIndex; Probabilities = probabilities; ModelLoaded = loaded; }
    }

    /// <summary>Loads and evaluates A4/T3 clipped-PPO actor weights.</summary>
    public sealed class ScenarioPPOPolicyLearning
    {
        [Serializable]
        private sealed class Model
        {
            public string policyId;
            public int trainingTimesteps;
            public int evaluationEpisodes;
            public float evaluationSuccessRate;
            public float evaluationCollisionRate;
            public int inputSize, hiddenSize, actionSize;
            public string[] actionNames;
            public float[] inputMean, inputScale, w1, b1, w2, b2;
        }

        private readonly Model model;
        public bool IsLoaded => model != null;
        public string PolicyId => model != null ? model.policyId : "MODEL_NOT_LOADED";
        public int TrainingTimesteps => model != null ? model.trainingTimesteps : 0;
        public float SuccessRate => model != null ? model.evaluationSuccessRate : 0f;
        public float UnsafeRate => model != null ? model.evaluationCollisionRate : 1f;
        public string ActionName(int index) => model != null && model.actionNames != null &&
            index >= 0 && index < model.actionNames.Length ? model.actionNames[index] : "Unknown";

        public ScenarioPPOPolicyLearning(string resourceName)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null) { Debug.LogError($"ATADTRL PPO: Resources/{resourceName}.json is missing."); return; }
            Model candidate = JsonUtility.FromJson<Model>(asset.text);
            if (candidate == null || candidate.inputSize != 8 || candidate.actionSize != 5 ||
                candidate.w1 == null || candidate.w1.Length != candidate.inputSize * candidate.hiddenSize ||
                candidate.w2 == null || candidate.w2.Length != candidate.hiddenSize * candidate.actionSize)
            { Debug.LogError($"ATADTRL PPO: {resourceName} has invalid weights."); return; }
            model = candidate;
            Debug.Log($"ATADTRL PPO: loaded {PolicyId}, {TrainingTimesteps:N0} training steps, " +
                      $"evaluation accuracy {SuccessRate:P1}, unsafe action rate {UnsafeRate:P1}.");
        }

        public ScenarioPPOInference Infer(float[] input)
        {
            if (model == null || input == null || input.Length != 8)
                return new ScenarioPPOInference(0, new[] { .8f, .05f, .05f, .05f, .05f }, false);
            float[] hidden = new float[model.hiddenSize];
            for (int h = 0; h < hidden.Length; h++)
            {
                float sum = model.b1[h];
                for (int i = 0; i < 8; i++)
                    sum += ((input[i] - model.inputMean[i]) / Mathf.Max(.00001f, model.inputScale[i])) *
                           model.w1[i * model.hiddenSize + h];
                hidden[h] = (float)Math.Tanh(sum);
            }
            float[] probability = new float[5];
            float max = float.NegativeInfinity;
            for (int a = 0; a < 5; a++)
            {
                float sum = model.b2[a];
                for (int h = 0; h < hidden.Length; h++) sum += hidden[h] * model.w2[h * 5 + a];
                probability[a] = sum; if (sum > max) max = sum;
            }
            float total = 0f;
            for (int a = 0; a < 5; a++) { probability[a] = Mathf.Exp(probability[a] - max); total += probability[a]; }
            int selected = 0;
            for (int a = 0; a < 5; a++)
            { probability[a] /= total; if (probability[a] > probability[selected]) selected = a; }
            return new ScenarioPPOInference(selected, probability, true);
        }
    }
}
