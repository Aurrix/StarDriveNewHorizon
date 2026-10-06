using System;
using System.Linq;
using SDGraphics;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.Data.Serialization;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game;

[StarDataType]
public sealed class PirateMarketSettings
{
    [StarData] public int AuctionTurns = 50, WarningTurns = 10, DeliveryTurns = 10, LeaseTurns = 100;
    [StarData] public int MaxLeases = 2, RaidTurns = 50;
    [StarData] public float LaunchFraction = .2f, HireFraction = .25f, BuyoutMultiplier = 3;
    [StarData] public float InvestmentCost = 1000, LootInvestment = 250;
}

[StarDataType]
public sealed class PirateClient
{
    [StarData] public Empire Empire;
    [StarData] public float Business, ReputationPenalty;
    [StarData] public int ProtectionStart, ProtectionEnd;
    [StarData] public float ProtectionPrice, ProtectionEarned;
    public int Reputation => (int)Math.Clamp(25 + Business / 1000 - ReputationPenalty, 0, 100);
}

[StarDataType]
public sealed class PirateFactionMarket
{
    [StarData] public double Investment;
    [StarData] public int NextAuction;
    [StarData] public Array<PirateClient> Clients = new();
}

[StarDataType]
public sealed class PirateBounty
{
    [StarData] public Empire Target;
    [StarData] public double Remaining;
    [StarData] public int FirstTurn;
}

[StarDataType]
public sealed class PirateContribution
{
    [StarData] public Empire Sponsor, Target;
    [StarData] public double Amount;
    [StarData] public int Turn;
}

public enum PirateLeaseStatus { Delivering, Active, Returned, Refunded }

[StarDataType]
public sealed class PirateLease
{
    [StarData] public int Id;
    [StarData] public Empire Faction, Customer;
    [StarData] public Planet Destination;
    [StarData] public string[] Designs;
    [StarData] public float Fee;
    [StarData] public int DeliveryTurn, EndTurn;
    [StarData] public PirateLeaseStatus Status;
    [StarData] public Array<Ship> Ships = new();
}

[StarDataType]
public sealed class PirateBountyRaid
{
    [StarData] public Empire Faction, Target;
    [StarData] public int LaunchTurn, EndTurn, Level;
    [StarData] public double Expenditure;
    [StarData] public bool Launched, Complete, BrokeProtection;
    [StarData] public Array<Ship> Ships = new();
    [StarData] public Ship Objective;
}

/// <summary>
/// All market transactions run on the simulation thread. Combat callbacks also take the
/// coordinator lock because ship updates can run in parallel. Bounty is an outstanding
/// incentive, never a second source of revenue when a raid spends it.
/// </summary>
[StarDataType]
public sealed class PirateUnderworld
{
    [StarData] public PirateMarketSettings Settings = new();
    [StarData] public Array<PirateBounty> Bounties = new();
    [StarData] public Array<PirateContribution> Contributions = new();
    [StarData] public Array<PirateLease> Leases = new();
    [StarData] public Array<PirateBountyRaid> Raids = new();
    [StarData] public int Turn;
    [StarData] int NextLeaseId = 1;
    [StarData] float LastStarDate;

    public int Duration(UniverseState u, int turns) => Math.Max(1, (int)Math.Ceiling(turns * u.ProductionPace));
    public double BountyFor(Empire target) => Bounties.FirstOrDefault(b => b.Target == target)?.Remaining ?? 0;
    public bool Locked(Empire faction, Empire target) => Raids.Any(r => !r.Complete && r.Faction == faction && r.Target == target);
    public bool IsBountyRaider(Ship ship)
    {
        lock (this) return Raids.Any(r => !r.Complete && r.Ships.Contains(ship));
    }
    public double NextLevelCost(Empire faction) => Settings.InvestmentCost * Math.Pow(Math.Max(1, faction.Pirates.Level), 2);

    public string LeaseDescription(int id)
    {
        lock (this)
        {
            var lease = Leases.FirstOrDefault(l => l.Id == id);
            return lease == null ? "" : Text("PirateLeaseTooltip", lease.Faction.Name, Math.Max(0, lease.EndTurn - Turn));
        }
    }

