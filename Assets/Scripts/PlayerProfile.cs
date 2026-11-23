using System;
using UnityEngine;

// Saved player choices (name, color, settings). Everything lives in PlayerPrefs so it survives restarts.
public static class PlayerProfile
{
    public static readonly string[] Names = { "MONKE", "BANANA", "GORILLA", "JUNGLE", "KING", "SWINGER", "CHIMP", "SILVERBACK" };
    public static readonly string[] ColorNames = { "Orange", "Red", "Blue", "Green", "Purple", "Pink", "Yellow", "White" };
    public static readonly Color[] Colors =
    {
        new Color(1f, 0.55f, 0.1f), new Color(0.9f, 0.15f, 0.15f), new Color(0.2f, 0.4f, 1f), new Color(0.2f, 0.8f, 0.3f),
        new Color(0.6f, 0.25f, 0.9f), new Color(1f, 0.45f, 0.75f), new Color(1f, 0.9f, 0.2f), new Color(0.95f, 0.95f, 0.95f)
    };
    public static readonly string[] TurnNames = { "Slow", "Medium", "Fast" };
    public static readonly float[] TurnSpeeds = { 60f, 120f, 180f };
    public static readonly string[] MonsterNames = { "Slow", "Normal", "Fast" };
    public static readonly float[] MonsterSpeeds = { 2.2f, 3.4f, 4.8f };

    public static event Action Changed;

    static int Get(string k, int d) { return PlayerPrefs.GetInt("gt_" + k, d); }
    static void Set(string k, int v) { PlayerPrefs.SetInt("gt_" + k, v); PlayerPrefs.Save(); if (Changed != null) Changed(); }

    public static int NameIdx { get { return Mathf.Clamp(Get("name", 0), 0, Names.Length - 1); } set { Set("name", value % Names.Length); } }
    public static int ColorIdx { get { return Mathf.Clamp(Get("color", 0), 0, Colors.Length - 1); } set { Set("color", value % Colors.Length); } }
    public static int TurnIdx { get { return Mathf.Clamp(Get("turn", 1), 0, 2); } set { Set("turn", value % 3); } }
    public static int MonsterSpeedIdx { get { return Mathf.Clamp(Get("mspeed", 1), 0, 2); } set { Set("mspeed", value % 3); } }
    public static bool MonsterOn { get { return Get("monster", 1) == 1; } set { Set("monster", value ? 1 : 0); } }
    public static bool MirrorOn { get { return Get("mirror", 1) == 1; } set { Set("mirror", value ? 1 : 0); } }

    public static string Name { get { return Names[NameIdx]; } }
    public static Color Color { get { return Colors[ColorIdx]; } }
    public static float TurnSpeed { get { return TurnSpeeds[TurnIdx]; } }
    public static float MonsterSpeed { get { return MonsterSpeeds[MonsterSpeedIdx]; } }
}

// Owner unlock. The Mods and Moderator pages of the menu stay locked until the code is typed on the menu keypad.
// NOTE: this is client-side, so it keeps casual players out but is not real security (see README).
public static class OwnerState
{
    public const int Code = 123;

    public static bool Unlocked
    {
        get { return PlayerPrefs.GetInt("gt_owner", 0) == 1; }
        set { PlayerPrefs.SetInt("gt_owner", value ? 1 : 0); PlayerPrefs.Save(); }
    }
}
