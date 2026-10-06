using System;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Audio;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game;

public sealed partial class ResearchScreenNew
{
    static readonly Color ResearchGold = new(212, 185, 130);
    static readonly Color ResearchCyan = new(108, 206, 217);
    static readonly Color ResearchMuted = new(156, 177, 190);
    SubTexture ResearchBackground;
    ScrollList<CategoryRow> Categories;
    RectF CategoryBounds, QueueBounds;
    public RectF TreeViewport { get; private set; }

    void CreateResearchLayout()
    {
        ResearchBackground = ResourceManager.Texture("ResearchMenu/Backgrounds/observatory");
        float top = ScreenHeight >= 900 ? 146 : 110;
        float sidebar = ScreenWidth >= 1440 ? 230 : 185;
        CategoryBounds = new RectF(16, top, sidebar, ScreenHeight - top - 18);
        QueueBounds = new RectF(CategoryBounds.Right + 12, ScreenHeight - 146,
            ScreenWidth - CategoryBounds.Right - 28, 128);
        TreeViewport = new RectF(CategoryBounds.Right + 12, top + 42,
            QueueBounds.W, QueueBounds.Y - top - 68);
        Categories = Add(new ScrollList<CategoryRow>(new RectF(CategoryBounds.X + 2, top + 40,
            sidebar - 4, CategoryBounds.H - 160), 44, ListStyle.Blue));
        Categories.Name = "ResearchCategories";
        Categories.OnClick = row =>
        {
            GameAudio.ResearchSelect();
            ClearResearchSearch();
            PopulateNodesFromRoot(row.Root);
        };
        Close = Add(new CloseButton(ScreenWidth - 48, 24));
    }

    void DrawResearchChrome(SpriteBatch batch)
    {
        batch.FillRectangle(new RectF(0, 0, ScreenWidth, ScreenHeight), Color.Black);
        batch.Draw(ResearchBackground, new RectF(8, 8, ScreenWidth - 16, ScreenHeight - 16), Color.White);
        batch.FillRectangle(new RectF(26, 24, ScreenWidth - 90, 73), new Color(4, 13, 23).Alpha(.85f));
        batch.DrawString(Fonts.Pirulen20, Localizer.Token(GameText.Research), new Vector2(38, 35), Colors.Cream);
        batch.DrawString(Fonts.Arial12, "Choose a discipline and plan your research.", new Vector2(40, 72), ResearchMuted);
        ResearchPanel(batch, CategoryBounds);
        ResearchPanel(batch, new RectF(TreeViewport.X, CategoryBounds.Y, TreeViewport.W, QueueBounds.Y - CategoryBounds.Y - 8));
        ResearchPanel(batch, SearchBounds);
        if (Search.Text.Length == 0 && !Search.HandlingInput)
            batch.DrawString(Fonts.Arial14Bold, "Search all technologies...", Search.Pos, ResearchMuted);
        batch.DrawString(Fonts.Pirulen12, "DISCIPLINES", new Vector2(CategoryBounds.X + 14, CategoryBounds.Y + 14), ResearchGold);
        RootNode root = GetCurrentlySelectedRootNode();
        string title = SearchResults.Visible ? $"All disciplines — {SearchResults.NumEntries} results" : root?.TechName ?? "No discovered disciplines";
        batch.DrawString(Fonts.Arial20Bold, FitLabel(title, Fonts.Arial20Bold, TreeViewport.W - 28), new Vector2(TreeViewport.X + 14, CategoryBounds.Y + 10), Colors.Cream);
        if (SearchResults.Visible && SearchResults.NumEntries == 0)
            batch.DrawString(Fonts.Arial14Bold, "No discovered technologies match your search.",
                new Vector2(TreeViewport.X + 24, TreeViewport.Y + 30), ResearchMuted);
        batch.DrawString(Fonts.Arial10, "Middle-drag: pan  |  Wheel: scroll  |  Shift+wheel: sideways",
            new Vector2(TreeViewport.X + 10, QueueBounds.Y - 23), ResearchMuted);
        string help = Fonts.Arial12.ParseText("Click tech: queue / remove\nCtrl+click: prioritize\nRight-click tech: details", CategoryBounds.W - 28);
        batch.DrawString(Fonts.Arial12, help, new Vector2(CategoryBounds.X + 14, CategoryBounds.Bottom - 100), ResearchMuted);
    }

    static string FitLabel(string text, Graphics.Font font, float width)
    {
        if (font.MeasureString(text).X <= width) return text;
        while (text.Length > 0 && font.MeasureString(text + "...").X > width)
            text = text.Substring(0, text.Length - 1);
        return text + "...";
    }

    static void ResearchPanel(SpriteBatch batch, RectF rect)
    {
        batch.FillRectangle(rect, new Color(5, 17, 29).Alpha(.86f));
        batch.DrawRectangle(rect, ResearchGold.Alpha(.35f));
    }

    void PanTree(Vector2 delta)
    {
        float right = TreeViewport.Right, bottom = TreeViewport.Bottom;
        foreach (TreeNode node in SubNodes.Values)
        {
            right = Math.Max(right, node.BaseRect.Right + 100);
            bottom = Math.Max(bottom, node.BaseRect.Bottom + 35);
        }
        foreach (var band in BranchBands)
        {
            right = Math.Max(right, band.Bounds.Right);
            bottom = Math.Max(bottom, band.Bounds.Bottom);
        }
        Vector2 offset = camera.Pos - GameBase.ScreenCenter + delta;
        camera.Pos = GameBase.ScreenCenter + new Vector2(
            Math.Clamp(offset.X, 0, right - TreeViewport.Right),
            Math.Clamp(offset.Y, 0, bottom - TreeViewport.Bottom));
    }

    sealed class CategoryRow : ScrollListItem<CategoryRow>
    {
        readonly SubTexture Icon;
        public readonly RootNode Root;

        public CategoryRow(ResearchScreenNew screen, RootNode root)
        {
            Root = root;
            Name = "ResearchCategory-" + root.Entry.UID;
            Icon = ResourceManager.TextureOrDefault("ResearchMenu/" + root.Entry.Tech.IconPath, "NewUI/icon_science");
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            bool selected = Root.nodeState == NodeState.Press;
            if (selected || Hovered)
                batch.FillRectangle(Rect, new Color(27, 59, 73).Alpha(selected ? .95f : .5f));
            if (selected) batch.FillRectangle(new RectF(X, Y, 3, Height), ResearchCyan);
            batch.FillRectangle(new RectF(X + 4, Bottom - 1, Width - 8, 1), ResearchGold.Alpha(.18f));
            ResearchQueueUIComponent.DrawResearchIcon(batch, Icon, new RectF(X + 8, Y + 12, 24, 20), selected ? Color.White : ResearchMuted);
            string title = Fonts.Arial12Bold.ParseText(Root.TechName, Width - 43);
            string[] lines = title.Split('\n');
            if (lines.Length > 2)
                title = lines[0] + "\n" + FitLabel(string.Join(" ", lines, 1, lines.Length - 1), Fonts.Arial12Bold, Width - 43);
            batch.DrawString(Fonts.Arial12Bold, title, new Vector2(X + 39, Y + (Height - Fonts.Arial12Bold.MeasureString(title).Y) / 2),
                selected ? Colors.Cream : ResearchMuted);
            if (Hovered) ToolTip.CreateTooltip($"{Root.TechName}\n{Root.Entry.Tech.Description.Text}");
            base.Draw(batch, elapsed);
        }
    }
}