    public PirateClient Client(Empire faction, Empire customer)
    {
        var market = faction.Pirates.Market;
        var client = market.Clients.FirstOrDefault(c => c.Empire == customer);
        if (client != null) return client;
        client = new PirateClient { Empire = customer };
        // Old saves used peace and a payment timer as their protection contract.
        if (!faction.IsAtWarWith(customer)
            && faction.Pirates.ThreatLevels.TryGetValue(customer.Id, out int threat) && threat > 0
            && faction.Pirates.PaymentTimers.TryGetValue(customer.Id, out int remaining) && remaining > 0)
        {
            client.ProtectionStart = Turn;
            client.ProtectionEnd = Turn + remaining;
        }
        market.Clients.Add(client);
        return client;
    }

    public bool Protected(Empire faction, Empire customer)
        => Client(faction, customer).ProtectionEnd > Turn && !faction.IsAtWarWith(customer);

    public float ProtectionPrice(Empire faction, Empire customer)
    {
        float percent = Encounter.GetEncounterForAI(faction, 0, out Encounter e) && e.PercentMoneyDemanded > 0
            ? e.PercentMoneyDemanded : 10;
        return Math.Max(100, faction.Pirates.GetMoneyModifier(customer, percent));
    }

    public double BuyoutPrice(Empire faction, Empire customer)
    {
        var client = Client(faction, customer);
        return Math.Max(client.ProtectionPrice, ProtectionPrice(faction, customer))
             * Settings.BuyoutMultiplier * (1 + client.Reputation / 100f);
    }

    static bool CustomerValid(Empire customer) => customer != null && !customer.IsFaction && !customer.IsDefeated;
    static bool FactionValid(Empire faction) => faction?.WeArePirates == true && !faction.IsDefeated;
    static bool CanSpend(Empire customer, double amount) => double.IsFinite(amount) && amount > 0 && amount <= customer.Money;

    public bool Contribute(Empire sponsor, Empire target, double amount)
    {
        amount = (float)amount; // Empire money transactions use single precision.
        lock (this)
        {
            if (!CustomerValid(sponsor) || !CustomerValid(target) || sponsor == target
                || !sponsor.IsKnown(target) || !CanSpend(sponsor, amount) || sponsor.Universe != target.Universe)
                return false;
            var factions = sponsor.Universe.PirateFactions.Where(FactionValid).ToArray();
            if (sponsor.Universe.P.DisablePirates || !factions.Any(f => sponsor.IsKnown(f))) return false;
            var bounty = Bounties.FirstOrDefault(b => b.Target == target);
            if (bounty == null)
            {
                bounty = new PirateBounty { Target = target, FirstTurn = Turn };
                Bounties.Add(bounty);
            }
            if (bounty.Remaining <= 0) bounty.FirstTurn = Turn;
            sponsor.AddMoney(-(float)amount);
            bounty.Remaining += amount;
            Contributions.Add(new PirateContribution { Sponsor = sponsor, Target = target, Amount = amount, Turn = Turn });
            foreach (Empire faction in factions)
                Receive(faction, sponsor, amount / factions.Length);
            return true;
        }
    }

    void Receive(Empire faction, Empire customer, double amount)
    {
        faction.Pirates.Market.Investment += amount;
        if (customer != null) Client(faction, customer).Business += (float)amount;
    }

    public bool PurchaseProtection(Empire faction, Empire customer, float quotedPrice = -1)
    {
        lock (this)
        {
            if (!FactionValid(faction) || !CustomerValid(customer) || faction.Universe != customer.Universe || !customer.IsKnown(faction)
                || Locked(faction, customer) || Protected(faction, customer)) return false;
            float price = ProtectionPrice(faction, customer);
            if (quotedPrice >= 0 && Math.Abs(price - quotedPrice) > .01f || !CanSpend(customer, price)) return false;
            customer.AddMoney(-price);
            var client = Client(faction, customer);
            client.ProtectionStart = Turn;
            client.ProtectionEnd = Turn + Duration(faction.Universe, faction.data.PiratePaymentPeriodTurns);
            client.ProtectionPrice = price;
            client.ProtectionEarned = 0;
            faction.AI.EndWarFromEvent(customer);
            faction.SignTreatyWith(customer, TreatyType.NonAggression);
            faction.Pirates.PaymentTimers[customer.Id] = client.ProtectionEnd - Turn;
            faction.Pirates.ResetThreatLevelFor(customer);
            return true;
        }
    }

    public string[] Package(Empire faction, int tier)
    {
        if (!FactionValid(faction) || tier < 0 || tier > 2) return System.Array.Empty<string>();
        var forces = new Pirates.PirateForces(faction, faction.Pirates.Level, chooseRandom: false);
        int fighters = new[] { 4, 8, 12 }[tier], frigates = new[] { 1, 3, 6 }[tier];
        return Enumerable.Repeat(forces.Fighter, fighters).Concat(Enumerable.Repeat(forces.Frigate, frigates)).ToArray();
    }

