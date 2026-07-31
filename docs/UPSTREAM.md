# Matgate upstream synchronization

Julgate originated from `Real-TTX/Matgate`, but both repositories now have independent commits.

## Current relationship

- Upstream repository: `Real-TTX/Matgate`
- Upstream branch: `main`
- Last reviewed upstream commit: `8f27b00585f44e68e6867a1b5a21eb08cc32f441` (Matgate 0.7.0)
- Common merge base: `c36a7879e3a780ead649fd76a38fcdc702e31a1c`
- At the 2026-07-31 review, Julgate was 13 commits ahead and 4 upstream commits behind.

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

The four upstream commits after the common base were reviewed. Their security and credential changes overlap with stricter Julgate implementations and are therefore not imported wholesale. The account-menu, account-tabs and self-service password-change UI from Matgate 0.7.0 remains a candidate for a separate manual Julgate port, where it can be tested against Julgate's embedded-tab and security behavior.
