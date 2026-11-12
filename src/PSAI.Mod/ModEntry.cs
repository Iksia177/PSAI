using UnityEngine;

namespace PSAI
{
    public sealed class ModEntry : IMod
    {
        private ModLog? log;
        private Bridge? bridge;
        private GameObject? host;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log("PSAI initialized (0.9.3).");
        }

        public void OnEnable()
        {
            bridge = new Bridge(log!);
            host = new GameObject("PSAI Bridge");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<BridgeRunner>().Initialize(bridge);
            log?.Log("PSAI bridge scheduled. Data folder: " + ModHelper.GetModDataFolder("psai"));
        }

        public void OnDisable()
        {
            bridge?.Stop();
            if (host != null) Object.Destroy(host);
            host = null;
            bridge = null;
            log?.Log("PSAI bridge disabled.");
            log = null;
        }
    }
}
