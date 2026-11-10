namespace PSAI
{
    public sealed class ModEntry : IMod
    {
        private ModLog? log;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log("PSAI preparation skeleton initialized (0.1.0).");
        }

        public void OnEnable()
        {
            log?.Log("PSAI preparation skeleton enabled.");
        }

        public void OnDisable()
        {
            log?.Log("PSAI preparation skeleton disabled.");
            log = null;
        }
    }
}
