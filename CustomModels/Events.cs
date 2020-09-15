using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {

        // [player name] = { model name }
        static readonly ConcurrentDictionary<string, HashSet<string>> SentCustomModels =
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

            var visibleModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                ModelInfo.GetRawModel(p.Model)
            };

            foreach (Player e in level.getPlayers()) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }
            foreach (PlayerBot e in level.Bots.Items) {
                visibleModels.Add(ModelInfo.GetRawModel(e.Model));
            }

            if (p.Extras.TryGet("TempBot_BotList", out object obj)) {
                if (obj != null) {
                    List<PlayerBot> botList = (List<PlayerBot>)obj;
                    foreach (var bot in botList) {
                        visibleModels.Add(ModelInfo.GetRawModel(bot.Model));
                    }
                }
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

            void checkEntity(Entity e) {
                if (new StoredCustomModel(e.Model).Exists()) {
                    e.UpdateModel(e.Model);
                }
            }

            // do ChangeModel on every entity with this model
            // so that we update the model on the client
            var loadedLevels = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            foreach (Player p in PlayerInfo.Online.Items) {
                checkEntity(p);

                if (p.Extras.TryGet("TempBot_BotList", out object obj)) {
                    if (obj != null) {
                        List<PlayerBot> botList = (List<PlayerBot>)obj;
                        foreach (var bot in botList) {
                            checkEntity(bot);
                        }
                    }
                }

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
            Debug("OnPlayerConnect {0}", p.name);

            SentCustomModels.TryAdd(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ModelNameToIdForPlayer.TryAdd(p.name, new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        }

        static void OnPlayerDisconnect(Player p, string reason) {
            Debug("OnPlayerDisconnect {0}", p.name);

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
            if (!canJoin) return;

            Debug("OnJoiningLevel {0} {1}", p.name, level.name);

            // send future/new model list to player
            CheckAddRemove(p, level);
        }

        static void OnJoinedLevel(Player p, Level prevLevel, Level level, ref bool announce) {
            Debug(
                "OnJoinedLevel {0} {1} -> {2}",
                p.name,
                prevLevel != null ? prevLevel.name : "null",
                level.name
            );

            if (prevLevel != null) {
                // tell other players still on the last map to remove our model
                // if we were the last one using that model
                foreach (Player e in prevLevel.getPlayers()) {
                    if (e == p) continue;
                    CheckAddRemove(e, prevLevel);
                }
            }
        }


        static readonly Memoizer1<string, SkinType> MemoizedGetSkinType =
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
        static readonly ConcurrentDictionary<Entity, TaskAndToken> GetSkinTypeTasks =
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

                if (MemoizedGetSkinType.GetCached(skinName, out SkinType skinType)) {
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
            if (cmd.CaselessEq("skin") && Hacks.CanUseHacks(p)) {
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
                            Command.Find("Skin").Use(
                                p,
                                "-own " + storedModel.defaultSkin,
                                data
                            );
                            p.cancelcommand = true;
                        }
                    } else if (splitArgs.Length > 0) {
                        var last = splitArgs[splitArgs.Length - 1];
                        MemoizedGetSkinType.Invalidate(last);
                    }
                }
            }
        }

    } // class CustomModelsPlugin
} // namespace MCGalaxy
