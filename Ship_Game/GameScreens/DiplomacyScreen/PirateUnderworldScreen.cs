using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Graphics;
using Ship_Game.Ships;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game;

/// <summary>Paused market UI. Every displayed quote is captured on the simulation thread.</summary>
public sealed class PirateUnderworldScreen : GameScreen
{
    readonly UniverseScreen Universe;
    Empire Faction;
    Planet Destination;
    string Tab = "PirateBounties", Help = "", Status = "", Credits = "";
    bool Busy;
    RectF Window, Hero;
    float MainX, MainWidth, HelpY;
    ScrollList<PirateFactionCard> Factions;
    PirateFactionPresentation Overview;
    ScrollList<MarketRow> Rows;
    MarketRow Selected;
    UITextEntry Amount;
    UIButton ActionButton, DestinationButton;
    readonly Array<UIButton> Tabs = new();
    SubTexture Portrait, Background;
    static string T(string key, params object[] args) => PirateUnderworld.Text(key, args);

    public PirateUnderworldScreen(UniverseScreen universe, Empire faction = null) : base(universe, toPause: universe)
    {
        Universe = universe;
        Faction = faction;
        IsPopup = true;
        TransitionOnTime = TransitionOffTime = .2f;
    }

    public override void LoadContent()
    {
        Window = new RectF(18, 18, ScreenWidth - 36, ScreenHeight - 36);
        Background = ResourceManager.Texture("Underworld/Terminal");
        CloseButton(Window.Right - 48, Window.Y + 20);
        float sidebar = Math.Clamp(Window.W * .25f, 260, 380);
        MainX = Window.X + sidebar + 30;
        MainWidth = Window.Right - 30 - MainX;
        Hero = new RectF(MainX, Window.Y + 85, MainWidth, ScreenHeight <= 768 ? 172 : 248);
        Factions = Add(new ScrollList<PirateFactionCard>(
            new RectF(Window.X + 20, Hero.Y - 15, sidebar, Window.H - 118), ScreenHeight <= 768 ? 222 : 300));
        Factions.Name = "PirateFactions";
        Factions.OnClick = card =>
        {
            if (Busy || card.Info.Faction == Faction) return;
            Faction = card.Info.Faction;
            Status = "";
            Refresh();
        };
        DestinationButton = ButtonAt(MainX, Window.Bottom - 121, "PirateDeliveryColony", () => Refresh(cyclePlanet: true));
        DestinationButton.Width = 235;
        DestinationButton.Tooltip = T("PirateDeliveryColony");
        string[] tabs = { "PirateBounties", "PirateRentals", "PirateProtection", "PirateContracts" };
        for (int i = 0; i < tabs.Length; ++i)
        {
            string tab = tabs[i];
            var button = ButtonAt(MainX + i * (MainWidth + 6) / 4, Hero.Bottom + 12, tab,
                () => { Tab = tab; Status = ""; Refresh(); });
            button.Size = new Vector2((MainWidth - 18) / 4, 36);
            button.Font = Fonts.Arial12Bold;
            Tabs.Add(button);
        }
        HelpY = Hero.Bottom + 60;
        float rowsY = HelpY + 38;
        Rows = Add(new ScrollList<MarketRow>(new RectF(MainX, rowsY,
            MainWidth, Window.Bottom - 138 - rowsY), 104));
        Rows.Name = "PirateMarketRows";
        Rows.OnClick = row =>
        {
            if (Selected != null) Selected.IsSelected = false;
            Selected = row;
            row.IsSelected = true;
            UpdateAction();
        };
        Amount = Add(new UITextEntry(new RectF(MainX + 8, Window.Bottom - 87, 140, 24), Fonts.Arial14Bold, "1000"));
        ActionButton = ButtonAt(Window.Right - 254, Window.Bottom - 96, "PirateSelectRow", Execute);
        ActionButton.Size = new Vector2(220, 40);
        Refresh();
    }

    UIButton ButtonAt(float x, float y, string key, Action action)
    {
        var button = Add(new PirateMarketButton(new Vector2(x, y), T(key)));
        button.Name = key;
        button.OnClick = _ => { if (!Busy) action(); };
        return button;
    }

