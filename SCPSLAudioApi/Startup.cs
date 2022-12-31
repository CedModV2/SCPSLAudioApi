namespace SCPSLAudioApi
{
    public class Startup
    {
        public static void SetupDependencies()
        {
            CosturaUtility.Initialize();
        }
    }
}