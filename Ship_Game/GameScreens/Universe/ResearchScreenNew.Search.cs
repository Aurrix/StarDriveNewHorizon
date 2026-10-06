using System;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;

namespace Ship_Game;

public sealed partial class ResearchScreenNew
{
    RectF SearchBounds;
    ScrollList<ResearchResult> SearchResults;

    void CreateResearchSearch()
    {
        SearchBounds = new RectF(300, 32, Math.Min(780, ScreenWidth - 380), 38);
        Search = Add(new UITextEntry(SearchBounds.X + 12, SearchBounds.Y + 9,
            SearchBounds.W - 58, 24, Fonts.Arial14Bold, ""));
        Search.Name = "ResearchSearch";
        Search.MaxCharacters = 48;
        Search.Color = Colors.Cream;
        Search.OnTextChanged = FilterResearch;
        var clear = Add(new UIButton(ButtonStyle.Default, new Vector2(SearchBounds.Right - 36, SearchBounds.Y + 5), "X"));
        clear.RectF = new RectF(SearchBounds.Right - 36, SearchBounds.Y + 5, 30, 28);
        clear.Name = "ResearchSearchClear";
        clear.Tooltip = "Clear search";
        clear.OnClick = _ => ClearResearchSearch();
        SearchResults = Add(new ScrollList<ResearchResult>(TreeViewport, 64, ListStyle.Blue));
        SearchResults.Name = "ResearchSearchResults";
        SearchResults.Visible = false;
        SearchResults.OnClick = result =>
        {
            TechEntry tech = result.Tech;
            if (!tech.CanBeResearched || Player.Research.IsQueued(tech.UID))
            {
                GameAudio.NegativeClick();
                return;
            }
            Player.Research.AddTechToQueue(tech.UID);
            Queue.ReloadResearchQueue();
            GameAudio.ResearchSelect();
        };
    }

    void ClearResearchSearch()
    {
        Search?.StopInput();
        Search?.Clear();
        FilterResearch("");
    }

    void FilterResearch(string text)
    {
        if (SearchResults == null) return;
        string query = text.Trim();
        SearchResults.Visible = query.Length > 0;
        SearchResults.SetItems(Player.TechEntries
            .Where(t => t.Discovered && !t.IsRoot && t.Tech.Name.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(t => t.Tech.Name.Text).Select(t => new ResearchResult(this, t)).ToArray());
    }

    sealed class ResearchResult : ScrollListItem<ResearchResult>
    {
        readonly ResearchScreenNew Screen;
        public readonly TechEntry Tech;
        public ResearchResult(ResearchScreenNew screen, TechEntry tech)
        {
            Screen = screen;
            Tech = tech;
            Name = "ResearchResult-" + tech.UID;
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            if (Hovered) batch.FillRectangle(Rect, new Microsoft.Xna.Framework.Color(27, 59, 73));
            ResearchQueueUIComponent.DrawResearchIcon(batch, ResearchQueueUIComponent.TechIcon(Tech), new RectF(X + 8, Y + 8, 44, 44), Microsoft.Xna.Framework.Color.White);
            batch.DrawString(Fonts.Arial14Bold, FitLabel(Tech.Tech.Name.Text, Fonts.Arial14Bold, Width - 220),
                new Vector2(X + 64, Y + 8), Colors.Cream);
            string status = !Tech.CanBeResearched ? "Completed" : Screen.Player.Research.IsQueued(Tech.UID) ? "Queued" : "Click to queue with prerequisites";
            batch.DrawString(Fonts.Arial12, status, new Vector2(X + 64, Y + 33), ResearchMuted);
            batch.DrawString(Fonts.Arial12, $"{Tech.TechCost:0} research", new Vector2(Right - 140, Y + 20), ResearchCyan);
            if (Hovered) ToolTip.CreateTooltip(Tech.Tech.Description.Text);
            base.Draw(batch, elapsed);
        }

        public override bool HandleInput(InputState input)
        {
            if (HitTest(input.CursorPosition) && input.RightMouseClick)
            {
                Screen.ScreenManager.AddScreen(new ResearchPopup(Screen.Universe, Tech.UID));
                return true;
            }
            return base.HandleInput(input);
        }
    }
}