    void Refresh(bool cyclePlanet = false)
    {
        if (Busy) return;
        Busy = true;
        UpdateAction();
        string tab = Tab;
        Universe.RunOnSimThread(() =>
        {
            var market = Universe.UState.Underworld;
            var player = Universe.Player;
            var factions = Universe.UState.PirateFactions.Where(f => !f.IsDefeated && player.IsKnown(f)).ToArray();
            int index = System.Array.IndexOf(factions, Faction);
            Empire faction = factions.Length == 0 ? null : factions[Math.Max(0, index)];
            var contacts = factions.Select(f => new PirateFactionPresentation(f, player, market)).ToArray();
            var overview = contacts.FirstOrDefault(c => c.Faction == faction);
            string credits = T("PirateBalance", player.Money);
            var planets = player.GetPlanets().ToArray();
            int planetIndex = System.Array.IndexOf(planets, Destination);
            Planet destination = planets.Length == 0 ? null : planets[(Math.Max(0, planetIndex) + (cyclePlanet ? 1 : 0)) % planets.Length];
            var rows = new Array<MarketRow>();
            string help = T(tab + "Help");
            if (faction != null)
            {
                var client = market.Client(faction, player);
                if (tab == "PirateBounties")
                {
                    foreach (Empire target in Universe.UState.MajorEmpires.Where(e => !e.IsDefeated && (e == player || player.IsKnown(e)))
                        .OrderByDescending(market.BountyFor))
                    {
                        Empire captured = target;
                        double own = market.Contributions.Where(c => c.Sponsor == player && c.Target == target).Sum(c => c.Amount);
                        rows.Add(new MarketRow(T("PirateBountyRow", target.Name, market.BountyFor(target)),
                            T("PirateOwnContributions", own), target == player ? null : amount => market.Contribute(player, captured, amount),
                            "PirateContribute", emblem: target));
                    }
                }
                else if (tab == "PirateRentals")
                {
                    for (int tier = 0; tier < 3; ++tier)
                    {
                        int package = tier;
                        string[] designs = market.Package(faction, tier);
                        float price = market.RentalPrice(faction, player, designs);
                        float upkeep = designs.Sum(n => ResourceManager.GetShipTemplate(n, out Ship ship)
                            ? ShipMaintenance.GetBaseMaintenance(ship.ShipData, player, 0) : 0);
                        string manifest = string.Join(", ", Enumerable.GroupBy(designs, n => n).Select(g => $"{Enumerable.Count(g)} × {g.Key}"));
                        bool available = market.RentalAvailable(faction, player, tier) && destination != null && price > 0;
                        rows.Add(new MarketRow(T("PiratePackage" + tier) + "  —  "
                            + (price < 0 ? T("PirateRentalUnavailable") : T("PirateRentalQuote", price, upkeep)),
                            manifest + "\n" + T("PirateRentalTerms", market.Duration(Universe.UState, market.Settings.DeliveryTurns),
                                market.Duration(Universe.UState, market.Settings.LeaseTurns)) + "  "
                                + (available ? "" : T("PirateRentalRequirement" + tier)),
                            available ? _ => market.PurchaseRental(faction, player, destination, package, price) : null, "PirateHire", artwork: overview.ArtPath));
                    }
                }
                else if (tab == "PirateProtection")
                {
                    float price = market.ProtectionPrice(faction, player);
                    bool locked = market.Locked(faction, player), active = market.Protected(faction, player);
                    rows.Add(new MarketRow(T("PirateProtectionQuote", price),
                        locked ? T("PirateProtectionLocked") : active
                            ? T("PirateProtectedUntil", client.ProtectionEnd - market.Turn, market.BuyoutPrice(faction, player))
                            : T("PirateProtectionTerms", market.Duration(Universe.UState, faction.data.PiratePaymentPeriodTurns)),
                        locked || active ? null : _ => market.PurchaseProtection(faction, player, price), "PirateBuyProtection"));
                    foreach (var raid in market.Raids.Where(r => !r.Complete && r.Target == player && r.Faction == faction))
                        rows.Add(new MarketRow(T("PirateIncomingRaid"), raid.Launched ? T("PirateRaidInProgress")
                            : T("PirateBetrayalWarning", faction.Name, raid.LaunchTurn - market.Turn), null, "PirateSelectRow"));
                }
            }
            if (tab == "PirateContracts")
            {
                foreach (var lease in market.Leases.Where(l => l.Customer == player).Reverse())
                {
                    int id = lease.Id;
                    rows.Add(new MarketRow(T("PirateContractRow", id, lease.Faction.Name, T("PirateLease" + lease.Status)),
                        T("PirateContractTerms", lease.Ships.Count(s => s.Active && s.Loyalty == player && s.PirateLeaseId == id),
                            Math.Max(0, (lease.Status == PirateLeaseStatus.Delivering ? lease.DeliveryTurn : lease.EndTurn) - market.Turn), lease.Fee),
                        lease.Status == PirateLeaseStatus.Active ? _ => market.Renew(player, id) : null, "PirateRenew"));
                }
                foreach (var contribution in market.Contributions.Where(c => c.Sponsor == player).Reverse())
                    rows.Add(new MarketRow(T("PirateContributionHistory", contribution.Target.Name, contribution.Amount),
                        T("PirateHistoryTurn", contribution.Turn), null, "PirateSelectRow"));
            }
            RunOnNextFrame(() =>
            {
                Faction = faction;
                Destination = destination;
                Overview = overview;
                Portrait = overview == null ? null : PirateFactionPresentation.LoadArt(overview.ArtPath);
                Credits = credits;
                Help = help;
                Factions.Reset();
                foreach (var contact in contacts)
                    Factions.AddItem(new PirateFactionCard(contact) { IsSelected = contact.Faction == faction });
                DestinationButton.Text = destination == null ? T("PirateDeliveryColony") : T("PirateDeliveryAt", destination.Name);
                DestinationButton.Visible = tab == "PirateRentals";
                Amount.Visible = tab == "PirateBounties";
                Rows.Reset();
                foreach (var row in rows)
                {
                    row.RowHeight = tab == "PirateRentals" ? 116 : 94;
                    Rows.AddItem(row);
                }
                Selected = null;
                Busy = false;
                UpdateAction();
            });
        });
    }

