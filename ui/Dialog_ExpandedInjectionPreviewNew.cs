// File: Dialog_ExpandedInjectionPreview.cs
// 文件功能：提供扩展的注入预览功能的UI对话框。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
using RimTalk.Memory;
using RimTalk.Memory.Debug;
using RimTalk.MemoryPatch;
using System.Reflection; // 用于访问私有字段，如果需要的话

namespace RimTalk_ExpandedPreview
{
    /// <summary>
    /// 增强型调试预览器 - 添加关键词和常识评分分析
    /// </summary>
    public class Dialog_ExpandedInjectionPreview : Window
    {
        // 继承自原版的一些字段 (为了简化，直接复制过来，如果原版是protected则可以直接使用)
        private Pawn selectedPawn;
        private Pawn targetPawn;
        private Vector2 scrollPosition;
        private string cachedPreview = "";
        private int cachedMemoryCount = 0;
        private int cachedKnowledgeCount = 0;
        private string contextInput = "";
        private PawnKeywordInfo lastTargetPawnKeywordInfo;
        // 新增字段
        private List<KnowledgeScoreDetail> allKnowledgeScoreDetails; // 存储所有常识的详细评分
        private KeywordExtractionInfo lastKeywordInfo; // 存储关键词提取信息
        private Vector2 keywordScrollPosition;
        private Vector2 knowledgeScoreScrollPosition;
        private bool showPawnKeywords = false; // 用于切换显示Pawn关键词的详细分类
        private bool showAllKnowledgeScores = false; // 用于切换显示所有常识（包括未通过阈值的）

        public override Vector2 InitialSize => new Vector2(1200f, 900f); // 扩大窗口尺寸以容纳更多信息

        public Dialog_ExpandedInjectionPreview()
        {
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;

            // 默认选择第一个殖民者
            if (Find.CurrentMap != null)
            {
                selectedPawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float yPos = 0f;

            // 标题
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.9f, 0.7f);
            Widgets.Label(new Rect(0f, yPos, inRect.width, 35f), "RTExpPrev_Preview_Title".Translate()); // 🔍 增强型调试预览器 - RimTalk JSON 模拟
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            yPos += 40f;

            // 殖民者选择器（当前角色 + 目标角色）
            DrawPawnSelectors(new Rect(0f, yPos, inRect.width, 80f));
            yPos += 85f;

            if (selectedPawn == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, inRect.height / 2 - 20f, inRect.width, 40f),
                    "RTExpPrev_Preview_NoColonistsAvailable".Translate()); // 没有可用的殖民者\n\n请进入游戏并加载存档
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // 上下文输入框
            DrawContextInput(new Rect(0f, yPos, inRect.width, 80f));
            yPos += 85f;

            // 统计信息
            DrawStats(new Rect(0f, yPos, inRect.width, 80f));
            yPos += 85f;

            // 刷新按钮及关键词窗口按钮（将关键词按钮放在刷新按钮左侧）
            float refreshW = 100f;
            float refreshH = 35f;
            Rect refreshButtonRect = new Rect(inRect.width - refreshW - 10f, yPos, refreshW, refreshH);

            // 两个关键词按钮尺寸与间距（会放在刷新按钮左侧）
            float kbBtnWidth = 140f;
            float kbBtnHeight = 30f;
            float kbSpacing = 8f;
            float kbTotalWidth = kbBtnWidth * 2f + kbSpacing;
            float kbLeftX = refreshButtonRect.x - kbSpacing - kbTotalWidth;
            Rect globalBtnRect = new Rect(kbLeftX, yPos + (refreshH - kbBtnHeight) / 2f, kbBtnWidth, kbBtnHeight);
            if (Widgets.ButtonText(globalBtnRect, "RTExpPrev_Preview_GlobalKeywordsButton".Translate("全局重要关键词")))
            {
                Find.WindowStack.Add(new Dialog_MECPSettings());
            }

            Rect saveBtnRect = new Rect(globalBtnRect.xMax + kbSpacing, globalBtnRect.y, kbBtnWidth, kbBtnHeight);
            bool inGame = Current.Game != null;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = inGame;
            if (Widgets.ButtonText(saveBtnRect, "RTExpPrev_Preview_SaveKeywordsButton".Translate("存档重要关键词")))
            {
                Find.WindowStack.Add(new Dialog_SaveGameKeywords());
            }
            GUI.enabled = prevEnabled;

            // 刷新按钮（放在最右侧）
            if (Widgets.ButtonText(refreshButtonRect, "RTExpPrev_Preview_RefreshButton".Translate())) // 刷新预览
            {
                RefreshPreview();
            }
            yPos += refreshH + 5f;

            // 分两列显示：左边是JSON模拟，右边是关键词和评分详情
            float leftWidth = inRect.width * 0.5f - 10f;
            float rightWidth = inRect.width * 0.5f - 10f;
            float sectionHeight = inRect.height - yPos - 50f;

            // 左列：RimTalk JSON模拟
            Rect leftColumnRect = new Rect(0f, yPos, leftWidth, sectionHeight);
            DrawPreview(leftColumnRect);

            // 右列：关键词提取和常识评分详情
            Rect rightColumnRect = new Rect(leftWidth + 20f, yPos, rightWidth, sectionHeight);
            DrawDetailedAnalysis(rightColumnRect);
        }

