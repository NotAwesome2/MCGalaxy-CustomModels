using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCGalaxy.Maths;
using MCGalaxy.Network;
using Newtonsoft.Json;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {
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
            public bool autoNameY = true;
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
                this.autoNameY = o.autoNameY;
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
                return (this.fileName ?? this.modelName).ToLower();
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

            public class ModelAndParts {
                public CustomModel model;
                public CustomModelPart[] parts;
            }
            public ModelAndParts ComputeModelAndParts() {
                var blockBench = ParseBlockBench();
                var model = this.ToCustomModel(blockBench);
                var parts = new List<Part>(blockBench.ToParts());

                if (this.autoNameY) {
                    float maxPartHeight = 0.0f;
                    foreach (var part in parts) {
                        if (part.max.Y > maxPartHeight) {
                            maxPartHeight = part.max.Y;
                        }
                    }
                    model.nameY = maxPartHeight + 0.5f / 16.0f;
                }

                if (this.fileName == null) {
                    // only apply modifiers if we aren't a file override

                    if (this.modifiers.Contains("sit") || this.modifiers.Contains("sitcute")) {
                        float? leftLegMaxY = null;
                        float? leftLegMinY = null;
                        float? leftLegMaxZ = null;
                        float? leftLegMinZ = null;

                        float? rightLegMaxY = null;
                        float? rightLegMinY = null;
                        float? rightLegMaxZ = null;
                        float? rightLegMinZ = null;

                        foreach (var part in parts) {
                            foreach (var anim in part.anims) {
                                if (anim.type == CustomModelAnimType.LeftLegX) {
                                    // find very top and bottom points of all leg parts
                                    if (!leftLegMaxY.HasValue || part.max.Y > leftLegMaxY) leftLegMaxY = part.max.Y;
                                    if (!leftLegMinY.HasValue || part.min.Y < leftLegMinY) leftLegMinY = part.min.Y;
                                    // only use 1 part for finding width of leg
                                    leftLegMaxZ = part.max.Z;
                                    leftLegMinZ = part.min.Z;
                                } else if (anim.type == CustomModelAnimType.RightLegX) {
                                    if (!rightLegMaxY.HasValue || part.max.Y > rightLegMaxY) rightLegMaxY = part.max.Y;
                                    if (!rightLegMinY.HasValue || part.min.Y < rightLegMinY) rightLegMinY = part.min.Y;
                                    rightLegMaxZ = part.max.Z;
                                    rightLegMinZ = part.min.Z;
                                }

                                if (
                                    anim.type == CustomModelAnimType.LeftLegX ||
                                    anim.type == CustomModelAnimType.RightLegX
                                ) {
                                    // rotate legs to point forward, pointed a little outwards
                                    part.rotation.X = 90.0f;
                                    if (this.modifiers.Contains("sitcute")) {
                                        part.rotation.Y = anim.type == CustomModelAnimType.LeftLegX ? 1.0f : -1.0f;
                                    } else {
                                        part.rotation.Y = anim.type == CustomModelAnimType.LeftLegX ? 5.0f : -5.0f;
                                    }
                                    part.rotation.Z = 0;
                                    anim.type = CustomModelAnimType.None;
                                }
                            }
                        }

                        var leftLegHeight = leftLegMaxY - leftLegMinY;
                        var leftLegForwardWidth = leftLegMaxZ - leftLegMinZ;

                        var rightLegHeight = rightLegMaxY - rightLegMinY;
                        var rightLegForwardWidth = rightLegMaxZ - rightLegMinZ;

                        // lower all parts by leg's Y height, up by the leg's width
                        var leftLower = leftLegHeight - leftLegForwardWidth / 2.0f;
                        var rightLower = rightLegHeight - rightLegForwardWidth / 2.0f;

                        var lower = leftLower.HasValue ? leftLower : rightLower;
                        if (lower.HasValue) {
                            foreach (var part in parts) {
                                part.min.Y -= lower.Value;
                                part.max.Y -= lower.Value;
                                part.rotationOrigin.Y -= lower.Value;

                                if (part.firstPersonArm) {
                                    // remove first person arm because offset changed
                                    part.firstPersonArm = false;
                                }
                            }
                            model.eyeY -= lower.Value;
                            model.nameY -= lower.Value;
                        }
                    }
                    if (this.modifiers.Contains("sitcute")) {
                        foreach (var part in parts) {
                            var appliedOffset = false;
                            foreach (var anim in part.anims) {
                                var left = anim.type == CustomModelAnimType.LeftArmX || anim.type == CustomModelAnimType.LeftArmZ;
                                var right = anim.type == CustomModelAnimType.RightArmX || anim.type == CustomModelAnimType.RightArmZ;
                                if (left || right) {
                                    // rotate legs to point forward, pointed a little outwards
                                    part.rotation.X = 50.0f;
                                    part.rotation.Y = left ? -40.0f : 40.0f;
                                    part.rotation.Z = left ? -25.0f : 25.0f;
                                    if (!appliedOffset) {
                                        part.max.Z += -0.5f / 16.0f;
                                        part.min.Z += -0.5f / 16.0f;
                                        appliedOffset = true;
                                    }
                                    anim.type = CustomModelAnimType.None;
                                }
                            }
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

                            void f(Part left, Part right) {
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
                            }

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

                var modelAndParts = new ModelAndParts {
                    model = model,
                    parts = parts.Select(part => part.ToCustomModelPart()).ToArray()
                };
                return modelAndParts;
            }

            public void Define(Player p) {
                var modelAndParts = this.ComputeModelAndParts();

                DefineModel(p, modelAndParts.model, modelAndParts.parts);
            }
        } // class StoredCustomModel


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


        static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> ModelNameToIdForPlayer =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

        static byte? GetModelId(Player p, string name, bool addNew = false) {
            lock (ModelNameToIdForPlayer) {
                var isPlus = Server.Config.ClassicubeAccountPlus ? p.name : p.truename;
                var modelNameToId = ModelNameToIdForPlayer[isPlus];
                if (modelNameToId.TryGetValue(name, out byte value)) {
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
                var isPlus = Server.Config.ClassicubeAccountPlus ? p.name : p.truename;
                Debug("DefineModel {0} {1} {2}", modelId, isPlus, model.name);

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
                    var isPlus = Server.Config.ClassicubeAccountPlus ? p.name : p.truename;
                    Debug("UndefineModel {0} {1} {2}", modelId, isPlus, name);

                    byte[] modelPacket = Packet.UndefineModel(modelId);
                    p.Send(modelPacket);

                    var modelNameToId = ModelNameToIdForPlayer[isPlus];
                    modelNameToId.TryRemove(name, out _);
                }
            }
        }


    } // class CustomModelsPlugin
} // namespace MCGalaxy