    void UpdateAction()
    {
        if (ActionButton == null) return;
        ActionButton.Enabled = !Busy && Selected?.Purchase != null;
        ActionButton.Text = T(Selected?.ActionKey ?? "PirateSelectRow");
    }

    void Execute()
    {
        if (Busy || Selected?.Purchase == null) return;
        double amount = 0;
        if (Tab == "PirateBounties" && (!double.TryParse(Amount.Text, NumberStyles.Number,
            CultureInfo.CurrentCulture, out amount) || !double.IsFinite(amount) || amount <= 0))
        {
            Status = T("PirateInvalidAmount");
            return;
        }
        var purchase = Selected.Purchase;
        Busy = true;
        UpdateAction();
        Universe.RunOnSimThread(() =>
        {
            bool success = purchase(amount);
            RunOnNextFrame(() =>
            {
                Status = T(success ? "PiratePurchaseSuccess" : "PiratePurchaseFailed");
                Busy = false;
                Refresh();
            });
        });
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        batch.Draw(Background, Window, Color.White);
        DrawText(batch, T("PirateUnderworld").ToUpperInvariant(), Window.X + 32, Window.Y + 23,
            Window.W - 360, Fonts.Pirulen20, PirateFactionPresentation.Cream);
        DrawText(batch, T("PirateMarketSubtitle"), Window.X + 34, Window.Y + 54, Window.W - 360, Fonts.Arial12, Color.Tan);
        DrawText(batch, Credits, Window.Right - 310, Window.Y + 38, 230, Fonts.Arial14Bold, Color.Wheat);
        if (Overview != null)
        {
            PirateFactionPresentation.Art(batch, Portrait, Hero, Color.White);
            batch.FillRectangle(new RectF(Hero.X, Hero.Bottom - 105, Hero.W, 105), new Color(5, 8, 12, 235).Premultiplied());
            batch.FillRectangle(new RectF(Hero.X, Hero.Y, Hero.W, 39), new Color(5, 8, 12, 175).Premultiplied());
            DrawText(batch, PirateFactionPresentation.Fit(Overview.Name.ToUpperInvariant(), Hero.W - 36, Fonts.Arial20Bold),
                Hero.X + 18, Hero.Y + 7, Hero.W - 36, Fonts.Arial20Bold, PirateFactionPresentation.Cream);
            batch.DrawRectangle(Hero, Overview.Accent);
            float half = Hero.W * .48f;
            DrawText(batch, Overview.DangerText, Hero.X + 18, Hero.Bottom - 97, half - 30, Fonts.Arial12Bold, Overview.DangerColor);
            Overview.Meter(batch, new RectF(Hero.X + 18, Hero.Bottom - 73, half - 36, 19));
            DrawText(batch, T("PirateStanding", Overview.Standing) + "  |  " + Overview.Relationship,
                Hero.X + half, Hero.Bottom - 97, Hero.W - half - 16, Fonts.Arial12Bold, PirateFactionPresentation.Cream);
            DrawText(batch, T("PirateNextRaid", Overview.NextRaid), Hero.X + half, Hero.Bottom - 60,
                Hero.W - half - 16, Fonts.Arial12, Color.Wheat);
            DrawText(batch, Overview.Level >= Pirates.MaxLevel ? T("PirateGrowthMaximum") : T("PirateGrowth", Overview.Level, Overview.Investment, Overview.NextLevelCost),
                Hero.X + 18, Hero.Bottom - 42, half - 32, Fonts.Arial10, Color.Wheat);
            var growth = new RectF(Hero.X + 18, Hero.Bottom - 16, Hero.W - 36, 6);
            batch.FillRectangle(growth, new Color(55, 48, 39));
            batch.FillRectangle(new RectF(growth.X, growth.Y, growth.W * Overview.Growth, growth.H), Overview.Accent);
        }
        else DrawText(batch, T("PirateNoContact"), Hero.X + 20, Hero.Y + 40, Hero.W - 40, Fonts.Arial14Bold, Color.Wheat);
        DrawText(batch, Help, MainX + 6, HelpY, MainWidth - 12, Fonts.Arial12, Color.LightGray);
        foreach (UIButton tab in Tabs)
        {
            ((PirateMarketButton)tab).Selected = tab.Name == Tab;
            if (tab.Name == Tab) batch.FillRectangle(new RectF(tab.X + 8, tab.Bottom + 3, tab.Width - 16, 2), Color.Gold);
        }
        if (Amount.Visible)
        {
            batch.FillRectangle(new RectF(Amount.X - 6, Amount.Y - 5, Amount.Width + 12, Amount.Height + 10), new Color(28, 26, 23));
            batch.DrawRectangle(new RectF(Amount.X - 6, Amount.Y - 5, Amount.Width + 12, Amount.Height + 10), PirateFactionPresentation.Brass);
            DrawText(batch, T("PirateCreditAmount"), MainX + 168, Window.Bottom - 91, MainWidth - 414, Fonts.Arial12, Color.Wheat);
        }
        DrawText(batch, Status, MainX, Window.Bottom - 45, MainWidth, Fonts.Arial12Bold, Color.Wheat);
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }

