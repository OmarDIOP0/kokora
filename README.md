# Kokora — le navétane de Nguékokh

Résultats, classements, buteurs et matchs en direct du navétane de Nguékokh (zonales 5A et 5B, 4 Grandes, Coupe du Maire).

> Projet en cours : phase 1 terminée (architecture, modèle de données, design system).
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

   L'identifiant peut être un e-mail ou un numéro de téléphone. Une fois le compte créé, vous pouvez supprimer ces deux secrets.

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

## Développement

- Front en mode surveillance (deux terminaux) : `npm run watch:css` et `npm run watch:js` dans `src/Kokora.Web`.
- Design system et maquettes (données fictives) : http://localhost:5099/design et http://localhost:5099/design/accueil (visibles en développement ou pour les administrateurs).
- Nouvelle migration :

  ```bash
  dotnet ef migrations add NomDeLaMigration -p src/Kokora.Infrastructure -s src/Kokora.Infrastructure -o Persistence/Migrations
  ```

- Tests : `dotnet test`

## Règles par défaut (modifiables par compétition dans l'admin)

- Victoire 3 pts, nul 1, défaite 0 ; forfait : défaite 0 pt et score de 3-0 pour l'adversaire.
- Départage : points, différence de buts, buts marqués, confrontation directe, fair-play.
- Suspensions : 3 cartons jaunes = 1 match ; 2e jaune = 1 match ; rouge direct = 1 match.
- Mi-temps de 45 min ; élimination directe : tirs au but directement (prolongations désactivables).

## Licences

Les bibliothèques utilisées sont sous licence libre (MIT, ISC, Apache-2.0, BSD, OFL pour les polices).
Les images sont traitées avec SkiaSharp (MIT).
