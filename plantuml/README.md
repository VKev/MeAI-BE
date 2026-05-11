# PlantUML Output

This folder contains end-to-end frontend feature diagrams, not raw endpoint inventories.

## Structure

- `features/`: one `.puml` file per frontend feature.
- Every file contains two PlantUML blocks: a class view and a detailed sequence flow.
- Sequence flows always begin with `User` or `Admin` and run through frontend, gateway, backend services, async delivery and frontend refresh paths when the feature uses them.

## Coverage Rules Used

- Focus only on real frontend features found in `MeAI-FE` and `MeAI-Social-Platform`.
- Keep the language human-readable instead of endpoint-slug style naming.
- Keep each sequence flow long and continuous so the return path back to frontend is visible.
- Do not use PlantUML `note` blocks.

## Diagram Inventory

- Frontend feature diagrams: `24`
- Excluded UI-only surfaces: `3`
- Tracker: [frontend-feature-diagram-tracker.md](/home/vinhdo/Documents/GitHub/MeAI-BE/plans/frontend-feature-diagram-tracker.md:1)
