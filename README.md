# NRAM

An ARAM-style 2.5D top-down multiplayer action game built in Unity. Two teams of players fight through a single lane, pushing minion waves and destroying the enemy crystal to win.

## Overview

NRAM is a networked real-time multiplayer game using **Unity Netcode for GameObjects (NGO)**. All gameplay logic is server-authoritative — the server owns minion AI, projectile movement, damage resolution, and game phase transitions. Clients handle local input, visual feedback, and UI.

Players choose between two classes in the lobby — **Tank** and **Ranger** — each with distinct health pools, movement speeds, and ability kits. The game supports two control schemes switchable at runtime: **WASD** (keyboard movement) and **Point & Click** (NavMesh-driven).

### Tech Stack
- Unity URP 17.3.0
- Netcode for GameObjects 2.9.2
- Unity New Input System

---

## Gameplay

### Game Flow
The session moves through four phases managed by `GamePhaseManager`:

1. **Lobby** — Players enter trigger zones to pick a team, select a character, and ready up. A training dummy is available for practice.
2. **Countdown** — Brief delay before the match begins.
3. **In-Game** — Minion waves spawn continuously. Players fight down the lane to destroy the enemy crystal.
4. **Game Over** — Victory condition met; session resets to lobby.

### Characters

| Class | HP | Speed | W | E |
|---|---|---|---|---|
| Tank | 150 | 8 | Shield (absorption) | Fan Shot (5-projectile spread) |
| Ranger | 100 | 10 | Dash (cursor-guided) | Triple Shot (3-projectile burst) |

Both classes share the **Q** ability: a chargeable single-shot projectile with range preview. All abilities scale with player level.

### Economy & Progression
- Players earn XP from killing minions and passively over time.
- Leveling up increases max health and mana.
- Mana regenerates naturally and faster near the team spawn zone.

---

## Controls

| Action | WASD Mode | Point & Click Mode |
|---|---|---|
| Move | WASD | Right-click ground |
| Q Ability | Q | Q |
| W Ability | W | W |
| E Ability | E | E |
| Auto-attack | Right-click enemy | Right-click enemy |

All bindings are fully remappable in the in-game settings panel.

---

## Scripts Reference

### Core Systems

| Script | Purpose |
|---|---|
| `GameManager.cs` | Central UI controller — menus, connection flow, settings panel, graphics quality, FPS display |
| `GamePhaseManager.cs` | Authoritative game-flow state machine — Lobby → Countdown → InGame → GameOver |
| `GameSettings.cs` | Global toggleable settings (movement mode, UI scales) |
| `GameKeybinds.cs` | Persistent keybinding system for both control schemes, with in-game rebind UI |
| `GameObjectRegistry.cs` | Static registry of all active players, minions, and structures for fast lookups |

### Player

| Script | Purpose |
|---|---|
| `PlayerController.cs` | Core player entity — WASD/point-click movement, team/character assignment, position sync |
| `PlayerHealth.cs` | Health, shields, death/respawn, kill announcements, and health bar display |
| `PlayerMana.cs` | Mana pool with natural regen and accelerated regen near spawn |
| `PlayerXP.cs` | Experience and leveling with passive XP gain and stat bonuses on level-up |
| `AutoAttacker.cs` | Auto-attack loop — enemy hover detection, homing projectile firing, dynamic cursor |
| `CameraFollow.cs` | Isometric smooth-follow camera; switches to free-cam edge/WASD pan in server spectator mode |

### Abilities

| Script | Purpose |
|---|---|
| `ProjectileShooter.cs` | Q — charged single-shot with range ring preview (both classes) |
| `DashAbility.cs` | W — Ranger dash to cursor position |
| `ShieldAbility.cs` | W — Tank temporary absorption shield |
| `TripleShotAbility.cs` | E — Ranger 3-projectile burst |
| `FanShotAbility.cs` | E — Tank 5-projectile fan spread |

### Projectiles

| Script | Purpose |
|---|---|
| `ProjectileController.cs` | Standard projectile — moves to target point, damages on contact |
| `HomingProjectileController.cs` | Auto-attack projectile — chases target in real-time |

### Minions & Structures

| Script | Purpose |
|---|---|
| `MinionSpawner.cs` | Server-side wave spawner — creates minions at intervals during the InGame phase |
| `MinionController.cs` | Minion AI — lanes, targeting, attack, position sync |
| `MinionHealth.cs` | Minion health/death, XP grants to nearby enemies on kill |
| `MinionSettings.cs` | Data container for all minion tuning parameters |
| `StructureHealth.cs` | Tower/crystal health — triggers game-over on crystal destruction |
| `TowerAttack.cs` | Tower auto-attack — targets nearest enemy unit in range |

### Lobby & Training

| Script | Purpose |
|---|---|
| `LobbyZone.cs` | Trigger zones for team selection, character selection, and ready-up |
| `TrainingDummy.cs` | Practice target — takes damage, auto-resets, acts as tutorial hub |
| `DummyHints.cs` | "Right-click to attack" tutorial prompt above the training dummy |

### Interfaces

| Script | Purpose |
|---|---|
| `IDamageable.cs` | Shared interface for any damageable entity |
