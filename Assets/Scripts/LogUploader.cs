using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class LogUploader
{
    private const string UploadUrl = "https://api.coah80.com/questcraft/logs";

    private const int MaxAttachments = 10;
    private const long MaxSingleFileBytes = 5 * 1024 * 1024;
    private const long MaxTotalBytes = 22 * 1024 * 1024;
    private const int CooldownSeconds = 60;
    private const string LastUploadKey = "logUploaderLastUploadUtc";

    private static readonly StringBuilder unityLog = new StringBuilder(64 * 1024);
    private static bool uploading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookUnityLog()
    {
        Application.logMessageReceived += (condition, stackTrace, type) =>
        {
            if (unityLog.Length > 1024 * 1024) return;
            unityLog.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] [")
                .Append(type).Append("] ").AppendLine(condition);
            if (type == LogType.Exception || type == LogType.Error)
                unityLog.AppendLine(stackTrace);
        };
    }

    public static IEnumerator SendLogs(UIHandler ui, string reason = null)
    {
        if (uploading)
        {
            ui.SetAndShowError("Log upload already in progress.");
            yield break;
        }

        int cooldownRemaining = GetCooldownRemaining();
        if (cooldownRemaining > 0)
        {
            ui.SetAndShowError("Please wait " + cooldownRemaining + " seconds before sending logs again.");
            yield break;
        }

        uploading = true;
        PlayerPrefs.SetString(LastUploadKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        PlayerPrefs.Save();

        string username = ui.loginHandler != null ? ui.loginHandler.selectedAccountUsername : null;
        bool loggedIn = !string.IsNullOrEmpty(username) && username != "Add Account";
        string hookName = loggedIn ? username : "QuestCraft VISOR";
        string avatarUrl = loggedIn ? "https://minotar.net/helm/" + username + "/100.png" : null;

        bool isCrash = !string.IsNullOrEmpty(reason);
        string title = isCrash ? reason : "QuestCraft VISOR Logs";
        int color = isCrash ? 0xD70A53 : 0x57F287;

        var payload = new StringBuilder();
        payload.Append("{\"username\": \"").Append(Escape(hookName)).Append('"');
        if (avatarUrl != null)
            payload.Append(", \"avatar_url\": \"").Append(Escape(avatarUrl)).Append('"');
        payload.Append(", \"embeds\": [{");
        payload.Append("\"title\": \"").Append(Escape(title)).Append('"');
        payload.Append(", \"color\": ").Append(color);
        payload.Append(", \"fields\": [");
        payload.Append("{\"name\": \"Player\", \"value\": \"").Append(Escape(loggedIn ? username : "not logged in")).Append("\", \"inline\": true},");
        payload.Append("{\"name\": \"Version\", \"value\": \"").Append(Escape(Application.version)).Append("\", \"inline\": true},");
        payload.Append("{\"name\": \"Device\", \"value\": \"").Append(Escape(SystemInfo.deviceModel)).Append("\", \"inline\": true},");
        payload.Append("{\"name\": \"OS\", \"value\": \"").Append(Escape(SystemInfo.operatingSystem)).Append("\", \"inline\": false}");
        payload.Append(']');
        if (avatarUrl != null)
            payload.Append(", \"thumbnail\": {\"url\": \"").Append(Escape(avatarUrl)).Append("\"}");
        payload.Append(", \"timestamp\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('"');
        payload.Append("}]}");

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("payload_json", payload.ToString())
        };

        long total = 0;
        int index = 0;
        foreach (var (path, name) in CollectLogFiles())
        {
            if (index >= MaxAttachments) break;
            byte[] data = ReadTail(path, MaxSingleFileBytes);
            if (data == null || data.Length == 0 || total + data.Length > MaxTotalBytes) continue;
            total += data.Length;
            form.Add(new MultipartFormFileSection("files[" + index + "]", data, name, "text/plain"));
            index++;
        }

        if (index < MaxAttachments)
        {
            byte[] launcherLog = Encoding.UTF8.GetBytes(unityLog.ToString());
            form.Add(new MultipartFormFileSection("files[" + index + "]", launcherLog, "launcher-unity.txt", "text/plain"));
            index++;
        }

        using (UnityWebRequest request = UnityWebRequest.Post(UploadUrl, form))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ui.SetAndShowError("Logs sent! (" + index + " files, " + (total / 1024) + " KB)");
            }
            else
            {
                ui.SetAndShowError("Log upload failed: " + request.responseCode + " " + request.error);
            }
        }
        uploading = false;
    }

    private static int GetCooldownRemaining()
    {
        if (!long.TryParse(PlayerPrefs.GetString(LastUploadKey, "0"), out long lastUpload))
            return 0;

        long elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUpload;
        if (elapsed >= CooldownSeconds)
            return 0;
        if (elapsed < 0)
            return CooldownSeconds;
        return CooldownSeconds - (int)elapsed;
    }

    public static long NewestCrashTimestamp()
    {
        long newest = 0;
        foreach (var (path, name) in CollectLogFiles())
        {
            bool isCrashFile = name.StartsWith("hs_err") || name.Contains("crash-");
            if (!isCrashFile) continue;
            long t = File.GetLastWriteTimeUtc(path).Ticks;
            if (t > newest) newest = t;
        }
        return newest;
    }

    private static List<(string path, string name)> CollectLogFiles()
    {
        string root = Application.persistentDataPath;
        var files = new List<(string, string)>();

        AddIfExists(files, Path.Combine(root, "latestlog.txt"), "latestlog.txt");

        string instancesDir = Path.Combine(root, "instances");
        if (Directory.Exists(instancesDir))
        {
            foreach (string inst in Directory.GetDirectories(instancesDir))
            {
                string instName = Path.GetFileName(inst);
                foreach (string gameDir in new[] { inst, Path.Combine(inst, ".minecraft") })
                {
                    AddGlob(files, Path.Combine(gameDir, "crash-reports"), "*.txt", 3,
                        f => instName + "-crash-" + Path.GetFileNameWithoutExtension(f) + ".txt");
                    AddIfExists(files, Path.Combine(gameDir, "logs", "latest.log"),
                        instName + "-latest.txt");
                    AddGlob(files, gameDir, "hs_err_*.log", 2,
                        f => instName + "-" + Path.GetFileNameWithoutExtension(f) + ".txt");
                }
            }
        }

        AddGlob(files, root, "hs_err_*.log", 2,
            f => "hs_err-" + Path.GetFileNameWithoutExtension(f) + ".txt");
        AddIfExists(files, Path.Combine(root, "instances.json"), "instances-json.txt");
        AddIfExists(files, Path.Combine(root, "launcher.conf"), "launcher-conf.txt");

        return files;
    }

    private static void AddIfExists(List<(string, string)> files, string path, string name)
    {
        if (File.Exists(path)) files.Add((path, name));
    }

    private static void AddGlob(List<(string, string)> files, string dir, string pattern,
        int newestCount, Func<string, string> nameFor)
    {
        if (!Directory.Exists(dir)) return;
        var matches = new List<string>(Directory.GetFiles(dir, pattern));
        matches.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        for (int i = 0; i < matches.Count && i < newestCount; i++)
            files.Add((matches[i], nameFor(matches[i])));
    }

    private static byte[] ReadTail(string path, long maxBytes)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0, fs.Length - maxBytes);
                fs.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[fs.Length - start];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = fs.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buffer;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read " + path + ": " + e.Message);
            return null;
        }
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