    public float RentalPrice(Empire faction, Empire customer, string[] designs)
    {
        float cost = 0;
        foreach (string name in designs)
        {
            if (string.IsNullOrEmpty(name) || !ResourceManager.GetShipTemplate(name, out Ship template)) return -1;
            cost += template.ShipData.BaseCost;
        }
        float discount = 1 - Client(faction, customer).Reputation * .002f;
        if (faction.data.Traits.Name == "Corsairs") discount *= .85f;
        return Math.Max(1, cost * Settings.HireFraction * discount);
    }

    public bool RentalAvailable(Empire faction, Empire customer, int tier)
    {
        if (!FactionValid(faction) || !CustomerValid(customer) || faction.Universe != customer.Universe || !customer.IsKnown(faction)
            || faction.IsAtWarWith(customer) || Locked(faction, customer) || tier < 0 || tier > 2) return false;
        int reputation = Client(faction, customer).Reputation;
        return (tier == 0 || tier == 1 && reputation >= 50 && faction.Pirates.Level >= 5
                || tier == 2 && reputation >= 75 && faction.Pirates.Level >= 10)
            && Leases.Count(l => l.Customer == customer && l.Faction == faction
                && l.Status is PirateLeaseStatus.Active or PirateLeaseStatus.Delivering) < Settings.MaxLeases;
    }

    public bool PurchaseRental(Empire faction, Empire customer, Planet destination, int tier, float quotedPrice = -1)
    {
        lock (this)
        {
            if (!RentalAvailable(faction, customer, tier) || destination?.Owner != customer) return false;
            string[] designs = Package(faction, tier);
            float fee = RentalPrice(faction, customer, designs);
            if (designs.Length == 0 || fee < 0 || !CanSpend(customer, fee)
                || quotedPrice >= 0 && Math.Abs(fee - quotedPrice) > .01f) return false;
            customer.AddMoney(-fee);
            // Delivery money is escrowed until the complete package exists.
            Leases.Add(new PirateLease { Id = NextLeaseId++, Faction = faction, Customer = customer,
                Destination = destination, Designs = designs, Fee = fee,
                DeliveryTurn = Turn + Duration(customer.Universe, Settings.DeliveryTurns) });
            return true;
        }
    }

    public bool Renew(Empire customer, int leaseId)
    {
        lock (this)
        {
            var lease = Leases.FirstOrDefault(l => l.Id == leaseId && l.Customer == customer);
            if (lease == null || lease.Status != PirateLeaseStatus.Active || Turn >= lease.EndTurn
                || !FactionValid(lease.Faction) || lease.Faction.IsAtWarWith(customer) || Locked(lease.Faction, customer)
                || lease.EndTurn - Turn > Duration(customer.Universe, Settings.LeaseTurns)
                || !lease.Ships.Any(s => s.Active && s.Loyalty == customer && s.PirateLeaseId == lease.Id)
                || !CanSpend(customer, lease.Fee)) return false;
            customer.AddMoney(-lease.Fee);
            lease.EndTurn += Duration(customer.Universe, Settings.LeaseTurns);
            Receive(lease.Faction, customer, lease.Fee);
            return true;
        }
    }

    public void Update(UniverseState u)
    {
        lock (this)
        {
            if (u.P.DisablePirates || u.StarDate <= LastStarDate) return;
            LastStarDate = u.StarDate;
            ++Turn;
            foreach (var bounty in Bounties)
                if (bounty.Target.IsDefeated) bounty.Remaining = 0;
            foreach (Empire faction in u.PirateFactions)
            {
                UpdateProtection(faction);
                if (!FactionValid(faction)) continue;
                var market = faction.Pirates.Market;
                if (market.NextAuction == 0) market.NextAuction = Turn + Duration(u, Settings.AuctionTurns);
                if (Turn >= market.NextAuction)
                {
                    market.NextAuction = Turn + Duration(u, Settings.AuctionTurns);
                    SelectRaid(faction);
                }
            }
            foreach (var raid in Raids) if (!raid.Complete) UpdateRaid(raid);
            foreach (var lease in Leases) UpdateLease(lease);
            foreach (Empire faction in u.PirateFactions.Where(FactionValid))
            {
                var market = faction.Pirates.Market;
                double cost = NextLevelCost(faction);
                if (faction.Pirates.Level < Pirates.MaxLevel && market.Investment >= cost)
                {
                    int level = faction.Pirates.Level;
                    faction.Pirates.TryLevelUp(u, alwaysLevelUp: true);
                    if (faction.Pirates.Level > level) market.Investment -= cost;
                }
            }
            if (Turn % Duration(u, Settings.AuctionTurns) == 0) UpdateBuyers(u);
        }
    }

