// 文件功能：处理注入预览的Harmony补丁。
// File: HarmonyPatches.cs
using HarmonyLib;
using RimTalk.MemoryPatch; // 引用原版Settings类
using RimTalk.Memory.Debug; // 引用原版InjectionPreview类
using Verse;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;

namespace RimTalk_ExpandedPreview
{
    [HarmonyPatch(typeof(RimTalkMemoryPatchSettings), nameof(RimTalkMemoryPatchSettings.DoSettingsWindowContents))]
    public static class Patch_OpenInjectionPreviewButton
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var originalMethod = AccessTools.Constructor(typeof(Dialog_InjectionPreview));
            var replacementMethod = AccessTools.Constructor(typeof(Dialog_ExpandedInjectionPreview)); // 你的新类

            for (int i = 0; i < codes.Count; i++)
            {
                // 寻找 Widgets.ButtonText("注入预览器") 后的 new Dialog_InjectionPreview()
                if (codes[i].opcode == OpCodes.Newobj && codes[i].operand as ConstructorInfo == originalMethod)
                {
                    // 替换为 new Dialog_ExpandedInjectionPreview()
                    codes[i].operand = replacementMethod;
                    Log.Message("[RimTalk_ExpandedPreview] Successfully patched Dialog_InjectionPreview constructor call.");
                    yield return codes[i];
                }
                else
                {
                    yield return codes[i];
                }
            }
        }
    }
}
