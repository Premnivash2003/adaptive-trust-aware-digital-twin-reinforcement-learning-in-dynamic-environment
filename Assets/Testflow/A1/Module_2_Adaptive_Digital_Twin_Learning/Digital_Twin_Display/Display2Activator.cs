using UnityEngine;

namespace ATADTRL.UI
{
    public class Display2Activator :
        MonoBehaviour
    {
        private void Start()
        {
            if (Display.displays.Length > 1)
            {
                Display.displays[1].Activate();

                Debug.Log(
                    "ATADTRL Monitor activated on Display 2.");
            }
            else
            {
                Debug.LogWarning(
                    "Display 2 is not available. " +
                    "Monitor will remain available through " +
                    "the configured Unity Game View.");
            }
        }
    }
}