using UnityEngine;

namespace PSAI
{
    public sealed class BridgeRunner : MonoBehaviour
    {
        private Bridge? bridge;

        internal void Initialize(Bridge instance) { bridge = instance; }

        // Update starts after the loader has completed IMod.OnEnable and registered
        // the mod as running. Loader-owned coroutines reject the earlier call.
        private void Update() { bridge?.Poll(); }

        private void OnDestroy()
        {
            bridge?.Stop();
            bridge = null;
        }
    }
}
