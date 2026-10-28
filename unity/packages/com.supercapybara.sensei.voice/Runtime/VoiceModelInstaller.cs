using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Sensei.Voice
{
    public static class VoiceModelInstaller
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        // Call from Unity's main thread: UnityWebRequest and Android JNI use it.
        public static async Task<string> EnsureInstalledAsync(string modelDirName, string version, CancellationToken ct)
        {
            CheckName(modelDirName);
            CheckName(version);
            await Gate.WaitAsync(ct);
            try
            {
                string target = Path.Combine(Application.persistentDataPath, modelDirName);
                string marker = Path.Combine(target, ".installed-" + version);
                if (File.Exists(marker)) return target;
                string staging = target + ".installing";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                try
                {
                    List<string> files = ListFiles(modelDirName);
                    if (files.Count == 0) throw new FileNotFoundException("No bundled files for " + modelDirName);
                    foreach (string file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        string destination = Path.GetFullPath(Path.Combine(staging, file));
                        if (!destination.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                            throw new IOException("Invalid model asset path");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        string source = Application.streamingAssetsPath + "/" + modelDirName + "/" + file;
                        byte[] bytes;
                        if (source.Contains("://"))
                        {
                            using (var request = UnityWebRequest.Get(source))
                            {
                                await SendAsync(request, ct);
                                bytes = request.downloadHandler.data;
                            }
                        }
                        else bytes = await Task.Run(() => File.ReadAllBytes(source), ct);
                        await Task.Run(() => File.WriteAllBytes(destination, bytes), ct);
                    }
                    ct.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(staging, ".installed-" + version), version);
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.Move(staging, target);
                    return target;
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }
            finally { Gate.Release(); }
        }

        private static void CheckName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Expected a directory/version name", nameof(name));
        }

        private static List<string> ListFiles(string model)
        {
            var files = new List<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
            string prefix = "assets/" + model + "/";
            using (var zip = new AndroidJavaObject("java.util.zip.ZipFile", Application.dataPath))
            {
                try
                {
                    using (var entries = zip.Call<AndroidJavaObject>("entries"))
                    {
                        while (entries.Call<bool>("hasMoreElements"))
                        {
                            using (var entry = entries.Call<AndroidJavaObject>("nextElement"))
                            {
                                string name = entry.Call<string>("getName");
                                if (!entry.Call<bool>("isDirectory") && name.StartsWith(prefix, StringComparison.Ordinal))
                                    files.Add(name.Substring(prefix.Length));
                            }
                        }
                    }
                }
                finally { zip.Call("close"); }
            }
#else
            string root = Path.Combine(Application.streamingAssetsPath, model);
            if (Directory.Exists(root))
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    if (!file.EndsWith(".meta", StringComparison.Ordinal)) files.Add(file.Substring(root.Length + 1).Replace('\\', '/'));
#endif
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        internal static async Task SendAsync(UnityWebRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (ct.IsCancellationRequested) { request.Abort(); ct.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            ct.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException("Speech HTTP request failed: status " + request.responseCode);
        }
    }
}
