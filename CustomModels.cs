//reference System.dll
//reference System.Core.dll
//reference System.Drawing.dll
//reference Newtonsoft.Json.dll

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using MCGalaxy.Commands;
using MCGalaxy.Events.PlayerEvents;
using MCGalaxy.Maths;
using MCGalaxy.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCGalaxy {
    public sealed class CustomModelsPlugin : Plugin {
        public override string name { get { return "CustomModels"; } }
        public override string MCGalaxy_Version { get { return "1.9.2.2"; } }
        public override string creator { get { return "SpiralP & Goodly"; } }

        //------------------------------------------------------------------bbmodel/ccmodel file loading

        // Path.GetExtension includes the period "."
        const string BBModelExt = ".bbmodel";
        const string CCModelExt = ".ccmodel";
        const string PublicModelsDirectory = "plugins/models/";
        const string PersonalModelsDirectory = "plugins/personal_models/";

        // don't store "name" because we will use filename for model name
        // don't store "parts" because we store those in the full .bbmodel file
        // don't store "u/vScale" because we take it from bbmodel's resolution.width
        class StoredCustomModel {
            [JsonIgnore] public string fullName;
            [JsonIgnore] public string modelName;
            [JsonIgnore] public HashSet<string> modifiers;

            public float nameY;
            public float eyeY;
            public Vec3F32 collisionBounds;
            public Vec3F32 pickingBoundsMin;
            public Vec3F32 pickingBoundsMax;
            public bool bobbing;
            public bool pushes;
            public bool usesHumanSkin;
            public bool calcHumanAnims;

            public StoredCustomModel() {
                this.fullName = null;
                this.modelName = null;
                this.modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                this.nameY = 32.5f;
                this.eyeY = 26.0f;
                this.collisionBounds = new Vec3F32 {
                    X = 8.6f,
                    Y = 28.1f,
                    Z = 8.6f
                };
                this.pickingBoundsMin = new Vec3F32 {
                    X = -8,
                    Y = 0,
                    Z = -4
                };
                this.pickingBoundsMax = new Vec3F32 {
                    X = 8,
                    Y = 32,
                    Z = 4
                };
                this.bobbing = true;
                this.pushes = true;
                this.usesHumanSkin = true;
                this.calcHumanAnims = true;
                this.cache = null;
            }

            public StoredCustomModel(string name) : this() {
                this.modelName = name;

                var split = name.Split(new string[] { " (" }, StringSplitOptions.None);
                if (split.Length == 2) {
                    this.modelName = split[0];

                    var attrs = split[1];
                    if (attrs.EndsWith(")")) {
                        // remove ")"
                        attrs = attrs.Substring(0, attrs.Length - 1);
                        foreach (var attr in attrs.SplitComma()) {
                            modifiers.Add(attr);
                        }
                        Logger.Log(LogType.Warning, "->> " + modifiers.Count);
                    }
                }

                this.UpdateFullName();
            }

            void UpdateFullName() {
                if (this.modifiers.Count > 0) {
                    this.fullName = this.modelName + " (" + this.modifiers.Join(",") + ")";
                } else {
                    this.fullName = this.modelName;
                }
            }

            public void AddModifier(string modifier) {
                this.modifiers.Add(modifier);
                this.UpdateFullName();
            }

            public void RemoveModifier(string modifier) {
                this.modifiers.Remove(modifier);
                this.UpdateFullName();
            }

            public void LoadFromCustomModel(CustomModel model) {
                // convert to pixel units
                this.nameY = model.nameY * 16.0f;
                this.eyeY = model.eyeY * 16.0f;
                this.collisionBounds = new Vec3F32 {
                    X = model.collisionBounds.X * 16.0f,
                    Y = model.collisionBounds.Y * 16.0f,
                    Z = model.collisionBounds.Z * 16.0f,
                };
                this.pickingBoundsMin = new Vec3F32 {
                    X = model.pickingBoundsMin.X * 16.0f,
                    Y = model.pickingBoundsMin.Y * 16.0f,
                    Z = model.pickingBoundsMin.Z * 16.0f,
                };
                this.pickingBoundsMax = new Vec3F32 {
                    X = model.pickingBoundsMax.X * 16.0f,
                    Y = model.pickingBoundsMax.Y * 16.0f,
                    Z = model.pickingBoundsMax.Z * 16.0f,
                };
                this.bobbing = model.bobbing;
                this.pushes = model.pushes;
                this.usesHumanSkin = model.usesHumanSkin;
                this.calcHumanAnims = model.calcHumanAnims;
            }

            [JsonIgnore] BlockBench.JsonRoot cache = null;
            BlockBench.JsonRoot ParseBlockBench() {
                if (cache != null) {
                    return cache;
                }

                string path = GetBBPath();
                string contentsBB = File.ReadAllText(path);
                var blockBench = BlockBench.Parse(contentsBB);
                cache = blockBench;
                return blockBench;
            }

            public CustomModelPart[] ToCustomModelParts() {
                var blockBench = ParseBlockBench();
                var parts = blockBench.ToCustomModelParts();

                if (this.modifiers.Contains("sitting")) {
                    CustomModelPart leg = null;
                    foreach (var part in parts) {
                        if (
                            part.anim == CustomModelAnim.LeftLeg ||
                            part.anim == CustomModelAnim.RightLeg
                        ) {
                            // rotate legs to point forward, pointed a little outwards
                            leg = part;
                            part.rotation.X = 90.0f;
                            part.rotation.Y = part.anim == CustomModelAnim.LeftLeg ? 5.0f : -5.0f;
                            part.anim = CustomModelAnim.None;
                        }
                    }

                    if (leg != null) {
                        var legHeight = leg.max.Y - leg.min.Y;
                        var legForwardWidth = leg.max.Z - leg.min.Z;
                        // lower all parts by leg's Y height, up by the leg's width
                        foreach (var part in parts) {
                            part.min.Y -= legHeight - legForwardWidth / 2.0f;
                            part.max.Y -= legHeight - legForwardWidth / 2.0f;
                            part.rotationOrigin.Y -= legHeight - legForwardWidth / 2.0f;

                            if (part.firstPersonArm) {
                                // remove first person arm because offset changed
                                part.firstPersonArm = false;
                            }
                        }
                    }
                }

                return parts;
            }

            public CustomModel ToCustomModel() {
                var blockBench = ParseBlockBench();

                // convert to block units
                var model = new CustomModel {
                    name = fullName,
                    // this is set in DefineModel
                    partCount = 0,
                    uScale = blockBench.resolution.width,
                    vScale = blockBench.resolution.height,

                    nameY = this.nameY / 16.0f,
                    eyeY = this.eyeY / 16.0f,
                    collisionBounds = new Vec3F32 {
                        X = this.collisionBounds.X / 16.0f,
                        Y = this.collisionBounds.Y / 16.0f,
                        Z = this.collisionBounds.Z / 16.0f,
                    },
                    pickingBoundsMin = new Vec3F32 {
                        X = this.pickingBoundsMin.X / 16.0f,
                        Y = this.pickingBoundsMin.Y / 16.0f,
                        Z = this.pickingBoundsMin.Z / 16.0f,
                    },
                    pickingBoundsMax = new Vec3F32 {
                        X = this.pickingBoundsMax.X / 16.0f,
                        Y = this.pickingBoundsMax.Y / 16.0f,
                        Z = this.pickingBoundsMax.Z / 16.0f,
                    },
                    bobbing = this.bobbing,
                    pushes = this.pushes,
                    usesHumanSkin = this.usesHumanSkin,
                    calcHumanAnims = this.calcHumanAnims,
                };

                return model;
            }

            public void WriteToFile() {
                string path = GetCCPath();
                string storedJsonModel = JsonConvert.SerializeObject(this, Formatting.Indented, jsonSettings);
                File.WriteAllText(path, storedJsonModel);
            }

            public void LoadFromFile() {
                string path = GetCCPath();
                string contentsCC = File.ReadAllText(path);
                StoredCustomModel o = JsonConvert.DeserializeObject<StoredCustomModel>(contentsCC, jsonSettings);
                this.nameY = o.nameY;
                this.eyeY = o.eyeY;
                this.collisionBounds = o.collisionBounds;
                this.pickingBoundsMin = o.pickingBoundsMin;
                this.pickingBoundsMax = o.pickingBoundsMax;
                this.bobbing = o.bobbing;
                this.pushes = o.pushes;
                this.usesHumanSkin = o.usesHumanSkin;
                this.calcHumanAnims = o.calcHumanAnims;
            }

            public bool Exists() {
                string path = GetCCPath();
                return File.Exists(path);
            }

            public static string GetPlayerName(string name) {
                if (name.Contains("+")) {
                    string[] split = name.Split('+');
                    string playerName = split[0];
                    return playerName + "+";
                } else {
                    return null;
                }
            }

            public static string GetFolderPath(string name) {
                string maybePlayerName = GetPlayerName(name);
                if (maybePlayerName != null) {
                    string folderPath = PersonalModelsDirectory + Path.GetFileName(maybePlayerName.ToLower()) + "/";
                    if (!Directory.Exists(folderPath)) {
                        Directory.CreateDirectory(folderPath);
                    }
                    return folderPath;
                } else {
                    return PublicModelsDirectory;
                }
            }

            public string GetCCPath() {
                return GetFolderPath(this.modelName) + Path.GetFileName(this.modelName.ToLower()) + CCModelExt;
            }

            public string GetBBPath() {
                return GetFolderPath(this.modelName) + Path.GetFileName(this.modelName.ToLower()) + BBModelExt;
            }

            public void WriteBBFile(string json) {
                File.WriteAllText(
                    GetBBPath(),
                    json
                );
            }
        }


        // returns how many models
        static int CreateMissingCCModels(bool isPersonal) {
            int count = 0;

            if (isPersonal) {
                foreach (var entry in new DirectoryInfo(PersonalModelsDirectory).GetDirectories()) {
                    string folderName = entry.Name;
                    if (folderName != folderName.ToLower()) {
                        RenameDirectory(PersonalModelsDirectory, folderName, folderName.ToLower());
                    }
                    count += CheckFolder(PersonalModelsDirectory + folderName + "/");
                }
            } else {
                count += CheckFolder(PublicModelsDirectory);
            }

            return count;
        }

        static void RenameDirectory(string folderPath, string currentFolderName, string folderName) {
            string fromPath = folderPath + currentFolderName;
            string toPath = folderPath + folderName;
            Logger.Log(
                LogType.SystemActivity,
                "CustomModels: Renaming {0} to {1}",
                fromPath,
                toPath
            );
            Directory.Move(
                fromPath,
                folderPath + "temp"
            );
            Directory.Move(
                folderPath + "temp",
                toPath
            );
        }

        static void RenameFile(string folderPath, string currentFileName, string fileName) {
            string fromPath = folderPath + currentFileName;
            string toPath = folderPath + fileName;
            Logger.Log(
                LogType.SystemActivity,
                "CustomModels: Renaming {0} to {1}",
                fromPath,
                toPath
            );
            File.Move(
                fromPath,
                folderPath + "temp"
            );
            File.Move(
                folderPath + "temp",
                toPath
            );
        }

        static int CheckFolder(string folderPath) {
            // make sure all cc files are lowercased
            foreach (var entry in new DirectoryInfo(folderPath).GetFiles()) {
                string fileName = entry.Name;
                if (fileName != fileName.ToLower()) {
                    RenameFile(folderPath, fileName, fileName.ToLower());
                }
            }

            int count = 0;
            foreach (var entry in new DirectoryInfo(folderPath).GetFiles()) {
                string fileName = entry.Name;
                if (fileName != fileName.ToLower()) {
                    RenameFile(folderPath, fileName, fileName.ToLower());
                    fileName = fileName.ToLower();
                }

                string modelName = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                if (!extension.CaselessEq(BBModelExt)) {
                    continue;
                }

                count += 1;
                var defaultModel = new StoredCustomModel(modelName);
                if (!defaultModel.Exists()) {
                    defaultModel.WriteToFile();

                    Logger.Log(
                        LogType.SystemActivity,
                        "CustomModels: Created a new default template \"{0}\" in {1}",
                        modelName + CCModelExt,
                        folderPath
                    );
                }
            }

            return count;
        }

        static Dictionary<string, Dictionary<string, byte>> ModelNameToIdForPlayer = new Dictionary<string, Dictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

        static byte? GetModelId(Player p, string name, bool addNew = false) {
            lock (ModelNameToIdForPlayer) {
                var modelNameToId = ModelNameToIdForPlayer[p.name];
                byte value;
                if (modelNameToId.TryGetValue(name, out value)) {
                    return value;
                } else {
                    if (addNew) {
                        for (int i = 0; i < Packet.MaxCustomModels; i++) {
                            if (!modelNameToId.ContainsValue((byte)i)) {
                                modelNameToId.Add(name, (byte)i);
                                return (byte)i;
                            }
                        }
                        throw new Exception("overflow MaxCustomModels");
                    } else {
                        return null;
                    }
                }
            }
        }

        static void DefineModel(Player p, CustomModel model, CustomModelPart[] parts) {
            Logger.Log(LogType.Warning, "DefineModel " + model.name);
            if (!p.Supports(CpeExt.CustomModels)) return;

            var modelId = GetModelId(p, model.name, true).Value;
            model.partCount = (byte)parts.Length;
            byte[] modelPacket = Packet.DefineModel(modelId, model);
            p.Send(modelPacket);

            foreach (var part in parts) {
                byte[] partPacket = Packet.DefineModelPart(modelId, part);
                p.Send(partPacket);
            }
        }

        static void UndefineModel(Player p, string name) {
            Logger.Log(LogType.Warning, "UndefineModel " + name);

            if (!p.Supports(CpeExt.CustomModels)) return;
            byte[] modelPacket = Packet.UndefineModel(GetModelId(p, name).Value);
            p.Send(modelPacket);

            var modelNameToId = ModelNameToIdForPlayer[p.name];
            modelNameToId.Remove(name);
        }

        //------------------------------------------------------------------ plugin interface

        CmdCustomModel command = null;
        public override void Load(bool startup) {
            if (!Server.Config.ClassicubeAccountPlus) {
                // sorry but i rely on "+" in filenames! :(
                Logger.Log(
                    LogType.Warning,
                    "CustomModels plugin refusing to load due to Config.ClassicubeAccountPlus not being enabled!"
                );
                return;
            }

            command = new CmdCustomModel();
            Command.Register(command);

            OnPlayerConnectEvent.Register(OnPlayerConnect, Priority.Low);
            OnPlayerDisconnectEvent.Register(OnPlayerDisconnect, Priority.Low);

            OnJoiningLevelEvent.Register(OnJoiningLevel, Priority.Low);
            OnJoinedLevelEvent.Register(OnJoinedLevel, Priority.Low);

            OnBeforeChangeModelEvent.Register(OnBeforeChangeModel, Priority.Low);

            if (!Directory.Exists(PublicModelsDirectory)) Directory.CreateDirectory(PublicModelsDirectory);
            if (!Directory.Exists(PersonalModelsDirectory)) Directory.CreateDirectory(PersonalModelsDirectory);

            int numModels = CreateMissingCCModels(false);
            int numPersonalModels = CreateMissingCCModels(true);
            Logger.Log(
                LogType.SystemActivity,
                "CustomModels Loaded with {0} Models and {1} Personal Models",
                numModels,
                numPersonalModels
            );

            // initialize because of a late plugin load
            foreach (Player p in PlayerInfo.Online.Items) {
                SentCustomModels.Add(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                ModelNameToIdForPlayer.Add(p.name, new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            }
        }

        public override void Unload(bool shutdown) {
            SentCustomModels.Clear();
            ModelNameToIdForPlayer.Clear();

            OnPlayerConnectEvent.Unregister(OnPlayerConnect);
            OnPlayerDisconnectEvent.Unregister(OnPlayerDisconnect);
            OnJoiningLevelEvent.Unregister(OnJoiningLevel);
            OnJoinedLevelEvent.Unregister(OnJoinedLevel);
            OnBeforeChangeModelEvent.Unregister(OnBeforeChangeModel);

            if (command != null) {
                Command.Unregister(command);
                command = null;
            }
        }

        static Dictionary<string, HashSet<string>> SentCustomModels = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static void CheckSendModel(Player p, string modelName) {
            lock (SentCustomModels) {
                var sentModels = SentCustomModels[p.name];

                if (!sentModels.Contains(modelName)) {
                    var storedModel = new StoredCustomModel(modelName);
                    if (storedModel.Exists()) {
                        storedModel.LoadFromFile();
                        DefineModel(p, storedModel.ToCustomModel(), storedModel.ToCustomModelParts());
                        sentModels.Add(modelName);
                    }
                }
            }
        }

        static void CheckRemoveModel(Player p, string modelName) {
            lock (SentCustomModels) {
                var sentModels = SentCustomModels[p.name];
                if (sentModels.Contains(modelName)) {
                    UndefineModel(p, modelName);
                    sentModels.Remove(modelName);
                }
            }
        }

        // removes all unused models from player, and
        // sends all missing models in level to player
        static void CheckAddRemove(Player p, Level level) {
            var visibleModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visibleModels.Add(ModelInfo.GetRawModel(p.Model));

            foreach (Player e in level.getPlayers()) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }
            foreach (PlayerBot e in level.Bots.Items) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }

            var sentModels = SentCustomModels[p.name];
            // clone so we can modify while we iterate
            foreach (var modelName in sentModels.ToArray()) {
                // remove models not found in this level
                if (!visibleModels.Contains(modelName)) {
                    CheckRemoveModel(p, modelName);
                }
            }

            // send new models not yet in player's list
            foreach (var modelName in visibleModels) {
                CheckSendModel(p, modelName);
            }
        }

        static void CheckUpdateAll(string modelName) {
            // re-define the model and do ChangeModel for each entity currently using this model

            // remove this model from everyone's sent list
            foreach (Player p in PlayerInfo.Online.Items) {
                CheckRemoveModel(p, modelName);
            }

            // add this model back to players who see entities using it
            foreach (Player p in PlayerInfo.Online.Items) {
                CheckAddRemove(p, p.level);
            }

            // do ChangeModel on every entity with this model
            // so that we update the model on the client
            var loadedLevels = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            foreach (Player p in PlayerInfo.Online.Items) {
                if (ModelInfo.GetRawModel(p.Model).CaselessEq(modelName)) {
                    Entities.UpdateModel(p, p.Model);
                }

                if (!loadedLevels.ContainsKey(p.level.name)) {
                    loadedLevels.Add(p.level.name, p.level);
                }
            }
            foreach (var entry in loadedLevels) {
                var level = entry.Value;
                foreach (PlayerBot e in level.Bots.Items) {
                    if (ModelInfo.GetRawModel(e.Model).CaselessEq(modelName)) {
                        Entities.UpdateModel(e, e.Model);
                    }
                }
            }
        }

        static void OnPlayerConnect(Player p) {
            SentCustomModels.Add(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ModelNameToIdForPlayer.Add(p.name, new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        }

        static void OnPlayerDisconnect(Player p, string reason) {
            SentCustomModels.Remove(p.name);
            ModelNameToIdForPlayer.Remove(p.name);

            Level prevLevel = p.level;
            if (prevLevel != null) {
                // tell other players still on the last map to remove our model
                // if we were the last one using that model
                foreach (Player e in prevLevel.getPlayers()) {
                    if (e == p) continue;
                    CheckAddRemove(e, prevLevel);
                }
            }
        }

        static void OnJoiningLevel(Player p, Level level, ref bool canJoin) {
            Level prevLevel = p.level;

            // send future/new model list to player
            CheckAddRemove(p, level);
        }

        static void OnJoinedLevel(Player p, Level prevLevel, Level level, ref bool announce) {
            if (prevLevel != null) {
                // tell other players still on the last map to remove our model
                // if we were the last one using that model
                foreach (Player e in prevLevel.getPlayers()) {
                    if (e == p) continue;
                    CheckAddRemove(e, prevLevel);
                }
            }
        }

        static void OnBeforeChangeModel(Player p, byte entityID, string modelName) {
            // UpdateSkinType(p);

            try {
                // use CheckAddRemove because we also want to remove the previous model,
                // if no one else is using it
                CheckAddRemove(p, p.level);
            } catch (Exception e) {
                Logger.Log(
                    LogType.Error,
                    "CustomModels OnBeforeChangeModel {0} {1}: {2}\n{3}",
                    p.name,
                    modelName,
                    e.Message,
                    e.StackTrace
                );
            }
        }


        //------------------------------------------------------------------ skin parsing

        // 32x64 (Steve) 64x64 (SteveLayers) 64x64 slim-arm (Alex)
        public enum SkinType { Steve, SteveLayers, Alex };
        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static bool IsAllColor(Color solid, Bitmap bmp, int x1, int y1, int width, int height, Player p) {
            int x, y;
            for (y = y1; y < y1 + height; y++) {
                for (x = x1; x < x1 + width; x++) {
                    //p.Message("x is %b{0}%S, y is %b{1}%S.", x, y);
                    Color col = bmp.GetPixel(x, y);
                    if (!col.Equals(solid)) {
                        //p.Message("It's not {0}, it's {1}!", solid, col);
                        return false;
                    }
                }
            }
            return true;
        }
        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static SkinType GetSkinType(Bitmap bmp, Player p) {
            Color col;
            int scale;
            if (bmp.Width == bmp.Height * 2) return SkinType.Steve;
            if (bmp.Width != bmp.Height) return SkinType.Steve;

            scale = bmp.Width / 64;
            // Minecraft alex skins have this particular pixel with alpha of 0
            col = bmp.GetPixel(54 * scale, 20 * scale);
            if (col.A < 128) { return SkinType.Alex; }
            Color black = Color.FromArgb(0, 0, 0);
            return IsAllColor(black, bmp, 54 * scale, 20 * scale, 2 * scale, 12 * scale, p)
                && IsAllColor(black, bmp, 50 * scale, 16 * scale, 2 * scale, 4 * scale, p) ? SkinType.Alex : SkinType.SteveLayers;
        }

        static Bitmap DecodeImage(byte[] data) {
            Bitmap bmp = null;
            try {
                bmp = new Bitmap(new MemoryStream(data));
                int width = bmp.Width;
                // sometimes Mono will return an invalid bitmap instance that throws ArgumentNullException,
                // so we make sure to check for that here rather than later.
                return bmp;
            } catch (Exception ex) {
                Logger.LogError("Error reading bitmap", ex);
                if (bmp != null) bmp.Dispose();
                return null;
            }
        }
        static Bitmap DownloadSkin(Player p) {
            string url = p.SkinName;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) {
                url = "http://www.classicube.net/static/skins/" + p.truename + ".png";
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) {
                    return null;
                }
            }

            byte[] data = null;
            try {
                using (WebClient client = HttpUtil.CreateWebClient()) {
                    p.Message("Downloading file from: &f" + url);
                    data = client.DownloadData(uri);
                }
                p.Message("Finished downloading.");
            } catch (Exception ex) {
                Logger.LogError("Error downloading", ex);
                return null;
            }

            return DecodeImage(data);
        }

        static void UpdateSkinTypeCore(Player p) {
            using (Bitmap skin = DownloadSkin(p)) {
                if (skin != null) {
                    p.Message("Skintype is %b{0}%S!", GetSkinType(skin, p));
                } else {
                    p.Message("Could not get your skin, assuming SkinType.Steve!");
                }
            }
        }

        static void UpdateSkinType(Player p) {
            Thread thread = new Thread(() => {
                try {
                    UpdateSkinTypeCore(p);
                } catch (Exception ex) {
                    // Do not want it taking down the whole server if error occurs
                    Logger.LogError("Error figuring out skin type", ex);
                }
            });
            thread.Name = "CustomModels_UpdatePlayerSkinType";
            thread.Start();
        }

        //------------------------------------------------------------------ commands

        class CmdCustomModel : Command2 {
            public override string name { get { return "CustomModel"; } }
            public override string shortcut { get { return "cm"; } }
            public override string type { get { return CommandTypes.Other; } }
            public override bool MessageBlockRestricted { get { return true; } }
            public override LevelPermission defaultRank { get { return LevelPermission.AdvBuilder; } }
            public override CommandPerm[] ExtraPerms {
                get {
                    return new[] {
                        new CommandPerm(LevelPermission.Operator, "can modify/upload public custom models."),
                    };
                }
            }
            public override CommandAlias[] Aliases {
                get {
                    return new[] {
                        new CommandAlias("MyCustomModel", "-own"),
                        new CommandAlias("MyCM", "-own"),
                    };
                }
            }

            public override void Help(Player p) {
                p.Message("%T/CustomModel list");
                p.Message("%H  List all public custom models.");

                p.Message("%T/CustomModel [-own/model name] upload [bbmodel url]");
                p.Message("%H  Upload a BlockBench file to use as your personal model.");

                p.Message("%T/CustomModel sit");
                p.Message("%H  Toggle sitting.");

                p.Message("%T/CustomModel [-own/model name] config [field] [value]");
                p.Message("%H  Configures options on your personal model.");
                p.Message("%H  See %T/Help CustomModel config fields %Hfor more details on [field]");
            }

            public override void Help(Player p, string message) {
                if (message.Trim() != "") {
                    var args = new List<string>(message.SplitSpaces());
                    if (args.Count >= 1) {
                        string subCommand = args.PopFront();
                        if (subCommand.CaselessEq("config")) {
                            if (args.Count >= 1) {
                                string subSubCommand = args.PopFront();
                                if (subSubCommand.CaselessEq("fields")) {
                                    var defaultStoredCustomModel = new StoredCustomModel("default");
                                    foreach (var entry in ModifiableFields) {
                                        var fieldName = entry.Key;
                                        var chatType = entry.Value;

                                        if (
                                            chatType.op &&
                                            !CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)
                                        ) {
                                            continue;
                                        }

                                        p.Message(
                                            "%Tconfig {0} {1}",
                                            fieldName,
                                            "[" + chatType.types.Join("] [") + "]"
                                        );
                                        p.Message(
                                            "%H  {0} %S(Default %T{1}%S)",
                                            chatType.desc,
                                            chatType.get.Invoke(defaultStoredCustomModel)
                                        );
                                    }
                                    return;
                                }
                            }
                        }
                    }
                }

                Help(p);
            }

            public override void Use(Player p, string message, CommandData data) {
                if (message.Trim() != "") {
                    var args = new List<string>(message.SplitSpaces());
                    if (args.Count >= 1) {
                        // /CustomModel [name]

                        string modelName = args.PopFront();
                        if (modelName.CaselessEq("list")) {
                            // /CustomModel list
                            List(p, null);
                            return;
                        } else if (modelName.CaselessEq("sit")) {
                            // /CustomModel [name] sit
                            Sit(p);
                            return;
                        } else if (modelName.CaselessEq("-own")) {
                            modelName = p.name;
                        } else {
                            string maybePlayerName = StoredCustomModel.GetPlayerName(modelName);
                            bool targettingSelf = maybePlayerName != null && maybePlayerName.CaselessEq(p.name);

                            if (!targettingSelf && !CheckExtraPerm(p, data, 1)) return;
                            if (!ValidModelName(p, modelName)) return;
                        }
                        modelName = Path.GetFileName(modelName);

                        if (args.Count >= 1) {
                            var subCommand = args.PopFront();
                            if (subCommand.CaselessEq("config")) {
                                // /CustomModel [name] config
                                Config(p, data, modelName, args);
                                return;
                            } else if (subCommand.CaselessEq("upload") && args.Count == 1) {
                                // /CustomModel [name] upload [url]
                                string url = args.PopFront();
                                Upload(p, modelName, url);
                                return;
                            } else if (subCommand.CaselessEq("list")) {
                                // /MyCustomModel list
                                List(p, StoredCustomModel.GetPlayerName(modelName));
                                return;
                            }
                        }
                    }
                }

                Help(p);
            }

            public static bool ValidModelName(Player p, string name) {
                if (Formatter.ValidName(p, name, "model")) {
                    if (name.Count(c => c == '+') <= 1) {
                        return true;
                    } else {
                        p.Message("\"{0}\" is not a valid model name.", name);
                    }
                }
                return false;
            }

            void Sit(Player p) {
                var storedModel = new StoredCustomModel(p.Model);
                if (!storedModel.Exists()) {
                    p.Message("%WYour current model isn't a Custom Model!");
                    return;
                }

                if (storedModel.modifiers.Contains("sitting")) {
                    storedModel.RemoveModifier("sitting");
                } else {
                    storedModel.AddModifier("sitting");
                }

                Entities.UpdateModel(p, storedModel.fullName);

                p.Message(
                   "%SNow %T{0}%S!",
                   storedModel.modifiers.Contains("sitting") ? "sitting" : "standing"
               );
            }

            class ChatType {
                public string[] types;
                public string desc;
                public Func<StoredCustomModel, string> get;
                // (model, p, input) => bool
                public Func<StoredCustomModel, Player, string[], bool> set;
                public bool op;

                public ChatType(
                    string[] types,
                    string desc,
                    Func<StoredCustomModel, string> get,
                    Func<StoredCustomModel, Player, string[], bool> set,
                    bool op = false
                ) {
                    this.types = types;
                    this.desc = desc;
                    this.get = get;
                    this.set = set;
                    this.op = op;
                }

                public ChatType(
                    string type,
                    string desc,
                    Func<StoredCustomModel, string> get,
                    Func<StoredCustomModel, Player, string, bool> set,
                    bool op = false
                ) : this(
                        new string[] { type },
                        desc,
                        get,
                        (model, p, inputs) => {
                            return set(model, p, inputs[0]);
                        },
                        op
                ) { }
            }

            static Dictionary<string, ChatType> ModifiableFields = new Dictionary<string, ChatType>(StringComparer.OrdinalIgnoreCase) {
                {
                    "nameY",
                    new ChatType(
                        "height",
                        "Name text height",
                        (model) => "" + model.nameY,
                        (model, p, input) => CommandParser.GetReal(p, input, "nameY", ref model.nameY)
                    )
                },
                {
                    "eyeY",
                    new ChatType(
                        "height",
                        "Eye position height",
                        (model) => "" + model.eyeY,
                        (model, p, input) => {
                            return CommandParser.GetReal(p, input, "eyeY", ref model.eyeY);
                        },
                        true
                    )
                },
                {
                    "collisionBounds",
                    new ChatType(
                        new string[] {"x", "y", "z"},
                        "How big you are",
                        (model) => {
                            return string.Format(
                                "({0}, {1}, {2})",
                                model.collisionBounds.X,
                                model.collisionBounds.Y,
                                model.collisionBounds.Z
                            );
                        },
                        (model, p, input) => {
                            if (!CommandParser.GetReal(p, input[0], "x", ref model.collisionBounds.X)) return false;
                            if (!CommandParser.GetReal(p, input[1], "y", ref model.collisionBounds.Y)) return false;
                            if (!CommandParser.GetReal(p, input[2], "z", ref model.collisionBounds.Z)) return false;
                            return true;
                        },
                        true
                    )
                },
                {
                    "pickingBounds",
                    new ChatType(
                        new string[] {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"},
                        "Hitbox coordinates",
                        (model) => {
                            return string.Format(
                                "from ({0}, {1}, {2}) to ({3}, {4}, {5})",
                                model.pickingBoundsMin.X,
                                model.pickingBoundsMin.Y,
                                model.pickingBoundsMin.Z,
                                model.pickingBoundsMax.X,
                                model.pickingBoundsMax.Y,
                                model.pickingBoundsMax.Z
                            );
                        },
                        (model, p, input) => {
                            if (!CommandParser.GetReal(p, input[0], "minX", ref model.pickingBoundsMin.X)) return false;
                            if (!CommandParser.GetReal(p, input[1], "minY", ref model.pickingBoundsMin.Y)) return false;
                            if (!CommandParser.GetReal(p, input[2], "minZ", ref model.pickingBoundsMin.Z)) return false;
                            if (!CommandParser.GetReal(p, input[3], "maxX", ref model.pickingBoundsMax.X)) return false;
                            if (!CommandParser.GetReal(p, input[4], "maxY", ref model.pickingBoundsMax.Y)) return false;
                            if (!CommandParser.GetReal(p, input[5], "maxZ", ref model.pickingBoundsMax.Z)) return false;
                            return true;
                        },
                        true
                    )
                },
                {
                    "bobbing",
                    new ChatType(
                        "bool",
                        "Third person bobbing animation",
                        (model) => model.bobbing.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.bobbing)
                    )
                },
                {
                    "pushes",
                    new ChatType(
                        "bool",
                        "Push other players",
                        (model) => model.pushes.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.pushes)
                    )
                },
                {
                    "usesHumanSkin",
                    new ChatType(
                        "bool",
                        "Fall back to using entity name for skin",
                        (model) => model.usesHumanSkin.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.usesHumanSkin)
                    )
                },
                {
                    "calcHumanAnims",
                    new ChatType(
                        "bool",
                        "Use Crazy Arms",
                        (model) => model.calcHumanAnims.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.calcHumanAnims)
                    )
                },
            };

            void Config(Player p, CommandData data, string modelName, List<string> args) {
                StoredCustomModel storedCustomModel = new StoredCustomModel(modelName);
                if (!storedCustomModel.Exists()) {
                    p.Message("%WCustom Model %S{0} %Wnot found!", modelName);
                    return;
                }

                storedCustomModel.LoadFromFile();

                if (args.Count == 0) {
                    // /CustomModel [name] config
                    foreach (var entry in ModifiableFields) {
                        var fieldName = entry.Key;
                        var chatType = entry.Value;
                        if (
                            chatType.op &&
                            !CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)
                        ) {
                            continue;
                        }

                        p.Message(
                            "{0} = %T{1}",
                            fieldName,
                            chatType.get.Invoke(storedCustomModel)
                        );
                    }
                    return;
                }

                if (args.Count >= 1) {
                    // /CustomModel [name] config [field]
                    // or
                    // /CustomModel [name] config [field] [value]
                    var fieldName = args.PopFront();
                    if (!ModifiableFields.ContainsKey(fieldName)) {
                        p.Message(
                            "%WNo such field %S{0}!",
                            fieldName
                        );
                        return;
                    }

                    var chatType = ModifiableFields[fieldName];
                    if (args.Count == 0) {
                        // /CustomModel [name] config [field]
                        p.Message(
                            "{0} = %T{1}",
                            fieldName,
                            chatType.get.Invoke(storedCustomModel)
                        );
                        return;
                    } else {
                        // /CustomModel config [field] [value]...
                        var values = args.ToArray();
                        if (values.Length != chatType.types.Length) {
                            p.Message(
                                "%WNot enough values for setting field %S{0}",
                                fieldName
                            );
                        } else {
                            if (chatType.op && !CheckExtraPerm(p, data, 1)) return;

                            if (chatType.set.Invoke(storedCustomModel, p, values)) {
                                // field was set, update file!
                                p.Message("%TField %S{0} %Tset!", fieldName);

                                storedCustomModel.WriteToFile();
                                CheckUpdateAll(modelName);
                            }
                        }
                    }
                }
            }
            //measured in pixels where 16 pixels = 1 block's length
            public const float maxWidth = 16;
            public const float maxHeight = 32;
			// graceLength is how far (in pixels) you can extend past max width/height on all sides
            static bool SizeAllowed(Vec3F32 boxCorner, float graceLength) {
                //convert to block-unit to match boxCorner
                const float maxWidthB = maxWidth / 16f;
                const float maxHeightB = maxHeight / 16f;
                float graceLengthB = graceLength / 16f;
                if (
                    boxCorner.Y < -graceLengthB ||
                    boxCorner.Y > maxHeightB + graceLengthB ||

                    boxCorner.X < -((maxWidthB / 2) + graceLengthB) ||
                    boxCorner.X > (maxWidthB / 2) + graceLengthB ||
                    boxCorner.Z < -((maxWidthB / 2) + graceLengthB) ||
                    boxCorner.Z > (maxWidthB / 2) + graceLengthB
                ) {
                    return false;
                }
                return true;
            }
            void Upload(Player p, string modelName, string url) {
                var bytes = HttpUtil.DownloadData(url, p);
                if (bytes != null) {
                    string json = System.Text.Encoding.UTF8.GetString(bytes);

                    // try parsing now so that we throw and don't save the invalid file
                    // and notify the user of the error
                    var parts = BlockBench.Parse(json).ToCustomModelParts();
                    if (parts.Length > Packet.MaxCustomModelParts) {
                        p.Message(
                            "%WNumber of model parts ({0}) exceeds max of {1}!",
                            parts.Length,
                            Packet.MaxCustomModelParts
                        );
                        return;
                    }

                    //only do size check if they can't upload global models
                    if (!CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)) {
                        for (int i = 0; i < parts.Length; i++) {
							// Models can be 1 block bigger if they aren't a purely personal model
							bool purePersonal = modelName.EndsWith("+");
							float graceLength = purePersonal ? 8.0f : 16.0f;
							
                            if (!SizeAllowed(parts[i].min, graceLength) || !SizeAllowed(parts[i].max, graceLength)) {
                                p.Message(
                                    "%WThe %b{0} cube in your list %Wis out of bounds.",
                                    ListicleNumber(i + 1)
                                );
                                p.Message(
                                    "%WYour {0} may not be larger than %b{1}%W pixels tall or %b{2}%W pixels wide.",
									purePersonal ? "personal model" : "model",
                                    maxHeight + graceLength,
                                    maxWidth + graceLength * 2,
									graceLength
                                );

								if (purePersonal) {
									p.Message("These limits only apply to your personal \"%b{0}%S\" model.", p.name.ToLower());
									p.Message("Models you upload with other names (e.g, /cm {0}bike upload) can be slightly larger.", p.name.ToLower());
								}
                                return;
                            }
                        }
                    }

                    var storedModel = new StoredCustomModel(modelName);
                    storedModel.WriteBBFile(json);

                    if (!storedModel.Exists()) {
                        // create a default ccmodel file if doesn't exist
                        storedModel.WriteToFile();
                    }

                    CheckUpdateAll(modelName);
                    p.Message(
                        "%TCustom Model %S{0} %Tupdated!",
                        modelName
                    );
                }
            }

            //shrug
            static string ListicleNumber(int n) {
                if (n > 3 && n < 21) { return n + "th"; }
                string suffix;
                switch (n % 10) {
                    case 1:
                        suffix = "st";
                        break;
                    case 2:
                        suffix = "nd";
                        break;
                    case 3:
                        suffix = "rd";
                        break;
                    default:
                        suffix = "th";
                        break;
                }
                return n + suffix;
            }

            void List(Player p, string playerName = null) {
                var folderPath = playerName == null
                    ? PublicModelsDirectory
                    : StoredCustomModel.GetFolderPath(playerName);
                var modelNames = new List<string>();
                foreach (var entry in new DirectoryInfo(folderPath).GetFiles()) {
                    string fileName = entry.Name;
                    if (Path.GetExtension(fileName).CaselessEq(CCModelExt)) {
                        string name = Path.GetFileNameWithoutExtension(fileName);
                        modelNames.Add(name);
                    }
                }
                p.Message(
                    "{0} Custom Models: %T{1}",
                    playerName == null ? "%SPublic" : "%T" + playerName + "%S's",
                    modelNames.Join("%S, %T")
                );
            }
        }

        //------------------------------------------------------------------bbmodel json parsing

        class Vec3F32Converter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return objectType == typeof(Vec3F32);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                var obj = JObject.Load(reader);
                return new Vec3F32 {
                    X = (float)obj["X"],
                    Y = (float)obj["Y"],
                    Z = (float)obj["Z"]
                };
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                var vec = (Vec3F32)value;
                serializer.Serialize(
                    writer,
                    new {
                        X = vec.X,
                        Y = vec.Y,
                        Z = vec.Z
                    }
                );
            }
        }

        static JsonSerializerSettings jsonSettings = new JsonSerializerSettings {
            Converters = new[] { new Vec3F32Converter() }
        };

        // ignore "Field is never assigned to"
