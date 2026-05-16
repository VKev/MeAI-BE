# PlantUML Output

This folder contains end-to-end frontend feature diagrams derived from the current route and API surface in `/home/vinhdo/Documents/GitHub/MeAI-FE`, not raw endpoint inventories.

## Structure

- `features/`: one `.puml` file per frontend feature workflow.
- Every file contains two PlantUML blocks: a class view and a detailed sequence flow.
- Sequence flows begin with `User`, `Admin`, or `Community User` and continue through frontend, gateway, backend services, async delivery, and frontend refresh paths when the feature truly uses them.

## Coverage Rules Used

- Focus only on real frontend workflows implemented in `apps/meai-fe` and `apps/meai-social-fe`.
- Exclude placeholders, redirect-only routes, static marketing pages, and proxy/helper routes.
- Keep the language human-readable instead of endpoint-slug style naming.
- Keep each sequence flow continuous so the return path back to frontend stays visible.
- Do not use PlantUML `note` blocks.

## Diagram Inventory

- Frontend feature diagrams: `30`
- Excluded UI-only or placeholder surfaces: `5`
- Tracker: [frontend-feature-diagram-tracker.md](/home/vinhdo/Documents/GitHub/MeAI-BE/plans/frontend-feature-diagram-tracker.md:1)
