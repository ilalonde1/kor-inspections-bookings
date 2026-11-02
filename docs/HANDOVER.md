# KOR Inspections Bookings - HANDOVER

## STATE NOW - 2 Nov 2026 16:10 BCDT (`date`), written by a Framework F1 helper at enrollment. Read first.

**The Inspections Bookings lead has not run a session under this handover yet.** At its first close it replaces this
block with its own state. Until then, these are the verified facts:

- **What it is:** the public field-review booking site, `bookings.korstructural.com`. ASP.NET Core 8 Razor Pages
  (`Kor.Inspections.App`, solution `Kor.Inspections.App/Kor.Inspections.App.sln`), xUnit tests in
  `Kor.Inspections.Tests`, Playwright e2e under `Kor.Inspections.App/tests/e2e`. Deploy script
  `scripts/deploy-bookings.ps1`; the publish convention is in memory `project_publish_runbook.md`.
- **Git, read 16:10:** branch `develop` at `f50771f` (28 Oct 13:20, "Holiday calendar: handle observed days across
  the year boundary"), equal to `origin/develop`. `main` is `5de397c` (merge of develop, 28 Oct 14:03), equal to
  `origin/main`, same tree as develop. Working tree clean before enrollment. Only remote: `origin` on GitHub
  (`ilalonde1/kor-inspections-bookings`); no KOR-FS01 remote. Nothing unpushed except the enrollment commit.
- **Live:** KOR-APP01 IIS site, `C:\inetpub\bookings.korstructural.com`. Its `Kor.Inspections.App.dll` (mtime
  28 Oct 13:21) has the same MD5 as `_Publish\Inspections Site\B57` (read 2 Nov 16:10) - live is B57 = develop
  `f50771f`, as the memory says.
- **Open item in memory, not started:** Teams meeting for "(Virtual)" inspectors (`project_teams_meeting_feature.md`),
  deferred on an Azure AD permission grant only Ian can make.
- **Framework:** enrolled 2 Nov - start routine in `CLAUDE.md`, the 26 guard hooks in `.claude/settings.json`
  (`.claude/settings.local.json` is Ian's and untouched), this handover.

## Five questions to answer before any work
1. **Where do things stand?** The STATE NOW block above, the board rows for this repo, `git status` / `git log`.
2. **What is next, in order?** Not set. It is the repo owner's (Ian's) to decide; this handover does not invent a
   plan. The only recorded candidate is the deferred Teams meeting feature, blocked on Ian's Azure AD grant.
3. **Three traps:**
   - `Kor.Inspections.App/appsettings.json` and `appsettings.Production.json` are git-tracked with real secrets
     (memory: Graph and AzureAd client secrets, SQL password, Deltek DSN). Never echo them, never "clean them up"
     unasked; rotation is Ian's call.
   - SQL Server here is case-insensitive collation: a `col <> LOWER(col)` check returns 0 rows whatever the data.
     Force `COLLATE Latin1_General_BIN` (memory `feedback_sql_collation.md`).
   - Work goes on `develop`, merged to `main`; no feature branches (memory `feedback_branching_workflow.md`). Publish
     the csproj, not the folder, or the test project lands in the build.
4. **Who owns what?** This repo's lead: the code, tests, handover and board rows. The Framework lead: the framework
   (start routine, guards, handover shape). Codex: bounded tasks from a lead only, diffs verified before commit.
   Ian: pushes to GitHub, deploys to KOR-APP01, Azure AD and secret rotation.
5. **What must you not touch?** The live site on KOR-APP01: no `scripts/deploy-bookings.ps1` run (or `-Rollback`)
   without Ian's yes. No push to GitHub without Ian's yes. `.claude/settings.local.json`, the tracked appsettings
   secrets, and the memory files are not yours to change unasked.
