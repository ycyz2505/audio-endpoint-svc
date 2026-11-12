using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AudioEndpointSvc
{
    internal static class Json
    {
        public static string ToLine<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T FromLine<T>(string line)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        public static T FromBytes<T>(byte[] data)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(data))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        public static T LoadFile<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text == null || text.Trim().Length == 0) return null;
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        public static void SaveFile<T>(string path, T value)
        {
            string tmp = path + ".tmp";
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, value);
                stream.Flush();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
