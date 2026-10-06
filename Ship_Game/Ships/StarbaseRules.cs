using System;
using Ship_Game.AI;
using Ship_Game.Universe;
using SDGraphics;

namespace Ship_Game.Ships;

// Strategic capabilities come from module data, never a particular module UID.
public readonly struct StationCapabilities
{
    public readonly float Borders, Sensors, Inhibition;
    public StationCapabilities(float borders, float sensors, float inhibition)
    { Borders = borders; Sensors = sensors; Inhibition = inhibition; }

    public static StationCapabilities ForDesign(IShipDesign design)
    {
        float borders = 0, sensors = 0, inhibition = 0;
        if (design == null) return default;
        foreach (string uid in design.UniqueModuleUIDs)
            if (ResourceManager.GetModuleTemplate(uid, out ShipModule module))
            {
                if (design.IsPlatformOrStation) borders = Math.Max(borders, module.BorderClaimRadius);
                sensors = Math.Max(sensors, module.SensorRange);
                inhibition = Math.Max(inhibition, module.InhibitionRadius);
            }
        return new(borders, sensors, inhibition);
    }
}

public static class StarbaseRules
{
    public const float NoBuildRadius = 15000;
    public const float SunOrbitWidth = 10000;
    public static bool IsStarbase(IShipDesign design) => StationCapabilities.ForDesign(design).Borders > 0;
    public static float InnerOrbit(SolarSystem system) => Math.Max(20000, system.SunDangerRadius + 1000);
    public static float OuterOrbit(SolarSystem system) => InnerOrbit(system) + SunOrbitWidth;

    public static SolarSystem SystemAt(Empire owner, Vector2 position)
    {
        SolarSystem nearest = null;
        float distance = float.MaxValue;
        foreach (SolarSystem system in owner.Universe.Systems)
        {
            float d = position.Distance(system.Position);
            if (d <= system.Radius && d < distance) { nearest = system; distance = d; }
        }
        return nearest;
    }

    // Shared by previews, goals and the final constructor deployment. Pending
    // deployments reserve a system and their clearance area just like live bases.
    public static bool CanDeploy(Empire owner, IShipDesign design, Vector2 position,
        Planet tether, Ship replacing, Goal reservation, out string reason)
    {
        reason = null;
        if (design == null) { reason = "No station design selected"; return false; }
        if (!design.IsPlatformOrStation) return true;
        bool command = IsStarbase(design);
        if (!command) return true;
        SolarSystem system = SystemAt(owner, position);
        if (command)
        {
            if (system == null || !system.IsExploredBy(owner))
                reason = "Starbases require an explored solar system";
            else if (tether != null || position.Distance(system.Position) < InnerOrbit(system)
                                   || position.Distance(system.Position) > OuterOrbit(system))
                reason = "Place the starbase in the marked orbit near the sun";
            else if (!owner.Universe.P.DisablePoliticalBorders)
                foreach (Empire other in owner.Universe.Empires)
                    if (other != owner && !other.IsDefeated && other.InfluenceActive && other.IsInBorderTerritory(position))
                    { reason = "Cannot establish a starbase in foreign or contested territory"; break; }
            if (reason != null) return false;
        }
        foreach (Ship station in owner.Universe.Ships)
        {
            if (station == replacing || !station.Active || station.Dying || !station.IsPlatformOrStation) continue;
            bool otherCommand = station.IsStarbase;
            if (command && otherCommand && SystemAt(owner, station.Position) == system)
                reason = "Only one starbase is allowed in a solar system";
            else if ((command && otherCommand) && position.InRadius(station.Position, NoBuildRadius))
                reason = "Starbase clearance: keep starbases 15,000 apart";
            if (reason != null) return false;
        }
        foreach (Empire empire in owner.Universe.Empires)
            foreach (Goal goal in empire.AI.Goals)
            {
                if (goal == reservation || !goal.IsDeploymentGoal || goal.ToBuild == null
                    || !goal.ToBuild.IsPlatformOrStation || (replacing != null && goal.OldShip == replacing)) continue;
                bool otherCommand = IsStarbase(goal.ToBuild);
                if (command && otherCommand && SystemAt(owner, goal.BuildPosition) == system)
                    reason = "A starbase is already being built in this system";
                else if ((command && otherCommand) && position.InRadius(goal.BuildPosition, NoBuildRadius))
                    reason = "This construction site is reserved by another starbase";
                if (reason != null) return false;
            }
        return true;
    }

    public static bool CanRefit(Ship ship, IShipDesign design)
    {
        if (ship == null || design == null || design == ship.ShipData || design.ShipRole.Protected
            || ship.IsSubspaceProjector || ship.IsResearchStation != design.IsResearchStation
            || ship.IsMiningStation != design.IsMiningStation) return false;
        bool sameHull = ship.ShipData.Hull == design.Hull;
        bool crossHull = (ship.IsStarbase || ship.IsResearchStation || ship.IsMiningStation) && design.IsPlatformOrStation;
        if (!sameHull && !crossHull) return false;
        Planet tether = ship.GetTether();
        if (design.IsShipyard && (tether == null || tether.Owner != ship.Loyalty)) return false;
        return CanDeploy(ship.Loyalty, design, ship.Position, tether, ship, null, out _);
    }
}
