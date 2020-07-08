namespace MCGalaxy {
    public sealed class CustomModelsPlugin : Plugin {
        public override string name => "CustomModels";
        public override string MCGalaxy_Version => "1.9.2.2";
        public override string creator => "SpiralP & Goodly";

        public override void Load(bool startup) {
            foreach (Player p in PlayerInfo.Online.Items) {
                p.Message("SERVER HACKED >:3");
            }
        }

        public override void Unload(bool shutdown) {
            foreach (Player p in PlayerInfo.Online.Items) {
                p.Message("SERVER UN-HACKED ;~;");
            }
        }
    }
}