    static void DrawText(SpriteBatch batch, string text, float x, float y, float width, Font font, Color color)
    {
        batch.DrawString(font, font.ParseText(text, Math.Max(30, width)), new Vector2(x, y), color);
    }

    sealed class MarketRow : ScrollListItem<MarketRow>
    {
        readonly string Title, Detail;
        public readonly Func<double, bool> Purchase;
        public readonly string ActionKey;
        public bool IsSelected;
        public int RowHeight = 94;
        public override int ItemHeight => RowHeight;
        readonly int Flag;
        readonly Color FlagColor;
        readonly string Artwork;
        public MarketRow(string title, string detail, Func<double, bool> purchase, string actionKey, Empire emblem = null, string artwork = null)
        {
            Title = title; Detail = detail; Purchase = purchase; ActionKey = actionKey;
            Flag = emblem?.data.Traits.FlagIndex ?? -1;
            FlagColor = emblem?.EmpireColor ?? Color.White;
            Artwork = artwork;
        }
        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            batch.FillRectangle(Rect, IsSelected ? new Color(52, 42, 25) : Hovered ? new Color(35, 32, 26) : new Color(13, 16, 19, 235).Premultiplied());
            batch.DrawRectangle(Rect, IsSelected ? Color.Goldenrod : new Color(65, 59, 47));
            if (IsSelected) batch.FillRectangle(new RectF(X, Y, 3, Height), Color.Gold);
            float inset = 12;
            if (Artwork != null)
            {
                PirateFactionPresentation.Art(batch, PirateFactionPresentation.LoadArt(Artwork), new RectF(X + 10, Y + 10, 100, Height - 20), Color.White);
                inset = 124;
            }
            else if (Flag >= 0 && ResourceManager.Flag(Flag) is SubTexture flag)
            {
                batch.Draw(flag, new RectF(X + 14, Y + 16, 50, 50), FlagColor);
                inset = 80;
            }
            string title = Fonts.Arial14Bold.ParseText(Title, Width - inset - 18);
            DrawText(batch, title, X + inset, Y + 8, Width - inset - 18, Fonts.Arial14Bold, Color.Wheat);
            DrawText(batch, Detail, X + inset, Y + 12 + Fonts.Arial14Bold.MeasureString(title).Y,
                Width - inset - 18, Fonts.Arial12, Color.LightGray);
            if (Hovered) ToolTip.CreateTooltip(Title + "\n" + Detail);
            base.Draw(batch, elapsed);
        }
    }
}