        private void DrawPawnSelectors(Rect rect)
        {
            // 第一行：当前角色选择器
            GUI.color = new Color(0.8f, 0.9f, 1f);
            Widgets.Label(new Rect(rect.x, rect.y, 120f, rect.height / 2), "RTExpPrev_Preview_CurrentPawnLabel".Translate()); // 当前角色：
            GUI.color = Color.white;

            Rect buttonRect = new Rect(rect.x + 130f, rect.y, 200f, 35f);

            string label = selectedPawn != null ? selectedPawn.LabelShort : "RTExpPrev_Preview_None".Translate().ToString(); // 无
            if (Widgets.ButtonText(buttonRect, label))
            {
                ShowPawnSelectionMenu(isPrimary: true);
            }

            // 显示选中殖民者的基本信息
            if (selectedPawn != null)
            {
                GUI.color = Color.gray;
                string info = $"{selectedPawn.def.label}";
                if (selectedPawn.gender != null)
                    info += $" | {selectedPawn.gender.GetLabel()}";
                Widgets.Label(new Rect(rect.x + 340f, rect.y + 8f, 300f, rect.height / 2), info);
                GUI.color = Color.white;
            }

            // 第二行：目标角色选择器
            float secondRowY = rect.y + 40f;
            GUI.color = new Color(1f, 0.9f, 0.8f);
            Widgets.Label(new Rect(rect.x, secondRowY, 120f, rect.height / 2), "RTExpPrev_Preview_TargetPawnLabel".Translate()); // 目标角色：
            GUI.color = Color.white;

            Rect targetButtonRect = new Rect(rect.x + 130f, secondRowY, 200f, 35f);

            string targetLabel = targetPawn != null ? targetPawn.LabelShort : "RTExpPrev_Preview_NoneClickToSelect".Translate().ToString(); // 无（点击选择）
            if (Widgets.ButtonText(targetButtonRect, targetLabel))
            {
                ShowPawnSelectionMenu(isPrimary: false);
            }

            // 显示目标角色信息
            if (targetPawn != null)
            {
                GUI.color = Color.gray;
                string targetInfo = $"{targetPawn.def.label}";
                if (targetPawn.gender != null)
                    targetInfo += $" | {targetPawn.gender.GetLabel()}";
                Widgets.Label(new Rect(rect.x + 340f, secondRowY + 8f, 300f, rect.height / 2), targetInfo);
                GUI.color = Color.white;

                // 清除按钮
                Rect clearButtonRect = new Rect(rect.x + 650f, secondRowY, 80f, 35f);
                if (Widgets.ButtonText(clearButtonRect, "RTExpPrev_Preview_ClearButton".Translate())) // 清除
                {
                    targetPawn = null;
                    cachedPreview = ""; // 清空缓存
                    allKnowledgeScoreDetails = null; // 清空新缓存
                    lastKeywordInfo = null; // 清空新缓存
                }
            }
        }

        private void ShowPawnSelectionMenu(bool isPrimary)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (Find.CurrentMap != null)
            {
                foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                {
                    Pawn localPawn = pawn;

                    string optionLabel = pawn.LabelShort;
                    if (!isPrimary && selectedPawn != null && pawn == selectedPawn)
                    {
                        optionLabel += " " + "RTExpPrev_Preview_SameAsCurrent".Translate(); // (与当前角色相同)
                    }

                    options.Add(new FloatMenuOption(optionLabel, delegate
                    {
                        if (isPrimary)
                        {
                            selectedPawn = localPawn;
                            if (targetPawn == localPawn)
                            {
                                targetPawn = null;
                            }
                        }
                        else
                        {
                            targetPawn = localPawn;
                        }
                        cachedPreview = "";
                        allKnowledgeScoreDetails = null;
                        lastKeywordInfo = null;
                    }));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawStats(Rect rect)
        {
            if (selectedPawn == null) return;

            var memoryComp = selectedPawn.TryGetComp<FourLayerMemoryComp>();
            if (memoryComp == null)
            {
                GUI.color = Color.yellow;
                Widgets.Label(rect, "RTExpPrev_Preview_NoMemoryComponent".Translate()); // 该殖民者没有记忆组件
                GUI.color = Color.white;
                return;
            }

            // 背景框
            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f));
            Rect innerRect = rect.ContractedBy(5f);

            float x = innerRect.x;
            float lineHeight = Text.LineHeight;

            // 第一行 - 记忆统计
            GUI.color = new Color(0.8f, 1f, 0.8f);
            Widgets.Label(new Rect(x, innerRect.y, 150f, lineHeight), "RTExpPrev_Preview_MemoryStatsHeader".Translate()); // 记忆层级统计：
            GUI.color = Color.white;

            x += 120f;
            GUI.color = new Color(0.7f, 0.7f, 1f);
            Widgets.Label(new Rect(x, innerRect.y, 250f, lineHeight),
                "RTExpPrev_Preview_ABMStats".Translate(memoryComp.ActiveMemories.Count)); // ABM: {0}/6 (固定，不注入)
            GUI.color = Color.white;

            x += 270f; // Adjusted for longer ABM string
            Widgets.Label(new Rect(x, innerRect.y, 150f, lineHeight),
                "RTExpPrev_Preview_SCMStats".Translate(memoryComp.SituationalMemories.Count)); // SCM: {0}

            x += 120f;
            Widgets.Label(new Rect(x, innerRect.y, 150f, lineHeight),
                "RTExpPrev_Preview_ELSStats".Translate(memoryComp.EventLogMemories.Count)); // ELS: {0}

            x += 120f;
            Widgets.Label(new Rect(x, innerRect.y, 150f, lineHeight),
                "RTExpPrev_Preview_CLPAStats".Translate(memoryComp.ArchiveMemories.Count)); // CLPA: {0}

            // 第二行 - 常识统计
            x = innerRect.x;
            GUI.color = new Color(1f, 1f, 0.8f);
            Widgets.Label(new Rect(x, innerRect.y + lineHeight + 5f, 150f, lineHeight), "RTExpPrev_Preview_KnowledgeStatsHeader".Translate()); // 常识库统计：
            GUI.color = Color.white;

            x += 120f;
            var library = MemoryManager.GetCommonKnowledge();
            int totalKnowledge = library.Entries.Count;
            int enabledKnowledge = library.Entries.Count(e => e.isEnabled);

            Widgets.Label(new Rect(x, innerRect.y + lineHeight + 5f, 300f, lineHeight),
                "RTExpPrev_Preview_KnowledgeStats".Translate(totalKnowledge, enabledKnowledge)); // 总数: {0} | 启用: {1}

            // 第三行 - 注入配置
            x = innerRect.x;
            GUI.color = new Color(0.8f, 0.8f, 1f);
            Widgets.Label(new Rect(x, innerRect.y + lineHeight * 2 + 10f, 150f, lineHeight), "RTExpPrev_Preview_InjectionConfigHeader".Translate()); // 注入配置：
            GUI.color = Color.white;

            x += 120f;
            var settings = RimTalkMemoryPatchMod.Settings;
            if (settings != null)
            {
                string mode = settings.useDynamicInjection ? "RTExpPrev_Preview_ModeDynamic".Translate() : "RTExpPrev_Preview_ModeStatic".Translate(); // 动态评分 / 静态顺序
                Widgets.Label(new Rect(x, innerRect.y + lineHeight * 2 + 10f, 700f, lineHeight),
                    "RTExpPrev_Preview_ConfigDetails".Translate(
                        mode,
                        settings.maxInjectedMemories,
                        settings.maxInjectedKnowledge,
                        settings.memoryScoreThreshold.ToString("F2"),
                        settings.knowledgeScoreThreshold.ToString("F2")
                    )
                );
            }
        }

