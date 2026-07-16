using System;
using System.Collections;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public static class UpdateChecker
{
    private const string ReleasesApi =
        "https://api.github.com/repos/coah80/questcraft-visor-releases/releases/latest";

    public static IEnumerator CheckForUpdate(Action<string> onUpdateAvailable)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(ReleasesApi))
        {
            request.SetRequestHeader("User-Agent", "QuestCraft-VISOR-Launcher");
            request.timeout = 15;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Update check failed: " + request.responseCode + " " + request.error);
                yield break;
            }

            string tag = null, url = null;
            try
            {
                JObject release = JObject.Parse(request.downloadHandler.text);
                tag = (string)release["tag_name"];
                url = (string)release["html_url"];
            }
            catch (Exception e)
            {
                Debug.Log("Update check: could not parse release info: " + e.Message);
            }

            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url)) yield break;

            Version latest = ExtractVersion(tag);
            Version current = ExtractVersion(Application.version);
            if (latest == null || current == null)
            {
                Debug.Log($"Update check: unparseable versions (current '{Application.version}', tag '{tag}')");
                yield break;
            }

            if (latest > current)
            {
                Debug.Log($"Update available: {current} -> {latest}");
                onUpdateAvailable(url);
            }
            else
            {
                Debug.Log($"Up to date ({current} >= {latest})");
            }
        }
    }

    private static Version ExtractVersion(string raw)
    {
        Match match = Regex.Match(raw ?? "", @"\d+(\.\d+)+");
        return match.Success && Version.TryParse(match.Value, out Version version) ? version : null;
    }
}
