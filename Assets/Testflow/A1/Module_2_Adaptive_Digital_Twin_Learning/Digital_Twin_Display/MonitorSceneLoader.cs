using UnityEngine;
using UnityEngine.SceneManagement;

namespace ATADTRL.UI
{
    public class MonitorSceneLoader :
        MonoBehaviour
    {
        [SerializeField]
        private string monitorSceneName =
            "ATADTRL_Monitor";

        private void Start()
        {
            Scene scene =
                SceneManager.GetSceneByName(
                    monitorSceneName);

            if (!scene.isLoaded)
            {
                SceneManager.LoadScene(
                    monitorSceneName,
                    LoadSceneMode.Additive);
            }
        }
    }
}