using UnityEngine;
using UnityEngine.AI;
using ATADTRL.Scenarios;

namespace ATADTRL.Core
{
    /// <summary>
    /// Bootstraps Module 1: builds the warehouse, initializes navigation and
    /// sensors, and hands control to the UI. Attach to a single "GameManager"
    /// GameObject in the scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public ScenarioManager scenarioManager;

        [Header("Runtime NavMesh")]
        [Tooltip("If true and running in the Editor, triggers a NavMesh bake after building geometry (Editor only).")]
        public bool autoBakeNavMeshInEditor = true;

        private void Start()
        {
            if (scenarioManager == null)
            {
                Debug.LogError("GameManager: ScenarioManager reference missing.");
                return;
            }

            scenarioManager.BuildWorldAndScenarios();

#if UNITY_EDITOR
            if (autoBakeNavMeshInEditor)
            {
                // ScenarioManager now builds a NavMeshSurface at runtime so
                // custom layouts work in both Editor and player builds. Keep
                // the legacy editor bake only as a fallback for older scenes.
                var existingTriangulation = NavMesh.CalculateTriangulation();
                if (existingTriangulation.vertices.Length == 0)
                {
#pragma warning disable 0618
                    UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore 0618
                }
                else
                {
                    Debug.Log($"ATADTRL: Using runtime navigation surface — " +
                              $"{existingTriangulation.vertices.Length} vertices already available.");
                }

                var triangulation = NavMesh.CalculateTriangulation();
                int vertCount = triangulation.vertices.Length;
                if (vertCount == 0)
                {
                    Debug.LogError("ATADTRL: NavMesh bake produced 0 vertices — the warehouse floor/aisles are NOT " +
                                    "walkable. The robot cannot move. Likely causes: (1) the AI Navigation package " +
                                    "isn't installed, so the legacy bake silently no-ops, or (2) the Window > AI > " +
                                    "Navigation > Bake tab's agent radius is too large for the 3m aisles. Try Window > " +
                                    "AI > Navigation, check the Bake tab settings, and re-run ATADTRL > Bake NavMesh Now.");
                }
                else
                {
                    Debug.Log($"ATADTRL: NavMesh baked successfully — {vertCount} vertices, " +
                              $"{triangulation.indices.Length / 3} triangles.");
                }
            }
#endif
            Debug.Log("ATADTRL Module 1 initialized. Warehouse built, scenarios loaded: " + scenarioManager.Scenarios.Count);
        }
    }
}
