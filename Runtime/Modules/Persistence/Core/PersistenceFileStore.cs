using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public static class PersistenceFileStore
    {
        public static string SaveDirectoryOverride { get; set; }

        #region Public API

        public static string GetSaveDirectory()
        {
            string path = !string.IsNullOrEmpty(SaveDirectoryOverride)
                ? SaveDirectoryOverride
                : Application.persistentDataPath;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        public static string GetSaveFilePath(int slotIndex)
        {
            return Path.Combine(GetSaveDirectory(), $"savegame_slot_{slotIndex}.json");
        }

        public static void WriteDocument(int slotIndex, PersistenceSaveDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            string targetPath = GetSaveFilePath(slotIndex);
            string tempPath = targetPath + ".tmp";
            string json = JsonConvert.SerializeObject(document, Formatting.Indented);

            File.WriteAllText(tempPath, json);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
        }

        public static bool TryReadDocument(int slotIndex, out PersistenceSaveDocument document)
        {
            document = null;
            string path = GetSaveFilePath(slotIndex);
            if (!File.Exists(path)) return false;

            string json = File.ReadAllText(path);
            document = JsonConvert.DeserializeObject<PersistenceSaveDocument>(json);
            document = PersistenceSaveDocument.MigrateToCurrentSchema(document);
            return document != null;
        }

        public static int DeleteSaveFiles()
        {
            string directory = GetSaveDirectory();
            if (!Directory.Exists(directory)) return 0;

            int deleted = 0;
            foreach (string file in Directory.GetFiles(directory, "savegame_slot_*.json"))
            {
                File.Delete(file);
                deleted++;
            }

            foreach (string file in Directory.GetFiles(directory, "savegame_slot_*.json.tmp"))
            {
                File.Delete(file);
            }

            return deleted;
        }

        #endregion
    }
}
