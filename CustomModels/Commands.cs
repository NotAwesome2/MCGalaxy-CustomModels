using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                // Only Operator+ ...
                new CommandPerm(LevelPermission.Operator, "can modify/upload public (or other people's) custom models."),
            };

            public override void Help(Player p) {
                p.Message("%T/CustomModel sit");
                p.Message("%H  Toggle sitting on your worn custom model.");

                p.Message("%T/CustomModel list <all/public/player name> <query>");
                p.Message("%H  List all public/personal custom models.");

                p.Message("%T/CustomModel goto <all/public/player name> <page>");
                p.Message("%H  Go to a generated map with all public/personal custom models.");

                p.Message("%T/CustomModel wear [model name]");
                p.Message("%H  Change your model and skin to a CustomModel.");

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
                        } else if (subCommand.CaselessEq("sitcute")) {
                            // /CustomModel sitcute
                            SitCute(p, data);
                            return;
                        } else if (subCommand.CaselessEq("list")) {
                            // /CustomModel list
                            List(p, null);
                            return;
                        } else if (subCommand.CaselessEq("goto") || subCommand.CaselessEq("visit")) {
                            // /CustomModel goto
                            Goto(p, null);
                            return;
                        }
                    } else if (args.Count >= 1) {
                        var arg = args.PopFront();

                        var checkPerms = true;
                        if (
                            subCommand.CaselessEq("list") ||
                            subCommand.CaselessEq("goto") ||
                            subCommand.CaselessEq("visit") ||
                            subCommand.CaselessEq("wear")
                        ) {
                            // anyone can list, goto, wear
                            checkPerms = false;
                        }

                        var modelName = TargetModelName(p, data, arg, checkPerms);
                        if (modelName == null) return;

                        if (
                            subCommand.CaselessEq("list") &&
                            (args.Count == 0 || args.Count == 1)
                        ) {
                            if (args.Count == 0) {
                                // /CustomModel list [name]
                                List(p, modelName);
                                return;
                            } else if (args.Count == 1) {
                                // /CustomModel list [name] [query]
                                string query = args.PopFront();
                                List(p, modelName, query);
                                return;
                            }
                        } else if (
                            (subCommand.CaselessEq("goto") || subCommand.CaselessEq("visit")) &&
                            (args.Count == 0 || args.Count == 1)
                        ) {
                            // /CustomModel goto [name] <page>
                            if (args.Count == 0) {
                                Goto(p, modelName);
                                return;
                            } else if (args.Count == 1) {
                                ushort page = 1;
                                if (!CommandParser.GetUShort(p, args.PopFront(), "page", ref page)) return;
                                if (page > 0) {
                                    page = (ushort)((int)page - 1);
                                }
                                Goto(p, modelName, page);
                                return;
                            }
                        } else if (subCommand.CaselessEq("wear") && args.Count == 0) {
                            // /CustomModel wear [name]
                            Wear(p, modelName, data);
                            return;

                        } else if (subCommand.CaselessEq("config") || subCommand.CaselessEq("edit")) {
                            // /CustomModel config [name] [field] [values...]
                            Config(p, modelName, args);
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
                string playerNameWithPlus = GetNameWithPlus(p.name);
                if (arg.CaselessEq("-own")) {
                    arg = playerNameWithPlus;
                }

                if (!ValidModelName(p, arg)) return null;

                if (checkPerms) {
                    string maybePlayerName = StoredCustomModel.GetPlayerName(arg);
                    bool targettingSelf = maybePlayerName != null && maybePlayerName.CaselessEq(playerNameWithPlus);

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

                // sit always toggles off sitcute
                if (storedModel.modifiers.Contains("sit") || storedModel.modifiers.Contains("sitcute")) {
                    storedModel.RemoveModifier("sitcute");
                    storedModel.RemoveModifier("sit");
                } else {
                    storedModel.AddModifier("sit");
                }

                p.HandleCommand("XModel", storedModel.GetFullNameWithScale(), data);
            }

            void SitCute(Player p, CommandData data) {
                var storedModel = new StoredCustomModel(p.Model);
                if (!storedModel.Exists()) {
                    p.Message("%WYour current model isn't a Custom Model!");
                    return;
                }

                // allow going from sit -> sitcute
                storedModel.RemoveModifier("sit");
                if (storedModel.modifiers.Contains("sitcute")) {
                    storedModel.RemoveModifier("sitcute");
                } else {
                    storedModel.AddModifier("sitcute");
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

            static readonly Dictionary<string, ModelField> ModifiableFields =
                new Dictionary<string, ModelField>(StringComparer.OrdinalIgnoreCase) {
                {
                    "nameY",
                    new ModelField(
                        "height",
                        "Name text height. Set to 'auto' to detect",
                        (model) => {
                            if (model.autoNameY) {
                                try {
                                    var modelAndParts = model.ComputeModelAndParts();
                                    var customModel = modelAndParts.model;
                                    return "" + customModel.nameY * 16.0f + " (auto)";
                                } catch (System.IO.FileNotFoundException) {
                                    return "" + model.nameY + " (auto)";
                                }
                            } else {
                                return "" + model.nameY + " (manual)";
                            }
                        },
                        (model, p, input) => {
                            if (input.CaselessEq("auto")) {
                                model.autoNameY = true;
                                return true;
                            } else {
                                model.autoNameY = false;
                                return CommandParser.GetReal(p, input, "nameY", ref model.nameY);
                            }
                        }
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
                        "Affects your collision with blocks",
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
                        "Affects render bounds and mouse clicks",
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

            void Config(Player p, string modelName, List<string> args) {
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



            List<string> GetModels(string playerName, Player p) {
                var folderPath = playerName == null
                    ? PublicModelsDirectory
                    : StoredCustomModel.GetFolderPath(playerName);

                if (playerName != null && !Directory.Exists(folderPath)) {
                    p.Message("%WPlayer %S{0} %Whas not created any models.", GetNameWithoutPlus(playerName));
                    return null;
                }

                var modelNames = new List<string>();
                foreach (var entry in new DirectoryInfo(folderPath).GetFiles()) {
                    string fileName = entry.Name;
                    if (Path.GetExtension(fileName).CaselessEq(CCModelExt)) {
                        string name = Path.GetFileNameWithoutExtension(fileName);
                        modelNames.Add(name);
                    }
                }

                modelNames = modelNames.OrderBy(name => name).ToList();
                return modelNames;
            }

            Dictionary<string, List<string>> GetAllModels(Player p) {
                var dict = new Dictionary<string, List<string>>();

                var publicModels = GetModels(null, p);
                if (publicModels.Count > 0) {
                    dict.Add("Public", publicModels);
                }

                foreach (var entry in new DirectoryInfo(PersonalModelsDirectory).GetDirectories()) {
                    string folderName = entry.Name;
                    var models = GetModels(folderName, p);
                    if (models.Count > 0) {
                        dict.Add(folderName, models);
                    }
                }

                return dict;
            }

            void List(Player p, string playerName = null, string query = null) {
                bool all = false;
                if (playerName != null) {
                    playerName = playerName.ToLower();
                    if (playerName == "all" || playerName == "count") {
                        all = true;
                    } else if (playerName == "public") {
                        playerName = null;
                    } else {
                        playerName = StoredCustomModel.GetPlayerName(playerName) ?? StoredCustomModel.GetPlayerName(playerName + "+");
                    }
                }

                if (query != null) {
                    query = query.ToLower();
                }

                if (all) {
                    var dict = GetAllModels(p);
                    if (dict == null) return;

                    if (query == null) {
                        p.Message("%TAll %SCustom Models");
                        foreach (var pair in dict.OrderByDescending(pair => pair.Value.Count)) {
                            p.Message("  %T{0,3}%S: %T{1}", pair.Value.Count, pair.Key);
                        }
                    } else {
                        var modelNames = new List<string>();
                        foreach (var pair in dict) {
                            modelNames.AddRange(
                                pair.Value.Where(
                                    (name) => name.Contains(query)
                                )
                            );
                        }

                        p.Message(
                            "%SSearching %TAll %SCustom Models: %T{0}",
                            modelNames.Join("%S, %T")
                        );
                    }
                } else {
                    var modelNames = GetModels(playerName, p);
                    if (modelNames == null) return;

                    if (query == null) {
                        p.Message(
                            "%T{0} %SCustom Models: %T{1}",
                            playerName == null ?
                                "Public" :
                                GetNameWithoutPlus(playerName) + "%S's",
                            modelNames.Join("%S, %T")
                        );
                    } else {
                        p.Message(
                            "%SSearching %T{0} %SCustom Models: %T{1}",
                            playerName == null ?
                                "Public" :
                                GetNameWithoutPlus(playerName) + "%S's",
                            modelNames.Where(
                                    (name) => name.Contains(query)
                                ).Join("%S, %T")
                        );
                    }
                }
            }

            void Goto(Player p, string playerName = null, ushort page = 0) {
                bool all = false;
                if (playerName != null) {
                    playerName = playerName.ToLower();
                    if (playerName == "all") {
                        all = true;
                    } else if (playerName == "public") {
                        playerName = null;
                    } else {
                        playerName = StoredCustomModel.GetPlayerName(playerName) ?? StoredCustomModel.GetPlayerName(playerName + "+");
                    }
                }

                List<string> modelNames;
                if (all) {
                    var dict = GetAllModels(p);
                    modelNames = dict
                        // public ones first
                        .OrderByDescending(pair => pair.Key == "Public")
                        // then by player name A-Z
                        .ThenBy(pair => pair.Key)
                        .Select(pair => pair.Value)
                        .SelectMany(x => x)
                        .OrderBy(name => name)
                        .ToList();
                } else {
                    modelNames = GetModels(playerName, p);
                }
                if (modelNames == null) return;

                // - 1 for our self player
                var partitionSize = Packet.MaxCustomModels - 1;
                var partitions = modelNames.Partition(partitionSize).ToList();
                if (page >= partitions.Count) {
                    p.Message(
                        "%WPage doesn't exist"
                    );
                    return;
                }
                var total = modelNames.Count;
                modelNames = partitions[page];
                p.Message(
                    "%HViewing %T{0} %Hmodels{1}",
                    total,
                    partitions.Count > 1
                        ? string.Format(
                            " %S(page %T{0}%S/%T{1}%S)",
                            page + 1,
                            partitions.Count
                        )
                        : ""
                );
                if (partitions.Count > 1 && page < (partitions.Count - 1)) {
                    p.Message(
                        "%SUse \"%H/cm goto {0} {1}%S\" to go to the next page",
                        playerName,
                        page + 2
                    );
                }

                var mapName = string.Format(
                    "&f{0} Custom Models{1}",
                    all ? "All" : (
                        playerName == null
                            ? "Public"
                            : GetNameWithoutPlus(playerName) + "'s"
                        ),
                    page != 0 ? string.Format(" ({0})", page + 1) : ""
                );

                ushort spacing = 4;
                ushort width = (ushort)(
                    // edges
                    (spacing * 2) +
                    // grass blocks
                    modelNames.Count +
                    // inbetween blocks
                    ((modelNames.Count - 1) * (spacing - 1))
                );
                ushort height = 1;
                ushort length = 16;
                byte[] blocks = new byte[width * height * length];

                Level lvl = new Level(mapName, width, height, length, blocks);
                for (int i = 0; i < blocks.Length; i++) {
                    blocks[i] = 1;
                }

                lvl.SaveChanges = false;
                lvl.ChangedSinceBackup = false;

                lvl.IsMuseum = true;
                lvl.BuildAccess.Min = LevelPermission.Nobody;
                lvl.Config.Physics = 0;

                lvl.spawnx = spacing;
                lvl.spawny = 2;
                lvl.Config.HorizonBlock = 1;
                lvl.Config.EdgeLevel = 1;
                lvl.Config.CloudsHeight = -0xFFFFFF;
                lvl.Config.SidesOffset = 0;
                lvl.Config.Buildable = false;
                lvl.Config.Deletable = false;

                for (ushort i = 0; i < modelNames.Count; i++) {
                    ushort x = (ushort)(spacing + (i * spacing));
                    ushort y = 0;
                    ushort z = spacing;

                    blocks[lvl.PosToInt(x, y, z)] = 2;

                    var modelName = modelNames[i];

                    var storedModel = new StoredCustomModel(modelName);
                    if (storedModel.Exists()) {
                        storedModel.LoadFromFile();
                    }

                    var skinName = p.SkinName;
                    if (
                        !storedModel.usesHumanSkin &&
                        storedModel.defaultSkin != null
                    ) {
                        skinName = storedModel.defaultSkin;
                    }

                    // hack because clients strip + at the end
                    var botName = "&f" + (modelName.EndsWith("+") ? modelName + "&0+" : modelName);
                    var bot = new PlayerBot(botName, lvl) {
                        Model = modelName,
                        SkinName = skinName,
                    };
                    bot.SetInitialPos(Position.FromFeetBlockCoords(x, y + 1, z));
                    bot.SetYawPitch(Orientation.DegreesToPacked(180), Orientation.DegreesToPacked(0));
                    bot.ClickedOnText = "/CustomModel wear " + modelName;

                    _ = lvl.Bots.Add(bot);
                }

                if (!PlayerActions.ChangeMap(p, lvl)) return;
            }

            void Wear(Player p, string modelName, CommandData data) {

                // check if we should use default skin
                var storedCustomModel = new StoredCustomModel(modelName);
                if (!storedCustomModel.Exists()) {
                    p.Message("%WCustom Model %S{0} %Wnot found!", modelName);
                    return;
                }
                p.HandleCommand("XModel", modelName, data);

                storedCustomModel.LoadFromFile();

                if (
                    !storedCustomModel.usesHumanSkin &&
                    storedCustomModel.defaultSkin != null
                ) {
                    p.HandleCommand("Skin", "-own " + storedCustomModel.defaultSkin, data);
                }
            }
        }


	    public sealed class CmdBypassModelSizeLimit : CmdManageList {
		    public override string name { get { return "BypassModelSizeLimit"; } }
		    public override string shortcut { get { return ""; } }
		    public override string type { get { return CommandTypes.Other; } }
		    public override LevelPermission defaultRank { get { return LevelPermission.Operator; } }
            protected override string ListName { get { return "bypass model size limit"; } }
            protected override PlayerList list { get { return CustomModelsPlugin.bypassMaxSize; } }

            public static bool CanBypassSizeLimit(Player p) {
                if (CommandExtraPerms.Find("CustomModel", 1).UsableBy(p.Rank)) { return true; }
                return bypassMaxSize.Contains(p.name);
            }
	    }


        /// <summary>
        /// What a nice reusable template... too bad it's tucked away into this plugin lmao
        /// </summary>
        public abstract class CmdManageList : Command2 {
            public override bool museumUsable { get { return true; } }
            protected virtual string AddArg { get { return "add"; } }
            protected virtual string RemoveArg { get { return "remove"; } }
            protected abstract string ListName { get; }
            protected abstract PlayerList list { get; }

            public override void Use(Player p, string message, CommandData data) {
                if (message.Length == 0) { Help(p); return; }
                if (message.CaselessEq("list")) { UseList(p); return; }
                string[] args = message.SplitSpaces(2);
                if (args.Length == 1) { p.Message("You need to provide a player name."); return; }
                string func = args[0];
                string arg = args[1];
                if (func.CaselessEq("check")) { UseCheck(p, arg); return; }
                if (func.CaselessEq(AddArg)) { UseAdd(p, arg); return; }
                if (func.CaselessEq(RemoveArg)) { UseRemove(p, arg); return; }
                p.Message("Unknown argument \"{0}\"", func);
            }
            void UseCheck(Player p, string message) {
                string whoName = PlayerInfo.FindMatchesPreferOnline(p, message);
                if (whoName == null) { return; }

                if (list.Contains(whoName)) {
                    p.Message("{0}&S is in the {0} list.", p.FormatNick(whoName), ListName);
                } else {
                    p.Message("{0}&S is not in the {1) list.", p.FormatNick(whoName), ListName);
                }
            }
            void UseAdd(Player p, string message) {
                string whoName = PlayerInfo.FindMatchesPreferOnline(p, message);
                if (whoName == null) { return; }

                if (list.Add(whoName)) {
                    list.Save();
                    p.Message("Successfully added {0}&S to the {1} list.", p.FormatNick(whoName), ListName);
                } else {
                    p.Message("{0}&S is already in the {1} list.", p.FormatNick(whoName), ListName);
                    p.Message("Use &b/{0} {1}&S to remove.", name, RemoveArg);

                }
            }
            void UseRemove(Player p, string message) {
                string whoName = PlayerInfo.FindMatchesPreferOnline(p, message);
                if (whoName == null) { return; }

                if (list.Remove(whoName)) {
                    list.Save();
                    p.Message("Successfully removed {0}&S from the {1} list.", p.FormatNick(whoName), ListName);
                } else {
                    p.Message("{0}&S was not in the {1} list to begin with.", p.FormatNick(whoName), ListName);
                }
            }
            void UseList(Player p) {
                //public void Output(Player p, string group, string listCmd, string modifier)
                list.Output(p, "Players in the "+ListName+" list", name+" check -all", "all");
                return;
            }

            public override void Help(Player p) {
                p.Message("&T/{0} {1} [name] &H- adds to the {2} list.", name, AddArg, ListName);
                p.Message("&T/{0} {1} [name]", name, RemoveArg);
                p.Message("&T/{0} check [name] &H- find out if [name] is in the {1} list.", name, ListName);
                p.Message("&T/{0} list &H- display players in the {1} list.", name, ListName);
            }
        }
    } // class CustomModelsPlugin
} // namespace MCGalaxy
