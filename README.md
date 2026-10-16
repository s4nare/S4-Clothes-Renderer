# S4 Clothes Renderer

![Gallery](docs/screens/gallery.png)

**FR** · [EN](#english)

Génère des miniatures PNG transparentes des vêtements (freemode homme et femme), véhicules, armes, props et peds de GTA V, directement depuis les fichiers du jeu et tes dossiers addon. Pensé pour les serveurs FiveM : boutiques, menus de vestiaire, inventaires, sites web.

- Lit GTA V directement (Legacy et Enhanced) : aucune extraction à faire.
- Ajoute tes dossiers `stream` de vêtements addon : à numéro égal, le dernier dossier remplace les précédents.
- Véhicules, armes, props et peds : jeu de base, DLC et dossiers addon/FiveM (`stream`), rangés par classe, type ou lettre.
- Véhicules aux couleurs d'usine du jeu : une seule ou toutes les variantes (`adder_0.png`, `adder_1.png`…), carrosserie et vitres brillantes.
- 16 catégories de vêtements : masques, cheveux, bras, pantalons, sacs, chaussures, chaînes, sous-hauts, gilets, décalcomanies, hauts, chapeaux, lunettes, boucles d'oreilles, montres, bracelets.
- Vue adaptée à chaque catégorie, cadrage automatique, couleur de cheveux naturelle, PNG transparents de 256 à 1024 px.
- Numéros du jeu (`male/11/42.png`) grâce à la ressource `clothes_refs`, ou rangement par catégorie.
- Une couleur ou toutes les variantes de texture (`42_0.png`, `42_1.png`…).
- Galerie par catégorie, rendu en direct, reprise là où tu t'étais arrêté.
- Rendu logiciel multi-cœur : plus de 100 images par seconde sur un CPU récent.
- Mise à jour automatique au démarrage, avec les nouveautés affichées dans le logiciel.

## Captures

| Véhicules | Armes |
|---|---|
| ![Véhicules](docs/screens/vehicles-colors.png) | ![Armes](docs/screens/weapons.png) |
| **Props** | **Peds** |
| ![Props](docs/screens/props.png) | ![Peds](docs/screens/peds.png) |

Rendu en direct : chaque image s'affiche dès qu'elle est écrite.

![Rendu](docs/screens/render.png)

## Images produites

Un PNG transparent par élément, une image par fichier :

**Vêtements · Homme**

| Masques | Cheveux | Bras | Pantalons | Sacs | Chaussures | Chaînes | Sous-hauts |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| <img src="docs/samples/clothes/male/masks.png" width="96"> | <img src="docs/samples/clothes/male/hair.png" width="96"> | <img src="docs/samples/clothes/male/arms.png" width="96"> | <img src="docs/samples/clothes/male/pants.png" width="96"> | <img src="docs/samples/clothes/male/bags.png" width="96"> | <img src="docs/samples/clothes/male/shoes.png" width="96"> | <img src="docs/samples/clothes/male/chains.png" width="96"> | <img src="docs/samples/clothes/male/undershirts.png" width="96"> |

| Gilets | Décalcomanies | Hauts | Chapeaux | Lunettes | Boucles d’oreilles | Montres | Bracelets |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| <img src="docs/samples/clothes/male/vests.png" width="96"> | <img src="docs/samples/clothes/male/decals.png" width="96"> | <img src="docs/samples/clothes/male/tops.png" width="96"> | <img src="docs/samples/clothes/male/hats.png" width="96"> | <img src="docs/samples/clothes/male/glasses.png" width="96"> | <img src="docs/samples/clothes/male/earrings.png" width="96"> | <img src="docs/samples/clothes/male/watches.png" width="96"> | <img src="docs/samples/clothes/male/bracelets.png" width="96"> |

**Vêtements · Femme**

| Masques | Cheveux | Bras | Pantalons | Sacs | Chaussures | Chaînes | Sous-hauts |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| <img src="docs/samples/clothes/female/masks.png" width="96"> | <img src="docs/samples/clothes/female/hair.png" width="96"> | <img src="docs/samples/clothes/female/arms.png" width="96"> | <img src="docs/samples/clothes/female/pants.png" width="96"> | <img src="docs/samples/clothes/female/bags.png" width="96"> | <img src="docs/samples/clothes/female/shoes.png" width="96"> | <img src="docs/samples/clothes/female/chains.png" width="96"> | <img src="docs/samples/clothes/female/undershirts.png" width="96"> |

| Gilets | Décalcomanies | Hauts | Chapeaux | Lunettes | Boucles d’oreilles | Montres | Bracelets |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| <img src="docs/samples/clothes/female/vests.png" width="96"> | <img src="docs/samples/clothes/female/decals.png" width="96"> | <img src="docs/samples/clothes/female/tops.png" width="96"> | <img src="docs/samples/clothes/female/hats.png" width="96"> | <img src="docs/samples/clothes/female/glasses.png" width="96"> | <img src="docs/samples/clothes/female/earrings.png" width="96"> | <img src="docs/samples/clothes/female/watches.png" width="96"> | <img src="docs/samples/clothes/female/bracelets.png" width="96"> |

**Véhicules · Armes · Props · Peds**

| `vehicles/adder.png` | `vehicles/champion.png` | `weapons/WEAPON_DOUBLEACTION.png` | `weapons/WEAPON_FLAREGUN.png` | `props/p_beefsplitter_s.png` | `props/p_bison_winch_s.png` | `peds/a_m_y_beach_01.png` | `peds/a_f_m_bevhills_01.png` |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| <img src="docs/samples/adder.png" width="96"> | <img src="docs/samples/champion.png" width="96"> | <img src="docs/samples/WEAPON_DOUBLEACTION.png" width="96"> | <img src="docs/samples/WEAPON_FLAREGUN.png" width="96"> | <img src="docs/samples/p_beefsplitter_s.png" width="96"> | <img src="docs/samples/p_bison_winch_s.png" width="96"> | <img src="docs/samples/a_m_y_beach_01.png" width="96"> | <img src="docs/samples/a_f_m_bevhills_01.png" width="96"> |

```
sortie/
  male/11/42.png        vêtements (numéros du jeu)
  female/4/17_2.png     variante de texture 2
  vehicles/adder.png
  weapons/WEAPON_PISTOL.png
  props/prop_bin_01a.png
  peds/a_m_y_beach_01.png
```

## Installation

1. Télécharge `S4ClothesRenderer.zip` dans les [Releases](../../releases) et décompresse-le.
2. Lance `S4ClothesRenderer.exe`. GTA V est détecté automatiquement (sinon, choisis son dossier dans **Rendu › Sources**).
3. Choisis les catégories, le dossier de sortie, puis **Lancer le rendu**.

Windows 10/11 64 bits. Le runtime WebView2 (déjà présent sur Windows 11) est requis.

## Numéros du jeu (`refs.json`)

Les vêtements DLC et addon sont numérotés par collection. Pour obtenir les numéros que FiveM utilise (`SetPedComponentVariation`) :

1. Dans **Rendu › Options**, choisis **Numéros du jeu**, puis **Installer sur mon serveur** et sélectionne le dossier `resources` de ton serveur.
2. Ajoute les lignes affichées (bouton **Copier**) à ton `server.cfg` : `ensure clothes_refs` et `add_ace group.admin command.clothes_refs allow`.
3. En jeu, tape `/clothes_refs` : `refs.json` est écrit dans la ressource et importé automatiquement par le logiciel.

Refais `/clothes_refs` à chaque ajout ou retrait de vêtements sur le serveur.

## Personnalisation

Pose un `branding.json` à côté de l'exe (voir `branding.example.json`) pour changer le nom, la couleur et le logo.

## Compiler

```
git clone --recursive https://github.com/s4nare/S4ClothesRenderer
dotnet publish src/S4ClothesRenderer/S4ClothesRenderer.csproj -c Release -o publish
```

.NET SDK 10 requis.

---

## English

Generates transparent PNG thumbnails of GTA V clothing (male and female freemode), vehicles, weapons, props and peds, straight from the game files and your addon folders. Built for FiveM servers: shops, wardrobe menus, inventories, websites.

- Reads GTA V directly (Legacy and Enhanced): nothing to extract.
- Add your addon clothing `stream` folders: for the same number, the last folder wins.
- Vehicles, weapons, props and peds: base game, DLC and addon/FiveM `stream` folders, grouped by class, type or letter.
- Vehicles in the game's factory colours: one or every variant (`adder_0.png`, `adder_1.png`…), glossy paint and glass.
- 16 clothing categories: masks, hair, arms, pants, bags, shoes, chains, undershirts, armour, decals, tops, hats, glasses, earrings, watches, bracelets.
- Per-category camera, automatic framing, natural hair colour, transparent PNGs from 256 to 1024 px.
- Game numbers (`male/11/42.png`) through the `clothes_refs` resource, or per-category layout.
- First colour only or every texture variant (`42_0.png`, `42_1.png`…).
- Per-category gallery, live progress, resumes where you left off.
- Multi-core software renderer: over 100 images per second on a recent CPU.
- Automatic updates on startup, with release notes shown in the app.

Screenshots and sample output: see [Captures](#captures) and [Images produites](#images-produites) above. One transparent PNG per item: `male/11/42.png`, `vehicles/adder.png`, `weapons/WEAPON_PISTOL.png`, `props/prop_bin_01a.png`, `peds/a_m_y_beach_01.png`.

### Install

1. Download `S4ClothesRenderer.zip` from [Releases](../../releases) and unzip it.
2. Run `S4ClothesRenderer.exe`. GTA V is detected automatically (otherwise pick its folder in **Render › Sources**).
3. Pick categories and the output folder, then **Start rendering**.

Windows 10/11 64-bit. The WebView2 runtime (built into Windows 11) is required.

### Game numbers (`refs.json`)

DLC and addon clothes are numbered per collection. To get the numbers FiveM uses (`SetPedComponentVariation`):

1. In **Render › Options**, choose **Game numbers**, then **Install on my server** and select your server `resources` folder.
2. Add the lines shown (**Copy** button) to your `server.cfg`: `ensure clothes_refs` and `add_ace group.admin command.clothes_refs allow`.
3. In game, type `/clothes_refs`: `refs.json` is written in the resource and imported automatically by the app.

Run `/clothes_refs` again whenever clothes are added to or removed from the server.

### Branding

Put a `branding.json` next to the exe (see `branding.example.json`) to change the name, colour and logo.

### Build

```
git clone --recursive https://github.com/s4nare/S4ClothesRenderer
dotnet publish src/S4ClothesRenderer/S4ClothesRenderer.csproj -c Release -o publish
```

Requires the .NET 10 SDK.

## Credits

- [CodeWalker](https://github.com/dexyfex/CodeWalker) by dexyfex (MIT) for reading GTA V archives and resources.
- Public Sans and JetBrains Mono fonts (SIL Open Font License).

GTA V and its assets belong to Rockstar Games. This tool only reads files from your own copy of the game.