    void UpdateProtection(Empire faction)
    {
        foreach (Empire customer in faction.Universe.MajorEmpires) Client(faction, customer);
        foreach (var client in faction.Pirates.Market.Clients)
        {
            if (client.ProtectionEnd <= client.ProtectionStart) continue;
            float earned = client.ProtectionPrice * Math.Clamp((Turn - client.ProtectionStart)
                / (float)(client.ProtectionEnd - client.ProtectionStart), 0, 1);
            if (earned > client.ProtectionEarned)
            {
                Receive(faction, client.Empire, earned - client.ProtectionEarned);
                client.ProtectionEarned = earned;
            }
            faction.Pirates.PaymentTimers[client.Empire.Id] = Math.Max(0, client.ProtectionEnd - Turn);
            if (faction.IsDefeated || client.Empire.IsDefeated)
                EndProtection(faction, client, refund: true);
            else if (Turn >= client.ProtectionEnd)
                EndProtection(faction, client, refund: false);
            else if (faction.IsAtWarWith(client.Empire))
            {
                client.ReputationPenalty += 20;
                EndProtection(faction, client, refund: false);
            }
        }
    }

    void EndProtection(Empire faction, PirateClient client, bool refund)
    {
        float unearned = Math.Max(0, client.ProtectionPrice - client.ProtectionEarned);
        if (refund) client.Empire.AddMoney(unearned);
        else Receive(faction, null, unearned);
        client.ProtectionStart = client.ProtectionEnd = Turn;
        client.ProtectionPrice = client.ProtectionEarned = 0;
        faction.Pirates.PaymentTimers[client.Empire.Id] = 0;
    }

    void SelectRaid(Empire faction)
    {
        if (Raids.Count(r => !r.Complete && r.Faction == faction) >= Math.Max(1, faction.Pirates.Level / 5)) return;
        var bounty = Bounties.Where(b => b.Remaining >= 1 && !b.Target.IsDefeated
                && !(GlobalStats.RestrictAIPlayerInteraction && b.Target.isPlayer)
                && !Locked(faction, b.Target)
                && (!Protected(faction, b.Target) || b.Remaining >= BuyoutPrice(faction, b.Target)))
            .OrderByDescending(b => b.Remaining).ThenBy(b => b.FirstTurn).ThenBy(b => b.Target.Id).FirstOrDefault();
        if (bounty == null || FindObjective(bounty.Target) == null) return;
        bool protection = Protected(faction, bounty.Target);
        var raid = new PirateBountyRaid { Faction = faction, Target = bounty.Target,
            Level = Math.Max(1, faction.Pirates.Level), BrokeProtection = protection,
            LaunchTurn = Turn + (protection ? Duration(faction.Universe, Settings.WarningTurns) : 0) };
        Raids.Add(raid);
        if (protection) Notify(bounty.Target, "PirateBetrayalWarning", faction.Name, raid.LaunchTurn - Turn);
    }

    static Ship FindObjective(Empire target) => target.OwnedShips.Where(s => s.Active && !s.IsHangarShip && !s.IsLaunchingOrLanding)
        .OrderByDescending(s => s.IsPlatformOrStation).ThenBy(s => s.BaseStrength).FirstOrDefault();

