// 文件功能：RimTalk扩展预览模组的主类。
using HarmonyLib;
using Verse;
using System.Reflection;
using UnityEngine; // For Rect
using System.Collections.Generic; // For List

namespace RimTalk_ExpandedPreview
{
    public class RimTalk_ExpandedPreviewMod : Mod
    {
        public static ExpandedPreviewSettings Settings; // 将 ModSettings 类型改为你的新类名

        public RimTalk_ExpandedPreviewMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ExpandedPreviewSettings>(); // 从 Verse.Mod 的方法获取设置实例

            var harmony = new Harmony("MEKP.RimTalkKnowledgePreview"); // 替换为你的唯一ID
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[RimTalk_ExpandedPreview] Mod loaded and Harmony patches applied.");
        }

        public override string SettingsCategory() => "RimTalk 扩展关键词"; // Mod 设置界面的标题

        // 修改：将设置界面简化为两个按钮：全局重要关键词、存档重要关键词。
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("RTExpPrev_Settings_Title".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            GUI.color = Color.gray;
            listing.Label("RTExpPrev_Settings_Description_Short".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);

            // 全局设置按钮（始终可用）
            Rect globalRect = listing.GetRect(40f);
            if (Widgets.ButtonText(globalRect, "全局重要关键词"))
            {
                Find.WindowStack.Add(new Dialog_MECPSettings());
            }

            listing.Gap(6f);

            // 存档设置按钮（仅在进入游戏后可用）
            Rect saveRect = listing.GetRect(40f);
            bool inGame = Current.Game != null;
            if (!inGame)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.DrawBox(saveRect);
                Widgets.Label(saveRect.ContractedBy(6f), "存档重要关键词（仅在进入存档后打开）");
                GUI.color = Color.white;
            }
            else
            {
                if (Widgets.ButtonText(saveRect, "存档重要关键词"))
                {
                    Find.WindowStack.Add(new Dialog_SaveGameKeywords());
                }
            }

            listing.Gap(12f);
            GUI.color = new Color(0.7f, 0.9f, 1f);
            listing.Label("RTExpPrev_Settings_Hint".Translate());
            GUI.color = Color.white;

            listing.End();
        }
    }
}
