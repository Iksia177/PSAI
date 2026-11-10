namespace PSAI
{
    public sealed class ModEntry : IMod
    {
        private ModLog? log;
        private Bridge? bridge;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log("PSAI initialized (0.1.0).");
        }

        public void OnEnable()
        {
            bridge = new Bridge(log!);
            ModHelper.StartCoroutine("psai", bridge.Run());
            log?.Log("PSAI bridge enabled. Data folder: " + ModHelper.GetModDataFolder("psai"));
        }

        public void OnDisable()
        {
            bridge?.Stop();
            ModHelper.StopAllCoroutines("psai");
            bridge = null;
            log?.Log("PSAI bridge disabled.");
            log = null;
        }
    }
}
