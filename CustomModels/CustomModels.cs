using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MCGalaxy.Commands;
using MCGalaxy.Events.EntityEvents;
using MCGalaxy.Events.PlayerEvents;
using MCGalaxy.Maths;
using MCGalaxy.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCGalaxy {
    public sealed class CustomModelsPlugin : Plugin {
        public override string name => "CustomModels";
        public override string MCGalaxy_Version => "1.9.2.2";
        public override string creator => "SpiralP & Goodly";

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
            public string defaultSkin = null;

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

            public void ApplySkinType(SkinType skinType) {
                this.RemoveModifier("steve");
                this.RemoveModifier("alex");

                if (skinType == SkinType.Steve) {
                    this.AddModifier("steve");
                } else if (skinType == SkinType.Alex) {
                    this.AddModifier("alex");
                }
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
                var jsonRoot = BlockBench.Parse(contentsBB);
                return jsonRoot;
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
                this.defaultSkin = o.defaultSkin;
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

            public string GetModelFileName() {
                return (this.fileName != null ? this.fileName : this.modelName).ToLower();
            }

            public string GetCCPath() {
                var modelName = GetModelFileName();
                return GetFolderPath(modelName) + Path.GetFileName(modelName) + CCModelExt;
            }

            public string GetBBPath() {
                var modelName = GetModelFileName();
                return GetFolderPath(modelName) + Path.GetFileName(modelName) + BBModelExt;
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

            public bool UsesHumanParts(List<Part> parts = null) {
                if (parts == null) {
                    var blockBench = ParseBlockBench();
                    parts = new List<Part>(blockBench.ToParts());
                }

                return parts.Find(part =>
                    part.skinLeftArm ||
                    part.skinRightArm ||
                    part.skinLeftLeg ||
                    part.skinRightLeg
                ) != null;
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
                            foreach (var anim in part.anims) {
                                if (
                                    anim.type == CustomModelAnimType.LeftLegX ||
                                    anim.type == CustomModelAnimType.RightLegX
                                ) {
                                    // rotate legs to point forward, pointed a little outwards
                                    leg = part;
                                    part.rotation.X = 90.0f;
                                    part.rotation.Y = anim.type == CustomModelAnimType.LeftLegX ? 5.0f : -5.0f;
                                    part.rotation.Z = 0;
                                    anim.type = CustomModelAnimType.None;
                                }
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

                                if (part.firstPersonArm) {
                                    // remove first person arm because offset changed
                                    part.firstPersonArm = false;
                                }
                            }
                            model.eyeY -= lower;
                            model.nameY -= lower;
                        }
                    }



                    if (UsesHumanParts(parts)) {
                        if (this.modifiers.Contains("alex")) {
                            // our entity is using an alex skin, convert model from SteveLayers to Alex
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
                        } else if (this.modifiers.Contains("steve")) {
                            // our entity is using a steve skin, convert from SteveLayers to Steve

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
                    }
                }

                DefineModel(p, model, parts.Select(part => part.ToCustomModelPart()).ToArray());
            }
        }

        class Part : CustomModelPart {
            public bool layer = false;
            public bool skinLeftArm = false;
            public bool skinRightArm = false;
            public bool skinLeftLeg = false;
            public bool skinRightLeg = false;

            public CustomModelPart ToCustomModelPart() {
                var fixedAnims = this.anims.Take(Packet.MaxCustomModelAnims).ToList();
                for (int i = fixedAnims.Count; i < Packet.MaxCustomModelAnims; i++) {
                    fixedAnims.Add(new CustomModelAnim {
                        type = CustomModelAnimType.None
                    });
                }
                this.anims = fixedAnims.ToArray();

                return this;
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

        static ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> ModelNameToIdForPlayer =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

        static byte? GetModelId(Player p, string name, bool addNew = false) {
            lock (ModelNameToIdForPlayer) {
                var modelNameToId = ModelNameToIdForPlayer[p.name];
                byte value;
                if (modelNameToId.TryGetValue(name, out value)) {
                    return value;
                } else {
                    if (addNew) {
                        for (int i = 0; i < Packet.MaxCustomModels; i++) {
                            byte id = (byte)i;
                            if (!modelNameToId.Values.Contains(id)) {
                                modelNameToId.TryAdd(name, id);
                                return id;
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
            bool hasV1 = p.Supports(CpeExt.CustomModels, 1);
            bool hasV2 = p.Supports(CpeExt.CustomModels, 2);
            if (hasV1 || hasV2) {

                var modelId = GetModelId(p, model.name, true).Value;
                Debug("DefineModel {0} {1} {2}", modelId, p.name, model.name);

                model.partCount = (byte)parts.Length;
                byte[] modelPacket = Packet.DefineModel(modelId, model);
                p.Send(modelPacket);

                foreach (var part in parts) {
                    if (hasV2) {
                        p.Send(Packet.DefineModelPartV2(modelId, part));
                    } else if (hasV1) {
                        p.Send(Packet.DefineModelPart(modelId, part));
                    }
                }
            }
        }

        static void UndefineModel(Player p, string name) {
            lock (ModelNameToIdForPlayer) {
                bool hasV1 = p.Supports(CpeExt.CustomModels, 1);
                bool hasV2 = p.Supports(CpeExt.CustomModels, 2);
                if (hasV1 || hasV2) {
                    var modelId = GetModelId(p, name).Value;
                    Debug("UndefineModel {0} {1} {2}", modelId, p.name, name);

                    byte[] modelPacket = Packet.UndefineModel(modelId);
                    p.Send(modelPacket);

                    var modelNameToId = ModelNameToIdForPlayer[p.name];
                    modelNameToId.TryRemove(name, out _);
                }
            }
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
            OnSendingModelEvent.Register(OnSendingModel, Priority.Low);
            OnPlayerCommandEvent.Register(OnPlayerCommand, Priority.Low);
            // OnEntitySpawnedEvent.Register(OnEntitySpawned, Priority.Low);

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
                SentCustomModels.TryAdd(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                ModelNameToIdForPlayer.TryAdd(p.name, new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            }
        }

        public override void Unload(bool shutdown) {
            SentCustomModels.Clear();
            ModelNameToIdForPlayer.Clear();

            OnPlayerConnectEvent.Unregister(OnPlayerConnect);
            OnPlayerDisconnectEvent.Unregister(OnPlayerDisconnect);
            OnJoiningLevelEvent.Unregister(OnJoiningLevel);
            OnJoinedLevelEvent.Unregister(OnJoinedLevel);
            OnSendingModelEvent.Unregister(OnSendingModel);
            OnPlayerCommandEvent.Unregister(OnPlayerCommand);
            // OnEntitySpawnedEvent.Unregister(OnEntitySpawned);

            if (command != null) {
                Command.Unregister(command);
                command = null;
            }
        }

        static ConcurrentDictionary<string, HashSet<string>> SentCustomModels =
            new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

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

        // sends all missing models in level to player,
        // and removes all unused models from player
        static void CheckAddRemove(Player p, Level level) {
            Debug("CheckAddRemove {0}", p.name);

            var visibleModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visibleModels.Add(ModelInfo.GetRawModel(p.Model));

            foreach (Player e in level.getPlayers()) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }
            foreach (PlayerBot e in level.Bots.Items) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }

            // send first so that the new model exists before removing the old one.
            // removing first will cause a couple ms of humanoid to be shown before the new model arrives
            //
            // send new models not yet in player's list
            foreach (var modelName in visibleModels) {
                CheckSendModel(p, modelName);
            }

            lock (SentCustomModels) {
                var sentModels = SentCustomModels[p.name];
                // clone so we can modify while we iterate
                foreach (var modelName in sentModels.ToArray()) {
                    // remove models not found in this level
                    if (!visibleModels.Contains(modelName)) {
                        CheckRemoveModel(p, modelName);
                    }
                }
            }
        }

        static void CheckUpdateAll(StoredCustomModel storedCustomModel) {
            // re-define the model and do ChangeModel for each entity currently using this model

            // remove this model from everyone's sent list
            foreach (Player p in PlayerInfo.Online.Items) {
                lock (SentCustomModels) {
                    var sentModels = SentCustomModels[p.name];
                    foreach (var modelName in sentModels.ToArray()) {
                        if (storedCustomModel.GetModelFileName().CaselessEq(new StoredCustomModel(modelName).GetModelFileName())) {
                            Debug("CheckUpdateAll remove {0} from {1}", modelName, p.name);
                            CheckRemoveModel(p, modelName);
                        }
                    }
                }
            }

            // add this model back to players who see entities using it
            foreach (Player p in PlayerInfo.Online.Items) {
                CheckAddRemove(p, p.level);
            }

            Action<Entity> checkEntity = (e) => {
                if (new StoredCustomModel(e.Model).Exists()) {
                    e.UpdateModel(e.Model);
                }
            };

            // do ChangeModel on every entity with this model
            // so that we update the model on the client
            var loadedLevels = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            foreach (Player p in PlayerInfo.Online.Items) {
                checkEntity(p);

                if (!loadedLevels.ContainsKey(p.level.name)) {
                    loadedLevels.Add(p.level.name, p.level);
                }
            }
            foreach (var entry in loadedLevels) {
                var level = entry.Value;
                foreach (PlayerBot e in level.Bots.Items) {
                    checkEntity(e);
                }
            }
        }

        static void OnPlayerConnect(Player p) {
            SentCustomModels.TryAdd(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ModelNameToIdForPlayer.TryAdd(p.name, new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        }

        static void OnPlayerDisconnect(Player p, string reason) {
            SentCustomModels.TryRemove(p.name, out _);
            ModelNameToIdForPlayer.TryRemove(p.name, out _);

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


        static Memoizer1<string, SkinType> MemoizedGetSkinType =
            new Memoizer1<string, SkinType>(
                GetSkinType,
                // cache for 1 hour
                new TimeSpan(1, 0, 0),
                (ex) => {
                    // Debug("" + ex.Message);
                    // default to SteveLayers if failed to GetSkinType
                    return SkinType.SteveLayers;
                });


        struct TaskAndToken {
            public Task task;
            public CancellationTokenSource cancelSource;
        }
        static ConcurrentDictionary<Entity, TaskAndToken> GetSkinTypeTasks =
            new ConcurrentDictionary<Entity, TaskAndToken>();

        static void UpdateModelForSkinType(Entity e, SkinType skinType) {
            var storedModel = new StoredCustomModel(e.Model);
            var oldModelName = storedModel.GetFullNameWithScale();
            storedModel.ApplySkinType(skinType);

            var newModelName = storedModel.GetFullNameWithScale();
            if (!oldModelName.CaselessEq(newModelName)) {
                Debug("UPDATE MODEL {0} -> {1}", oldModelName, newModelName);
                e.UpdateModel(newModelName);
            } else {
                Debug("already {0}", newModelName);
            }
        }

        static TaskAndToken SpawnGetSkinType(Entity e) {
            Debug("SpawnGetSkinType {0}", e.SkinName);
            var modelName = e.Model;
            var skinName = e.SkinName;

            var cancelSource = new CancellationTokenSource();
            var cancelToken = cancelSource.Token;
            Task task = new Task(
                () => {
                    cancelToken.ThrowIfCancellationRequested();

                    // check if we need to apply skin type transforms
                    var skinType = MemoizedGetSkinType.Get(skinName);
                    cancelToken.ThrowIfCancellationRequested();

                    lock (GetSkinTypeTasks) {
                        if (
                            e.Model == modelName &&
                            e.SkinName == skinName
                        ) {
                            UpdateModelForSkinType(e, skinType);
                        } else {
                            // we were so slow the entity was modified
                            Debug("weird!");
                        }

                        GetSkinTypeTasks.TryRemove(e, out _);
                        Debug("removed {0}; {1} more tasks", skinName, GetSkinTypeTasks.Count);
                    }
                },
                cancelToken
            );
            task.Start();

            return new TaskAndToken {
                task = task,
                cancelSource = cancelSource,
            };
        }

        // Called when model is being sent to a player.
        static void OnSendingModel(Entity e, ref string modelName, Player dst) {
            if (e.Model.StartsWith("$")) {
                // don't run if $model
                return;
            }
            Debug("OnSendingModel {0}: {1}", dst.name, modelName);

            var storedModel = new StoredCustomModel(modelName);
            if (storedModel.Exists() && storedModel.UsesHumanParts()) {
                // if this is a custom model and it uses human parts,
                // check if we need to skin transform

                var skinName = e.SkinName;

                SkinType skinType;
                if (MemoizedGetSkinType.GetCached(skinName, out skinType)) {
                    var oldModelName = storedModel.GetFullNameWithScale();
                    storedModel.ApplySkinType(skinType);

                    var newModelName = storedModel.GetFullNameWithScale();
                    if (!oldModelName.CaselessEq(newModelName)) {
                        Debug("OVERRIDE MODEL {0} -> {1}", oldModelName, newModelName);
                        modelName = newModelName;
                        e.SetModel(newModelName);
                    } else {
                        Debug("already {0}", newModelName);
                    }
                } else {
                    // spawn long task
                    lock (GetSkinTypeTasks) {
                        GetSkinTypeTasks.AddOrUpdate(
                            e,
                            (e2) => {
                                return SpawnGetSkinType(e2);
                            },
                            (e2, oldValue) => {
                                Debug("cancelling {0}", e2.SkinName);
                                oldValue.cancelSource.Cancel();
                                return SpawnGetSkinType(e2);
                            }
                        );
                    }
                }
            }

            // make sure the model is already defined for player
            // before we send the ChangeModel packet
            //
            // also check if we should remove unused old model
            CheckAddRemove(dst, dst.level);

            // // check if we should use default skin
            // if (
            //     e == dst &&
            //     (
            //         // unset skin
            //         dst.SkinName == dst.truename ||
            //         // or some other unsaved skin
            //         !Server.skins.Contains(dst.name)
            //     ) &&
            //     storedModel.Exists()
            // ) {
            // }
        }

        static void OnPlayerCommand(Player p, string cmd, string args, CommandData data) {
            if (cmd.CaselessEq("skin")) {
                var splitArgs = args.Trim().Length == 0 ? new string[] { } : args.SplitSpaces();
                if (splitArgs.Length == 0) {
                    // check if we should use default skin
                    var storedModel = new StoredCustomModel(p.Model);
                    if (storedModel.Exists()) {
                        storedModel.LoadFromFile();

                        if (
                            !storedModel.usesHumanSkin &&
                            storedModel.defaultSkin != null
                        ) {
                            Debug("Setting {0} to defaultSkin {1}", p.name, storedModel.defaultSkin);
                            p.SkinName = storedModel.defaultSkin;
                            Entities.GlobalRespawn(p);
                            p.Message("Changed your own skin to &c" + p.SkinName);
                            p.cancelcommand = true;
                        }
                    } else if (splitArgs.Length > 0) {
                        var last = splitArgs[splitArgs.Length - 1];
                        MemoizedGetSkinType.Invalidate(last);
                    }
                }
            }
        }

        //------------------------------------------------------------------ skin type parsing/model transforming

        // 32x64 (Steve), 64x64 (SteveLayers), 64x64 slim-arm (Alex)
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
            // TODO set timeout!
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

        //------------------------------------------------------------------ commands

        class CmdCustomModel : Command2 {
            public override string name => "CustomModel";
            public override string shortcut => "cm";
            public override string type => CommandTypes.Other;
            public override bool MessageBlockRestricted => true;
            public override LevelPermission defaultRank => LevelPermission.AdvBuilder;
            public override CommandPerm[] ExtraPerms => new[] {
                new CommandPerm(LevelPermission.Operator, "can modify/upload public custom models."),
            };

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
                        if (subCommand.CaselessEq("config") || subCommand.CaselessEq("edit")) {
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
                        }
                    } else if (args.Count >= 1) {
                        var arg = args.PopFront();

                        if (subCommand.CaselessEq("list") && args.Count == 0) {
                            // /CustomModel list [name]
                            var ag = TargetModelName(p, data, arg, false);
                            if (ag == null) return;

                            List(p, StoredCustomModel.GetPlayerName(ag));
                            return;
                        }

                        var modelName = TargetModelName(p, data, arg);
                        if (modelName == null) return;

                        if (subCommand.CaselessEq("config") || subCommand.CaselessEq("edit")) {
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

            private string TargetModelName(Player p, CommandData data, string arg, bool checkPerms = true) {
                if (arg.CaselessEq("-own")) {
                    arg = p.name;
                }

                if (!ValidModelName(p, arg)) return null;

                if (checkPerms) {
                    string maybePlayerName = StoredCustomModel.GetPlayerName(arg);
                    bool targettingSelf = maybePlayerName != null && maybePlayerName.CaselessEq(p.name);

                    // if you aren't targetting your own models,
                    // and you aren't admin, denied
                    if (!targettingSelf && !CheckExtraPerm(p, data, 1)) return null;
                }

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

            static Dictionary<string, ModelField> ModifiableFields =
                new Dictionary<string, ModelField>(StringComparer.OrdinalIgnoreCase) {
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
                {
                    "defaultSkin",
                    new ModelField(
                        "skin",
                        "Set default skin",
                        (model) => model.defaultSkin == null ? "unset" : model.defaultSkin.ToString(),
                        (model, p, input) => {
                            if (input.Length == 0 || input == "unset") {
                                model.defaultSkin = null;
                                return true;
                            }

                            var skin = input;
                            if (skin[0] == '+') {
                                skin = "https://minotar.net/skin/" + skin.Substring(1) + ".png";
                            }

                            if (skin.CaselessStarts("http://") || skin.CaselessStarts("https")) {
                                HttpUtil.FilterURL(ref skin);
                            }

                            if (skin.Length > NetUtils.StringSize) {
                                p.Message("The skin must be " + NetUtils.StringSize + " characters or less.");
                                return false;
                            }

                            model.defaultSkin = skin;
                            return true;
                        }
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
                                CheckUpdateAll(storedCustomModel);
                            }
                        }
                    }
                }
            }

            void Upload(Player p, string modelName, string url) {
                var bytes = HttpUtil.DownloadData(url, p);
                if (bytes != null) {
                    string json = System.Text.Encoding.UTF8.GetString(bytes);

                    // try parsing now so that we throw and don't save the invalid file
                    // and notify the user of the error
                    if (!BlockBench.IsValid(json, p, modelName)) {
                        return;
                    }

                    // override filename because file might not exist yet
                    var storedModel = new StoredCustomModel(modelName, true);
                    storedModel.WriteBBFile(json);

                    if (!storedModel.Exists()) {
                        // create a default ccmodel file if doesn't exist
                        storedModel.WriteToFile();
                    }

                    CheckUpdateAll(storedModel);
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

            public static bool IsValid(string json, Player p, string modelName) {
                var jsonRoot = Parse(json);
                var parts = jsonRoot.ToParts();

                if (!jsonRoot.IsValid(p)) {
                    return false;
                }

                if (parts.Length > Packet.MaxCustomModelParts) {
                    p.Message(
                        "%WNumber of model parts ({0}) exceeds max of {1}!",
                        parts.Length,
                        Packet.MaxCustomModelParts
                    );
                    return false;
                }

                // only do size check if they can't upload global models
                if (!CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)) {
                    for (int i = 0; i < parts.Length; i++) {
                        // Models can be 1 block bigger if they aren't a purely personal model
                        bool purePersonal = new StoredCustomModel(modelName).IsPersonalPrimary();
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
                            return false;
                        }
                    }
                }

                for (int i = 0; i < parts.Length; i++) {
                    var part = parts[i];
                    if (part.anims.Length > Packet.MaxCustomModelAnims) {
                        p.Message(
                            "%WThe %b{0} cube in your list %Whas more than %b{1} %Wanimations.",
                            ListicleNumber(i + 1),
                            Packet.MaxCustomModelAnims
                        );
                        break;
                    }
                }

                return true;
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


            public class JsonRoot {
                public Meta meta;
                public string name;
                public Element[] elements;
                public UuidOrGroup[] outliner;
                public Resolution resolution;

                public bool IsValid(Player p) {
                    // warn player if unsupported features were used
                    bool warnings = false;

                    // check if not free model
                    if (this.meta.model_format != "free") {
                        p.Message(
                            "%WModel uses format %b{0} %W(should be using %b\"Free Model\"%W)!",
                            this.meta.model_format
                        );
                        warnings = true;
                    }

                    UInt16? lastTexture = null;
                    Func<JsonRoot.Face, string> bad = (face) => {
                        // check for uv rotation
                        if (face.rotation != 0) {
                            return "uses UV rotation";
                        }

                        // check for no assigned texture
                        if (!face.texture.HasValue) {
                            return "doesn't have a texture";
                        } else {
                            // check if using more than 1 texture
                            if (lastTexture.HasValue) {
                                if (lastTexture.Value != face.texture.Value) {
                                    return "uses a different texture";
                                }
                            } else {
                                lastTexture = face.texture.Value;
                            }
                        }

                        return null;
                    };
                    for (int i = 0; i < this.elements.Length; i++) {
                        var e = this.elements[i];
                        string reason = null;

                        Action<string> warn = (faceName) => {
                            p.Message(
                                "%WThe %b{0} %Wface on the %b{1} %Wcube {2}!",
                                faceName,
                                e.name,
                                reason
                            );
                            warnings = true;
                        };

                        reason = bad(e.faces.up);
                        if (reason != null) { warn("up"); }
                        reason = bad(e.faces.down);
                        if (reason != null) { warn("down"); }
                        reason = bad(e.faces.north);
                        if (reason != null) { warn("north"); }
                        reason = bad(e.faces.south);
                        if (reason != null) { warn("south"); }
                        reason = bad(e.faces.east);
                        if (reason != null) { warn("east"); }
                        reason = bad(e.faces.west);
                        if (reason != null) { warn("west"); }
                    }


                    Action<UuidOrGroup> test = null;
                    test = (uuidOrGroup) => {
                        if (uuidOrGroup.group != null) {
                            var g = uuidOrGroup.group;
                            // if pivot point exists, and rotation isn't 0
                            if (
                                g.rotation[0] != 0 ||
                                g.rotation[1] != 0 ||
                                g.rotation[2] != 0
                            ) {
                                p.Message(
                                    "%WThe %b{0} %Wgroup uses rotation!",
                                    g.name
                                );
                                warnings = true;
                            }

                            foreach (var innerGroup in uuidOrGroup.group.children) {
                                test(innerGroup);
                            }
                        }
                    };
                    foreach (var uuidOrGroup in this.outliner) {
                        test(uuidOrGroup);
                    }

                    if (warnings) {
                        p.Message("%WThese BlockBench features aren't supported in ClassiCube.");
                    }


                    return true;
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
                        // a uuid

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
                        var part = ToPart(e);
                        if (part != null) {
                            parts.Add(part);
                        }
                    } else {
                        // a group

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

                    var anims = new List<CustomModelAnim>();
                    var partName = e.name.Replace(" ", "");
                    foreach (var attr in partName.SplitComma()) {
                        float? a = null;
                        float? b = null;
                        float? c = null;
                        float? d = null;

                        var colonSplit = attr.Split(':');
                        var attrName = colonSplit[0];
                        if (colonSplit.Length >= 2) {
                            var modifiers = colonSplit[1].Replace(" ", "").Split('|');
                            if (modifiers.Length > 0) {
                                a = float.Parse(modifiers[0]);
                                if (modifiers.Length > 1) {
                                    b = float.Parse(modifiers[1]);
                                    if (modifiers.Length > 2) {
                                        c = float.Parse(modifiers[2]);
                                        if (modifiers.Length > 3) {
                                            d = float.Parse(modifiers[3]);
                                        }
                                    }
                                }
                            }
                        }


                        PartNameToAnim toAnim;
                        if (PartNamesToAnim.TryGetValue(attrName, out toAnim)) {
                            anims.AddRange(toAnim.ToAnim(a, b, c, d));

                        } else if (attrName.CaselessEq("leftidle")) {
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.X,
                                a = ANIM_IDLE_XPERIOD,
                                b = ANIM_IDLE_MAX,
                                c = 0,
                                d = 0,
                            });
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.Z,
                                a = ANIM_IDLE_ZPERIOD,
                                b = -ANIM_IDLE_MAX,
                                c = 0.25f,
                                d = 1,
                            });

                        } else if (attrName.CaselessEq("rightidle")) {
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.X,
                                a = ANIM_IDLE_XPERIOD,
                                b = -ANIM_IDLE_MAX,
                                c = 0,
                                d = 0,
                            });
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.Z,
                                a = ANIM_IDLE_ZPERIOD,
                                b = ANIM_IDLE_MAX,
                                c = 0.25f,
                                d = 1,
                            });

                        } else if (attrName.CaselessEq("fullbright")) {
                            part.fullbright = true;
                        } else if (attrName.CaselessEq("hand")) {
                            part.firstPersonArm = true;
                        } else if (attrName.CaselessEq("layer")) {
                            part.layer = true;
                        } else if (attrName.CaselessEq("humanleftarm")) {
                            part.skinLeftArm = true;
                        } else if (attrName.CaselessEq("humanrightarm")) {
                            part.skinRightArm = true;
                        } else if (attrName.CaselessEq("humanleftleg")) {
                            part.skinLeftLeg = true;
                        } else if (attrName.CaselessEq("humanrightleg")) {
                            part.skinRightLeg = true;
                        }
                    }
                    part.anims = anims.ToArray();

                    return part;
                }

                static Dictionary<string, PartNameToAnim> PartNamesToAnim = new Dictionary<string, PartNameToAnim>(StringComparer.OrdinalIgnoreCase) {
                    { "head", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.X) },
                    { "headx", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.X) },
                    { "heady", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.Y) },
                    { "headz", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.Z) },

                    { "leftleg", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.X) },
                    { "leftlegx", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.X) },
                    { "leftlegy", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.Y) },
                    { "leftlegz", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.Z) },

                    { "rightleg", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.X) },
                    { "rightlegx", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.X) },
                    { "rightlegy", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.Y) },
                    { "rightlegz", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.Z) },

                    { "leftarm", new PartNameToAnim(
                        new []{ CustomModelAnimType.LeftArmX, CustomModelAnimType.LeftArmZ },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Z}
                    ) },
                    { "leftarmxx", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.X) },
                    { "leftarmxy", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.Y) },
                    { "leftarmxz", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.Z) },

                    { "rightarm", new PartNameToAnim(
                        new []{ CustomModelAnimType.RightArmX, CustomModelAnimType.RightArmZ },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Z}
                    ) },
                    { "rightarmxx", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.X) },
                    { "rightarmxy", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.Y) },
                    { "rightarmxz", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.Z) },

                    { "leftarmzx", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.X) },
                    { "leftarmzy", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.Y) },
                    { "leftarmzz", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.Z) },

                    { "rightarmzx", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.X) },
                    { "rightarmzy", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.Y) },
                    { "rightarmzz", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.Z) },

                    /*
                        a: speed
                        b: shift pos
                    */
                    { "spinx", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.X, 1.0f, 0.0f) },
                    { "spiny", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.Y, 1.0f, 0.0f) },
                    { "spinz", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.Z, 1.0f, 0.0f) },

                    { "spinxvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.X, 1.0f, 0.0f) },
                    { "spinyvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.Y, 1.0f, 0.0f) },
                    { "spinzvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.Z, 1.0f, 0.0f) },

                    /*
                        a: speed
                        b: width
                        c: shift cycle
                        d: shift pos
                    */
                    { "sinx", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "siny", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinz", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "sinxvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinyvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinzvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "cosx", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosy", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosz", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },

                    { "cosxvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosyvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "coszvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },

                    { "pistonx", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistony", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonz", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "pistonxvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonyvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonzvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },
                };

                class PartNameToAnim {
                    CustomModelAnimType[] types;
                    CustomModelAnimAxis[] axes;
                    float defaultA;
                    float defaultB;
                    float defaultC;
                    float defaultD;
                    Action<CustomModelAnim> action;

                    public PartNameToAnim(
                        CustomModelAnimType[] types,
                        CustomModelAnimAxis[] axes,
                        float defaultA = 1.0f,
                        float defaultB = 1.0f,
                        float defaultC = 1.0f,
                        float defaultD = 1.0f,
                        Action<CustomModelAnim> action = null
                    ) {
                        this.types = types;
                        this.axes = axes;
                        this.defaultA = defaultA;
                        this.defaultB = defaultB;
                        this.defaultC = defaultC;
                        this.defaultD = defaultD;
                        this.action = action;
                    }

                    public PartNameToAnim(
                        CustomModelAnimType type,
                        CustomModelAnimAxis axis,
                        float defaultA = 1.0f,
                        float defaultB = 1.0f,
                        float defaultC = 1.0f,
                        float defaultD = 1.0f,
                        Action<CustomModelAnim> action = null
                    ) : this(
                        new[] { type },
                        new[] { axis },
                        defaultA,
                        defaultB,
                        defaultC,
                        defaultD,
                        action

                    ) { }

                    public CustomModelAnim[] ToAnim(
                        float? a = null,
                        float? b = null,
                        float? c = null,
                        float? d = null
                    ) {
                        var anims = new List<CustomModelAnim>();
                        for (int i = 0; i < types.Length; i++) {
                            var type = types[i];
                            var axis = axes[i];

                            var anim = new CustomModelAnim {
                                type = type,
                                axis = axis,
                                a = a.HasValue ? a.Value : this.defaultA,
                                b = b.HasValue ? b.Value : this.defaultB,
                                c = c.HasValue ? c.Value : this.defaultC,
                                d = d.HasValue ? d.Value : this.defaultD,
                            };
                            if (this.action != null) {
                                this.action.Invoke(anim);
                            }

                            anims.Add(anim);
                        }
                        return anims.ToArray();
                    }
                }

                const float MATH_PI = 3.1415926535897931f;
                const float MATH_DEG2RAD = (MATH_PI / 180.0f);
                const float ANIM_MAX_ANGLE = (110 * MATH_DEG2RAD);
                const float ANIM_ARM_MAX = (60.0f * MATH_DEG2RAD);
                const float ANIM_LEG_MAX = (80.0f * MATH_DEG2RAD);
                const float ANIM_IDLE_MAX = (3.0f * MATH_DEG2RAD);
                const float ANIM_IDLE_XPERIOD = (2.0f * MATH_PI / 5.0f);
                const float ANIM_IDLE_ZPERIOD = (2.0f * MATH_PI / 3.5f);


                public class Resolution {
                    public UInt16 width;
                    public UInt16 height;
                }
                public class Meta {
                    public string model_format;
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
                    public UInt16? texture = null;
                    public UInt16 rotation = 0;
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

        class Memoizer1<TKey, TValue> {
            private Func<TKey, TValue> fetch;
            private TimeSpan? cacheLifeTime;
            private Func<Exception, TValue> exceptionHandler;

            public Memoizer1(
                Func<TKey, TValue> fetch,
                TimeSpan? cacheLifeTime = null,
                Func<Exception, TValue> exceptionHandler = null
            ) {
                this.fetch = fetch;
                this.cacheLifeTime = cacheLifeTime;
                this.exceptionHandler = exceptionHandler;
            }

            private struct CacheEntry {
                public TValue value;
                public System.Timers.Timer deathTimer;
            }

            // we want to lock per key instead of for all access on the cache directory
            private ConcurrentDictionary<TKey, object> cacheLocks = new ConcurrentDictionary<TKey, object>();
            private object GetCacheLock(TKey key) {
                return cacheLocks.GetOrAdd(key, (_) => new object());
            }

            private ConcurrentDictionary<TKey, CacheEntry> cache = new ConcurrentDictionary<TKey, CacheEntry>();
            public TValue Get(TKey key) {
                lock (GetCacheLock(key)) {
                    CacheEntry entry;
                    if (cache.TryGetValue(key, out entry)) {
                        Debug("Memoizer1 Hit {0}", key);
                        return entry.value;
                    }

                    TValue value = Fetch(key);
                    entry.value = value;
                    if (cacheLifeTime.HasValue) {
                        var timer = new System.Timers.Timer(cacheLifeTime.Value.TotalMilliseconds);
                        timer.AutoReset = false;
                        timer.Elapsed += (obj, elapsedEventArgs) => {
                            timer.Stop();
                            timer.Dispose();

                            Debug("Memoizer1 Removing {0}", key);
                            lock (GetCacheLock(key)) {
                                cache.TryRemove(key, out _);
                            }
                        };
                        timer.Start();
                        entry.deathTimer = timer;
                    }
                    cache.TryAdd(key, entry);

                    return value;
                }
            }

            public bool GetCached(TKey key, out TValue value) {
                CacheEntry entry;
                if (cache.TryGetValue(key, out entry)) {
                    value = entry.value;
                    return true;
                }
                value = default(TValue);
                return false;
            }

            public TValue Fetch(TKey key) {
                TValue ret;
                bool threw = false;
                var stopwatch = Stopwatch.StartNew();
                try {
                    ret = this.fetch.Invoke(key);
                    stopwatch.Stop();
                } catch (Exception ex) {
                    stopwatch.Stop();
                    threw = true;
                    if (this.exceptionHandler != null) {
                        ret = this.exceptionHandler.Invoke(ex);
                    } else {
                        throw ex;
                    }
                } finally {
                    Debug("Memoizer1 Fetch {0} took {1}s" + (threw ? " (threw)" : ""), key, stopwatch.Elapsed.TotalSeconds);
                }

                return ret;
            }

            public void InvalidateAll() {
                foreach (var key in cache.Keys.ToArray()) {
                    Invalidate(key);
                }
            }

            public void Invalidate(TKey key) {
                lock (GetCacheLock(key)) {
                    CacheEntry entry;
                    if (cache.TryRemove(key, out entry)) {
                        if (entry.deathTimer != null) {
                            entry.deathTimer.Stop();
                            entry.deathTimer.Dispose();
                        }
                    }
                }
            }
        }

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

        private static bool debug = false;
        private static void Debug(string format, object arg0, object arg1, object arg2) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0, arg1, arg2);
        }
        private static void Debug(string format, object arg0, object arg1) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0, arg1);
        }
        private static void Debug(string format, object arg0) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format, arg0);
        }
        private static void Debug(string format) {
            if (!debug) return;
            Logger.Log(LogType.Debug, format);
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
