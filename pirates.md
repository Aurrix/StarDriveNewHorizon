# Pirate Underworld: Bounties, Fleet Rentals, and Funded Growth

## Summary

Create a dedicated **Underworld** screen where empires fund anonymous bounties, rent pirate fleets, buy protection, and build relationships with individual factions.

The central tradeoff: **the pirates you finance today can become tomorrow’s galactic menace.** All major empires participate under the same rules.

Build on the existing pirate factions, bases, ship tiers, extortion, and raids. The first release includes the complete gameplay loop; intelligence trading, named captains, and narrative events remain future additions.

## Gameplay

### Shared bounty board

- One galaxy-wide board tracks persistent bounty totals against major empires. Multiple sponsors can contribute to the same target.
- Contributions are immediate and nonrefundable. Sponsor identities remain anonymous; players can see their own contribution history.
- Split contribution revenue equally among living pirate factions. Track pirate funding separately from outstanding bounty, so money strengthens pirates only once.
- Each faction periodically selects the highest eligible bounty. Break ties by oldest outstanding contribution, then empire ID. Already launched raids keep their target.
- Launching a raid consumes part of its target’s bounty. Confirmed destruction or capture of the target’s ships and stations consumes additional bounty. Failed raids retain their launch cost.
- Count destruction or capture once per asset; ordinary hull damage earns nothing. Player-controlled rentals cannot claim bounties.
- When a target is defeated, retire its remaining bounty without refunds. Disable contributions when no pirate factions survive.

### Protection and betrayal

- Retain recurring protection payments and ordinary extortion raids.
- A sufficiently large bounty can buy out protection. Show the threshold in advance, then give the client a warning period before breaking the agreement.
- Refund the unused portion of protection when betrayal takes effect. Reserve this liability when accounting for pirate growth.
- Lock the selected attack during the warning period. The target cannot cancel it by immediately buying protection again.
- Existing fleet leases remain valid through betrayal. New rentals are unavailable while the faction is hostile.
- Protection becomes available again after the bounty raid concludes.

### Delivered fleet rentals

- Offer light, medium, and heavy packages using the faction’s existing ship designs. Faction level and reputation determine availability.
- Show the exact composition, upfront fee, normal ship upkeep, delivery delay, and lease duration before purchase.
- Deliver at a selected owned colony after a delay; start the lease timer on delivery. If that colony is lost, use another owned colony; refund an undeliverable order.
- Players command delivered ships normally and may split or combine fleets. Track contracts by individual ship IDs.
- Permit repairs and resupply; block gifting, scrapping, refitting, and voluntary scuttling through both UI and simulation commands.
- Allow manual renewal before expiration. Surviving ships return to pirate ownership and retreat when the lease ends; destroyed ships receive no free replacement.
- Captured ships remain with their captor. Expiration must never transfer a captured ship remotely.
- If the issuing faction dies, existing leases run to expiration; surviving contractors then depart from play.

### Reputation and faction identity

- Give each faction a separate reputation score with each major empire.
- Settled business improves standing; broken agreements and attacks on the faction reduce it. Calculate business gains from aggregate spending, preventing gains from splitting tiny payments.
- Higher standing unlocks larger rentals, lowers hire prices, and raises the bounty required to buy out protection.
- Use existing faction identities and artwork. Give Corsairs a rental-price advantage and Draugar stronger bounty raids; modded factions receive neutral defaults.
- Preserve uncertainty about loyalty through explicit prices and betrayal thresholds, rather than random lease cancellation.

### Funded growth

- Replace random payment-driven leveling with visible investment progress. Protection revenue, rental fees, and bounty contributions fund expansion; successful ordinary raids contribute fixed loot value.
- Reuse existing base creation, technology improvements, ship tiers, and flagship progression.
- Rising levels increase raid strength, rental quality, and operational capacity. High-level factions can launch repeated large raids and demand substantially more protection.
- Keep the existing level ceiling of 20 as the initial technical limit. Achieve the galactic-menace outcome through stronger forces and additional operations within that range.
- Destroyed bases reduce level and erase current progress toward the next level, preventing immediate recovery from banked progress.

## Implementation

- Add a serialized universe-level `PirateUnderworld` coordinator for the shared board, contribution accounting, raid commitments, and once-per-turn processing.
- Extend faction pirate state with investment progress, reputation, explicit protection agreements, and lease records. Keep these separate from ordinary war relationships.
- Provide shared simulation commands for contribution, protection purchase, rental purchase, and renewal. UI and AI use the same validation and payment paths.
- Route existing player encounter payments and AI protection payments through this accounting. Preserve the existing first-contact colony thresholds.
- Implement bounty raids as tracked missions using existing targeting and combat behavior. Record their participating ships, launch expenditure, and completion state; avoid recalling unrelated ships in the same system.
- Add a lease-specific ownership-transfer path using existing ship-list, fleet, troop, and influence updates, without boarding rewards or salvage-based pirate growth.
- Explicitly preserve rental upkeep: the current maintenance exemption for ships that cannot be refitted must not make leased ships free.
- Add the Underworld screen to Diplomacy, with links from pirate encounters and notifications. Include faction overview, shared bounties, rentals, protection, and contract history. Hide unknown empire identities.
- Marshal purchases to the simulation thread and revalidate funds and availability there.
- Save all balances, deadlines, contracts, and credited assets. Older saves initialize an empty board and neutral reputation while preserving pirate levels and existing protection timers.
- Register new sources in the projects’ explicit compile lists and localize new interface text.

## Initial Balance Defaults

These are configurable starting values for implementation and playtesting:

| Mechanic | Initial default |
|---|---|
| Bounty selection interval | 50 turns per faction |
| Launch expenditure | 20% of the selected target’s current bounty |
| Damage expenditure | Destroyed/captured asset’s base production value, capped by remaining bounty |
| Raid strength | Existing level-based strength, increased by launch expenditure up to 3× |
| Protection buyout | 3× the current agreement’s full price, increased by reputation |
| Betrayal warning | 10 turns |
| Rental delivery / term | 10 / 100 turns |
| Rental fee | 25% of package base production value, plus normal upkeep |
| Concurrent rentals | Two contracts per customer per faction |
| Investment needed for next level | `1,000 × current level²` credits |
| Reputation | 0–100; start at 25; improved access at 50 and 75 |

Scale contract and raid durations with production pace. Snapshot raid level and expenditure at launch so the same contribution cannot inflate that mission twice through immediate leveling.

AI budgets reserve normal operating expenses before discretionary pirate spending. AI contributes against war enemies, rents when it needs military strength, and compares protection cost against pirate danger. It receives no privileged sponsor information.

## Validation

- Verify pooled contributions, permanent balances, anonymous views, target ordering, launch deductions, damage deductions, and prevention of duplicate growth or asset credit.
- Exercise protection expiry, buyout thresholds, warning periods, compensation, and existing leases surviving betrayal.
- Test delivery, renewal, expiration, split fleets, capture, destruction, faction defeat, upkeep, and blocked conversion actions.
- Round-trip saves during bidding, betrayal warnings, delivery, active raids, and leases; load an older save without duplicating goals or resetting pirate strength.
- Test missing modded ship designs and delivery failure without lost money or partial contracts.
- Run fleet, ownership-transfer, finance, and serialization regressions.
- Playtest the complete loop with multiple AI sponsors: a target climbs the board, a raid spends its bounty, pirate investment produces visible growth, and base destruction reverses that growth.
- Check the new screen at supported resolutions and verify every price, deadline, and betrayal warning is understandable before committing money.
