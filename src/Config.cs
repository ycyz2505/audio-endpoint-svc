using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace CamWatch
{
    [DataContract]
    internal sealed class AppConfig
    {
        [DataMember(Name = "passwordHash")] public string PasswordHash;
        [DataMember(Name = "passwordSalt")] public string PasswordSalt;
        [DataMember(Name = "taskbarMode")] public string TaskbarMode;
        [DataMember(Name = "taskbarApps")] public List<string> TaskbarApps;
        [DataMember(Name = "retentionDays")] public int RetentionDays;
        [DataMember(Name = "pollMs")] public int PollMs;
        [DataMember(Name = "disguiseName")] public string DisguiseName;

        public static AppConfig CreateDefault()
        {
            AppConfig config = new AppConfig();
            config.PasswordHash = "";
            config.PasswordSalt = "";
            config.TaskbarMode = "any";
            config.TaskbarApps = new List<string>();
            config.RetentionDays = 90;
            config.PollMs = 1000;
            config.DisguiseName = "Realtek 高清晰音频管理器";
            return config;
        }

        public void Normalize()
        {
            if (PasswordHash == null) PasswordHash = "";
            if (PasswordSalt == null) PasswordSalt = "";
            if (TaskbarMode != "only" && TaskbarMode != "exclude") TaskbarMode = "any";
            if (TaskbarApps == null) TaskbarApps = new List<string>();
            if (RetentionDays < 1 || RetentionDays > 3650) RetentionDays = 90;
            if (PollMs < 500 || PollMs > 60000) PollMs = 1000;
            if (DisguiseName == null || DisguiseName.Trim().Length == 0) DisguiseName = "Realtek 高清晰音频管理器";
        }
    }

    internal sealed class ConfigStore
    {
        private readonly string _path;

        public AppConfig Value { get; private set; }

        public ConfigStore(string baseDir)
        {
            _path = Path.Combine(baseDir, "config.json");
            AppConfig loaded = null;
            try { loaded = Json.LoadFile<AppConfig>(_path); }
            catch { loaded = null; }
            if (loaded == null) loaded = AppConfig.CreateDefault();
            loaded.Normalize();
            Value = loaded;
        }

        public void Save()
        {
            Value.Normalize();
            Json.SaveFile<AppConfig>(_path, Value);
        }

        public bool HasPassword
        {
            get { return !string.IsNullOrEmpty(Value.PasswordHash) && !string.IsNullOrEmpty(Value.PasswordSalt); }
        }

        public void SetPassword(string password)
        {
            byte[] salt = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            byte[] hash = Derive(password, salt);
            Value.PasswordSalt = Convert.ToBase64String(salt);
            Value.PasswordHash = Convert.ToBase64String(hash);
            Save();
        }

        public bool CheckPassword(string password)
        {
            if (!HasPassword) return false;
            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(Value.PasswordSalt);
                expected = Convert.FromBase64String(Value.PasswordHash);
            }
            catch
            {
                return false;
            }
            byte[] actual = Derive(password, salt);
            return FixedTimeEquals(expected, actual);
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000))
            {
                return pbkdf2.GetBytes(32);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
