# Spell Taxonomy & Attack System — Design Contract

> **Statut :** design validé, *avant* implémentation. Date : 2026-07-28.
> Ce document est **la référence** à lire avant de toucher au pipeline d'attaque
> (cast, dégâts, effets, spawn de sous-spells). Il ne décrit pas le code actuel
> mais le **contrat cible** vers lequel on migre. Les écarts avec le code présent
> sont listés en §11 (défauts) et §12 (ordre de migration).

---

## Sommaire

1. [Le modèle en 3 couches](#1-le-modèle-en-3-couches)
2. [Familles de delivery](#2-familles-de-delivery)
3. [Axes orthogonaux](#3-axes-orthogonaux)
4. [Le système de tags](#4-le-système-de-tags)
5. [EMultiCast (sémantique d'Amount)](#5-emulticast--sémantique-damount)
6. [Matrice Trait × Famille](#6-matrice-trait--famille)
7. [Émetteurs (spells qui émettent des spells)](#7-émetteurs)
8. [Summon (agents) + système de team](#8-summon-agents--team)
9. [Buff / Debuff + canal d'effet générique](#9-buff--debuff)
10. [ResolveHit — résolution unifiée du coup](#10-resolvehit)
11. [Défauts actuels que ce design corrige](#11-défauts-actuels)
12. [Ordre de migration](#12-ordre-de-migration)
13. [Décisions encore ouvertes](#13-décisions-ouvertes)
14. [Annexe — classification du roster](#14-annexe--roster)

---

## 1. Le modèle en 3 couches

Un spell se décrit sur trois couches, et **elles ne doivent jamais se confondre** :

| Couche | C'est quoi | Rôle |
|---|---|---|
| **Capacité** | composants ECS bakés + forme (`Bounce`, `ZoneAttack`, `LinearMovement`…) | **vérité terrain** — ce que le spell *peut* faire |
| **Tags** | projection cohérente de la capacité (`Projectile`, `Bouncing`, `Elemental`…) | **monnaie de synergie** — ce que le spell *est*, requêtable |
| **Stats / Modifiers** | pierce count, dmg mult… gated par tags | **combien** |

### Règle d'or

> **Le tag DÉCRIT une capacité, il ne la CRÉE pas.**

Un tag est *dérivé* de la capacité (ou *validé* contre elle), jamais l'inverse.
Conséquences :

- La [matrice Trait×Famille](#6-matrice-trait--famille) devient **automatique** : une Area
  n'a jamais de composant `Bounce` → n'émet jamais le tag `Bouncing` → aucune synergie
  ne peut la faire rebondir. « Les zones ne rebondissent pas » est vrai *par construction*.
- Les **upgrades qui étendent** un spell agissent sur la **capacité** (activent un composant
  pré-baké désactivé) ; le tag *suit* et alimente les synergies aval.
  Ex : upgrade « Fireball → Explosive » active `ExplodeOnContact` → le tag `Explosive`
  apparaît → « +dmg aux Explosive » inclut désormais Fireball.
- Pas de **tag fantôme** (un label qui promet une capacité absente).

---

## 2. Familles de delivery

### Le discriminateur unique

> **Une famille se définit par : le hitbox est-il *consommé* en touchant ?**

| Famille | Consommé au contact ? | Définition | Set d'authoring (recette) |
|---|---|---|---|
| **Projectile** | **Oui** (ou après N pierce/bounce) puis meurt | un hitbox mobile émis du caster ; les dégâts sont sa raison d'être | `LinearMovement` + `DamageOnContact` + `DestroyOnContact` + `Lifetime` + `HitEntityMemory` + `Destructible` |
| **Area** | **Non** — vit toute sa durée | une région qui blesse ce qui la chevauche ; dégâts = effet de présence | `ZoneAttack`* + `Lifetime` + ancre |
| **Summon** | N/A — c'est un agent | une entité autonome (IA + allégeance) qui combat pour toi | `SummonAgent`* + réutilise les rails de cast / la stack ennemie |

\* `ZoneAttack` et `SummonAgent` sont des composants **cibles** (voir §12), pas encore présents.

### Le mouvement est orthogonal, pas un discriminateur

Une **Area peut se déplacer** (tornade qui erre) sans devenir un Projectile — elle n'est
pas consommée. Un **Projectile peut être immobile un instant** (mine) sans devenir une Area —
il est consommé au contact. Voir [§3 Motion](#3-axes-orthogonaux).

---

## 3. Axes orthogonaux

Ces axes se combinent **par-dessus** la famille. Ils ne créent jamais de nouvelle famille.

### Communs

- **Motion** : `None | Linear | Homing | Orbit | AttachedToCaster | Roam`
  (composants de mouvement déjà séparés : `LinearMovement`, `OrbitMovement`,
  `FollowTargetMovement`, `AttachToCaster`, `CopyEntityPosition`).

### Spécifiques à la famille **Area**

- **Anchor** : `Forward` (devant le caster) · `Caster` (suit le joueur) · `WorldTarget` (posé au sol)
- **Cadence** : `Burst` (chaque cible touchée **une fois**, mémoire hit-once) · `OverTime` (re-touche par tick, buffer enter/exit)
- **Shape** : `Circle | Cone | Ring`
- **ShapeMotion** : `Static` (`RadiusStart == RadiusEnd`) · `Expand` (rayon interpolé) · `Sweep` (le cône balaie l'angle)
- **Direction** : `Facing`/`Aim` · `NearestEnemy` · `RandomInRange` · `AtCaster`

### Identité ≠ Famille

« Strike », « Nova », « Aura », « Slash » sont des **identités** (thème + VFX + params + nom + tags),
**pas** des formes. Un *lightning strike* = mécanisme `Area` (OverlapSphere à un point, Burst,
`ActivationDelay` pour le télégraphe) + identité « Strike ». L'éclair vertical est du **VFX**.
Le tagger `Area` est *aussi* correct pour les synergies : un strike veut les mêmes modifieurs
qu'une aura (+rayon, +count, +dégâts) → il en profite gratuitement.

---

## 4. Le système de tags

`ESpellTag` est un `[Flags] enum : uint`. Chaque tag appartient à un **axe** avec une **cardinalité**
et une **source** (qui détermine s'il est dérivable).

| Tag | Axe | Cardinalité | Source (→ dérivable ?) |
|---|---|---|---|
| `Projectile` | **Forme** | exactement 1\* | composants projectile → **dérivé** |
| `Area` | Forme | | `ZoneAttack` → **dérivé** (après fusion) |
| `Summon` | Forme | | `SummonAgent` → **dérivé** |
| `Ranged` | **Portée** | ≤ 1 | intention → **authoré** |
| `Melee` | Portée | | intention → **authoré** |
| `Physical` | **Type de dégât** | exactement 1 (offensif) | intention → **authoré** |
| `Elemental` | Type de dégât | | intention → **authoré** |
| `Buff` | **Intention** | ≤ 1 | payload → authoré *now*, **dérivable** quand effet = data |
| `Debuff` | Intention | | idem |
| `Explosive` | **Comportement** | N | `ExplodeOnContact` → **dérivé (aujourd'hui)** |
| `Piercing` | Comportement | | `Pierce` → **dérivé (aujourd'hui)** |
| `Bouncing` | Comportement | | `Bounce` → **dérivé (aujourd'hui)** |
| `Burn` | **Effet** | N | tag-driven → **authoré** (dérivable quand effets = data) |
| `Slow` | Effet | | idem |
| `Stun` | Effet | | idem |
| `Knockback` | Effet | | idem |

\* Forme = exactement 1 pour un spell spatial. Un buff/debuff pur roule sur `Area`/`Summon` :
pas de cas « form-less » côté spell (un buff permanent sans forme = **upgrade de stat**, hors système).

### Choix élémentaire : `Elemental` / `Physical`, pas un tag par élément

Un tag par élément (`Fire`, `Ice`…) génère des upgrades trop spécifiques (« +dmg au Feu »
est mort sans feu). À la place : axe **`Physical | Elemental`**. La saveur élémentaire est déjà
portée par les **tags d'effet** :

- Feu = `Elemental` + `Burn` → « +durée de Burn » **est** la synergie feu
- Glace = `Elemental` + `Slow` → « +Slow » **est** la synergie glace
- Foudre = `Elemental` + `Stun`

Bonus futur : l'axe Physical/Elemental ouvre la porte aux **résistances** (armure vs physique,
résistance vs élémentaire) sans re-toucher les tags.

### Dérivation vs validation (UI de l'inspector)

La dérivation est **par tag**, selon qu'une source mécanique existe :

| Groupe | Dérivable ? | UI |
|---|---|---|
| **Comportements** (Explosive/Piercing/Bouncing) | ✅ tout de suite | read-only (preview) |
| **Forme** (Projectile/Area/Summon) | ✅ après fusion `ZoneAttack` | read-only |
| **Effets** (Burn/Slow/Stun/KB) | ❌ pas encore (le tag *est* la source) | authoré, migre vers dérivé avec « effets par spell » |
| **Sémantiques** (Ranged/Melee, Physical/Elemental, Buff/Debuff) | jamais | authoré (éditable) |

→ L'inspector **scinde** : section *dérivée* read-only + section *sémantique* éditable.
On ne cache jamais tout le champ.

---

## 5. EMultiCast — sémantique d'Amount

`Amount` ne se comporte **pas** pareil selon le spell (une Area couvre du multi-castable *et* du
singulier). Ce n'est donc **pas une règle de forme** mais un **champ par spell** sur `SpellDataSO` :

```
EMultiCast :
  Single       → Amount ignoré                       (Shockwave, VoidSlash, FrozenBlow, auras)
  Spread       → N en éventail, même direction        (Fireball classique, cônes)
  MultiTarget  → N instances sur N cibles distinctes   (ShockStrike, éclairs ; Fireball "MultiTarget")
```

- `Single` ≠ « touche 1 ennemi ». Shockwave est `Single` (un cast) mais touche tous les ennemis
  collés dans son rayon. `EMultiCast` = *combien de fois on caste*, pas *combien un cast touche*.
- **Fallback** (verrouillé) : `MultiTarget` acquiert jusqu'à N cibles **distinctes** ; le surplus
  (Amount > cibles dispo) part en **`Spread`** (éventail forward). Pas de double-assignation sur
  une cible déjà prise.
- Évolution : ce champ deviendra plus tard le **layout de l'émetteur primaire** (§7). Pour
  stabiliser, l'enum suffit.

---

## 6. Matrice Trait × Famille

| Trait | Projectile | Area | Summon | Raison |
|---|:---:|:---:|:---:|---|
| **Bounce** | ✅ | ❌ | ❌ | rebondir = rediriger un hitbox qui voyage |
| **Pierce** | ✅ | ❌ | ❌ | percer = survivre à la consommation ; une Area n'est jamais consommée |
| **Homing** | ✅ | ~ (= Motion Roam) | N/A (agent = IA) | |
| **Explode** | ✅ | ✅ | ✅ (on death) | en réalité un **preset d'Émetteur** (child = AoE) |
| **Effets** (Burn/Slow/Stun/KB) | ✅ | ✅ | ✅ | vivent dans [`ResolveHit`](#10-resolvehit) |
| **Emitter** | ✅ | ✅ | ✅ | toute famille peut émettre |

**Application :** par la composition (§1) + un warning de validation. Un prefab Area ne bake pas
`Bounce` → l'activation est ignorée silencieusement ; le warning rend l'incohérence visible au designer.

---

## 7. Émetteurs

Un **Émetteur** = « un spell qui produit d'autres spells ». C'est la généralisation de l'actuel
`SubSpellsSpawner` (aujourd'hui figé sur Orbital/Circle) + `SpellBlob.ChildPrefabIndex`.

| Paramètre | Valeurs | Ex : 3 piliers de glace |
|---|---|---|
| **Layout** | Circle / **Line** / Arc / Fan / AtTargets | `Line` (le long de la direction) |
| **Count** | N | `3` |
| **Timing** | Instant / **Sequential**(interval) / OverLifetime | `Sequential` 0.15s → le « 1-à-1 » |
| **Trigger** | OnActivation / **OnImpact** / OnDeath / Continuous | `OnImpact` |
| **Child** | *un spell complet* (récursif) | `IcePillar` (Area/WorldTarget/Burst/Circle + `ActivationDelay` + `Slow`) |
| **AttachChildren** | attaché+persistant (orbes) / **détaché+one-shot** (piliers) | détaché |
| **DirectionSource** | CasterForward / TowardTarget / Radial | forward du parent |

L'émetteur **absorbe** trois choses aujourd'hui séparées :

- **Orbital (FireOrbs)** = Émetteur `Circle` + children attachés + `OrbitMovement`.
  → l'Orbital n'est **pas** une famille, c'est un émetteur.
- **Multishot (`Amount` projectile)** = Émetteur `Fan`, children détachés.
- **Explosions** = Émetteur (child = AoE, trigger = OnImpact/OnDeath).

Un spell devient un **arbre** : racine (famille) → émetteur(s) → feuilles (spells complets),
qui repassent par le même `ZoneAttack`/`DamageOnContact` + `ResolveHit`. **Zéro nouveau chemin de dégâts.**

### Récursion & sécurité (résolu)

Un émetteur peut référencer **le même spell** que son parent (ex : Fireball qui, à l'impact, émet
2 Fireballs en `Spread` — upgrade « Split »). La récursion est donc **autorisée mais bornée**, pas
interdite : interdire self/ancestor rendrait le split fireball→fireball impossible.

**Mécanisme :** chaque entité spawnée porte un compteur de génération `SplitDepth`. L'émetteur ne se
déclenche que si `SplitDepth > 0` ; les enfants héritent de `SplitDepth − 1`. À 0, le spell ne re-split plus.

**Deux leviers d'upgrade distincts :**

| Levier | Effet | Exemple |
|---|---|---|
| **Count** | + de fragments *par* split | 2 → 3 → 4 par impact |
| **Depth** | + de *générations* (split qui re-split) | les fragments re-splittent |

**Terminaison :** une cascade finit toujours (`SplitDepth` décrémente jusqu'à 0). Le risque n'est
donc pas l'infini mais la **depth *initiale*** : `Count` et `Depth` sont tous deux upgradables
**par spell** (composition libre : depth-upgrade sur certains spells, count-upgrade sur d'autres),
et rien ne borne la depth de départ sans un gate dédié.

⚠️ **Explosion exponentielle** : total ≈ `(C^(D+1) − 1)/(C − 1)`. C=2/D=2 → 7 ; C=3/D=3 → 40 ;
C=4/D=4 → 341.

Deux garde-fous **distincts**, à ne pas confondre :

| Garde-fou | Rôle | Statut |
|---|---|---|
| **Budget de spawn par lignée de cast** | anti-crash runtime (budget épuisé → plus aucun spawn) | **obligatoire dès le jour 1** — c'est lui qui rend le report du gate sûr |
| **Gate d'upgrade** (`Depth ≥ X` → plus d'upgrade de ce type) | limite de design/contenu | **reporté** (plus tard, pas bloquant) |

Plus, indépendant de la perf :

- **Atténuation des dégâts par génération** (ex : ×0.6) — **obligatoire** : sinon le split est une
  **multiplication de dégâts gratuite**, même classe d'exploit que l'empilement d'Amount (§11).

---

## 8. Summon (agents) + Team

### Défini par l'agentivité, pas par le thème

Un Summon n'est pas « ce qui est invoqué ». C'est **un agent autonome** : une boucle de
comportement (IA / ciblage / décisions) + une **allégeance**.

- **Les fire orbs ne sont PAS des summons** — ils n'ont ni IA ni allégeance ; c'est un
  Émetteur(Circle) de hitbox bêtes attachés.

### Réutilise l'existant

| Type | Réutilise… |
|---|---|
| Tourelle | `ActiveSpell` + `SpellCooldownSystem` + un ciblage → **un mini-caster** (peut lancer n'importe quel spell de la DB : projectile, zone, heal, debuff…) |
| Mob / familier | toute la stack ennemie (flow field, avoidance, `Health`, `EnemyTargetingSystem`) |
| Réincarnation / Charme | prend un `Enemy`, **inverse l'allégeance**, re-cible |

### Comportement = axes orthogonaux (pas un enum plat)

| Axe | Valeurs |
|---|---|
| **Allégeance** | Ami / Ennemi / Neutre |
| **Mobilité** | Fixe / Orbite / Suit / Roam(IA) / Leashed |
| **Offense** | Contact / Boucle de cast / Les deux |
| **Ciblage** | Plus proche / Cible du caster / Aucun |
| **Fin de vie** | Timer / Jusqu'à mort / Cap N vivants |

`ESummonArchetype` (Turret/Orbiter/Minion/Necromancy) n'est qu'un **preset** qui pré-remplit ces axes.

### Prérequis fondateur : le **team / allégeance**

L'opposition joueur/ennemi est aujourd'hui **hardcodée** (layers de collision + `isPlayerCaster` +
`NearestTarget` visant le joueur en dur). Le ticket d'entrée de Summon n'est **pas** un `SummonSystem`,
c'est un vrai **système d'allégeance**. Une fois posé, tourelles/mobs/réincarnation/charme tombent
presque gratuitement. À designer dans une passe séparée. Ajouts mineurs : **cap de summons** +
**héritage de stats** (le summon scale avec les `Final*` de son propriétaire).

---

## 9. Buff / Heal / Debuff — mécanisme (validé 2026-08-01)

**Intentions-riders sur une forme**, orthogonales à la famille. Un buff est un spell → il a une forme :

| Exemple | Tags | Porteur |
|---|---|---|
| Bannière de ralliement (+dmg alliés autour) | `Summon \| Buff` | totem |
| Zone de hâte (+atk speed dedans) | `Area \| Buff` (ancrée caster) | aura |
| Boule de feu affaiblissante | `Projectile \| Elemental \| Debuff` | rider offensif |
| Malédiction de vulnérabilité (ennemis +dmg reçus) | `Area \| Debuff` | zone |
| Charme (retourne un ennemi X s) | `Area \| Debuff` (+ effet) | zone/rider → **réutilise le team** |

- **Buff permanent sans forme = upgrade de stat classique** (hors système de spell).

Un Buff/Heal, c'est **le même delivery qu'un dégât** — seuls changent le *résultat* et l'*allégeance*
(une aura de soin = une `Area` centrée caster qui écrit un soin dans le buffer **allié** au lieu d'un
dégât ennemi). Tout se ramène à **deux primitives**, alignées sur ce qui existe déjà (§ code : couche
delta-instantané `DamageBufferElement` vs couche effet-persistant `Burn/Slow/Stun`).

### 9.1 Canal Heal — delta de vie instantané

Symétrique de `DamageBufferElement` : un **`HealBufferElement { int Amount }`** sur les entités
soignables ; **producteurs multiples → un sink**. Répartition **verrouillée** :

| Concern | Qui | Pourquoi |
|---|---|---|
| **Magnitude** (conversion lifesteal, flat tourelle, %MaxHP pickup, drip régen) | **Producteur** | policy propre à la source |
| **Carryover** (banque fractionnaire float → PV entiers, car `Health` est `int`) | **Producteur** | pour que chaque source pulse à **sa** cadence (régen 2/s = `+1` toutes ~0.5s) |
| **Feedback vert** (nombre / couleur / position / cadence) | **Producteur** | **fontaine multi-sources** + lisibilité de la fréquence du heal |
| **Ordre** (après dégât+mort), **somme**, **clamp** MaxHealth, écriture **unique** dans `Health` | **Sink** (`ApplyHealJob` dans `HealthSystem`) | seule autorité qui voit le résultat partagé (empêche 2 sources de dépasser le max, empêche un heal de ressusciter) |

- Producteurs écrivent des **int** (carryover déjà fait) → `HealBufferElement` reste `{ int Amount }`, pas de tag de source.
- Producteurs **se gatent à plein PV** (`if health >= max return`) → pas de chiffre parasite ; le clamp du sink est le filet de sécurité (overheal simultané).
- **Ordre intra-frame :** dégâts → check mort → **si vivant** heal → clamp. `ApplyHealJob` **après** `ApplyDamageJob`, skip des morts (aujourd'hui LifeSteal tourne après HealthSystem exprès pour ça).
- **LifeSteal devient un producteur** : garde conversion + cooldown + strongest-proc + **son propre carryover**, écrit un int + émet son feedback.
- **Zone HoT = producteur direct** (son propre timer/config) → procs **séparés** de la régen. À ne pas confondre avec un **buff de régen** (+X/s temporisé) qui, lui, passe par `TempStats.HealthRegen` (§9.2) et **fusionne** avec la régen de base. Deux besoins, deux canaux ; le mécanisme périodique (timer→heal→buffer) peut être factorisé et instancié pour les deux.

### 9.2 Canal Buff/Debuff — modificateur de stat temporaire

**Naming verrouillé (matrice permanent × temporaire, portée × temporalité) :**

|                          | Permanent (`*Upgrade`)          | Temporaire (`*Buff`)          |
|--------------------------|---------------------------------|-------------------------------|
| **Character** (`Stat*`)  | `CharacterStatUpgrade[SO]`      | **`CharacterStatBuff`**       |
| **Spell** (`Spell*`)     | `SpellStatUpgrade` (ex-`SpellModifier`) | `SpellStatBuff` (futur) |

Deux axes clairs : portée (`Stat*` character / `Spell*` spell) × temporalité (`*Upgrade` permanent / `*Buff` temporaire). Chaque case prédictible.

**Rename obligatoire préalable** :
- `SpellModifier` → `SpellStatUpgrade` (le buffer d'upgrades permanent tag-gated sur les spells)
- `StatUpgradeSO` → `CharacterStatUpgradeSO` (SO d'upgrade permanent character)

Libère « Modifier »/`Buff` pour la temporalité et aligne SO ↔ runtime.

#### Objets

- **`CharacterStatBuff` (buffer, source de vérité)** : entrées `{ ECharacterStat Stat, float Value, float Remaining }`, chacune avec **son propre chrono**. `Value < 0` = debuff (convention GD, umbrella "Buff"). Un producer **ajoute une entrée** — il n'écrit **jamais** `LiveStats` directement (sinon plus d'expiration ni de stacking indépendants).
- **`CharacterStatBuffSystem`** : chaque frame, décrémente `Remaining`, retire les expirés. **Émet `SpellStatsCalculationRequest` on-change (add/remove)** pour forcer le recalcul du cache spell (voir plus bas).

#### LiveStats — cache runtime centralisé (renommage + élargissement de `FinalStats`)

Aujourd'hui `FinalStats` ne couvre que 4 champs (dont 2 mort-nés). Résultat : mouvement voit les slows mais HealthSystem lit `CoreStats.Armor` en direct → un debuff `-Armor` serait ignoré. **Incohérence structurelle.**

→ **`FinalStats` renommé `LiveStats`** et **élargi à 5 champs** (stats consommées tick-par-tick) :

| Champ LiveStats | Consommateur | Formule composition |
|---|---|---|
| `MoveSpeed`   | Movement                | `BaseMoveSpeed × (1 + CoreStats.MoveSpeed + StatBuffSum.MoveSpeed − SlowEffect.SpeedReduction)` |
| `PickupRange` | Pickup                  | `BasePickupRange × (1 + CoreStats.PickupRange + StatBuffSum.PickupRange)` |
| `Armor`       | HealthSystem            | `BaseArmor + CoreStats.Armor + StatBuffSum.Armor` |
| `HealthRegen` | HealthRegenSystem       | `CoreStats.HealthRegen + StatBuffSum.HealthRegen` |
| `KBResist`    | KnockbackSystem         | `CoreStats.KnockbackResistance + StatBuffSum.KnockbackResistance` |

**Écrit chaque frame par `ActiveEffectsSystem`** (renommer le job interne en `ComposeLiveStatsJob`). Tous les consommateurs runtime **lisent `LiveStats`**, jamais `CoreStats` en direct.

#### Stats spell — pas dans LiveStats, lues on-demand

**Décision critique :** les 12 stats spell (`Damage`, `Amount`, `Pierce`, `Bounce`, `SpellSize/Speed/Duration`, `CastRange`, `Crit*`, `LifeStealChance`, `AttackSpeed`) **NE sont PAS dans LiveStats**. Raison : `SpellStatsCalculationSystem` ne tourne **pas** chaque frame — il tourne **on-demand** (via `SpellStatsCalculationRequest`) et **cache** ses résultats dans `ActiveSpell.FinalX`. Les mettre dans LiveStats = 12 champs recalculés chaque frame pour rien.

À la place : **`SpellStatsCalculationSystem` lit `CoreStats.X + CharacterStatBuffSum(X)` directement** lors de son recalcul (patch mécanique de la formule additive). Le **trigger** est assuré par `CharacterStatBuffSystem` qui émet la request on add/remove — sans quoi le cache spell serait périmé après un buff.

#### Deux pipelines de composition — schéma

```
                      SOURCES
       [CoreStats permanent]      [CharacterStatBuff buffer]
              │                            │
              │  ┌─── CharacterStatBuffSystem (tick + trigger on change)
              │  │             │
              ▼  ▼             ▼ trigger
   ┌─ TICK-PER-FRAME ─┐  ┌─ ON-DEMAND ────────┐
   │ ActiveEffectsSys │  │ SpellStatsCalcSys  │
   │ + Slow/Burn/…    │  │ + SpellData baked  │
   │                  │  │ + Local + SpellStatUpgrade │
   │ writes ↓         │  │ writes ↓           │
   │ [LiveStats]      │  │ [ActiveSpell.Final*] │
   └──────────────────┘  └────────────────────┘
              │                     │
              ▼                     ▼
   Movement/Health/Regen/KB   SpellCastingSystem
```

**Consommateurs ne lisent jamais** `CoreStats` ou `CharacterStatBuff` **en direct** — toujours via un cache (LiveStats pour tick, ActiveSpell.FinalX pour spell).

#### Distinction Buff vs ActiveEffect (Slow/Stun/Burn/KB)

Coexistent, ne se remplacent pas — natures différentes.

- **`CharacterStatBuff` = modificateur numérique additif** de stat (Damage, Armor, HealthRegen…), multi-instances, additif.
- **`ActiveEffect` dédié = effet comportemental** (Burn=dégât tick, Stun=bloque action, KB=impulsion, Slow=vitesse à stacking "plus fort gagne"), single-instance, politique spéciale.

Règle de décision :
```
1. Modif d'une stat numérique de CoreStats ?
   NON → ActiveEffect dédié (Burn/Stun/KB) ou producer (Heal/Damage)
   OUI → passe à 2
2. Beaucoup d'applicants simultanés attendus (foule) ?
   OUI → ActiveEffect dédié avec politique "plus fort gagne" (comme Slow)
   NON → CharacterStatBuff (additif propre)
```

Cas pratiques :
| Cas | Canal |
|---|---|
| Aura +30% Damage 5s | `CharacterStatBuff` { Damage, +0.3, 5 } |
| Aura ennemie slow -30% multi-source | `SlowEffect` |
| Zone lave dégât tick | `BurnEffect` (via HazardZone) |
| Debuff assassin -25% Armor 4s | `CharacterStatBuff` { Armor, -0.25, 4 } |
| Boss stun 2s | `StunEffect` |
| Buff +3 Amount 10s | `CharacterStatBuff` { Amount, +3, 10 } |
| Zone soin +5 PV/0.5s | Producer direct → `HealBuffer` (§9.1) |
| Zone bénédiction +2 régen/s | `CharacterStatBuff` { HealthRegen, +2, refresh } |

- **Composition additive** (`finalFrac = coreStats.X + statBuffSum.X`), jamais multiplicative → anti-exploit d'empilement (même classe que §11 « Amount empilé »).
- Un spell peut porter une stat locale (ex. `LocalLifeStealChanceBonus` existe déjà) qui s'additionne au même endroit.
- Portée : `CharacterStatBuff` buffer + `LiveStats` composant uniquement sur les entités portant `CoreStats` (Player + Enemy), **pas** sur `DamageBufferAuthoring` (Destructibles n'ont pas de stats character).

### 9.3 Slow reste un effet dédié (design A — validé)

Slow **est** un changement de stat (vitesse), mais son **stacking diffère** : appliqué par une **foule**,
l'additif exploserait (20 ennemis × −30% = `−600%`). Donc :

- **Slow / Stun / Burn / Knockback = effets dédiés** (`IEnableableComponent`), **hors** `TempStats`.
- **Design A** : un seul `SlowEffect` par entité ; le « plus fort gagne » (aujourd'hui : écrase → dernier gagne) est traité **à l'application**, pas via une politique Max dans le buffer. Sa contribution à la vitesse est lue dans le calcul de `FinalStats`.
- Règle mémorisable : **`TempStats` = buffs numériques additifs (peu de sources)** ; **effets dédiés = CC / comportements à stacking spécial (multi-sources)**. Un debuff `-armure` d'aura unique peut être un StatModifier ; un slow de masse, non.

### 9.4 HitAction + allégeance (la forme que `ResolveHit` consomme)

```
enum EAllegiance : byte { Enemies, Self, Allies, All }   // trivial aujourd'hui, branché au team (§8) plus tard
enum EStatOp     : byte { Add, Mult }

HitAction {
    EAllegiance    Target;      // ← hook forward-compatible team system
    float          HealAmount;  // >0 = soin instantané            → HealBuffer (§9.1)
    ECharacterStat Stat;  EStatOp Op;  float StatValue;  float Duration;  // stat mod → StatModifierBuffer (§9.2)
}
```

`Target` est le seul champ à poser **correct maintenant** pour ne pas retoucher `ResolveHit` plus tard :
`Self` = caster, `Enemies` = TargetLayers actuels (hardcodé tant qu'il n'y a pas de team). Conforme à la
règle d'or : c'est la présence d'un HitAction positif qui *fait* le buff ; le tag `Buff` est **dérivé**
pour la synergie, il ne crée rien. Aujourd'hui `FinalStats` ne lit que `SlowEffect` et `dmgMultBonus`
est hardcodé à 1.0 → §9.2 le remplace.

---

## 10. ResolveHit

> **Statut : CODÉ (2026-08-09).** Les 3 voies de résolution vivantes (contact `CollisionSystem`,
> Area Burst, Area OverTime) passent par le helper. Le crit gruyère est corrigé. L'explosion n'est
> **pas** un résolveur — c'est un spawner qui recrée un `DamageOnContact` repassant par le contact.

Avant : 3 voies (contact, area Burst, area OverTime) ré-implémentaient chacune roll crit →
`DamageBufferElement` → effets par tag → lifesteal → tracking. D'où la moitié des bugs (§11).
Cible atteinte : **un helper `static` Burst-compatible partagé**.

```
ResolveHit.Apply(in ResolveHitContext ctx, ecb, sortKey, target, in HitAction action, in HitSource src, ref rng)
```

- **`HitAction`** = **union taguée atomique** (`EHitKind {Damage, Heal, ApplyBuff}` + `EAllegiance`) :
  un seul résultat par action. Un spell qui tape **et** debuff = une *liste* d'actions (futur
  `DynamicBuffer<HitAction>`), pas une struct multi-sorties. **Amendement au §9.4** : la `HitAction`
  y était esquissée heal/buff-only ; elle porte aussi le payload dégât (`Damage/CritChance/
  CritMultiplier/Tags`). En Burst, le value-type + `switch(Kind)` **est** le seul moyen de stocker une
  liste d'effets hétérogène (pas de dispatch virtuel en job) → l'union *est* l'élément de liste.
- **Discipline anti-god-struct** : le corps lourd est le bras **Damage** (crit + buffer + effets par
  tag + lifesteal + tracking — le seul code vraiment mutualisé). **Heal** = `HealBufferElement` (§9.1),
  **ApplyBuff** = `CharacterStatBuff` + `SpellStatsCalculationRequest` (§9.2) : des appends de 2 lignes.
  Les 3 bras sont codés ; seul **Damage** est câblé aux 3 sites (heal/buff attendent un producer réel).
- Roll crit **avec application du multiplicateur, à un seul endroit** (corrige l'Area cosmétique et le
  tick sans crit). Troncature `int` centralisée sur **une** ligne (point d'entrée de la future migration
  « float jusqu'à l'affichage », §11).
- **`HitSource`** (précalculé par le caller) porte le *qui/où* : `Caster`, `DatabaseIndex` (-1 = pas de
  source → skip tracking+lifesteal), `PushOrigin` (knockback), `Shake` (contact/area/DoT diffèrent).
- **`ResolveHitContext`** = bundle read-only (config effets, lookups Slow/Stun/Burn/KB/LtW, buffer
  `ActiveSpell`, file de tracking) construit une fois par `OnUpdate`, copié dans chaque job.
- **Allégeance** = hook stocké ; la **sélection de cible reste au caller** (filtre physique `TargetLayers`)
  tant qu'il n'y a pas de team (§8). `Self` = caster, `Enemies` = TargetLayers, hardcodé.

Les systèmes de famille ne font plus que **géométrie + timing** ; `ResolveHit` fait **toutes les conséquences**.

---

## 11. Défauts actuels que ce design corrige

| # | Défaut | Corrigé par |
|---|---|---|
| Crit gruyère | Area = crit **cosmétique** (multiplicateur non appliqué), tick = **jamais** de crit | ✅ **CORRIGÉ** — `ResolveHit` unique (2026-08-09) |
| `HitEntityMemory` O(n²) | jamais purgé → falaise de perf sur pierce/bounce en foule | fenêtre glissante / set borné |
| Explosion stats mortes | `Radius *= finalSize` jeté ; `Scale=1f` en dur ; `IsCritical` non lu | émetteur/`ResolveHit` propres |
| Double-cast ennemi | reset cooldown commenté + buffer vidé à 30 Hz → N casts selon le framerate | reset au cast |
| Troncature `int` | dégâts tronqués à chaque étage → petits coups à 0 / plancher | dégâts `float` jusqu'à l'affichage |
| `Amount` empilé | N zones au même point → ×N dégâts (exploit) | [`EMultiCast`](#5-emulticast--sémantique-damount) |
| Sémantique modifier | `Damage` Multiply fait `×value` mais `Size` fait `×(1+value)` → footgun | règle unique : Flat `+v`, Multiply `×(1+v)` |
| Effets globaux | 1 seule config (`ActiveEffectsConfig`) pour tous les spells | effets paramétrés par spell |
| Sweep troué | filtre au **cône instantané** → coups fantômes selon le framerate | **arc accumulé** (+ sweep autour de la normale planète) |

---

## 12. Ordre de migration

1. **Fusionner `AreaAttack` + `DamageOnTick` → `ZoneAttack`** (Anchor + Cadence + Shape+Motion),
   un seul `ZoneAttackSystem` (branche sur Cadence pour la dédup, partage forme).
   Corriger le **sweep** (arc accumulé, autour de la normale). → débloque VoidSlash≡FreezingBlow,
   ShockStrike, zone variable/fixe, et supprime la duplication `IsInShape`.
2. ✅ **FAIT (2026-08-09)** — **Extraire `ResolveHit`** (allégeance + résultat dégât/heal/buff) et brancher les voies dessus. Voir §10.
3. **Dériver les tags** : Comportements tout de suite ; Forme après l'étape 1.
4. **Généraliser l'Émetteur** (Layout/Timing/Trigger/child détachable) ; Orbital et multishot y passent.
5. **Team / Summon** — passe de design séparée (prérequis : allégeance).
6. **Effets par spell** (magnitude/durée) → puis dériver les tags d'effet.

---

## 13. Décisions ouvertes

- ✅ **Récursion émetteur** : autorisée, self-reference permise (split fireball→fireball), cascade auto-terminée par `SplitDepth`. `Count` **et** `Depth` upgradables par spell. Sécurité : **budget de spawn par lignée (obligatoire dès le départ)** + falloff de dégâts par génération ; le **gate max-depth est reporté** (le budget le rend sûr). Voir §7. *(toutes les décisions sont désormais verrouillées)*
- ✅ **Fallback `MultiTarget`** : le surplus (Amount > cibles distinctes) part en **`Spread`** ; pas de convergence.
- ✅ **ShockStrike** : `Area` (a un splash — touche plusieurs ennemis collés).
- ✅ **`ResolveHit`** : « applique un résultat (dégât/heal/buff) + allégeance » dès le départ. **CODÉ 2026-08-09** — union taguée atomique `HitAction {EHitKind, EAllegiance}` list-ready ; bras Damage câblé aux 3 voies (contact/Burst/OverTime), Heal/ApplyBuff prêts mais sans producer. Crit gruyère corrigé.
- ✅ **Buff / Heal / Debuff** (§9, validé 2026-08-01) : deux primitives — **Heal** = `HealBufferElement`
  multi-producteurs → un sink (magnitude+carryover+**feedback par-source** au producteur ; ordre+somme+clamp+mort
  au sink ; LifeSteal & zone HoT = producteurs) ; **Buff/Debuff** = `StatModifierBuffer` (liste temporisée) →
  `TempStats` (somme dérivée) lu **additivement** `CoreStats.X + TempStats.X` par FinalStats **et** le calcul
  par-cast. **Slow reste dédié (design A)**, hors TempStats (stacking « plus fort gagne » à l'application).
  `HitAction` porte `EAllegiance` (Self/Enemies hardcodés sans team).

---

## 14. Annexe — Roster

Classification **proposée** (à confirmer contre les prefabs réels).

| Spell (`ESpellID`) | Famille | Tags principaux | Notes |
|---|---|---|---|
| Fireball | Projectile | `Projectile \| Ranged \| Elemental` | `EMultiCast` selon design |
| PoisonNeedle | Projectile | `Projectile \| Ranged \| Physical` | |
| ProjectileGeometric | Projectile | `Projectile \| Ranged` + `Bouncing` | a un upgrade Bounce |
| ShockChain | Projectile | `Projectile \| Ranged \| Elemental` + `Bouncing` | chain lightning |
| VoidSlash | Area | `Area \| Melee` | Forward/Burst/Cone **+Sweep** ; `Single` |
| FreezingBlow | Area | `Area \| Melee \| Elemental` + `Slow` | **Forward**/Facing/**Burst** — coup d'épée vertical qui frappe le sol ; upgrade piliers = Émetteur `Line` |
| ShockStrike | Area | `Area \| Ranged \| Elemental` + `Stun` | WorldTarget/Burst/Circle + `ActivationDelay` ; `MultiTarget` |
| Shockwave / VoidPulse | Area | `Area` | Caster/Burst/Ring **Expand** ; `Single` |
| FrozenZone | Area | `Area \| Elemental` + `Slow` | OverTime, ancrée caster/world |
| PoisonFloor | Area | `Area \| Debuff` + `Slow`/DoT | WorldTarget/OverTime |
| LightningTornado | Area | `Area \| Elemental` | OverTime + Motion `Roam` (à confirmer) |
| DiscoWaveSlash | Area | `Area \| Melee` | slash à durée |
| FireOrbs | (Émetteur) | `Elemental` + `Burn` | **pas une famille** : Émetteur `Circle` attaché |
| — futurs — | Summon | `Summon` (+ `Buff`/`Debuff`) | tourelles, mobs, réincarnation, charme |

---

*Fin du contrat. Toute implémentation du pipeline d'attaque doit s'y conformer ou amender ce document.*
