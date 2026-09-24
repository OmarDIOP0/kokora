# Kokora — le navétane de Nguékokh

Résultats, classements, buteurs et matchs en direct du navétane de Nguékokh (zonales 5A et 5B, 4 Grandes, Coupe du Maire).

> Projet en cours : phases 1 (architecture, design system), 2 (administration) et 3 (partie publique) terminées.
> Ce README sera complété au fil des phases (déploiement, sauvegardes, Docker).

## Pile technique

| Couche | Choix |
|---|---|
| Web | ASP.NET Core MVC (.NET 10), vues Razor, SignalR |
| Données | EF Core 10 + PostgreSQL (noms de tables en `snake_case`) |
| Comptes | ASP.NET Core Identity (rôles `SuperAdmin`, `Admin`, `Rédacteur`, `Utilisateur`) |
| Front | Tailwind CSS v4 (CLI), esbuild, htmx, Alpine.js, Day.js, icônes Lucide rendues côté serveur |
| Polices | Barlow Condensed (scores, titres) + Barlow (texte), auto-hébergées |

```
src/
  Kokora.Domain/          entités, énumérations, règles (aucune dépendance)
  Kokora.Application/     services métier, abstractions (IAppDbContext, ICurrentUser)
  Kokora.Infrastructure/  EF Core, migrations, Identity, audit
  Kokora.Web/             contrôleurs, vues, tag helpers, Assets/ (CSS/JS sources)
tests/
  Kokora.Domain.Tests/
  Kokora.Application.Tests/
```

