using System;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Graphics;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game;

// Captured on the simulation thread. Drawing never creates clients or reads live contracts.
public sealed class PirateFactionPresentation
{
    public readonly Empire Faction;
    public readonly string Name, ArtPath, Relationship;
    public readonly int Level, Standing, NextRaid;
    public readonly double Investment, NextLevelCost;
    public readonly Color Accent;
    public float Growth => Level >= Pirates.MaxLevel ? 1f : (float)Math.Clamp(Investment / Math.Max(1, NextLevelCost), 0, 1);
    public int Danger => DangerTier(Level);
    public static int DangerTier(int level) => level <= 0 ? 0 : (Math.Clamp(level, 1, Pirates.MaxLevel) - 1) / 4 + 1;
    public string DangerText => PirateUnderworld.Text("PirateDanger" + Danger);
    public Color DangerColor => Danger >= 5 ? new Color(242, 78, 100) : Danger >= 4 ? new Color(255, 155, 54) : new Color(226, 186, 94);

    public PirateFactionPresentation(Empire faction, Empire player, PirateUnderworld market)
    {
        Faction = faction;
        Name = faction.Name;
        Level = faction.Pirates.Level;
        var client = market.Client(faction, player);
        Standing = client.Reputation;
        Investment = faction.Pirates.Market.Investment;
        NextLevelCost = market.NextLevelCost(faction);
        NextRaid = faction.Pirates.Market.NextAuction == 0
            ? market.Duration(faction.Universe, market.Settings.AuctionTurns)
            : Math.Max(0, faction.Pirates.Market.NextAuction - market.Turn);
        Relationship = market.Locked(faction, player) ? PirateUnderworld.Text("PirateIncomingRaid")
            : market.Protected(faction, player) ? PirateUnderworld.Text("PirateProtectionRemaining", client.ProtectionEnd - market.Turn)
            : faction.IsAtWarWith(player) ? PirateUnderworld.Text("PirateHostile") : PirateUnderworld.Text("PirateNoAgreement");
        string identity = faction.data.Traits.Name;
        // The identity fallback preserves artwork for saves created before PirateArtwork existed.
        ArtPath = !string.IsNullOrWhiteSpace(faction.data.PirateArtwork) ? faction.data.PirateArtwork
            : identity == "Draugar" ? "Underworld/Draugar" : identity == "Corsairs" ? "Underworld/Corsairs" : "Encounters/pirates3";
        Accent = identity == "Draugar" ? new Color(182, 151, 232)
            : identity == "Corsairs" ? new Color(231, 174, 77) : faction.EmpireColor;
    }

    public static readonly Color Brass = new(153, 119, 63);
    public static readonly Color Cream = new(239, 226, 197);

    public static SubTexture LoadArt(string path) => ResourceManager.TextureOrDefault(path, "Encounters/pirates3");

    public static string Fit(string text, float width, Font font)
    {
        if (font.TextWidth(text) <= width) return text;
        int length = text.Length;
        while (length > 0 && font.TextWidth(text.Substring(0, length) + "...") > width) --length;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) --length;
        return text.Substring(0, length) + "...";
    }

    public static void Art(SpriteBatch batch, SubTexture texture, RectF rect, Color tint)
    {
        // Center crop instead of stretching ships when switching between cards and banners.
        float scale = Math.Max(rect.W / texture.Width, rect.H / texture.Height);
        int w = Math.Min(texture.Width, Math.Max(1, (int)(rect.W / scale)));
        int h = Math.Min(texture.Height, Math.Max(1, (int)(rect.H / scale)));
        var source = new SDGraphics.Rectangle(texture.X + (texture.Width - w) / 2,
            texture.Y + (texture.Height - h) / 2, w, h);
        batch.Draw(texture.Texture, (SDGraphics.Rectangle)rect, source, tint);
    }

    public static void Text(SpriteBatch batch, string text, float x, float y, float width, Font font, Color color)
        => batch.DrawString(font, font.ParseText(text, Math.Max(30, width)), new Vector2(x, y), color);

    public void Meter(SpriteBatch batch, RectF rect)
    {
        batch.FillRectangle(rect, new Color(8, 10, 12));
        batch.DrawRectangle(rect, Brass);
        float w = (rect.W - 12) / 5;
        for (int i = 0; i < 5; ++i)
        {
            var segment = new RectF(rect.X + 4 + i * (w + 1), rect.Y + 3, w - 2, rect.H - 6);
            batch.FillRectangle(segment, i < Danger ? DangerColor : new Color(48, 47, 46));
            if (i < Danger) batch.FillRectangle(new RectF(segment.X, segment.Y, segment.W, 2), Cream);
        }
    }
}

