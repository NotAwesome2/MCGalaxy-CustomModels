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
using System.Text.RegularExpressions;
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
            [JsonIgnore] public string modelName = null;
            [JsonIgnore] public HashSet<string> modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            [JsonIgnore] public float scale = 1.0f;
            // override filename when reading/writing
            [JsonIgnore] public string fileName = null;

            public float nameY = 32.5f;
            public float eyeY = 26.0f;
            public Vec3F32 collisionBounds = new Vec3F32 {
                X = 8.6f,
                Y = 28.1f,
                Z = 8.6f
            };
            public Vec3F32 pickingBoundsMin = new Vec3F32 {
                X = -8,
                Y = 0,
                Z = -4
            };
            public Vec3F32 pickingBoundsMax = new Vec3F32 {
                X = 8,
                Y = 32,
                Z = 4
            };
            public bool bobbing = true;
            public bool pushes = true;
            public bool usesHumanSkin = true;
            public bool calcHumanAnims = true;

            // json deserialize will call our (name, ...) constructor if no
            // 0 param () constructor exists
            public StoredCustomModel() { }

            public StoredCustomModel(string name, bool overrideFileName = false) {
                this.modelName = ModelInfo.GetRawModel(name);
                this.scale = ModelInfo.GetRawScale(name);

                var split = this.modelName.Split(new char[] { '(' }, StringSplitOptions.RemoveEmptyEntries);
                // "player+named", "aaa,bbbb)"
                if (split.Length == 2 && split[1].EndsWith(")")) {
                    if (overrideFileName || this.Exists()) {
                        // if "player+ (sit)" was a file, use that as override
                        this.fileName = this.modelName;
                    }
                    this.modelName = split[0];

                    // remove ")"
                    var attrs = split[1].Substring(0, split[1].Length - 1);
                    foreach (var attr in attrs.SplitComma()) {
                        if (attr.Trim() == "") continue;
                        this.modifiers.Add(attr);
                    }
                }
            }

            public string GetFullName() {
                var name = this.modelName;
                if (this.modifiers.Count > 0) {
                    var modifierNames = this.modifiers.ToArray();
                    Array.Sort(modifierNames);
                    name += "(" + modifierNames.Join(",") + ")";
                }
                return name;
            }

            public string GetFullNameWithScale() {
                var name = this.GetFullName();
                if (this.scale != 1.0f) {
                    name += "|" + this.scale;
                }
                return name;
            }

            public bool AddModifier(string modifier) {
                return this.modifiers.Add(modifier);
            }

            public bool RemoveModifier(string modifier) {
                return this.modifiers.Remove(modifier);
            }

            public bool IsPersonal() {
                return modelName.Contains("+");
            }

            public bool IsPersonalPrimary() {
                return modelName.EndsWith("+");
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

            BlockBench.JsonRoot ParseBlockBench() {
                string path = GetBBPath();
                string contentsBB = File.ReadAllText(path);
                var blockBench = BlockBench.Parse(contentsBB);
                return blockBench;
            }

            CustomModel ToCustomModel(BlockBench.JsonRoot blockBench) {
                // convert to block units
                var model = new CustomModel {
                    name = GetFullName(),
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
                Directory.CreateDirectory(Path.GetDirectoryName(path));
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
                    return PersonalModelsDirectory + Path.GetFileName(maybePlayerName.ToLower()) + "/";
                } else {
                    return PublicModelsDirectory;
                }
            }

            public string GetCCPath() {
                var modelName = this.fileName != null ? this.fileName : this.modelName;
                return GetFolderPath(modelName) + Path.GetFileName(modelName.ToLower()) + CCModelExt;
            }

            public string GetBBPath() {
                var modelName = this.fileName != null ? this.fileName : this.modelName;
                return GetFolderPath(modelName) + Path.GetFileName(modelName.ToLower()) + BBModelExt;
            }

            public void WriteBBFile(string json) {
                var path = GetBBPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
            }

            public void Delete() {
                File.Delete(GetCCPath());
                File.Delete(GetBBPath());
            }

            public void Undefine(Player p) {
                UndefineModel(p, GetFullName());
            }

            public void Define(Player p) {
                var blockBench = ParseBlockBench();
                var model = this.ToCustomModel(blockBench);
                var parts = new List<Part>(blockBench.ToParts());

                if (this.fileName == null) {
                    // only apply modifiers if we aren't a file override

                    if (this.modifiers.Contains("sit")) {
                        Part leg = null;
                        foreach (var part in parts) {
                            if (
                                part.anim == CustomModelAnim.LeftLeg ||
                                part.anim == CustomModelAnim.RightLeg
                            ) {
                                // rotate legs to point forward, pointed a little outwards
                                leg = part;
                                part.rotation.X = 90.0f;
                                part.rotation.Y = part.anim == CustomModelAnim.LeftLeg ? 5.0f : -5.0f;
                                part.rotation.Z = 0;
                                part.anim = CustomModelAnim.None;
                            }
                        }

                        if (leg != null) {
                            var legHeight = leg.max.Y - leg.min.Y;
                            var legForwardWidth = leg.max.Z - leg.min.Z;
                            // lower all parts by leg's Y height, up by the leg's width
                            var lower = legHeight - legForwardWidth / 2.0f;
                            foreach (var part in parts) {
                                part.min.Y -= lower;
                                part.max.Y -= lower;
                                part.rotationOrigin.Y -= lower;
                            }
                            model.eyeY -= lower;
                            model.nameY -= lower;
                        }
                    }

                    // our entity is using a steve model, convert from SteveLayers to Steve
                    if (this.modifiers.Contains("steve")) {
                        // remove layer parts
                        parts = parts.Where(part => !part.layer).ToList();

                        // halve all uv "y coord"/v
                        foreach (var part in parts) {
                            part.v1[0] *= 2;
                            part.v1[1] *= 2;
                            part.v1[2] *= 2;
                            part.v1[3] *= 2;
                            part.v1[4] *= 2;
                            part.v1[5] *= 2;

                            part.v2[0] *= 2;
                            part.v2[1] *= 2;
                            part.v2[2] *= 2;
                            part.v2[3] *= 2;
                            part.v2[4] *= 2;
                            part.v2[5] *= 2;
                        }

                        Action<Part, Part> f = (left, right) => {
                            // there's only 1 leg/arm in the steve model
                            left.u1 = (ushort[])right.u1.Clone();
                            left.u2 = (ushort[])right.u2.Clone();
                            left.v1 = (ushort[])right.v1.Clone();
                            left.v2 = (ushort[])right.v2.Clone();

                            // swap u's
                            Swap(ref left.u1, ref left.u2);

                            /* uv coords in order: top, bottom, front, back, left, right */
                            // swap west and east
                            Swap(ref left.u1[4], ref left.u1[5]);
                            Swap(ref left.u2[4], ref left.u2[5]);
                            Swap(ref left.v1[4], ref left.v1[5]);
                            Swap(ref left.v2[4], ref left.v2[5]);
                        };

                        Part leftArm = parts.Find(part => part.skinLeftArm);
                        Part rightArm = parts.Find(part => part.skinRightArm);
                        if (leftArm != null && rightArm != null) {
                            f(leftArm, rightArm);
                        }

                        Part leftLeg = parts.Find(part => part.skinLeftLeg);
                        Part rightLeg = parts.Find(part => part.skinRightLeg);
                        if (leftLeg != null && rightLeg != null) {
                            f(leftLeg, rightLeg);
                        }
                    }

                    if (this.modifiers.Contains("alex")) {
                        foreach (var part in parts) {
                            if (part.skinLeftArm || part.skinRightArm) {
                                // top
                                part.u1[0] -= 1;

                                // down
                                part.u1[1] -= 1;
                                part.u2[1] -= 2;

                                // north
                                part.u1[2] -= 1;

                                // south
                                part.u1[3] -= 2;
                                part.u2[3] -= 1;

                                // east
                                part.u1[5] -= 1;
                                part.u2[5] -= 1;
                            }
                            if (part.skinLeftArm) {
                                part.min.X += 1.0f / 16.0f;
                            } else if (part.skinRightArm) {
                                part.max.X -= 1.0f / 16.0f;
                            }
                        }
                    }
                }

                DefineModel(p, model, parts.ToArray());
            }
        }

        class Part : CustomModelPart {
            public bool layer = false;
            public bool skinLeftArm = false;
            public bool skinRightArm = false;
            public bool skinLeftLeg = false;
            public bool skinRightLeg = false;
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
                var defaultModel = new StoredCustomModel(modelName, true);
                if (!defaultModel.Exists()) {
                    defaultModel.WriteToFile();

                    Logger.Log(
                        LogType.SystemActivity,
                        "CustomModels: Created a new default template for \"{0}\" in {1}",
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
            // Logger.Log(LogType.SystemActivity, "DefineModel {0} {1}", p.name, model.name);
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
            // Logger.Log(LogType.SystemActivity, "UndefineModel {0} {1}", p.name, name);
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

            OnPlayerCommandEvent.Register(OnPlayerCommand, Priority.Low);

            Directory.CreateDirectory(PublicModelsDirectory);
            Directory.CreateDirectory(PersonalModelsDirectory);

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
                        storedModel.Define(p);
                        sentModels.Add(modelName);
                    }
                }
            }
        }

        static void CheckRemoveModel(Player p, string modelName) {
            lock (SentCustomModels) {
                var sentModels = SentCustomModels[p.name];
                if (sentModels.Contains(modelName)) {
                    var storedModel = new StoredCustomModel(modelName);
                    if (storedModel.Exists()) {
                        storedModel.Undefine(p);
                        sentModels.Remove(modelName);
                    }
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

            // TODO if someone does layers(c,b,a) it will DefineModel layers(a,b,c)
            // and they won't see the change
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

        static void OnPlayerCommand(Player p, string cmd, string args, CommandData data) {
            if (cmd.CaselessEq("skin")) {
                // p.SkinName
                // Logger.Log(LogType.Warning, "skin {0}", p.name);
            }
        }


        //------------------------------------------------------------------ skin parsing

        // 32x64 (Steve) 64x64 (SteveLayers) 64x64 slim-arm (Alex)
        public enum SkinType { Steve, SteveLayers, Alex };
        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static bool IsAllColor(Color solid, Bitmap bmp, int x1, int y1, int width, int height) {
            int x, y;
            for (y = y1; y < y1 + height; y++) {
                for (x = x1; x < x1 + width; x++) {
                    //e.Message("x is %b{0}%S, y is %b{1}%S.", x, y);
                    Color col = bmp.GetPixel(x, y);
                    if (!col.Equals(solid)) {
                        //e.Message("It's not {0}, it's {1}!", solid, col);
                        return false;
                    }
                }
            }
            return true;
        }

        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static SkinType GetSkinType(Bitmap bmp) {
            Color col;
            int scale;
            if (bmp.Width == bmp.Height * 2) return SkinType.Steve;
            if (bmp.Width != bmp.Height) return SkinType.SteveLayers;

            scale = bmp.Width / 64;
            // Minecraft alex skins have this particular pixel with alpha of 0
            col = bmp.GetPixel(54 * scale, 20 * scale);
            if (col.A < 128) { return SkinType.Alex; }
            Color black = Color.FromArgb(0, 0, 0);
            return IsAllColor(black, bmp, 54 * scale, 20 * scale, 2 * scale, 12 * scale)
                && IsAllColor(black, bmp, 50 * scale, 16 * scale, 2 * scale, 4 * scale) ? SkinType.Alex : SkinType.SteveLayers;
        }

        static Bitmap FetchBitmap(Uri uri) {
            using (WebClient client = HttpUtil.CreateWebClient()) {
                var data = client.DownloadData(uri);
                return new Bitmap(new MemoryStream(data));
            }
        }

        static Uri GetSkinUrl(string skinName) {
            Uri uri;
            if (Uri.TryCreate(skinName, UriKind.Absolute, out uri)) {
                return uri;
            }

            if (Uri.TryCreate("http://www.classicube.net/static/skins/" + skinName + ".png", UriKind.Absolute, out uri)) {
                return uri;
            }

            throw new Exception("couldn't convert " + skinName + " to a Uri");
        }

        static SkinType GetSkinType(string skinName) {
            var uri = GetSkinUrl(skinName);

            using (Bitmap bmp = FetchBitmap(uri)) {
                return GetSkinType(bmp);
            }
        }

        static void GetSkinTypeAndUpdateModel(Entity e) {
            var skinType = GetSkinType(e.SkinName);
            Logger.Log(LogType.Warning, "Skintype is %b{0}%S!", skinType);

            var storedModel = new StoredCustomModel(e.Model);
            if (!storedModel.Exists()) {
                Logger.Log(LogType.Warning, "%WYour current model isn't a Custom Model!");
                return;
            }

            storedModel.RemoveModifier("steve");
            storedModel.RemoveModifier("alex");

            if (skinType == SkinType.Steve) {
                storedModel.AddModifier("steve");
            } else if (skinType == SkinType.Alex) {
                storedModel.AddModifier("alex");
            }

            var name = storedModel.GetFullNameWithScale();
            if (!e.Model.CaselessEq(name)) {
                // e.HandleCommand("XModel", name, e.DefaultCmdData);
                Entities.UpdateModel(e, name);
            }
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

            public override void Help(Player p) {
                p.Message("%T/CustomModel sit");
                p.Message("%H  Toggle sitting on your worn custom model.");

                p.Message("%T/CustomModel list [-own]");
                p.Message("%H  List all public or personal custom models.");

                p.Message("%T/CustomModel upload [model name] [bbmodel url]");
                p.Message("%H  Upload a BlockBench file to use as your personal model.");

                p.Message("%T/CustomModel delete [model name]");
                p.Message("%H  Delete a model.");

                p.Message("%T/CustomModel config [model name] [field] [value]");
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
                                        var modelField = entry.Value;

                                        if (!modelField.CanEdit(p)) {
                                            continue;
                                        }

                                        p.Message(
                                            "%Tconfig {0} {1}",
                                            fieldName,
                                            "[" + modelField.types.Join("] [") + "]"
                                        );
                                        p.Message(
                                            "%H  {0} %S(Default %T{1}%S)",
                                            modelField.desc,
                                            modelField.get.Invoke(defaultStoredCustomModel)
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
                var args = new List<string>(message.Trim().SplitSpaces());
                if (args.Count >= 1) {
                    string subCommand = args.PopFront();

                    if (args.Count == 0) {
                        if (subCommand.CaselessEq("sit")) {
                            // /CustomModel sit
                            Sit(p, data);
                            return;
                        } else if (subCommand.CaselessEq("list")) {
                            // /CustomModel list
                            List(p, null);
                            return;
                            // } else if (subCommand.CaselessEq("fixskin")) {
                            //     UpdateSkinType(p);
                            //     return;
                        }
                    } else if (args.Count >= 1) {
                        var modelName = TargetModelName(p, data, args.PopFront());
                        if (modelName == null) return;

                        if (subCommand.CaselessEq("list") && args.Count == 0) {
                            // /CustomModel list [name]
                            List(p, StoredCustomModel.GetPlayerName(modelName));
                            return;
                        } else if (subCommand.CaselessEq("config")) {
                            // /CustomModel config [name] [field] [values...]
                            Config(p, data, modelName, args);
                            return;
                        } else if (subCommand.CaselessEq("upload") && args.Count == 1) {
                            // /CustomModel upload [name] [url]
                            string url = args.PopFront();
                            Upload(p, modelName, url);
                            return;
                        } else if (subCommand.CaselessEq("delete") && args.Count == 0) {
                            // /CustomModel delete [name]
                            Delete(p, modelName);
                            return;
                        }
                    }
                }

                Help(p);
            }

            private string TargetModelName(Player p, CommandData data, string arg) {
                if (arg.CaselessEq("-own")) {
                    arg = p.name;
                }

                if (!ValidModelName(p, arg)) return null;

                string maybePlayerName = StoredCustomModel.GetPlayerName(arg);
                bool targettingSelf = maybePlayerName != null && maybePlayerName.CaselessEq(p.name);

                // if you aren't targetting your own models,
                // and you aren't admin, denied
                if (!targettingSelf && !CheckExtraPerm(p, data, 1)) return null;

                return Path.GetFileName(arg);
            }

            private static readonly Regex regex = new Regex("^[\\w\\.]+\\+?\\w*[\\w\\(\\,\\)]*$");
            public static bool ValidModelName(Player p, string name) {
                if (regex.IsMatch(name)) {
                    return true;
                } else {
                    p.Message("\"{0}\" is not a valid model name.", name);
                    return false;
                }
            }

            void Sit(Player p, CommandData data) {
                var storedModel = new StoredCustomModel(p.Model);
                if (!storedModel.Exists()) {
                    p.Message("%WYour current model isn't a Custom Model!");
                    return;
                }

                if (storedModel.modifiers.Contains("sit")) {
                    storedModel.RemoveModifier("sit");
                } else {
                    storedModel.AddModifier("sit");
                }

                p.HandleCommand("XModel", storedModel.GetFullNameWithScale(), data);
            }

            class ModelField {
                public string[] types;
                public string desc;
                public Func<StoredCustomModel, string> get;
                // (model, p, input) => bool
                public Func<StoredCustomModel, Player, string[], bool> set;
                // other config fields
                public bool extra;

                public ModelField(
                    string[] types,
                    string desc,
                    Func<StoredCustomModel, string> get,
                    Func<StoredCustomModel, Player, string[], bool> set,
                    bool extra = false
                ) {
                    this.types = types;
                    this.desc = desc;
                    this.get = get;
                    this.set = set;
                    this.extra = extra;
                }

                public ModelField(
                    string type,
                    string desc,
                    Func<StoredCustomModel, string> get,
                    Func<StoredCustomModel, Player, string, bool> set,
                    bool extra = false
                ) : this(
                        new string[] { type },
                        desc,
                        get,
                        (model, p, inputs) => {
                            return set(model, p, inputs[0]);
                        },
                        extra
                ) { }

                // this doesn't check access for player vs model,
                // only checks if they can edit a certain field on this model
                public bool CanEdit(Player p, string modelName = null) {
                    if (this.extra) {
                        // you can edit these if you're op,
                        if (CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)) {
                            return true;
                        } else {
                            // or if it's not a primary personal model
                            if (modelName != null && new StoredCustomModel(modelName, true).IsPersonalPrimary()) {
                                // is a primary personal model
                                return false;
                            } else {
                                return true;
                            }
                        }
                    } else {
                        return true;
                    }
                }
            }

            static Dictionary<string, ModelField> ModifiableFields = new Dictionary<string, ModelField>(StringComparer.OrdinalIgnoreCase) {
                {
                    "nameY",
                    new ModelField(
                        "height",
                        "Name text height",
                        (model) => "" + model.nameY,
                        (model, p, input) => CommandParser.GetReal(p, input, "nameY", ref model.nameY)
                    )
                },
                {
                    "eyeY",
                    new ModelField(
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
                    new ModelField(
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
                    new ModelField(
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
                    new ModelField(
                        "bool",
                        "Third person bobbing animation",
                        (model) => model.bobbing.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.bobbing)
                    )
                },
                {
                    "pushes",
                    new ModelField(
                        "bool",
                        "Push other players",
                        (model) => model.pushes.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.pushes)
                    )
                },
                {
                    "usesHumanSkin",
                    new ModelField(
                        "bool",
                        "Fall back to using entity name for skin",
                        (model) => model.usesHumanSkin.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.usesHumanSkin)
                    )
                },
                {
                    "calcHumanAnims",
                    new ModelField(
                        "bool",
                        "Use Crazy Arms",
                        (model) => model.calcHumanAnims.ToString(),
                        (model, p, input) => CommandParser.GetBool(p, input, ref model.calcHumanAnims)
                    )
                },
            };

            void Config(Player p, CommandData data, string modelName, List<string> args) {
                StoredCustomModel storedCustomModel = new StoredCustomModel(modelName, true);
                if (!storedCustomModel.Exists()) {
                    p.Message("%WCustom Model %S{0} %Wnot found!", modelName);
                    return;
                }

                storedCustomModel.LoadFromFile();

                if (args.Count == 0) {
                    // /CustomModel [name] config
                    foreach (var entry in ModifiableFields) {
                        var fieldName = entry.Key;
                        var modelField = entry.Value;
                        if (!modelField.CanEdit(p, modelName)) {
                            continue;
                        }

                        p.Message(
                            "{0} = %T{1}",
                            fieldName,
                            modelField.get.Invoke(storedCustomModel)
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

                    var modelField = ModifiableFields[fieldName];
                    if (args.Count == 0) {
                        // /CustomModel [name] config [field]
                        p.Message(
                            "{0} = %T{1}",
                            fieldName,
                            modelField.get.Invoke(storedCustomModel)
                        );
                        return;
                    } else {
                        // /CustomModel config [field] [value]...
                        var values = args.ToArray();
                        if (values.Length != modelField.types.Length) {
                            p.Message(
                                "%WNot enough values for setting field %S{0}",
                                fieldName
                            );
                        } else {
                            if (!modelField.CanEdit(p, modelName)) {
                                p.Message("%WYou can't edit this field on a primary personal model!");
                                return;
                            }

                            if (modelField.set.Invoke(storedCustomModel, p, values)) {
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
                    var parts = BlockBench.Parse(json).ToParts();
                    if (parts.Length > Packet.MaxCustomModelParts) {
                        p.Message(
                            "%WNumber of model parts ({0}) exceeds max of {1}!",
                            parts.Length,
                            Packet.MaxCustomModelParts
                        );
                        return;
                    }

                    // override filename because file might not exist yet
                    var storedModel = new StoredCustomModel(modelName, true);

                    //only do size check if they can't upload global models
                    if (!CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)) {
                        for (int i = 0; i < parts.Length; i++) {
                            // Models can be 1 block bigger if they aren't a purely personal model
                            bool purePersonal = storedModel.IsPersonalPrimary();
                            float graceLength = purePersonal ? 8.0f : 16.0f;

                            if (
                                !SizeAllowed(parts[i].min, graceLength) ||
                                !SizeAllowed(parts[i].max, graceLength)
                            ) {
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

            void Delete(Player p, string modelName) {
                StoredCustomModel storedCustomModel = new StoredCustomModel(modelName, true);
                if (!storedCustomModel.Exists()) {
                    p.Message("%WCustom Model %S{0} %Wnot found!", modelName);
                    return;
                }
                storedCustomModel.Delete();
                p.Message("%TCustom Model %S{0} %Wdeleted!", modelName);
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

                public Part[] ToParts() {
                    var parts = new List<Part>();

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
                    List<Part> parts,
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
                        var part = ToPart(e);
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

                Part ToPart(Element e) {
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

                    var part = new Part {
                        min = min,
                        max = max,
                        u1 = u2,
                        v1 = v1,
                        u2 = u1,
                        v2 = v2,
                        rotationOrigin = rotationOrigin,
                        rotation = rotation,
                    };

                    var name = e.name.Replace(" ", "");
                    foreach (var attr in name.SplitComma()) {
                        var colonSplit = attr.Split(':');
                        if (colonSplit.Length >= 2) {
                            part.animModifier = float.Parse(colonSplit[1]);
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
                        } else if (attr.CaselessStarts("layer")) {
                            part.layer = true;
                        } else if (attr.CaselessStarts("humanleftarm")) {
                            part.skinLeftArm = true;
                        } else if (attr.CaselessStarts("humanrightarm")) {
                            part.skinRightArm = true;
                        } else if (attr.CaselessStarts("humanleftleg")) {
                            part.skinLeftLeg = true;
                        } else if (attr.CaselessStarts("humanrightleg")) {
                            part.skinRightLeg = true;
                        }
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

        static void Swap<T>(ref T lhs, ref T rhs) {
            T temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        class Memoizer1<IN, OUT> {
            private Func<IN, OUT> fetch;
            private TimeSpan? cacheLifeTime;

            public Memoizer1(Func<IN, OUT> fetch, TimeSpan? cacheLifeTime = null) {
                this.fetch = fetch;
                this.cacheLifeTime = cacheLifeTime;
            }

            private struct CacheEntry {
                public OUT value;
                public DateTime dieTime;
            }
            private Dictionary<IN, CacheEntry> cache = new Dictionary<IN, CacheEntry>();
            private Dictionary<IN, object> lockHandles = new Dictionary<IN, object>();
            private static object masterLockHandle = new object();
            public OUT Get(IN key) {

                object lockHandle;
                lock (masterLockHandle) {
                    if (!lockHandles.TryGetValue(key, out lockHandle)) {
                        lockHandle = new object();
                        lockHandles.Add(key, lockHandle);
                    }
                }

                lock (lockHandle) {
                    CacheEntry entry;
                    if (cache.TryGetValue(key, out entry)) {
                        if (cacheLifeTime.HasValue) {
                            if (entry.dieTime <= DateTime.UtcNow) {
                                return entry.value;
                            }
                        } else {
                            return entry.value;
                        }
                    }

                    OUT value = Fetch(key);
                    entry.value = value;
                    if (cacheLifeTime.HasValue) {
                        entry.dieTime = DateTime.UtcNow + cacheLifeTime.Value;
                    }
                    cache.Add(key, entry);

                    return value;
                }
            }

            public OUT Fetch(IN key) {
                return this.fetch.Invoke(key);
            }
        }


    } // class CustomModelsPlugin

    static class ListExtension {
        public static T PopFront<T>(this List<T> list) {
            T r = list[0];
            list.RemoveAt(0);
            return r;
        }
    }

} // namespace MCGalaxy
