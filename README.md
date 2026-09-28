# CS2 Local Kit

CS2 Local Kit is an independent toolkit for **local/offline Counter-Strike 2 bot play** with two deliberately separated concerns:

- an externally pinned enhanced-bot runtime;
- a small, project-owned human cosmetics layer for local practice.

This repository starts from a clean history. It is **not** a fork of Local-Arena or CS2-Bot-Improver. The previous `Whatnamed/Local-Arena` repository is retained only as legacy reference and is not an upstream for new development.

## Project principles

- Keep Bot runtime and human cosmetics operationally isolated.
- Prefer pinned external components over maintaining large downstream forks.
- Keep game-update-sensitive code behind a small compatibility boundary.
- Fail closed: a broken cosmetics integration must not alter bot count, match state, scores, teams, or normal game flow.
- Treat in-game behavior as manually verified evidence; builds and static tests cannot prove models, animations, HUD, FPS, scoreboards, or round behavior.
- Keep local releases, presets, backups, diagnostics, and app data in the persistent local data area rather than disposable caches.

## Documentation

Start with [docs/README.md](docs/README.md).

The durable project definition is split by responsibility:

- [Product scope](docs/PRODUCT-SCOPE.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Upstreams and provenance](docs/UPSTREAMS.md)
- [Compatibility watch process](docs/COMPATIBILITY-WATCH.md)
- [Local workspace and persistent-data layout](docs/LOCAL-WORKSPACE.md)
- [Manual in-game acceptance](docs/MANUAL-ACCEPTANCE.md)
- [Development workflow](CONTRIBUTING.md)
- [Agent rules](AGENTS.md)

## Current maturity

The repository is intentionally bootstrapped before feature implementation. Experimental runtime work belongs under `experiments/` until the underlying CS2 behavior has been demonstrated in game and is suitable to promote into production code.

This project is intended for local/offline use. It does not target Valve matchmaking, FACEIT, or public community-server cosmetics.