#pragma warning disable 0649
        class BlockBench {
            public class JsonRoot {
                public Meta meta;
                public string name;
                public Element[] elements;
                public UuidOrGroup[] outliner;
                public Resolution resolution;

                public string ToJson() {
                    return JsonConvert.SerializeObject(this);
                }

                public CustomModelPart[] ToCustomModelParts() {
                    var parts = new List<CustomModelPart>();

                    var elementByUuid = new Dictionary<string, Element>();
                    foreach (Element e in this.elements) {
                        elementByUuid.Add(e.uuid, e);

                    }

                    foreach (var uuidOrGroup in this.outliner) {
                        HandleGroup(
                            uuidOrGroup,
                            elementByUuid,
                            parts,
                            new[] { 0.0f, 0.0f, 0.0f },
                            new[] { 0.0f, 0.0f, 0.0f },
                            true
                        );
                    }

                    return parts.ToArray();
                }

                void HandleGroup(
                    UuidOrGroup uuidOrGroup,
                    Dictionary<string, Element> elementByUuid,
                    List<CustomModelPart> parts,
                    float[] rotation,
                    float[] origin,
                    bool visibility
                ) {
                    if (uuidOrGroup.group == null) {
                        // Logger.Log(LogType.Warning, "uuid " + uuidOrGroup.uuid);
                        var e = elementByUuid[uuidOrGroup.uuid];
                        e.rotation[0] += rotation[0];
                        e.rotation[1] += rotation[1];
                        e.rotation[2] += rotation[2];
                        e.origin[0] += origin[0];
                        e.origin[1] += origin[1];
                        e.origin[2] += origin[2];
                        if (!visibility) {
                            e.visibility = visibility;
                        }
                        // if (e.name.StartsWith("tail")) {
                        //     Logger.Log(LogType.Warning, "rotation " + e.rotation[0] + " " + e.rotation[1] + " " + e.rotation[2]);
                        //     Logger.Log(LogType.Warning, "origin " + e.origin[0] + " " + e.origin[1] + " " + e.origin[2]);
                        // }
                        var part = ToCustomModelPart(e);
                        if (part != null) {
                            parts.Add(part);
                        }
                    } else {
                        // Logger.Log(LogType.Warning, "group " + uuidOrGroup.group.uuid);
                        var innerRotation = new[] {
                            uuidOrGroup.group.rotation[0],
                            uuidOrGroup.group.rotation[1],
                            uuidOrGroup.group.rotation[2],
                        };
                        var innerOrigin = new[] {
                            uuidOrGroup.group.origin[0],
                            uuidOrGroup.group.origin[1],
                            uuidOrGroup.group.origin[2],
                        };
                        foreach (var innerGroup in uuidOrGroup.group.children) {
                            HandleGroup(
                                innerGroup,
                                elementByUuid,
                                parts,
                                rotation,
                                origin,
                                uuidOrGroup.group.visibility
                            );
                        }
                    }
                }

                CustomModelPart ToCustomModelPart(Element e) {
                    if (!e.visibility) {
                        return null;
                    }

                    Vec3F32 rotation = new Vec3F32 { X = 0, Y = 0, Z = 0 };
                    if (e.rotation != null) {
                        rotation.X = e.rotation[0];
                        rotation.Y = e.rotation[1];
                        rotation.Z = e.rotation[2];
                    }

                    Vec3F32 min = new Vec3F32 {
                        X = (e.from[0] - e.inflate) / 16.0f,
                        Y = (e.from[1] - e.inflate) / 16.0f,
                        Z = (e.from[2] - e.inflate) / 16.0f,
                    };
                    Vec3F32 max = new Vec3F32 {
                        X = (e.to[0] + e.inflate) / 16.0f,
                        Y = (e.to[1] + e.inflate) / 16.0f,
                        Z = (e.to[2] + e.inflate) / 16.0f,
                    };

                    var rotationOrigin = new Vec3F32 {
                        X = e.origin[0] / 16.0f,
                        Y = e.origin[1] / 16.0f,
                        Z = e.origin[2] / 16.0f,
                    };

                    // faces in order [u1, v1, u2, v2]
                    /* uv coords in order: top, bottom, front, back, left, right */
                    // swap up's uv's
                    UInt16[] u1 = new UInt16[] {
                        e.faces.up.uv[2],
                        e.faces.down.uv[0],
                        e.faces.north.uv[0],
                        e.faces.south.uv[0],
                        e.faces.east.uv[0],
                        e.faces.west.uv[0],
                    };
                    UInt16[] v1 = new[] {
                        e.faces.up.uv[3],
                        e.faces.down.uv[1],
                        e.faces.north.uv[1],
                        e.faces.south.uv[1],
                        e.faces.east.uv[1],
                        e.faces.west.uv[1],
                    };
                    UInt16[] u2 = new[] {
                        e.faces.up.uv[0],
                        e.faces.down.uv[2],
                        e.faces.north.uv[2],
                        e.faces.south.uv[2],
                        e.faces.east.uv[2],
                        e.faces.west.uv[2],
                    };
                    UInt16[] v2 = new[] {
                        e.faces.up.uv[1],
                        e.faces.down.uv[3],
                        e.faces.north.uv[3],
                        e.faces.south.uv[3],
                        e.faces.east.uv[3],
                        e.faces.west.uv[3],
                    };

                    var part = new CustomModelPart {
                        min = min,
                        max = max,
                        u1 = u2,
                        v1 = v1,
                        u2 = u1,
                        v2 = v2,
                        rotationOrigin = rotationOrigin,
                        rotation = rotation,
                        anim = CustomModelAnim.None,
                        fullbright = false,
                    };

                    var name = e.name.Replace(" ", "");
                    foreach (var attr in name.SplitComma()) {
                        float animModifier = 1.0f;
                        var colonSplit = attr.Split(':');
                        if (colonSplit.Length >= 2) {
                            animModifier = float.Parse(colonSplit[1]);
                        }

                        if (attr.CaselessStarts("head")) {
                            part.anim = CustomModelAnim.Head;
                        } else if (attr.CaselessStarts("leftleg")) {
                            part.anim = CustomModelAnim.LeftLeg;
                        } else if (attr.CaselessStarts("rightleg")) {
                            part.anim = CustomModelAnim.RightLeg;
                        } else if (attr.CaselessStarts("leftarm")) {
                            part.anim = CustomModelAnim.LeftArm;
                        } else if (attr.CaselessStarts("rightarm")) {
                            part.anim = CustomModelAnim.RightArm;
                        } else if (attr.CaselessStarts("spinxvelocity")) {
                            part.anim = CustomModelAnim.SpinXVelocity;
                        } else if (attr.CaselessStarts("spinyvelocity")) {
                            part.anim = CustomModelAnim.SpinYVelocity;
                        } else if (attr.CaselessStarts("spinzvelocity")) {
                            part.anim = CustomModelAnim.SpinZVelocity;
                        } else if (attr.CaselessStarts("spinx")) {
                            part.anim = CustomModelAnim.SpinX;
                        } else if (attr.CaselessStarts("spiny")) {
                            part.anim = CustomModelAnim.SpinY;
                        } else if (attr.CaselessStarts("spinz")) {
                            part.anim = CustomModelAnim.SpinZ;
                        } else if (attr.CaselessStarts("fullbright")) {
                            part.fullbright = true;
                        } else if (attr.CaselessStarts("hand")) {
                            part.firstPersonArm = true;
                        }

                        part.animModifier = animModifier;
                    }

                    return part;
                }

                public class Resolution {
                    public UInt16 width;
                    public UInt16 height;
                }
                public class Meta {
                    public bool box_uv;
                }
                public class Element {
                    public Element() {
                        this.rotation = new[] { 0.0f, 0.0f, 0.0f };
                        this.origin = new[] { 0.0f, 0.0f, 0.0f };
                        this.visibility = true;
                        this.inflate = 0.0f;
                    }

                    public string name;
                    // 3 numbers
                    public float[] from;
                    // 3 numbers
                    public float[] to;

                    public bool visibility;

                    // if set to 1, uses a default png with some colors on it,
                    // we will only support skin pngs, so maybe notify user?
                    public UInt16 autouv;

                    public float inflate;

                    // if false, mirroring is enabled
                    // if null, mirroring is disabled
                    public bool? shade;

                    // 3 numbers
                    public float[] rotation;

                    /// "Pivot Point"
                    // 3 numbers
                    public float[] origin;

                    public Faces faces;
                    public string uuid;
                }
                public class Faces {
                    public Face north;
                    public Face east;
                    public Face south;
                    public Face west;
                    public Face up;
                    public Face down;
                }
                public class Face {
                    public Face() {
                        this.rotation = 0;
                    }

                    // 4 numbers
                    public UInt16[] uv;
                    public UInt16? texture;
                    public UInt16 rotation;
                }
                public class UuidOrGroup {
                    public string uuid;
                    public OutlinerGroup group;
                }
                public class OutlinerGroup {
                    public OutlinerGroup() {
                        this.rotation = new[] { 0.0f, 0.0f, 0.0f };
                        this.origin = new[] { 0.0f, 0.0f, 0.0f };
                        this.visibility = true;
                    }
                    public string name;
                    public string uuid;

                    public bool visibility;

                    // 3 numbers
                    public float[] rotation;

                    /// "Pivot Point"
                    // 3 numbers
                    public float[] origin;

                    public UuidOrGroup[] children;
                }
                public class JsonUuidOrGroup : JsonConverter {
                    public override bool CanConvert(Type objectType) {
                        return objectType == typeof(UuidOrGroup);
                    }

                    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                        if (reader.TokenType == JsonToken.String) {
                            JValue jValue = new JValue(reader.Value);
                            return new UuidOrGroup {
                                uuid = (string)jValue,
                                group = null,
                            };
                        } else {
                            JObject jo = JObject.Load(reader);
                            var group = new OutlinerGroup { };
                            serializer.Populate(jo.CreateReader(), group);
                            return new UuidOrGroup {
                                uuid = null,
                                group = group,
                            };
                        }
                    }

                    public override bool CanWrite {
                        get { return false; }
                    }

                    public override void WriteJson(JsonWriter writer,
                        object value, JsonSerializer serializer) {
                        throw new NotImplementedException();
                    }
                }

            }

            static JsonSerializerSettings jsonSettings = new JsonSerializerSettings {
                Converters = new[] { new JsonRoot.JsonUuidOrGroup() }
            };

            public static JsonRoot Parse(string json) {
                JsonRoot m = JsonConvert.DeserializeObject<JsonRoot>(json, jsonSettings);
                return m;
            }
        } // class BlockBench
#pragma warning restore 0649
    } // class CustomModelsPlugin

    static class ListExtension {
        public static T PopFront<T>(this List<T> list) {
            T r = list[0];
            list.RemoveAt(0);
            return r;
        }
    }
} // namespace MCGalaxy
