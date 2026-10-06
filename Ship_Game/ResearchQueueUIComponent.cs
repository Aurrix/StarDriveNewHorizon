using System;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game;

// Fixed screen-space dock. EmpireResearch owns ordering and prerequisite validation.
public sealed class ResearchQueueUIComponent : UIPanel
{
    readonly ResearchScreenNew Screen;
    EmpireResearch Research => Screen.Player.Research;
    static readonly Color Cyan = new(108, 206, 217);
    static readonly Color Muted = new(156, 177, 190);
    static readonly Color Gold = new(212, 185, 130);
    readonly UIButton Previous, Next, Earlier, Later, Prioritize, RemoveButton;
    string[] QueueIds = Array.Empty<string>();
    string HoveredUid;
    public string SelectedUid { get; private set; }
    public int FirstVisible { get; private set; }
    public int VisibleCapacity => Math.Max(1, (int)(Width - ActiveWidth - 82) / 58);
    float ActiveWidth => Width < 900 ? 280 : 365;
    public RectF ActiveIcon => new(X + 16, Y + 30, 62, 62);
    RectF QueuedIcon(int index) => new(X + ActiveWidth + 38 + index * 58, Y + 33, 46, 46);

    public ResearchQueueUIComponent(ResearchScreenNew screen, in Rectangle container)
        : base(container, new Color(5, 17, 29))
    {
        Screen = screen;
        Name = "ResearchQueueDock";
        Previous = Control("<", "ResearchQueuePrevious", "Previous queued technologies", _ => Scroll(-VisibleCapacity));
        Next = Control(">", "ResearchQueueNext", "More queued technologies", _ => Scroll(VisibleCapacity));
        Earlier = Control("<", "ResearchQueueEarlier", "Move earlier (respects prerequisites)", _ => MoveSelected(-1));
        Later = Control(">", "ResearchQueueLater", "Move later (respects prerequisites)", _ => MoveSelected(1));
        Prioritize = Control("^", "ResearchQueuePrioritize", "Prioritize with prerequisites", _ =>
        {
            int index = Research.IndexInQueue(SelectedUid);
            if (index > 0) Research.MoveToTopWithPreReqs(index);
            ReloadResearchQueue();
        });
        RemoveButton = Control("X", "ResearchQueueRemove", "Remove selected research and dependent queued technologies", _ =>
        {
            if (SelectedUid != null) Research.RemoveTechFromQueue(SelectedUid);
            ReloadResearchQueue();
        });
        ReloadResearchQueue();
    }

    UIButton Control(string text, string name, string tooltip, Action<UIButton> action)
    {
        var button = Add(new UIButton(ButtonStyle.Default, text));
        button.Name = name;
        button.Tooltip = tooltip;
        button.OnClick = action;
        button.Normal = button.Hover = button.Pressed = null;
        button.DefaultColor = new Color(18, 40, 54);
        button.HoverColor = new Color(32, 69, 83);
        return button;
    }

    void Scroll(int amount)
    {
        FirstVisible = Math.Clamp(FirstVisible + amount, 0, Math.Max(0, QueueIds.Length - 1 - VisibleCapacity));
        PerformLayout();
    }

    void MoveSelected(int direction)
    {
        int index = Research.IndexInQueue(SelectedUid);
        if (index < 0) return;
        if (direction < 0) Research.MoveUp(index);
        else Research.MoveDown(index);
        ReloadResearchQueue();
    }

