using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[Serializable] public class ScoreEntry { public string name; public int color; public float time; }
[Serializable] class ScoreList { public List<ScoreEntry> items = new List<ScoreEntry>(); }

// Saved best survival times (how long you lasted before the monster got you).
public static class Scores
{
    const string Key = "gt_scores";
    const string DeathKey = "gt_deaths";

    public static int Deaths { get { return PlayerPrefs.GetInt(DeathKey, 0); } }

    public static List<ScoreEntry> Load()
    {
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return new List<ScoreEntry>();
        try
        {
            ScoreList l = JsonUtility.FromJson<ScoreList>(json);
            return (l != null && l.items != null) ? l.items : new List<ScoreEntry>();
        }
        catch (Exception) { return new List<ScoreEntry>(); }
    }

    public static void AddDeath(float survivedSeconds)
    {
        PlayerPrefs.SetInt(DeathKey, Deaths + 1);
        if (survivedSeconds >= 3f)
        {
            List<ScoreEntry> l = Load();
            l.Add(new ScoreEntry { name = PlayerProfile.Name, color = PlayerProfile.ColorIdx, time = survivedSeconds });
            l.Sort((a, b) => b.time.CompareTo(a.time));
            if (l.Count > 8) l.RemoveRange(8, l.Count - 8);
            Save(l);
        }
        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.DeleteKey(DeathKey);
        PlayerPrefs.Save();
    }

    static void Save(List<ScoreEntry> l)
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(new ScoreList { items = l }));
    }
}

// The leaderboard screen. It is placed on the map's "scoreboard" object (see GtagBootstrap.BuildBoard).
// Two canvases back to back, so it reads correctly from either side (a single canvas shows mirrored text from behind).
public class LeaderboardBoard : MonoBehaviour
{
    readonly System.Collections.Generic.List<Text> texts = new System.Collections.Generic.List<Text>();
    GtagBootstrap boot;
    float next;

    public static LeaderboardBoard Create(GtagBootstrap boot, Vector3 pos, Quaternion rot, float scale)
    {
        var root = new GameObject("LeaderboardBoard");
        var lb = root.AddComponent<LeaderboardBoard>();
        lb.boot = boot;
        Vector3 back = rot * Vector3.forward;   // canvas forward points away from the viewer
        lb.texts.Add(MakeFace("Front", root.transform, pos, rot, scale));
        lb.texts.Add(MakeFace("Back", root.transform, pos + back * 0.012f, rot * Quaternion.Euler(0f, 180f, 0f), scale));
        return lb;
    }

    static Text MakeFace(string name, Transform parent, Vector3 pos, Quaternion rot, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400f, 420f);
        rt.localScale = Vector3.one * scale;

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        bgGo.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 420f);
        bgGo.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.04f, 1f);

        var tGo = new GameObject("Text", typeof(RectTransform));
        tGo.transform.SetParent(go.transform, false);
        tGo.GetComponent<RectTransform>().sizeDelta = new Vector2(370f, 400f);
        var t = tGo.AddComponent<Text>();
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        t.font = f;
        t.fontSize = 24;
        t.supportRichText = true;
        t.alignment = TextAnchor.UpperLeft;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    static string Fmt(float s) { return string.Format("{0}:{1:00}", (int)s / 60, (int)s % 60); }
    static string Tag(string name, int colorIdx)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(PlayerProfile.Colors[Mathf.Clamp(colorIdx, 0, PlayerProfile.Colors.Length - 1)]) + ">" + name + "</color>";
    }

    void Update()
    {
        if (Time.unscaledTime < next || texts.Count == 0) return;
        next = Time.unscaledTime + 0.5f;

        var sb = new StringBuilder();
        sb.AppendLine("<b>LEADERBOARD</b>");
        sb.AppendLine("<size=18>Longest time alive</size>");
        sb.AppendLine();
        float now = boot != null ? boot.RunTime : 0f;
        sb.AppendLine("NOW  " + Tag(PlayerProfile.Name, PlayerProfile.ColorIdx) + "  " + Fmt(now));
        sb.AppendLine();
        List<ScoreEntry> l = Scores.Load();
        if (l.Count == 0) sb.AppendLine("<size=18>No times yet - survive the monster!</size>");
        for (int i = 0; i < l.Count; i++)
            sb.AppendLine((i + 1) + ". " + Tag(l[i].name, l[i].color) + "  " + Fmt(l[i].time));
        sb.AppendLine();
        sb.AppendLine("<size=18>Deaths: " + Scores.Deaths + "</size>");
        string txt = sb.ToString();
        foreach (Text t in texts) t.text = txt;
    }
}