    void UpdateRaid(PirateBountyRaid raid)
    {
        if (!FactionValid(raid.Faction) || raid.Target.IsDefeated) { FinishRaid(raid); return; }
        if (!raid.Launched)
        {
            if (Turn < raid.LaunchTurn) return;
            var bounty = Bounties.FirstOrDefault(b => b.Target == raid.Target);
            raid.Objective = FindObjective(raid.Target);
            if (raid.Objective == null || bounty == null || bounty.Remaining < 1) { FinishRaid(raid); return; }
            var forces = new Pirates.PirateForces(raid.Faction, raid.Level, chooseRandom: false);
            if (string.IsNullOrEmpty(forces.Frigate) || !ResourceManager.GetShipTemplate(forces.Frigate, out Ship template))
            { FinishRaid(raid); return; }
            raid.Expenditure = Math.Min(bounty.Remaining, Math.Max(1, Math.Round(bounty.Remaining * Settings.LaunchFraction, 2)));
            float baseStrength = raid.Level * 1000;
            double strength = baseStrength * (1 + Math.Min(2, raid.Expenditure / Math.Max(1, template.ShipData.BaseCost * 4)));
            if (raid.Faction.data.Traits.Name == "Draugar") strength *= 1.2;
            Vector2 position = raid.Objective.Position + new Vector2(raid.Objective.System?.Radius ?? 80000, 0);
            position.X = Math.Clamp(position.X, -raid.Faction.Universe.Size + 1000, raid.Faction.Universe.Size - 1000);
            while (strength > 0 && raid.Ships.Count < raid.Level * 10)
            {
                Ship ship = Ship.CreateShipAtPoint(raid.Faction.Universe, forces.Frigate, raid.Faction,
                    position + new Vector2(raid.Ships.Count * 100, 0));
                if (ship == null) break;
                raid.Faction.Pirates.SpawnedShips.Add(ship.Id);
                raid.Ships.Add(ship);
                strength -= Math.Max(1, ship.BaseStrength);
            }
            if (raid.Ships.Count == 0) { FinishRaid(raid); return; }
            bounty.Remaining -= raid.Expenditure;
            if (raid.BrokeProtection) EndProtection(raid.Faction, Client(raid.Faction, raid.Target), refund: true);
            Empire.SetRelationsAsKnown(raid.Faction, raid.Target);
            raid.Faction.AI.DeclareWarFromEvent(raid.Target, WarType.SkirmishWar);
            foreach (Ship ship in raid.Ships) ship.AI.OrderAttackSpecificTarget(raid.Objective);
            raid.Launched = true;
            raid.EndTurn = Turn + Duration(raid.Faction.Universe, Settings.RaidTurns);
            Notify(raid.Target, "PirateRaidLaunched", raid.Faction.Name);
        }
        else if (Turn >= raid.EndTurn || !raid.Ships.Any(s => s.Active && s.Loyalty == raid.Faction)) FinishRaid(raid);
        else if (raid.Objective == null || !raid.Objective.Active || raid.Objective.Loyalty != raid.Target)
        {
            raid.Objective = FindObjective(raid.Target);
            if (raid.Objective == null) FinishRaid(raid);
            else foreach (Ship ship in raid.Ships)
                if (ship.Active && ship.Loyalty == raid.Faction) ship.AI.OrderAttackSpecificTarget(raid.Objective);
        }
    }

    static void FinishRaid(PirateBountyRaid raid)
    {
        raid.Complete = true;
        foreach (Ship ship in raid.Ships)
            if (ship.Active && ship.Loyalty == raid.Faction) ship.AI.OrderPirateFleeHome();
    }