public sealed class PirateFactionCard : ScrollListItem<PirateFactionCard>
{
    public readonly PirateFactionPresentation Info;
    readonly SubTexture Artwork;
    public bool IsSelected;
    public PirateFactionCard(PirateFactionPresentation info)
    {
        Info = info;
        Artwork = PirateFactionPresentation.LoadArt(info.ArtPath);
        Name = "PirateFaction" + info.Faction.Id;
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        PirateFactionPresentation.Art(batch, Artwork, RectF, Color.White);
        batch.FillRectangle(new RectF(X, Bottom - 94, Width, 94), new Color(5, 8, 12, 235).Premultiplied());
        batch.DrawRectangle(RectF, IsSelected || Hovered ? Info.Accent : PirateFactionPresentation.Brass, IsSelected ? 2 : 1);
        PirateFactionPresentation.Text(batch, PirateFactionPresentation.Fit(Info.Name, Width - 24, Fonts.Arial20Bold),
            X + 12, Bottom - 87, Width - 24, Fonts.Arial20Bold, PirateFactionPresentation.Cream);
        PirateFactionPresentation.Text(batch, Info.DangerText, X + 12, Bottom - 57, Width - 24, Fonts.Arial12Bold, Info.DangerColor);
        Info.Meter(batch, new RectF(X + 12, Bottom - 34, Width - 24, 17));
        if (Hovered) ToolTip.CreateTooltip(Info.Name + "\n" + PirateUnderworld.Text("PirateDangerHelp", Info.Level) + "\n" + Info.Relationship);
        base.Draw(batch, elapsed);
    }
}

// A compact illustrated contact button in the diplomacy overview.
public sealed class PirateContactButton : UIButton
{
    readonly PirateFactionPresentation Info;
    readonly SubTexture Artwork;
    public PirateContactButton(PirateFactionPresentation info, RectF rect, Action open)
        : base(ButtonStyle.Default, rect.Pos, "")
    {
        Info = info;
        Artwork = PirateFactionPresentation.LoadArt(info.ArtPath);
        RectF = rect;
        Name = "PirateContact" + info.Faction.Id;
        Tooltip = info.Name + " — " + info.Relationship + "\n" + PirateUnderworld.Text("PirateOpenChannel")
            + "\n" + PirateUnderworld.Text("PirateDangerHelp", info.Level);
        OnClick = _ => open();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        batch.FillRectangle(RectF, new Color(12, 13, 16));
        PirateFactionPresentation.Art(batch, Artwork, new RectF(X, Y, 88, Height), Color.White);
        batch.DrawRectangle(RectF, State == PressState.Hover ? Info.Accent : PirateFactionPresentation.Brass);
        float textWidth = Math.Max(100, Width * .43f - 94);
        PirateFactionPresentation.Text(batch, PirateFactionPresentation.Fit(Info.Name, textWidth, Fonts.Arial14Bold),
            X + 98, Y + 5, textWidth, Fonts.Arial14Bold, PirateFactionPresentation.Cream);
        PirateFactionPresentation.Text(batch, PirateFactionPresentation.Fit(Info.Relationship, textWidth, Fonts.Arial10),
            X + 98, Y + 29, textWidth, Fonts.Arial10, Color.LightGray);
        float meterX = X + Width * .48f;
        PirateFactionPresentation.Text(batch, Info.DangerText, meterX, Y + 5, Width * .26f, Fonts.Arial10, Info.DangerColor);
        Info.Meter(batch, new RectF(meterX, Y + 29, Width * .24f, 14));
        PirateFactionPresentation.Text(batch, PirateUnderworld.Text("PirateOpenChannel"), X + Width * .77f,
            Y + 17, Width * .22f, Fonts.Arial12Bold, Info.Accent);
    }
}

public sealed class PirateMarketButton : UIButton
{
    public bool Selected;
    public PirateMarketButton(Vector2 pos, string text) : base(ButtonStyle.Default, pos, text)
    {
        Normal = Hover = Pressed = null;
        Size = new Vector2(220, 32);
        Font = Fonts.Arial12Bold;
        HoverColor = new Color(103, 77, 36);
        PressColor = new Color(69, 49, 23);
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        if (!Visible) return;
        DefaultColor = Selected ? new Color(95, 69, 28) : new Color(27, 27, 26);
        base.Draw(batch, elapsed);
        batch.DrawRectangle(RectF, Enabled ? PirateFactionPresentation.Brass : new Color(66, 61, 50));
        if (Selected || State == PressState.Hover)
            batch.FillRectangle(new RectF(X + 2, Y + 1, Width - 4, 2), PirateFactionPresentation.Cream);
    }
}
