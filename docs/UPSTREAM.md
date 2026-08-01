# Matgate upstream synchronization

Julgate originated from `Real-TTX/Matgate`, but both repositories now have independent commits.

## Current relationship

- Upstream repository: `Real-TTX/Matgate`
- Upstream branch: `main`
- Last reviewed upstream commit: `8f27b00585f44e68e6867a1b5a21eb08cc32f441` (Matgate 0.7.0)
- Original common merge base: `c36a7879e3a780ead649fd76a38fcdc702e31a1c`
- Upstream commit `8f27b00585f44e68e6867a1b5a21eb08cc32f441` was recorded as reviewed ancestry on 2026-08-01, so Julgate is no longer behind that Matgate state.

## Policy

Do not replace the Julgate tree with the current Matgate tree. Julgate has intentionally different implementations for:

- credential encryption and legacy migration;
- file and archive security boundaries;
- request limits and audit logging;
- website-proxy destination validation;
- container hardening, secrets, backups and restore validation;
- Julgate branding and AE01-derived interface behavior.

A blind merge would create overlapping implementations or regress those guarantees.

For every new upstream commit:

1. Compare it with the recorded upstream commit.
2. Classify each changed file as already implemented, safe to cherry-pick, or requiring a manual port.
3. Port user-facing improvements and compatible bug fixes in a normal Julgate pull request.
4. Add regression tests before merging.
5. Update the reviewed upstream commit in this document.

## Reviewed Matgate 0.7.0 changes

The four upstream commits after the original common base were reviewed. Their security and credential changes overlap with stricter Julgate implementations and were not imported wholesale. The self-service password change was ported through Julgate 0.7.8 with Julgate-specific validation and regression tests. Matgate's account-menu and generic account-tabs were not imported because Julgate uses a different shell and embedded-tab architecture; they remain optional standalone UX work rather than an upstream merge requirement.
