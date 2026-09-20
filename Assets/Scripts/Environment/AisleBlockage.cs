using UnityEngine;
using UnityEngine.AI;

namespace ATADTRL.Environment
{
    /// <summary>
    /// A temporary NavMeshObstacle placed over an aisle bounds to simulate a
    /// blocked aisle for a specific time window within an episode. Activated
    /// and deactivated by ScenarioManager, never by scenario-specific
    /// hard-coded logic in the navigation controller.
    /// </summary>
    public class AisleBlockage : MonoBehaviour
    {
        private NavMeshObstacle _obstacle;
        private BoxCollider _collider;
        private float _startTime;
        private float _endTime;
        private bool _armed;

        public void Setup(Bounds bounds)
        {
            transform.position = bounds.center;
            transform.localScale = Vector3.one;

            _obstacle = gameObject.GetComponent<NavMeshObstacle>();
            if (_obstacle == null) _obstacle = gameObject.AddComponent<NavMeshObstacle>();
            _obstacle.shape = NavMeshObstacleShape.Box;
            _obstacle.size = bounds.size;
            _obstacle.carving = true;
            _obstacle.carveOnlyStationary = true;
            _obstacle.enabled = false;

            _collider = gameObject.GetComponent<BoxCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = bounds.size;
            _collider.enabled = false;
        }

        public void Schedule(float episodeStartTime, float startOffset, float endOffset)
        {
            _startTime = episodeStartTime + startOffset;
            _endTime = endOffset < 0 ? float.MaxValue : episodeStartTime + endOffset;
            _armed = true;
            _obstacle.enabled = false;
            if (_collider != null) _collider.enabled = false;
        }

        private void Update()
        {
            if (!_armed) return;
            bool shouldBlock = Time.time >= _startTime && Time.time < _endTime;
            if (_obstacle.enabled != shouldBlock)
            {
                _obstacle.enabled = shouldBlock;
                if (_collider != null) _collider.enabled = shouldBlock;
            }
        }

        public void Reset()
        {
            _armed = false;
            if (_obstacle != null) _obstacle.enabled = false;
            if (_collider != null) _collider.enabled = false;
        }
    }
}
