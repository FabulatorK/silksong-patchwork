using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MonoMod.Utils;
using Patchwork;
using Patchwork.Util;
using TeamCherry.Localization;

public class DialogueHandler
{
    public static string TextDumpPath { get { return Path.Combine(Plugin.BasePath, "TextDumps"); } }
    public static string TextLoadPath { get { return Path.Combine(Plugin.BasePath, "Text"); } }

    public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> TextCache = [];

    public static void DumpText()
    {
        foreach(string lang in Language.GetLanguages())
        {
            Plugin.Logger.LogInfo($"--- Dumping language: {lang} ---");
            Language.SwitchLanguage(lang);
            foreach(string sheet in Language.GetSheets())
            {
                IOUtil.EnsureDirectoryExists(Path.Combine(TextDumpPath, sheet));
                string filePath = Path.Combine(TextDumpPath, sheet, $"{lang}.yml");
                using StreamWriter writer = new StreamWriter(filePath);
                Plugin.Logger.LogInfo($"# {sheet}:");
                foreach (var key in Language.GetKeys(sheet))
                {
                    string value = Language.Get(key, sheet);
                    Plugin.Logger.LogInfo($"  {key} = {value}");
                    writer.WriteLine($"{key}: \"{value.Replace("\"", "\\\"")}\"");
                }
                writer.Flush();
                writer.Close();
            }
        }
    }

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(Language), nameof(Language.Get), new[] { typeof(string), typeof(string) }),
            postfix: new HarmonyMethod(typeof(DialogueHandler), nameof(GetTextPostfix))
        );
    }

    private static void GetTextPostfix(string key, string sheetTitle, ref string __result)
    {
        Plugin.Logger.LogInfo($"Key requested: {key} from sheet: {sheetTitle}, value: {__result}");

        string lang = Language.CurrentLanguage().ToString();
        if (!TextCache.ContainsKey(lang))
            TextCache[lang] = [];
        if (!TextCache[lang].ContainsKey(sheetTitle))
            TextCache[lang][sheetTitle] = LoadTextSheet(sheetTitle, lang);
        if (TextCache[lang][sheetTitle].ContainsKey(key))
            __result = TextCache[lang][sheetTitle][key];
    }

    private static Dictionary<string, string> LoadTextSheet(string sheetTitle, string lang)
    {
        Dictionary<string, string> sheetData = new Dictionary<string, string>();
        sheetData.AddRange(LoadTextSheet(sheetTitle, lang, TextLoadPath));
        foreach (var packPath in Plugin.PluginPackPaths)
        {
            var packSheetData = LoadTextSheet(sheetTitle, lang, Path.Combine(packPath, "Text"));
            foreach (var kvp in packSheetData)
                sheetData[kvp.Key] = kvp.Value;
        }
        return sheetData;
    }

    private static Dictionary<string, string> LoadTextSheet(string sheetTitle, string lang, string basePath)
    {
        string filePath = Path.Combine(basePath, sheetTitle, $"{lang}.yml");
        Dictionary<string, string> sheetData = new Dictionary<string, string>();
        if (!File.Exists(filePath))
            return sheetData;
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;
            var splitIndex = line.IndexOf(':');
            if (splitIndex == -1)
                continue;
            string key = line.Substring(0, splitIndex).Trim();
            string value = line.Substring(splitIndex + 1).Trim().Trim('"').Replace("\\\"", "\"");
            sheetData[key] = value;
        }
        return sheetData;
    }
}