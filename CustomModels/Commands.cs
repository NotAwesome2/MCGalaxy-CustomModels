using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MCGalaxy.Commands;
using MCGalaxy.Network;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {

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
                            if (
                                model.collisionBounds.X <= 0 || model.collisionBounds.X > 100 ||
                                model.collisionBounds.Y <= 0 || model.collisionBounds.Y > 100 ||
                                model.collisionBounds.Z <= 0 || model.collisionBounds.Z > 100
                            ) {
                                p.Message("%WBad value! Numbers should be in range (0, 100]");
                                return false;
                            }
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
                            if (
                                model.pickingBoundsMin.X < -100 || model.pickingBoundsMin.X > 100 ||
                                model.pickingBoundsMin.Y < -100 || model.pickingBoundsMin.Y > 100 ||
                                model.pickingBoundsMin.Z < -100 || model.pickingBoundsMin.Z > 100 ||
                                model.pickingBoundsMax.X < -100 || model.pickingBoundsMax.X > 100 ||
                                model.pickingBoundsMax.Y < -100 || model.pickingBoundsMax.Y > 100 ||
                                model.pickingBoundsMax.Z < -100 || model.pickingBoundsMax.Z > 100
                            ) {
                                p.Message("%WBad value! Numbers should be in range [-100, 100]");
                                return false;
                            }
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

    } // class CustomModelsPlugin
} // namespace MCGalaxy
