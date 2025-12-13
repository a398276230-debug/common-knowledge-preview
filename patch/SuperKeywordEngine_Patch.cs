using HarmonyLib;
using Verse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // 需要这个命名空间来使用正则表达式
using System.Reflection;

// 文件功能：超级关键词引擎的Harmony补丁。
// 使用 [StaticConstructorOnStartup] 确保 Harmony 补丁在游戏启动时被应用
[StaticConstructorOnStartup]
public static class SuperKeywordEngine_Patch
{
    static SuperKeywordEngine_Patch()
    {
        try
        {
            var harmony = new Harmony("MEKP.RimTalkKnowledgePreview"); // 请替换为你的 Mod 唯一 ID

            // 在运行时通过已加载程序集查找目标类型，避免加载顺序问题导致的 null
            Type targetType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); } catch { return Type.EmptyTypes; }
                })
                .FirstOrDefault(t => t.FullName == "RimTalk.Memory.SuperKeywordEngine");

            if (targetType == null)
            {
                Log.Error("[RimTalkKeywordCleaner] Could not find type RimTalk.Memory.SuperKeywordEngine in loaded assemblies.");
                return;
            }

            // 使用明确的方法签名查找原始方法，避免重载或可选参数导致匹配失败
            var original = targetType.GetMethod(
                "ExtractKeywords",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(string), typeof(int) },
                null
            );

            if (original == null)
            {
                Log.Error("[RimTalkKeywordCleaner] Could not find method ExtractKeywords(string, int) on RimTalk.Memory.SuperKeywordEngine.");
                return;
            }

            var prefix = new HarmonyMethod(typeof(SuperKeywordEngine_Patch).GetMethod(nameof(PreprocessExtractKeywordsPrefix), BindingFlags.Static | BindingFlags.Public));

            harmony.Patch(original, prefix: prefix);

            Log.Message("[RimTalkKeywordCleaner] Applied Harmony patch to SuperKeywordEngine.ExtractKeywords to clean input text.");
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalkKeywordCleaner] Failed to apply Harmony patch: {ex}");
        }
    }

    // 这是你的前缀补丁方法
    // 参数 'ref string text' 允许你修改原始方法的 'text' 参数
    // 参数 'ref List<RimTalk.Memory.WeightedKeyword> __result' 允许你在前缀中设置结果并跳过原始方法
    // 返回 'bool'：如果返回 true，则原始方法会继续执行；如果返回 false，则原始方法将被跳过。
    public static bool PreprocessExtractKeywordsPrefix(ref string text, int maxKeywords, ref List<RimTalk.Memory.WeightedKeyword> __result)
    {
        if (string.IsNullOrEmpty(text))
        {
            // 如果传入的文本为空，直接返回空列表，并跳过原始方法
            __result = new List<RimTalk.Memory.WeightedKeyword>();
            return false;
        }

        // 调用我们自定义的文本预处理方法
        text = CleanText(text);

        // 如果预处理后文本变为空，也直接返回空列表，并跳过原始方法
        if (string.IsNullOrEmpty(text))
        {
            __result = new List<RimTalk.Memory.WeightedKeyword>();
            return false;
        }

        // 返回 true，让原始的 ExtractKeywords 方法继续执行，但它将使用我们修改后的 'text' 参数
        return true;
    }

    // --- 文本预处理逻辑 (与之前在 SuperKeywordEngine 中建议的类似) ---
    // 注意：将正则表达式定义为静态只读字段可以提高性能，避免每次调用时都重新编译。
    // 使用通用的 Unicode 类来匹配所有字母和数字，防止某些 Unicode 属性名在不同运行时下不可用
    private static readonly Regex _nonLetterNumberSpaceRegex = new Regex(
        @"[^\p{L}\p{N}\s]",
        RegexOptions.Compiled
    );
    private static readonly Regex _multipleSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

    private static string CleanText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return rawText;

        // 1. 去除所有非字母、数字和空白的字符，替换为单个空格
        string cleanedText = _nonLetterNumberSpaceRegex.Replace(rawText, " ");

        // 2. 将多个连续的空白字符替换为单个空格，并移除字符串两端的空格
        cleanedText = _multipleSpaceRegex.Replace(cleanedText, " ").Trim();

        return cleanedText;
    }

    // 你的 Mod 类 (RimTalk_ExpandedPreviewMod.cs) 保持不变，
    // 只需要确保它的构造函数中调用了 Harmony.PatchAll(Assembly.GetExecutingAssembly())
    // 或者，如果你只打这一个补丁，可以直接在 StaticConstructorOnStartup 中进行 Harmony.Patch 调用，
    // 这样你的 Mod 类甚至不需要有 Harmony 实例化和 PatchAll 的逻辑。
    // 对于你提供的 RimTalk_ExpandedPreviewMod.cs，它会通过 PatchAll 找到这个静态类中的补丁。
}
