#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ATADTRL.EditorTools
{
    /// <summary>
    /// Keeps only the secondary Display 2/3 dashboards out of the Scene view.
    /// Display 1 is deliberately restored and never modified by this helper.
    /// The four warehouse boundary walls are also hidden only in Scene view so the
    /// complete interior can be inspected from outside.
    /// </summary>
    [InitializeOnLoad]
    internal static class HideATADTRLMonitorInSceneView
    {
        private const int Display3ShadowLayer = 29;
        private const int Display2DashboardLayer = 30;
        private const int Display1ControlPanelLayer = 31;
        private static bool refreshQueued;

        static HideATADTRLMonitorInSceneView()
        {
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorApplication.playModeStateChanged += _ => QueueRefresh();
            EditorSceneManager.sceneOpened += (_, __) => QueueRefresh();
            QueueRefresh();
            EditorApplication.delayCall += FocusWarehouse;
        }

        [MenuItem("ATADTRL/Scene View/Hide Display 2 and 3 Content")]
        private static void HideFromMenu()
        {
            HideDisplayCanvases();
            SceneView.RepaintAll();
        }

        [MenuItem("ATADTRL/Scene View/Focus Complete Warehouse")]
        private static void FocusWarehouse()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            GameObject warehouse = GameObject.Find("WarehouseManager");
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (warehouse == null || sceneView == null)
            {
                return;
            }

            Renderer[] renderers = warehouse.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            HideDisplayCanvases();
            sceneView.orthographic = false;
            sceneView.pivot = bounds.center;
            sceneView.rotation = Quaternion.Euler(35f, -45f, 0f);
            sceneView.size = Mathf.Max(12f, bounds.extents.magnitude * 0.9f);
            sceneView.Repaint();
        }

        private static void QueueRefresh()
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                refreshQueued = false;
                HideDisplayCanvases();
            };
        }

        private static void HideDisplayCanvases()
        {
            // Restore normal UI-layer visibility. An earlier broad workaround
            // hid/re-layered Display 1, which is outside the Display 3 scope.
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                Tools.visibleLayers |= 1 << uiLayer;
            }

            // The independent Display 3 simulation is deliberately rendered on
            // layer 29. Keep it out of Scene 1's editor view while leaving the
            // Display 3 camera (whose culling mask targets this layer) untouched.
            Tools.visibleLayers &= ~(1 << Display3ShadowLayer);
            Tools.visibleLayers &= ~(1 << Display2DashboardLayer);
            Tools.visibleLayers &= ~(1 << Display1ControlPanelLayer);

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null)
                {
                    continue;
                }

                GameObject monitor = canvas.gameObject;
                if (!monitor.scene.IsValid() || EditorUtility.IsPersistent(monitor))
                {
                    continue;
                }

                if (canvas.targetDisplay == 0)
                {
                    // Hide only the Warehouse Operations control Canvas. Other
                    // Display-1/world-space canvases such as rack signs remain.
                    bool isOperationsPanel = monitor.name == "Canvas" ||
                        canvas.GetComponentInChildren<ATADTRL.UI.ScenarioUIManager>(true) != null;
                    if (isOperationsPanel)
                    {
                        SetLayerRecursively(monitor.transform, Display1ControlPanelLayer);
                        SceneVisibilityManager.instance.Hide(monitor, true);
                    }
                    continue;
                }

                // Keep each secondary display on an isolated editor layer.
                // Overlay canvases still render normally in their Game tabs;
                // only the Scene camera excludes these layers.
                int isolatedLayer = canvas.targetDisplay == 1
                    ? Display2DashboardLayer
                    : Display3ShadowLayer;
                SetLayerRecursively(monitor.transform, isolatedLayer);

                SceneVisibilityManager.instance.Hide(monitor, true);
            }

            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform item in transforms)
            {
                if (item == null || !IsWarehouseBoundaryWall(item.name))
                {
                    continue;
                }

                GameObject wall = item.gameObject;
                if (!wall.scene.IsValid() || EditorUtility.IsPersistent(wall))
                {
                    continue;
                }

                SceneVisibilityManager.instance.Hide(wall, true);
            }
        }

        private static bool IsWarehouseBoundaryWall(string objectName)
        {
            return objectName == "Wall_North" || objectName == "Wall_South" ||
                   objectName == "Wall_East" || objectName == "Wall_West";
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

    }
}
#endif
