using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// One local save file for this machine (JSON in Application.persistentDataPath/save.json): coins, XP/level,
    /// best stars per level, hats and gear owned/worn, and lifetime totals. Written on every change (small file).
    /// On first run it copies the old PlayerPrefs values (stars_*, hats_owned, hat_equipped, hat_color) so nothing
    /// is lost. Steam Cloud can later sync this same file.
    /// </summary>
    public static class SaveSystem
    {
        [Serializable]
        public sealed class LevelStars { public string level; public int stars; }

        [Serializable]
        public sealed class Data
        {
            public int version = 1;
            public int coins;
            public int xp;
            public int level = 1;
            public List<LevelStars> stars = new();
            public int hatsOwned;
            public int hatEquipped;
            public int hatColor;
            public int gearOwned;
            public int gloves;
            public int glasses;
            public int deliveries;
            public int levelsPlayed;
        }

        private const string FileName = "save.json";
        private static Data _data;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Raised on this machine after any change (UI refresh).</summary>
        public static event Action Changed;

        public static Data Current
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    _data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                    if (_data != null) { _data.stars ??= new List<LevelStars>(); return; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[SaveSystem] Could not read save, starting fresh: " + e.Message); }
            _data = new Data();
            MigrateFromPlayerPrefs();
            Save();
        }

        private static void MigrateFromPlayerPrefs()
        {
            _data.hatsOwned = PlayerPrefs.GetInt("hats_owned", 0);
            _data.hatEquipped = PlayerPrefs.GetInt("hat_equipped", 0);
            _data.hatColor = PlayerPrefs.GetInt("hat_color", 0);
            foreach (string level in new[] { "Tutorial", "Level01", "Level02", "Level02 George" })
            {
                int stars = PlayerPrefs.GetInt("stars_" + level, 0);
                if (stars > 0) _data.stars.Add(new LevelStars { level = level, stars = stars });
            }
        }

        public static void Save()
        {
            if (_data == null) return;
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_data, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("[SaveSystem] Could not write save: " + e.Message); }
            Changed?.Invoke();
        }

        // ── Stars ──
        public static int Stars(string level)
        {
            foreach (var s in Current.stars) if (s.level == level) return s.stars;
            return 0;
        }

        public static void SetBestStars(string level, int stars)
        {
            foreach (var s in Current.stars)
                if (s.level == level) { if (stars > s.stars) { s.stars = stars; Save(); } return; }
            Current.stars.Add(new LevelStars { level = level, stars = stars });
            Save();
        }

        // ── Coins ──
        public static void SetCoins(int coins)
        {
            coins = Mathf.Clamp(coins, 0, 1000000);
            if (Current.coins == coins) return;
            Current.coins = coins;
            Save();
        }

        // ── Hats ──
        public static bool OwnsHat(int hat) => hat == 0 || (Current.hatsOwned & (1 << hat)) != 0;
        public static void MarkHatOwned(int hat) { Current.hatsOwned |= 1 << hat; Save(); }
        public static void SetHat(int hat, int color)
        {
            if (Current.hatEquipped == hat && Current.hatColor == color) return;
            Current.hatEquipped = hat; Current.hatColor = color; Save();
        }

        // ── Gear ──
        public static bool OwnsGear(int gearBit) => (Current.gearOwned & (1 << gearBit)) != 0;
        public static void MarkGearOwned(int gearBit) { Current.gearOwned |= 1 << gearBit; Save(); }
    }
}
