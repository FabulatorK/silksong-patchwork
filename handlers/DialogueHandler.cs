using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Patchwork;
using Patchwork.Util;
using TeamCherry.Localization;

public class DialogueHandler
{
    public static string TextDumpPath { get { return Path.Combine(Plugin.BasePath, "TextDumps"); } }

    public static Dictionary<string, Dictionary<string, string>> TextCache = new Dictionary<string, Dictionary<string, string>>();

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
    }
}