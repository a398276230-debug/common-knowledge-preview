// 文件功能：处理主标签页的Harmony补丁。
// File: HarmonyPatches.cs (添加以下内容)
using HarmonyLib;
using RimTalk.Memory.Debug; // 引用原版InjectionPreview类
using RimTalk.Memory.UI;    // 引用原版MainTabWindow_Memory类
using RimTalk.MemoryPatch;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace RimTalk_ExpandedPreview
{
    // ... (之前的 Patch_OpenInjectionPreviewButton 保持不变) ...

    [HarmonyPatch(typeof(MainTabWindow_Memory), "DrawPawnSelection")]
    public static class Patch_OpenInjectionPreviewMainTabButton
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            // 目标：替换 new RimTalk.Memory.Debug.Dialog_InjectionPreview()
            var originalMethod = AccessTools.Constructor(typeof(Dialog_InjectionPreview));
            // 替换为：new RimTalk_ExpandedPreview.Dialog_ExpandedInjectionPreview()
            var replacementMethod = AccessTools.Constructor(typeof(Dialog_ExpandedInjectionPreview));

            for (int i = 0; i < codes.Count; i++)
            {
                // 寻找 Widgets.ButtonText("RimTalk_UI_Preview".Translate()) 后的 new Dialog_InjectionPreview()
                // 精确匹配，确保只替换preview按钮的逻辑
                if (codes[i].opcode == OpCodes.Newobj && codes[i].operand as ConstructorInfo == originalMethod)
                {
                    // 检查前几行指令，确保是在预览按钮的IF块内
                    // 典型的模式是：
                    // call Widgets.ButtonText(...)
                    // brfalse/brtrue
                    // ldarg.0 / call Find.WindowStack.Add
                    // newobj Dialog_InjectionPreview
                    // callvirt Find.WindowStack.Add

                    // 向前查找 ButtonText 调用，以确保我们是正确的按钮
                    bool isPreviewButton = false;
                    for (int j = Math.Max(0, i - 10); j < i; j++) // 往前看10条指令
                    {
                        if (codes[j].opcode == OpCodes.Call && codes[j].operand is MethodInfo method && method.Name == "ButtonText")
                        {
                            // 检查 ButtonText 的参数是否是 "RimTalk_UI_Preview".Translate()
                            // 这通常涉及到 ldsfld 然后 call Translate()
                            // 实际的字符串可能在更前面的指令中加载
                            // 简化检查：只要是 ButtonText 后面跟着 Newobj Dialog_InjectionPreview 就可以假定是它
                            isPreviewButton = true; // 找到了 ButtonText
                            break;
                        }
                    }

                    if (isPreviewButton)
                    {
                        codes[i].operand = replacementMethod;
                        Log.Message("[RimTalk_ExpandedPreview] Successfully patched Dialog_InjectionPreview constructor call in MainTabWindow_Memory.DrawPawnSelection.");
                    }
                }
            }
            return codes;
        }
    }
}