        private void DrawPreview(Rect rect)
        {
            // 按钮位置由外层 DoWindowContents 控制（已经绘制在刷新按钮左侧），此处只绘制预览内容

            // 如果缓存为空，自动刷新
            if (string.IsNullOrEmpty(cachedPreview))
            {
                RefreshPreview();
            }

            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            // 标题
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.7f, 0.9f, 1f);
            Widgets.Label(rect.ContractedBy(5f).TopPart(0.05f), "RTExpPrev_Preview_JSONSimulationHeader".Translate()); // RimTalk JSON请求模拟
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect scrollArea = rect.ContractedBy(5f);
            scrollArea.yMin += rect.height * 0.05f; // 为标题留出空间

            // 滚动视图
            float contentHeight = Text.CalcHeight(cachedPreview, scrollArea.width - 20f);
            Rect viewRect = new Rect(0f, 0f, scrollArea.width - 20f, contentHeight + 50f);

            Widgets.BeginScrollView(scrollArea, ref scrollPosition, viewRect);

            // 显示内容
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), cachedPreview);
            GUI.color = Color.white;

            Widgets.EndScrollView();
        }

        private void DrawDetailedAnalysis(Rect rect)
        {
            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            Rect innerRect = rect.ContractedBy(5f);
            float currentY = innerRect.y;

            // 关键词提取详情
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.8f, 0.5f);
            Widgets.Label(new Rect(innerRect.x, currentY, innerRect.width, 30f), "RTExpPrev_Preview_KeywordDetailsHeader".Translate()); // 🔑 关键词提取详情
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            currentY += 35f;

            Rect keywordArea = new Rect(innerRect.x, currentY, innerRect.width, innerRect.height * 0.5f);
            Widgets.DrawBoxSolid(keywordArea, new Color(0.15f, 0.15f, 0.15f, 0.5f)); // 关键词区域背景

            DrawKeywordExtraction(keywordArea.ContractedBy(5f));
            currentY += keywordArea.height + 10f; // 10f 是间隔

            // 常识评分详情
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.8f, 1f, 0.8f);
            Widgets.Label(new Rect(innerRect.x, currentY, innerRect.width, 30f), "RTExpPrev_Preview_KnowledgeScoringHeader".Translate()); // 🎓 常识评分详情
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            currentY += 35f;

            Rect knowledgeScoreArea = new Rect(innerRect.x, currentY, innerRect.width, innerRect.height - (currentY - innerRect.y));
            Widgets.DrawBoxSolid(knowledgeScoreArea, new Color(0.15f, 0.15f, 0.15f, 0.5f)); // 常识评分区域背景

            DrawKnowledgeScoring(knowledgeScoreArea.ContractedBy(5f));
        }

        private void DrawKeywordExtraction(Rect rect)
        {
            if (lastKeywordInfo == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "RTExpPrev_Preview_ClickRefreshKeywords".Translate());
                GUI.color = Color.white;
                return;
            }
            // --- 核心改动：将整个区域变为一个大的滚动视图 ---
            float totalContentHeight = 0f;
            float viewWidth = rect.width - 16f; // 为滚动条留出空间
                                                // --- 预计算所有内容的总高度 (这部分保持不变) ---
            totalContentHeight += Text.LineHeight; // 上下文
            if (lastKeywordInfo.PawnInfo != null) totalContentHeight += Text.LineHeight;
            if (lastTargetPawnKeywordInfo != null) totalContentHeight += Text.LineHeight;
            totalContentHeight += Text.LineHeight; // 总计
            totalContentHeight += 5f; // 间隔
            totalContentHeight += Text.LineHeight; // "核心关键词"
            totalContentHeight += 30f; // 复选框 + 间隔
                                       // 构建关键词列表字符串以便计算高度 (这部分保持不变)
            var sb = new StringBuilder();
            // ⭐ 注意：我们只显示ContextKeywords，但复制时需要它们
            string contextKw = lastKeywordInfo.ContextKeywords.Any() ? string.Join(", ", lastKeywordInfo.ContextKeywords) : "RTExpPrev_Preview_None".Translate().ToString();
            sb.AppendLine(contextKw);
            sb.AppendLine();
            if (showPawnKeywords)
            {
                if (lastKeywordInfo.PawnInfo != null)
                {
                    var pawnInfo = lastKeywordInfo.PawnInfo;
                    sb.AppendLine("RTExpPrev_Preview_PawnKeywordsCategoryHeader".Translate(pawnInfo.PawnName));
                    AppendKeywords(sb, "RTExpPrev_Preview_NameKeywords".Translate(), pawnInfo.NameKeywords);
                    // ... (省略所有 AppendKeywords 调用)
                    AppendKeywords(sb, "RTExpPrev_Preview_AgeKeywords".Translate(), pawnInfo.AgeKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_GenderKeywords".Translate(), pawnInfo.GenderKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_RaceKeywords".Translate(), pawnInfo.RaceKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_TraitKeywords".Translate(), pawnInfo.TraitKeywords, 10);
                    AppendKeywords(sb, "RTExpPrev_Preview_SkillKeywords".Translate(), pawnInfo.SkillKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_SkillLevelKeywords".Translate(), pawnInfo.SkillLevelKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_HealthKeywords".Translate(), pawnInfo.HealthKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_RelationshipKeywords".Translate(), pawnInfo.RelationshipKeywords, 10);
                    AppendKeywords(sb, "RTExpPrev_Preview_AdultBackstoryKeywords".Translate(), pawnInfo.BackstoryKeywords, 15);
                    AppendKeywords(sb, "RTExpPrev_Preview_ChildhoodKeywords".Translate(), pawnInfo.ChildhoodKeywords, 15);
                }
                if (lastTargetPawnKeywordInfo != null)
                {
                    sb.AppendLine();
                    var targetPawnInfo = lastTargetPawnKeywordInfo;
                    sb.AppendLine("RTExpPrev_Preview_TargetPawnKeywordsCategoryHeader".Translate(targetPawnInfo.PawnName));
                    // ... (省略所有 AppendKeywords 调用)
                    AppendKeywords(sb, "RTExpPrev_Preview_NameKeywords".Translate(), targetPawnInfo.NameKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_AgeKeywords".Translate(), targetPawnInfo.AgeKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_GenderKeywords".Translate(), targetPawnInfo.GenderKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_RaceKeywords".Translate(), targetPawnInfo.RaceKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_TraitKeywords".Translate(), targetPawnInfo.TraitKeywords, 10);
                    AppendKeywords(sb, "RTExpPrev_Preview_SkillKeywords".Translate(), targetPawnInfo.SkillKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_SkillLevelKeywords".Translate(), targetPawnInfo.SkillLevelKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_HealthKeywords".Translate(), targetPawnInfo.HealthKeywords);
                    AppendKeywords(sb, "RTExpPrev_Preview_RelationshipKeywords".Translate(), targetPawnInfo.RelationshipKeywords, 10);
                    AppendKeywords(sb, "RTExpPrev_Preview_AdultBackstoryKeywords".Translate(), targetPawnInfo.BackstoryKeywords, 15);
                    AppendKeywords(sb, "RTExpPrev_Preview_ChildhoodKeywords".Translate(), targetPawnInfo.ChildhoodKeywords, 15);
                }
            }
            string content = sb.ToString();
            totalContentHeight += Text.CalcHeight(content, viewWidth);
            // --- 开始绘制 ---
            Rect viewRect = new Rect(0f, 0f, viewWidth, totalContentHeight);
            Widgets.BeginScrollView(rect, ref keywordScrollPosition, viewRect);
            float currentY = 0f;
            // --- 绘制头部统计信息 ---
            Widgets.Label(new Rect(0f, currentY, viewWidth, Text.LineHeight),
                "RTExpPrev_Preview_ContextKeywordsCount".Translate(lastKeywordInfo.ContextKeywords.Count));
            currentY += Text.LineHeight;
            if (lastKeywordInfo.PawnInfo != null)
            {
                Widgets.Label(new Rect(0f, currentY, viewWidth, Text.LineHeight),
                    "RTExpPrev_Preview_PawnInfoKeywordsCount".Translate(
                        lastKeywordInfo.PawnInfo.PawnName,
                        lastKeywordInfo.PawnInfo.TotalCount
                    ));
                currentY += Text.LineHeight;
            }
            if (lastTargetPawnKeywordInfo != null)
            {
                Widgets.Label(new Rect(0f, currentY, viewWidth, Text.LineHeight),
                    "RTExpPrev_Preview_TargetPawnInfoKeywordsCount".Translate(
                        lastTargetPawnKeywordInfo.PawnName,
                        lastTargetPawnKeywordInfo.TotalCount
                    ));
                currentY += Text.LineHeight;
            }
            Widgets.Label(new Rect(0f, currentY, viewWidth, Text.LineHeight),
                "RTExpPrev_Preview_TotalKeywordsCount".Translate(lastKeywordInfo.TotalKeywords));
            currentY += Text.LineHeight + 5f;
            // --- 绘制“核心关键词”标题、复选框和【新增的复制按钮】 ---
            Widgets.Label(new Rect(0f, currentY, viewWidth, Text.LineHeight), "RTExpPrev_Preview_CoreKeywordsHeader".Translate());
            currentY += Text.LineHeight;
            // 复选框
            Rect toggleRect = new Rect(0f, currentY, viewWidth * 0.6f, 24f); // 缩短复选框宽度，为按钮留出空间
            Widgets.CheckboxLabeled(toggleRect, "RTExpPrev_Preview_ShowPawnKeywordsToggle".Translate(), ref showPawnKeywords);
            // ⭐ 新增：复制按钮
            Rect copyButtonRect = new Rect(toggleRect.xMax + 10f, currentY, 150f, 28f);
            if (Widgets.ButtonText(copyButtonRect, "RTExpPrev_Preview_CopyContextKeywords".Translate())) // 复制上下文关键词
            {
                // 检查是否有关键词可复制
                if (lastKeywordInfo.ContextKeywords != null && lastKeywordInfo.ContextKeywords.Any())
                {
                    // 将列表转换为用", "分隔的字符串
                    string keywordsToCopy = string.Join(", ", lastKeywordInfo.ContextKeywords);
                    // 复制到系统剪贴板
                    GUIUtility.systemCopyBuffer = keywordsToCopy;
                    // 显示成功消息
                    Messages.Message("RTExpPrev_Preview_CopySuccess".Translate(lastKeywordInfo.ContextKeywords.Count), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    // 如果没有关键词，也给个提示
                    Messages.Message("RTExpPrev_Preview_CopyNothing".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
            }
            TooltipHandler.TipRegion(copyButtonRect, "RTExpPrev_Preview_CopyContextKeywordsTooltip".Translate());

            currentY += 30f;
            // --- 绘制关键词列表 ---
            float listHeight = Text.CalcHeight(content, viewWidth);
            Widgets.Label(new Rect(0f, currentY, viewWidth, listHeight), content);

            Widgets.EndScrollView();
        }

        private void AppendKeywords(StringBuilder sb, string title, List<string> keywords, int limit = -1)
        {
            if (keywords.Any())
            {
                sb.AppendLine(title + " " + "RTExpPrev_Preview_CountParentheses".Translate(keywords.Count)); // ({0}个)
                IEnumerable<string> displayKeywords = keywords;
                if (limit > 0 && keywords.Count > limit)
                {
                    displayKeywords = keywords.Take(limit);
                }
                sb.AppendLine($"    {string.Join(", ", displayKeywords)}");
                if (limit > 0 && keywords.Count > limit)
                {
                    sb.AppendLine("    " + "RTExpPrev_Preview_MoreKeywords".Translate(keywords.Count - limit).ToString()); // ... (还有 {0} 个)
                }
            }
        }

        private void DrawKnowledgeScoring(Rect rect)
        {
            if (allKnowledgeScoreDetails == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "RTExpPrev_Preview_ClickRefreshKnowledgeScores".Translate()); // 点击 '刷新预览' 获取常识评分数据.
                GUI.color = Color.white;
                return;
            }

            float currentY = rect.y;

            // 阈值信息
            var settings = RimTalkMemoryPatchMod.Settings;
            float threshold = settings?.knowledgeScoreThreshold ?? 0.1f;
            GUI.color = new Color(0.9f, 0.9f, 0.7f);
            Widgets.Label(new Rect(rect.x, currentY, rect.width, Text.LineHeight),
                "RTExpPrev_Preview_KnowledgeInjectionThreshold".Translate(threshold.ToString("F2"))); // 常识注入阈值: {0}
            GUI.color = Color.white;
            currentY += Text.LineHeight;

            Rect toggleRect = new Rect(rect.x, currentY, rect.width * 0.8f, 24f);
            Widgets.CheckboxLabeled(toggleRect, "RTExpPrev_Preview_ShowAllKnowledgeToggle".Translate(), ref showAllKnowledgeScores); // 显示所有常识 (包括未通过阈值的)
            currentY += 30f;

            Rect scrollArea = new Rect(rect.x, currentY, rect.width, rect.height - (currentY - rect.y));

            StringBuilder sb = new StringBuilder();
            int index = 1;
            foreach (var detail in allKnowledgeScoreDetails)
            {
                if (!showAllKnowledgeScores && detail.TotalScore < threshold) continue;

                // 使用颜色区分是否通过阈值
                if (detail.TotalScore >= threshold)
                {
                    sb.Append("✅ ");
                    sb.Append("<color=#8aff8a>"); // 绿色
                }
                else
                {
                    sb.Append("❌ ");
                    sb.Append("<color=#ff8a8a>"); // 红色
                }

                sb.AppendLine("RTExpPrev_Preview_TotalScore".Translate(index, detail.TotalScore.ToString("F3"))); // [{0}] 总分: {1}
                sb.AppendLine("    " + "RTExpPrev_Preview_TagContent".Translate(detail.Entry.tag, detail.Entry.content.Truncate(80))); // 标签: {0} | 内容: {1}
                sb.AppendLine("    " + "RTExpPrev_Preview_BaseImportanceScore".Translate(detail.ImportanceScore.ToString("F3"))); // ├─ 基础重要性分: {0}
                sb.AppendLine("    " + "RTExpPrev_Preview_TagMatchScore".Translate(detail.TagScore.ToString("F3"), string.Join(", ", detail.MatchedTags))); // ├─ 标签匹配分: {0} ({1})

                // 显示匹配关键词（最多3个），优先使用 detail.MatchedKeywords（来自 CommonKnowledgeLibrary）回退到反射方法
                string matchedPreview = string.Empty;
                try
                {
                    if (detail.MatchedKeywords != null && detail.MatchedKeywords.Any())
                    {
                        var top = detail.MatchedKeywords.Take(3).ToList();
                        string more = detail.MatchedKeywords.Count > 3 ? "RTExpPrev_Preview_MoreMatchKeywords".Translate(detail.MatchedKeywords.Count - 3).ToString() : string.Empty; // ...({0} more)
                        matchedPreview = "RTExpPrev_Preview_MatchedKeywordsList".Translate(string.Join(", ", top), more); // (匹配: {0}{1})
                    }
                    else
                    {
                        matchedPreview = GetMatchedKeywordsPreview(detail, 3);
                    }
                }
                catch { matchedPreview = GetMatchedKeywordsPreview(detail, 3); }


                sb.AppendLine("    " + "RTExpPrev_Preview_KeywordContentScore".Translate(detail.KeywordMatchCount, matchedPreview)); // ├─ 关键词内容分: {0}个关键词匹配 {1}
                sb.AppendLine("    " + "RTExpPrev_Preview_ExactMatchBonus".Translate(detail.JaccardScore.ToString("F3"))); // ├─ 精确匹配加成: {0}
                sb.AppendLine("    " + "RTExpPrev_Preview_Status".Translate(detail.FailReason)); // └─ 状态: {0}
                sb.AppendLine($"</color>");
                sb.AppendLine();
                index++;
            }

            float contentHeight = Text.CalcHeight(sb.ToString(), scrollArea.width - 20f);
            Rect viewRect = new Rect(0f, 0f, scrollArea.width - 20f, contentHeight + 50f);

            Widgets.BeginScrollView(scrollArea, ref knowledgeScoreScrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), sb.ToString());
            Widgets.EndScrollView();
        }

        // 重写 RefreshPreview 方法以捕获更多数据
        private void RefreshPreview()
        {
            if (selectedPawn == null)
            {
                cachedPreview = "RTExpPrev_Preview_NoPawnSelected".Translate(); // 未选择殖民者
                allKnowledgeScoreDetails = null;
                lastKeywordInfo = null;
                return;
            }

            var memoryComp = selectedPawn.TryGetComp<FourLayerMemoryComp>();
            if (memoryComp == null)
            {
                cachedPreview = "RTExpPrev_Preview_NoMemoryComponent".Translate(); // 该殖民者没有记忆组件
                allKnowledgeScoreDetails = null;
                lastKeywordInfo = null;
                return;
            }

            var settings = RimTalkMemoryPatchMod.Settings;
            if (settings == null)
            {
                cachedPreview = "RTExpPrev_Preview_CannotLoadSettings".Translate(); // 无法加载Mod设置
                allKnowledgeScoreDetails = null;
                lastKeywordInfo = null;
                return;
            }

            try
            {
                var preview = new System.Text.StringBuilder();

                // ===== 模拟 RimTalk JSON 结构 =====
                preview.AppendLine("RTExpPrev_Preview_JSONSimulationBlock_Start".Translate()); // ╔════...
                preview.AppendLine("RTExpPrev_Preview_JSONSimulationBlock_Title".Translate()); // ║        RimTalk API JSON 请求模拟...
                preview.AppendLine("RTExpPrev_Preview_JSONSimulationBlock_End".Translate()); // ╚════...
                preview.AppendLine();

                preview.AppendLine("RTExpPrev_Preview_PawnLabel".Translate(selectedPawn.LabelShort)); // 殖民者: {0}
                if (targetPawn != null)
                {
                    preview.AppendLine("RTExpPrev_Preview_TargetPawnLabel_WithPawn".Translate(targetPawn.LabelShort)); // 目标角色: {0}
                }
                preview.AppendLine("RTExpPrev_Preview_Time".Translate(Find.TickManager.TicksGame.ToStringTicksToPeriod())); // 时间: {0}
                preview.AppendLine("RTExpPrev_Preview_InjectionMode".Translate(settings.useDynamicInjection ? "RTExpPrev_Preview_ModeDynamic".Translate() : "RTExpPrev_Preview_ModeStatic".Translate())); // 注入模式: {0}

                // ⭐ 显示上下文输入状态
                if (string.IsNullOrEmpty(contextInput))
                {
                    preview.AppendLine("RTExpPrev_Preview_ContextEmpty".Translate()); // 上下文: 空（基于重要性+层级评分）
                }
                else
                {
                    preview.AppendLine("RTExpPrev_Preview_ContextInputPreview".Translate(contextInput.Substring(0, Math.Min(50, contextInput.Length)))); // 上下文: "{0}..."
                }
                preview.AppendLine();

                // 先获取记忆和常识内容
                string memoryInjection = null;
                string knowledgeInjection = null;
                List<DynamicMemoryInjection.MemoryScore> memoryScores = null;
                List<KnowledgeScore> knowledgeScores = null; // 用于 JSON 模 simulated 中的实际注入列表

                if (settings.useDynamicInjection)
                {
                    // ⭐ 使用用户输入的上下文
                    string actualContext = string.IsNullOrEmpty(contextInput) ? "" : contextInput;

                    memoryInjection = DynamicMemoryInjection.InjectMemoriesWithDetails(
                        memoryComp,
                        actualContext,
                        settings.maxInjectedMemories,
                        out memoryScores
                    );
                }

                var library = MemoryManager.GetCommonKnowledge();

                // ⭐ 调用增强的 InjectKnowledgeWithDetails 来捕获所有评分细节和关键词信息
                // 注意：这里用到了 CommonKnowledgeLibrary 里的 `InjectKnowledgeWithDetails` 的最新重载
                string testContext = string.IsNullOrEmpty(contextInput) ? "" : contextInput;
                if (string.IsNullOrEmpty(testContext))
                {
                    testContext = selectedPawn != null ? selectedPawn.LabelShort : "";
                    if (targetPawn != null)
                    {
                        testContext += " " + targetPawn.LabelShort;
                    }
                }
                lastTargetPawnKeywordInfo = null;

                knowledgeInjection = library.InjectKnowledgeWithDetails(
                    testContext,
                    settings.maxInjectedKnowledge,
                    out knowledgeScores,          // 捕获实际注入的常识列表
                    out allKnowledgeScoreDetails, // 捕获所有常识的评分细节 (新增)
                    out lastKeywordInfo,          // 捕获关键词信息 (新增)
                    selectedPawn,
                    targetPawn
                );
                // ⭐ 新增: 从补丁的静态字段获取数据
                this.lastTargetPawnKeywordInfo = Patch_CommonKnowledgeLibrary.LastExtractedTargetPawnInfo;
                // 清理静态字段，防止下次刷新时数据陈旧
                Patch_CommonKnowledgeLibrary.LastExtractedTargetPawnInfo = null;
                cachedMemoryCount = memoryScores?.Count ?? 0;
                cachedKnowledgeCount = knowledgeScores?.Count ?? 0;

                // 构建完整的system content
                var systemContent = new System.Text.StringBuilder();

                // 【优先级1: 常识库】- 放在最上方，可以覆盖RimTalk内置提示词
                if (!string.IsNullOrEmpty(knowledgeInjection))
                {
                    systemContent.AppendLine("RTExpPrev_Preview_KnowledgeSectionHeader".Translate()); // 【常识】
                    systemContent.AppendLine(knowledgeInjection);
                    systemContent.AppendLine();
                }
                // ... (后续的 preview 字符串构建保持不变) ...
                // 【优先级2: RimTalk内置提示词将在这里】
                systemContent.AppendLine("RTExpPrev_Preview_RimTalkSystemPrompt_Part1".Translate()); // 你是一个RimWorld殖民地的角色扮演AI。
                systemContent.AppendLine("RTExpPrev_Preview_RimTalkSystemPrompt_Part2".Translate(selectedPawn.LabelShort)); // 你正在扮演 {0}。
                systemContent.AppendLine();

                // 【优先级3: 记忆】- 放在最后，提供上下文
                if (!string.IsNullOrEmpty(memoryInjection))
                {
                    systemContent.AppendLine("RTExpPrev_Preview_MemorySectionHeader".Translate()); // 【记忆】
                    systemContent.AppendLine(memoryInjection);
                    systemContent.AppendLine();
                }

                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_FullJSONStructureHeader".Translate()); // 📋 完整的 JSON 请求结构:
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();

                preview.AppendLine("{");
                preview.AppendLine("  \"model\": \"gpt-4\",");
                preview.AppendLine("  \"messages\": [");
                preview.AppendLine("    {");
                preview.AppendLine("      \"role\": \"system\",");
                preview.AppendLine("      \"content\": \"");

                // 显示实际的system content，带缩进和转义
                var systemLines = systemContent.ToString().Split('\n');
                foreach (var line in systemLines.Take(20)) // 限制显示前20行
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        string escapedLine = line.Replace("\"", "\\\"").Replace("\r", "");
                        preview.AppendLine($"        {escapedLine}");
                    }
                }

                if (systemLines.Length > 20)
                {
                    preview.AppendLine("        " + "RTExpPrev_Preview_ContentTruncated".Translate(systemLines.Length)); // ... (共 {0} 行，省略剩余部分)
                }

                preview.AppendLine("      \"");
                preview.AppendLine("    }, ");
                preview.AppendLine("    {");
                preview.AppendLine("      \"role\": \"user\", ");
                preview.AppendLine("      \"content\": \"RTExpPrev_Preview_UserDialoguePlaceholder".Translate()); // [用户输入的对话内容]
                preview.AppendLine("    }");
                preview.AppendLine("  ],");
                preview.AppendLine("  \"temperature\": 0.7,");
                preview.AppendLine("  \"max_tokens\": 500");
                preview.AppendLine("}");
                preview.AppendLine();

                // ===== 记忆注入详细分析 =====
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_MemoryInjectionAnalysisHeader".Translate()); // 📝 【ExpandMemory - 记忆注入详细分析】
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();

                if (memoryInjection != null && memoryScores != null)
                {
                    preview.AppendLine("RTExpPrev_Preview_DynamicMemorySelectionCount".Translate(memoryScores.Count)); // 🎯 动态评分选择了 {0} 条记忆
                    preview.AppendLine("RTExpPrev_Preview_ScoringThreshold".Translate(settings.memoryScoreThreshold.ToString("F2"))); // 📊 评分阈值: {0} (低于此分数不注入)
                    preview.AppendLine();

                    // 显示评分详情
                    for (int i = 0; i < memoryScores.Count; i++)
                    {
                        var score = memoryScores[i];
                        var memory = score.Memory;

                        // 使用颜色代码标注来源
                        string source = GetMemorySourceTag(memory.layer);
                        string colorTag = GetMemoryColorTag(memory.layer);

                        preview.AppendLine("RTExpPrev_Preview_MemoryScoreDetail".Translate(
                            i + 1,
                            colorTag,
                            score.TotalScore.ToString("F3")
                        )); // [{0}] {1} 评分: {2}
                        preview.AppendLine("    " + "RTExpPrev_Preview_MemorySourceType".Translate(source, memory.TypeName)); // 来源: {0} | 类型: {1}
                        preview.AppendLine("    " + "RTExpPrev_Preview_ImportanceScore".Translate(score.ImportanceScore.ToString("F3"))); // ├─ 重要性: {0}
                        preview.AppendLine("    " + "RTExpPrev_Preview_KeywordScore".Translate(score.KeywordScore.ToString("F3"))); // ├─ 关键词: {0}
                        preview.AppendLine("    " + "RTExpPrev_Preview_TimeScore".Translate(score.TimeScore.ToString("F3"))); // ├─ 时间: {0} (SCM/ELS不计时间)
                        preview.AppendLine("    " + "RTExpPrev_Preview_BonusScore".Translate(score.BonusScore.ToString("F3"))); // └─ 加成: {0} (层级+固定+编辑)
                        preview.AppendLine("    " + "RTExpPrev_Preview_MemoryContent".Translate(memory.content)); // 内容: "{0}"
                        preview.AppendLine();
                    }
                }
                else
                {
                    preview.AppendLine("RTExpPrev_Preview_NoMemoryThreshold".Translate()); // ⚠️ 没有记忆达到阈值，返回 null (不注入记忆)
                    preview.AppendLine("RTExpPrev_Preview_CurrentThreshold".Translate(settings.memoryScoreThreshold.ToString("F2"))); // 📊 当前阈值: {0}
                    preview.AppendLine();
                }

                preview.AppendLine();

                // ===== 常识注入信息 (不再显示详细评分，因为右侧面板会展示) =====
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_KnowledgeInjectionInfoHeader".Translate()); // 🎓 【ExpandMemory - 常识库注入信息】
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();
                if (knowledgeInjection != null && knowledgeScores != null && knowledgeScores.Any())
                {
                    preview.AppendLine("RTExpPrev_Preview_DynamicKnowledgeSelectionCount".Translate(knowledgeScores.Count)); // 🎯 动态评分选择了 {0} 条常识 (详细评分请看右侧面板)
                    preview.AppendLine("RTExpPrev_Preview_ScoringThreshold".Translate(settings.knowledgeScoreThreshold.ToString("F2"))); // 📊 评分阈值: {0}
                    preview.AppendLine();
                    foreach (var ks in knowledgeScores)
                    {
                        preview.AppendLine("RTExpPrev_Preview_KnowledgeEntryScore".Translate(ks.Entry.tag, ks.Entry.content.Truncate(80), ks.Score.ToString("F3"))); // - [{0}] {1} (得分: {2})
                    }
                    preview.AppendLine();
                }
                else
                {
                    preview.AppendLine("RTExpPrev_Preview_NoKnowledgeThreshold".Translate()); // ⚠️ 没有常识达到阈值，返回 null (不注入常识)
                    preview.AppendLine("RTExpPrev_Preview_CurrentThreshold".Translate(settings.knowledgeScoreThreshold.ToString("F2"))); // 📊 当前阈值: {0}
                    preview.AppendLine();
                }

                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_InjectionStatsHeader".Translate()); // 📊 【注入统计】
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();
                preview.AppendLine("RTExpPrev_Preview_MemoryInjectedCount".Translate(cachedMemoryCount)); // ✅ 记忆注入: {0} 条
                preview.AppendLine("RTExpPrev_Preview_KnowledgeInjectedCount".Translate(cachedKnowledgeCount)); // ✅ 常识注入: {0} 条
                preview.AppendLine("RTExpPrev_Preview_TokenEstimate".Translate(EstimateTokens(memoryInjection, knowledgeInjection))); // 📦 总Token估算: ~{0} tokens
                preview.AppendLine("RTExpPrev_Preview_CostEstimate".Translate(EstimateCost(memoryInjection, knowledgeInjection).ToString("F4"))); // 💰 API成本估算: ~${0} (GPT-4)
                preview.AppendLine();

                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_ThankYouMessage".Translate()); // 感谢使用 RimTalk 调试预览器！
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();

                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine("RTExpPrev_Preview_ColorLegendHeader".Translate()); // 💡 【颜色标注说明】
                preview.AppendLine("RTExpPrev_Preview_Divider".Translate()); // ━━━━━━━━━
                preview.AppendLine();
                preview.AppendLine("RTExpPrev_Preview_Legend_ABM".Translate()); // 🟦 [ABM] - 超短期记忆 (不会被注入，保留给 TalkHistory)
                preview.AppendLine("RTExpPrev_Preview_Legend_SCM".Translate()); // 🟨 [SCM] - 短期记忆 (近期事件，无时间加成)
                preview.AppendLine("RTExpPrev_Preview_Legend_ELS".Translate()); // 🟧 [ELS] - 中期记忆 (AI总结，无时间加成)
                preview.AppendLine("RTExpPrev_Preview_Legend_CLPA".Translate()); // 🟪 [CLPA] - 长期记忆 (核心人设，有时间加成)
                preview.AppendLine("RTExpPrev_Preview_Legend_Knowledge".Translate()); // 📘 [常识] - 常识库条目 (世界观/背景知识)
                preview.AppendLine();

                cachedPreview = preview.ToString();
            }
            catch (Exception ex)
            {
                cachedPreview = "RTExpPrev_Preview_ErrorGenerating".Translate(ex.Message, ex.StackTrace); // 生成预览时发生错误: {0}\n{1}
                allKnowledgeScoreDetails = null;
                lastKeywordInfo = null;
                Log.Error(cachedPreview);
            }
        }

        private void DrawContextInput(Rect rect)
        {
            // 标签
            GUI.color = new Color(1f, 0.9f, 0.8f);
            Widgets.Label(new Rect(rect.x, rect.y, 120f, 30f), "RTExpPrev_Preview_ContextInputLabel".Translate()); // 上下文输入：
            GUI.color = Color.white;

            // ⭐ 新增：读取上次RimTalk输入按钮
            Rect loadButtonRect = new Rect(rect.x + rect.width - 150f, rect.y, 140f, 30f);
            if (Widgets.ButtonText(loadButtonRect, "RTExpPrev_Preview_LoadLastInputButton".Translate())) // 读取上次输入 📥
            {
                LoadLastRimTalkContext();
            }
            TooltipHandler.TipRegion(loadButtonRect, "RTExpPrev_Preview_LoadLastInputTooltip".Translate()); // 从RimTalk读取最后一次发送给AI的对话内容...

            // 输入框 - 使用TextArea支持多行
            Rect textFieldRect = new Rect(rect.x + 130f, rect.y, rect.width - 290f, 60f);

            string newInput = Widgets.TextArea(textFieldRect, contextInput);
            if (newInput != contextInput)
            {
                contextInput = newInput;
                cachedPreview = ""; // 清空缓存，标记需要刷新
                allKnowledgeScoreDetails = null; // 清空新缓存
                lastKeywordInfo = null; // 清空新缓存
            }

            // 提示文字（如果为空）
            if (string.IsNullOrEmpty(contextInput))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(textFieldRect.x + 5f, textFieldRect.y + 5f, textFieldRect.width - 10f, 40f),
                    "RTExpPrev_Preview_ContextInputPlaceholder".Translate()); // 输入对话上下文（例如：最近的对话内容、话题等）\n留空则仅基于重要性和层级评分
                GUI.color = Color.white;
            }
        }

        // ⭐ 新增：从RimTalk加载最后一次请求的上下文
        private void LoadLastRimTalkContext()
        {
            try
            {
                string lastContext = RimTalk.Memory.Patches.RimTalkMemoryAPI.GetLastRimTalkContext(
                    out Pawn lastPawn,
                    out int lastTick
                );

                if (string.IsNullOrEmpty(lastContext))
                {
                    Messages.Message("RTExpPrev_Preview_NoRecentDialogue".Translate(), MessageTypeDefOf.RejectInput, false); // 未找到RimTalk的最近对话记录
                    return;
                }

                // 计算距离上次请求的时间
                int currentTick = Find.TickManager.TicksGame;
                int ticksAgo = currentTick - lastTick;
                string timeAgo = "";
                if (ticksAgo < 60) timeAgo = "RTExpPrev_Time_JustNow".Translate(); // 刚刚
                else if (ticksAgo < 2500) timeAgo = "RTExpPrev_Time_MinutesAgo".Translate((ticksAgo / 60).ToString()); // {0}分钟前
                else if (ticksAgo < 60000) timeAgo = "RTExpPrev_Time_HoursAgo".Translate((ticksAgo / 2500).ToString()); // {0}小时前
                else timeAgo = "RTExpPrev_Time_DaysAgo".Translate((ticksAgo / 60000).ToString()); // {0}天前

                // 设置上下文
                contextInput = lastContext;

                // 如果殖民者不同，也切换殖民者
                if (lastPawn != null && lastPawn != selectedPawn)
                {
                    selectedPawn = lastPawn;
                }

                // 清空缓存，标记需要刷新
                cachedPreview = "";
                allKnowledgeScoreDetails = null;
                lastKeywordInfo = null;

                // 显示成功消息
                string pawnName = lastPawn != null ? lastPawn.LabelShort : "RTExpPrev_Preview_UnknownPawn".Translate().ToString(); // 未知
                Messages.Message("RTExpPrev_Preview_LoadSuccess".Translate(pawnName, timeAgo), MessageTypeDefOf.PositiveEvent, false); // 已加载 {0} 的最后一次对话（{1}）
            }
            catch (Exception ex)
            {
                Messages.Message("RTExpPrev_Preview_LoadFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false); // 读取失败：{0}
                Log.Error("RTExpPrev_Preview_LoadFailedLogError".Translate(ex.Message)); // Failed to load last RimTalk context: {0}
            }
        }

        private string GetMemorySourceTag(MemoryLayer layer)
        {
            switch (layer)
            {
                case MemoryLayer.Active:
                    return "RTExpPrev_MemoryLayer_ABM".Translate(); // ABM (不注入)
                case MemoryLayer.Situational:
                    return "RTExpPrev_MemoryLayer_SCM".Translate(); // SCM (ExpandMemory)
                case MemoryLayer.EventLog:
                    return "RTExpPrev_MemoryLayer_ELS".Translate(); // ELS (ExpandMemory)
                case MemoryLayer.Archive:
                    return "RTExpPrev_MemoryLayer_CLPA".Translate(); // CLPA (ExpandMemory)
                default:
                    return "RTExpPrev_MemoryLayer_Unknown".Translate(); // Unknown
            }
        }

        private string GetMemoryColorTag(MemoryLayer layer)
        {
            switch (layer)
            {
                case MemoryLayer.Active:
                    return "🟦";
                case MemoryLayer.Situational:
                    return "🟨";
                case MemoryLayer.EventLog:
                    return "🟧";
                case MemoryLayer.Archive:
                    return "🟪";
                default:
                    return "⬜";
            }
        }

        private int EstimateTokens(string memoryText, string knowledgeText)
        {
            int total = 0;

            // RimTalk 内部估算：中文约 1.5 字符 = 1 token
            if (!string.IsNullOrEmpty(memoryText))
            {
                total += (int)(memoryText.Length / 1.5f);
            }

            if (!string.IsNullOrEmpty(knowledgeText))
            {
                total += (int)(knowledgeText.Length / 1.5f);
            }

            return total;
        }

        private float EstimateCost(string memoryText, string knowledgeText)
        {
            int tokens = EstimateTokens(memoryText, knowledgeText);
            // GPT-4 input cost: $0.03 per 1K tokens (这是 RimTalk 默认的估算，可根据实际模型价格调整)
            return tokens * 0.03f / 1000f;
        }

        // 通过反射尝试获取 KnowledgeScoreDetail 中记录的匹配关键词集合，返回最多 max 个并格式化
        private string GetMatchedKeywordsPreview(KnowledgeScoreDetail detail, int max)
        {
            if (detail == null) return string.Empty;

            try
            {
                var type = detail.GetType();

                // 优先寻找属性名中包含 "keyword" 的 IEnumerable<string>
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    var name = p.Name.ToLowerInvariant();
                    if (!name.Contains("keyword") && !name.Contains("matched")) continue;
                    var value = p.GetValue(detail) as System.Collections.IEnumerable;
                    if (value == null) continue;
                    var list = new List<string>();
                    foreach (var o in value)
                    {
                        if (o == null) continue;
                        list.Add(o.ToString());
                    }
                    if (list.Count > 0)
                    {
                        var display = list.Take(max);
                        string more = list.Count > max ? "RTExpPrev_Preview_MoreMatchKeywords".Translate(list.Count - max).ToString() : string.Empty; // ...({0} more)
                        return "RTExpPrev_Preview_MatchedKeywordsList".Translate(string.Join(", ", display), more); // (匹配: {0}{1})
                    }
                }

                // 如果没有找到关键字集合，尝试查找包含 "match" 的属性
                foreach (var p in props)
                {
                    var name = p.Name.ToLowerInvariant();
                    if (!name.Contains("match")) continue;
                    var value = p.GetValue(detail) as System.Collections.IEnumerable;
                    if (value == null) continue;
                    var list = new List<string>();
                    foreach (var o in value)
                    {
                        if (o == null) continue;
                        list.Add(o.ToString());
                    }
                    if (list.Count > 0)
                    {
                        var display = list.Take(max);
                        string more = list.Count > max ? "RTExpPrev_Preview_MoreMatchKeywords".Translate(list.Count - max).ToString() : string.Empty; // ...({0} more)
                        return "RTExpPrev_Preview_MatchedKeywordsList".Translate(string.Join(", ", display), more); // (匹配: {0}{1})
                    }
                }

                // 回退：使用 MatchedTags（如果有）
                var tagsProp = props.FirstOrDefault(p => p.Name == "MatchedTags");
                if (tagsProp != null)
                {
                    var value = tagsProp.GetValue(detail) as System.Collections.IEnumerable;
                    if (value != null)
                    {
                        var list = new List<string>();
                        foreach (var o in value) if (o != null) list.Add(o.ToString());
                        if (list.Count > 0)
                        {
                            var display = list.Take(max);
                            string more = list.Count > max ? "RTExpPrev_Preview_MoreMatchKeywords".Translate(list.Count - max).ToString() : string.Empty; // ...({0} more)
                            return "RTExpPrev_Preview_MatchedTagsList".Translate(string.Join(", ", display), more); // (匹配标签: {0}{1})
                        }
                    }
                }
            }
            catch { }

            return string.Empty;
        }
    }
}