    void UpdateLease(PirateLease lease)
    {
        if (lease.Status == PirateLeaseStatus.Delivering && Turn >= lease.DeliveryTurn)
        {
            if (lease.Destination?.Owner != lease.Customer)
                lease.Destination = lease.Customer.GetPlanets().FirstOrDefault();
            bool valid = FactionValid(lease.Faction) && !lease.Customer.IsDefeated && lease.Destination != null
                && lease.Designs.All(n => !string.IsNullOrEmpty(n) && ResourceManager.GetShipTemplate(n, out _));
            if (valid)
            {
                foreach (string name in lease.Designs)
                {
                    Ship ship = Ship.CreateShipAtPoint(lease.Customer.Universe, name, lease.Customer,
                        lease.Destination.Position + new Vector2(5000 + lease.Ships.Count * 300, 0));
                    if (ship == null) { valid = false; break; }
                    ship.PirateLeaseId = lease.Id;
                    ship.WasPirateLease = true;
                    lease.Ships.Add(ship);
                }
            }
            if (!valid)
            {
                foreach (Ship ship in lease.Ships) ship.QueueTotalRemoval();
                lease.Customer.AddMoney(lease.Fee);
                lease.Status = PirateLeaseStatus.Refunded;
                Notify(lease.Customer, "PirateRentalRefund", lease.Faction.Name);
                return;
            }
            // Never replace the player's last fleet when all numbered slots are occupied.
            int key = lease.Customer.CreateFleetKey();
            var fleet = lease.Customer.GetFleetOrNull(key);
            if (lease.Customer.isPlayer && (fleet == null || fleet.CountShips == 0))
            {
                fleet = lease.Customer.CreateFleet(key, lease.Faction.Name);
                fleet.AddShips(lease.Ships);
                fleet.AutoArrange();
            }
            lease.Status = PirateLeaseStatus.Active;
            lease.EndTurn = Turn + Duration(lease.Customer.Universe, Settings.LeaseTurns);
            Receive(lease.Faction, lease.Customer, lease.Fee);
            Notify(lease.Customer, "PirateRentalDelivered", lease.Faction.Name, lease.EndTurn - Turn);
        }
        if (lease.Status != PirateLeaseStatus.Active) return;
        if (Turn == lease.EndTurn - Duration(lease.Customer.Universe, Settings.WarningTurns))
            Notify(lease.Customer, "PirateRentalExpiring", lease.Faction.Name, lease.EndTurn - Turn);
        if (Turn < lease.EndTurn && !lease.Customer.IsDefeated) return;
        bool returning = false;
        foreach (Ship ship in lease.Ships)
        {
            if (!ship.Active || ship.Loyalty != lease.Customer || ship.PirateLeaseId != lease.Id) continue;
            returning = true;
            if (lease.Faction.IsDefeated) ship.QueueTotalRemoval();
            else
            {
                // Returning contractors are recognized as the pirates' own ships, not loot.
                lease.Faction.Pirates.SpawnedShips.AddUnique(ship.Id);
                ship.LoyaltyChangeForPirateLease(lease.Faction);
            }
        }
        // Keep the contract active until deferred ownership changes have completed. If a
        // save happens before that update, the next turn safely reissues the return.
        if (returning) return;
        lease.Status = PirateLeaseStatus.Returned;
        Notify(lease.Customer, "PirateRentalReturned", lease.Faction.Name);
    }

    public void RecordAssetLoss(Ship victim, Empire previousOwner, Empire attacker, bool captured = false)
    {
        lock (this)
        {
            if (previousOwner == attacker || attacker == null) return;
            if (previousOwner.WeArePirates && !attacker.IsFaction)
                Client(previousOwner, attacker).ReputationPenalty += previousOwner.Pirates.IsBase(victim) ? 30 : 3;
            if (captured) victim.PirateLeaseId = 0;
            if (victim.PirateBountyCredited || victim.WasPirateLease || victim.IsHangarShip || !attacker.WeArePirates) return;
            var bounty = Bounties.FirstOrDefault(b => b.Target == previousOwner);
            if (bounty == null || bounty.Remaining <= 0) return;
            victim.PirateBountyCredited = true;
            bounty.Remaining = Math.Max(0, bounty.Remaining - Math.Max(0, victim.ShipData.BaseCost));
        }
    }

    void UpdateBuyers(UniverseState u)
    {
        foreach (Empire buyer in u.MajorEmpires.Where(e => !e.isPlayer && !e.IsDefeated))
        {
            double available = buyer.Money - Math.Max(1000, buyer.AllSpending * 20);
            if (available <= 0) continue;
            var enemy = u.MajorEmpires.Where(e => e != buyer && !e.IsDefeated && buyer.IsKnown(e) && buyer.IsAtWarWith(e)
                    && !(GlobalStats.RestrictAIPlayerInteraction && e.isPlayer))
                .OrderByDescending(e => e.CurrentMilitaryStrength).FirstOrDefault();
            if (enemy == null) continue;
            Contribute(buyer, enemy, Math.Min(available * .1, 5000));
            if (buyer.CurrentMilitaryStrength >= enemy.CurrentMilitaryStrength) continue;
            foreach (Empire faction in u.PirateFactions.Where(FactionValid))
            {
                float fee = RentalPrice(faction, buyer, Package(faction, 0));
                if (fee <= 0 || fee > available * .25) continue;
                if (PurchaseRental(faction, buyer, buyer.GetPlanets().FirstOrDefault(), 0)) break;
            }
        }
    }

    internal static void Notify(Empire recipient, string key, params object[] args)
    {
        if (!recipient.isPlayer || recipient.Universe.Screen?.NotificationManager == null) return;
        recipient.Universe.Notifications.AddNotification(new Notification { Message = Text(key, args),
            Title = Text("PirateUnderworld"), Action = "PirateUnderworld", Important = true,
            IconPath = "NewUI/icon_spy_notification", Pause = true });
    }

    public static string Text(string key, params object[] args) => string.Format(Localizer.Token(key), args);
}