    public override void PerformLayout()
    {
        if (Previous == null) return;
        Previous.RectF = new RectF(X + ActiveWidth + 4, Y + 42, 26, 28);
        Next.RectF = new RectF(Right - 32, Y + 42, 26, 28);
        Previous.Visible = FirstVisible > 0;
        Next.Visible = FirstVisible + VisibleCapacity < QueueIds.Length - 1;
        int index = SelectedUid == null ? -1 : Research.IndexInQueue(SelectedUid);
        float controlsX = X + ActiveWidth + 38;
        Earlier.RectF = new RectF(controlsX, Y + 94, 26, 24);
        RemoveButton.RectF = new RectF(controlsX + 30, Y + 94, 26, 24);
        Later.RectF = new RectF(controlsX + 60, Y + 94, 26, 24);
        Prioritize.RectF = new RectF(controlsX + 90, Y + 94, 26, 24);
        Earlier.Visible = Later.Visible = Prioritize.Visible = RemoveButton.Visible = index >= 0;
        Earlier.Enabled = index >= 0 && Research.CanMoveUp(index);
        Later.Enabled = index >= 0 && Research.CanMoveDown(index);
        Prioritize.Enabled = index > 0;
        base.PerformLayout();
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;
        HoveredUid = null;
        if (base.HandleInput(input)) return true;
        if (!HitTest(input.CursorPosition)) return false;
        if (input.ScrollIn || input.ScrollOut)
        {
            Scroll(input.ScrollIn ? -1 : 1);
            return true;
        }
        if (QueueIds.Length > 0 && ActiveIcon.HitTest(input.CursorPosition)) HoveredUid = QueueIds[0];
        for (int i = 0; i < VisibleCapacity && FirstVisible + i + 1 < QueueIds.Length; ++i)
            if (QueuedIcon(i).HitTest(input.CursorPosition)) HoveredUid = QueueIds[FirstVisible + i + 1];
        if (HoveredUid != null)
        {
            TechEntry tech = Screen.Player.GetTechEntry(HoveredUid);
            ToolTip.CreateTooltip($"{tech.Tech.Name.Text} - {tech.TechCost:0} research\n{tech.Tech.Description.Text}");
            if (input.RightMouseClick)
                Screen.ScreenManager.AddScreen(new ResearchPopup(Screen.Universe, HoveredUid));
            else if (input.LeftMouseClick)
            {
                SelectedUid = HoveredUid;
                PerformLayout();
            }
        }
        return input.LeftMouseClick || input.RightMouseClick;
    }

    public override void Update(float fixedDeltaTime)
    {
        if (!QueueIds.SequenceEqual(Screen.Player.data.ResearchQueue)) ReloadResearchQueue();
        base.Update(fixedDeltaTime);
    }

    public static SubTexture TechIcon(TechEntry tech)
        => ResourceManager.TextureOrDefault("TechIcons/" + (tech.Tech.IconPath ?? tech.UID), "NewUI/icon_science");

    public static void DrawResearchIcon(SpriteBatch batch, SubTexture texture, RectF bounds, Color color)
    {
        float scale = Math.Min(bounds.W / texture.Width, bounds.H / texture.Height);
        float width = texture.Width * scale, height = texture.Height * scale;
        batch.Draw(texture, new RectF(bounds.X + (bounds.W - width) / 2,
            bounds.Y + (bounds.H - height) / 2, width, height), color);
    }