## Prérequis

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) (build du CSS et du JS)
- [PostgreSQL 16+](https://www.postgresql.org/download/)
- Outil EF : `dotnet tool install -g dotnet-ef`

## Installation

1. Créer la base (une seule fois) :

   ```bash
   psql -U postgres -c "CREATE DATABASE kokora;"
   ```

2. Enregistrer la chaîne de connexion **hors du code** (user secrets, stockés dans votre profil Windows) :

   ```bash
   dotnet user-secrets --project src/Kokora.Web set "ConnectionStrings:Kokora" "Host=localhost;Port=5432;Database=kokora;Username=postgres;Password=VOTRE_MOT_DE_PASSE"
   ```

3. Créer le premier SuperAdmin (créé automatiquement au démarrage s'il n'en existe aucun) :

   ```bash
   dotnet user-secrets --project src/Kokora.Web set "Bootstrap:SuperAdmin:Login" "admin@exemple.sn"
   dotnet user-secrets --project src/Kokora.Web set "Bootstrap:SuperAdmin:Password" "UnMotDePasseSolide2026"
   ```

   Le compte est créé **au démarrage** : si l'application tourne déjà, redémarrez-la. L'identifiant peut être un e-mail ou un numéro de téléphone. Une fois le compte créé, vous pouvez supprimer ces deux secrets.

4. Installer les paquets front et construire CSS/JS :

   ```bash
   cd src/Kokora.Web
   npm install
   npm run build
   ```

   (Le build .NET lance aussi `npm run build` automatiquement si les fichiers sont absents, et toujours en Release.)

5. Lancer :

   ```bash
   dotnet run --project src/Kokora.Web
   ```

   Les migrations sont appliquées automatiquement au démarrage. Application : http://localhost:5099

## Administration (`/admin`)

Connexion : `/compte/connexion` (e-mail ou numéro de téléphone). Rôles :

| Rôle | Accès |
|---|---|
| SuperAdmin, Admin | tout l'admin sportif : saisons, compétitions, phases, poules, tableaux, équipes, joueurs, stades, arbitres, calendrier, données de démo, journal d'audit |
| Rédacteur | tableau de bord (et les infos, à venir) |

Parcours type d'une saison :

1. **Saisons et compétitions** → « Nouvelle saison », puis « Structure type » : crée Zonale 5A, Zonale 5B, 4 Grandes Zone 5A, 4 Grandes Zone 5B et Coupe du Maire (tout reste modifiable : règles de points, départage, suspensions, durée des matchs).
2. **Équipes** : les ASC avec logo (converti automatiquement en WebP), couleurs, quartier, zone.
3. **Joueurs** : un par un, ou import d'un fichier Excel/CSV (modèle téléchargeable sur la page d'import).
4. Dans chaque zonale, **Phase de poules** → créer les poules (4 ou 5 équipes), puis « Calendrier » pour générer toutes les rencontres (aller simple ou aller-retour ; avec 5 équipes, une équipe est exempte à chaque journée).
5. **Phase finale / 4 Grandes / Coupe** : « Créer le tableau » (2 à 32 équipes) ; les tours sont reliés, chaque vainqueur passe automatiquement au tour suivant.
6. **Calendrier** : ajuster dates, stades et arbitres ; reporter un match en un clic ; cocher « Match à l'affiche » pour le compte à rebours de l'accueil.
7. **Résultat** (depuis le calendrier ou le tableau de bord « Résultats à saisir ») : score, mi-temps, tirs au but, forfait (score administratif automatique), buteurs, passeurs et cartons. Les classements sont recalculés aussitôt et le vainqueur d'un match à élimination passe au tour suivant.
8. **Qualification** (page d'une phase à élimination) : pour chaque place du premier tour, choisir « 1er de la Poule A », « Vainqueur Demi-finales 2 »… puis « Générer la qualification ». Une équipe choisie à la main sur un match reste prioritaire.

Toutes les modifications sont tracées dans le **journal d'audit** (qui, quand, valeurs avant/après).

**Données de démonstration** : `/admin/demo` crée 17 ASC fictives (« ASC Démo 1 »…), leurs joueurs et deux zonales avec des résultats simulés. Un bouton les supprime toutes sans toucher aux vraies données. Disponible tant qu'aucune vraie saison n'existe pour l'année en cours.

## Partie publique

- `/` : matchs par jour (bande de dates), à venir, résultats ; bloc « En direct » rafraîchi toutes les 30 s ; équipes suivies en premier.
- `/matchs/{id}-{equipes}` : fiche match (chronologie, compositions, stats, confrontations), partage WhatsApp, aperçu Open Graph.
- `/classements/{competition}` : tableaux par poule (forme, zones qualificatives, pénalités) et tableau final.
- Le choix de compétition est mémorisé (cookie `k-comp`). Classements mis en cache mémoire, invalidés à chaque résultat.

## Développement

- Front en mode surveillance (deux terminaux) : `npm run watch:css` et `npm run watch:js` dans `src/Kokora.Web`.
- Design system et maquettes (données fictives) : http://localhost:5099/design et http://localhost:5099/design/accueil (visibles en développement ou pour les administrateurs).
- Nouvelle migration :

  ```bash
  dotnet ef migrations add NomDeLaMigration -p src/Kokora.Infrastructure -s src/Kokora.Infrastructure -o Persistence/Migrations
  ```

- Tests : `dotnet test`
  - `Kokora.Domain.Tests`, `Kokora.Application.Tests` : logique pure (calendrier, dates, slugs…).
  - `Kokora.Web.Tests` : application complète sur PostgreSQL. Les bases `kokora_tests` et `kokora_tests_services` sont **recréées à chaque exécution** (jamais la base `kokora`). Connexion : variable `KOKORA_TEST_CONNECTION`, sinon le user-secret du projet Web.
  - Relecture visuelle de l'admin sans se connecter : `KOKORA_SNAPSHOTS=1 dotnet test tests/Kokora.Web.Tests --filter Write_admin_snapshots`, puis ouvrir `http://localhost:5099/_snapshots/dashboard.html` (dossier ignoré par Git).

## Règles par défaut (modifiables par compétition dans l'admin)

- Victoire 3 pts, nul 1, défaite 0 ; forfait : défaite 0 pt et score de 3-0 pour l'adversaire.
- Départage : points, différence de buts, buts marqués, confrontation directe, fair-play.
- Suspensions : 3 cartons jaunes = 1 match ; 2e jaune = 1 match ; rouge direct = 1 match.
- Mi-temps de 45 min ; élimination directe : tirs au but directement (prolongations désactivables).

## Licences

Les bibliothèques utilisées sont sous licence libre (MIT, ISC, Apache-2.0, BSD, OFL pour les polices).
Les images sont traitées avec SkiaSharp (MIT).
