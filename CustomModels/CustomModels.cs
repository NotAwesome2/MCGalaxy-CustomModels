using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using MCGalaxy.Events.EntityEvents;
using MCGalaxy.Events.PlayerEvents;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin : Plugin {
        public override string name => "CustomModels";
        public override string MCGalaxy_Version => "1.9.2.2";
        public override string creator => "SpiralP & Goodly";

        //------------------------------------------------------------------bbmodel/ccmodel file loading

        // Path.GetExtension includes the period "."
        const string BBModelExt = ".bbmodel";
        const string CCModelExt = ".ccmodel";
        const string PublicModelsDirectory = "plugins/models/";
        const string PersonalModelsDirectory = "plugins/personal_models/";

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

        //------------------------------------------------------------------ plugin interface


        static CmdCustomModel command = null;
        static HttpSkinServer httpSkinServer = null;
        public override void Load(bool startup) {
            command = new CmdCustomModel();
            Command.Register(command);

            OnPlayerConnectEvent.Register(OnPlayerConnect, Priority.Low);
            OnPlayerDisconnectEvent.Register(OnPlayerDisconnect, Priority.Low);
            OnJoiningLevelEvent.Register(OnJoiningLevel, Priority.Low);
            OnJoinedLevelEvent.Register(OnJoinedLevel, Priority.Low);
            OnSendingModelEvent.Register(OnSendingModel, Priority.Low);
            OnPlayerCommandEvent.Register(OnPlayerCommand, Priority.Low);
            OnEntitySpawnedEvent.Register(OnEntitySpawned, Priority.Low);

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

            httpSkinServer = new HttpSkinServer();
            httpSkinServer.Start();
        }

        public override void Unload(bool shutdown) {
            Debug("Unload");
            if (httpSkinServer != null) {
                httpSkinServer.Stop();
                httpSkinServer = null;
            }

            SentCustomModels.Clear();
            ModelNameToIdForPlayer.Clear();

            OnPlayerConnectEvent.Unregister(OnPlayerConnect);
            OnPlayerDisconnectEvent.Unregister(OnPlayerDisconnect);
            OnJoiningLevelEvent.Unregister(OnJoiningLevel);
            OnJoinedLevelEvent.Unregister(OnJoinedLevel);
            OnSendingModelEvent.Unregister(OnSendingModel);
            OnPlayerCommandEvent.Unregister(OnPlayerCommand);
            OnEntitySpawnedEvent.Unregister(OnEntitySpawned);

            if (command != null) {
                Command.Unregister(command);
                command = null;
            }
        }

    } // class CustomModelsPlugin

} // namespace MCGalaxy