    void DrawIcon(SpriteBatch batch, string uid, RectF rect, string badge = null)
    {
        batch.FillRectangle(rect, new Color(14, 30, 42));
        DrawResearchIcon(batch, TechIcon(Screen.Player.GetTechEntry(uid)), rect.Bevel(-5), Color.White);
        batch.DrawRectangle(rect, uid == Research.Topic || uid == SelectedUid || uid == HoveredUid ? Cyan : Muted.Alpha(.6f));
        if (badge != null)
        {
            batch.FillRectangle(new RectF(rect.X, rect.Y, 16, 16), new Color(5, 17, 29));
            batch.DrawString(Fonts.Arial10, badge, rect.Pos + new Vector2(3, 1), Colors.Cream);
        }
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        base.Draw(batch, elapsed);
        batch.DrawRectangle(RectF, Gold.Alpha(.65f));
        batch.DrawString(Fonts.Pirulen12, "ACTIVE RESEARCH", new Vector2(X + 16, Y + 9), Gold);
        batch.DrawString(Fonts.Pirulen12, "UP NEXT", new Vector2(X + ActiveWidth + 20, Y + 9), Gold);
        batch.FillRectangle(new RectF(X + ActiveWidth, Y + 16, 1, Height - 32), Gold.Alpha(.5f));
        if (QueueIds.Length == 0)
        {
            batch.DrawString(Fonts.Arial12, "Select a technology\nto begin research.", new Vector2(X + 16, Y + 44), Muted);
        }
        else
        {
            TechEntry tech = Research.Current;
            DrawIcon(batch, tech.UID, ActiveIcon);
            float progress = tech.TechCost > 0 ? Math.Clamp(tech.PercentResearched, 0, 1) : 1;
            RectF bar = new(ActiveIcon.X, ActiveIcon.Bottom + 6, ActiveIcon.W, 5);
            batch.FillRectangle(bar, new Color(31, 55, 69));
            batch.FillRectangle(new RectF(bar.X, bar.Y, bar.W * progress, bar.H), Cyan);
            string title = tech.Tech.Name.Text;
            while (title.Length > 0 && Fonts.Arial12Bold.TextWidth(title) > ActiveWidth - 110)
                title = title.Substring(0, title.Length - 1);
            if (title != tech.Tech.Name.Text && title.Length > 3) title = title.Substring(0, title.Length - 3) + "...";
            batch.DrawString(Fonts.Arial12Bold, title, new Vector2(X + 92, Y + 36), Colors.Cream);
            float turns = MathF.Ceiling(Math.Max(0, tech.TechCost - tech.Progress) / Math.Max(.01f, Research.NetResearch));
            string time = Research.NetResearch <= 0 ? "No research output" : turns > 999 ? ">999 turns" : $"{turns:0} turns";
            batch.DrawString(Fonts.Arial10, $"{progress:P0}", new Vector2(ActiveIcon.X, ActiveIcon.Bottom + 13), Cyan);
            batch.DrawString(Fonts.Arial12, time, new Vector2(X + 92, Y + 60), Cyan);
            if (Research.DisruptionMultiplier < 1)
            {
                RectF warning = new(X + 92, Y + 83, ActiveWidth - 104, 22);
                batch.DrawString(Fonts.Arial10, $"Disrupted: {Research.DisruptionMultiplier:P0} output", warning.Pos, Color.OrangeRed);
                if (warning.HitTest(Screen.ScreenManager.input.CursorPosition)) ToolTip.CreateTooltip(GameText.ResearchDisruptedByInfiltrationTip);
            }
        }
        for (int i = 0; i < VisibleCapacity && FirstVisible + i + 1 < QueueIds.Length; ++i)
            DrawIcon(batch, QueueIds[FirstVisible + i + 1], QueuedIcon(i), (FirstVisible + i + 1).ToString());
        if (QueueIds.Length <= 1)
            batch.DrawString(Fonts.Arial12, "Queue technologies from the tree or search.", new Vector2(X + ActiveWidth + 38, Y + 44), Muted);
        if (SelectedUid != null)
        {
            string selected = Screen.Player.GetTechEntry(SelectedUid).Tech.Name.Text;
            float availableWidth = Width - ActiveWidth - 180;
            while (selected.Length > 0 && Fonts.Arial10.TextWidth(selected) > availableWidth)
                selected = selected.Substring(0, selected.Length - 1);
            batch.DrawString(Fonts.Arial10, selected, new Vector2(X + ActiveWidth + 162, Y + 100), Muted);
        }
    }

    public void AddToResearchQueue(TechEntry tech)
    {
        if (Research.AddToQueue(tech.UID)) ReloadResearchQueue();
    }

    public void ReloadResearchQueue()
    {
        QueueIds = Screen.Player.data.ResearchQueue.ToArray();
        if (SelectedUid == null || !QueueIds.Contains(SelectedUid)) SelectedUid = QueueIds.FirstOrDefault();
        FirstVisible = Math.Clamp(FirstVisible, 0, Math.Max(0, QueueIds.Length - 1 - VisibleCapacity));
        PerformLayout();
    }
}
