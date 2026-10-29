# YORU Android V9.3 ? More Back navigation (90309)

Root cause: GoBack handled Details specially but sent every other non-Home page to Home. More's child pages had no parent routing.

MobileNavigation now defines the existing section hierarchy: MyList, Genres, Seasonal and Settings all return to More. More remains their selected bottom tab, including when opening details from one of these pages. Details returns to its original listing, then Back returns to More. The rules depend on restored page names, so changing language/recreating Settings retains the proper parent. Toolbar and Android Back use the same handler; subpages also expose the existing toolbar Back button.

More/subpage transitions use a nonblocking 140ms content fade after the new rows are applied, respect system animation settings and cancel cleanly on rapid navigation. Returning to More uses its local rows and saved position, without bootstrapping Home or requesting APIs. Existing catalogs, search and data caches remain intact.

Application changes: new src/Yoru.Core/MobileNavigation.cs; MainActivity.cs integrates parent routing, selection and transitions; versionCode 90309. Regression checks in tests/Yoru.Core.Checks/Program.cs exercise all four parents, selected-tab rules, nested details, restored-page names and unchanged root/tab Back behavior. No navbar styling, streaming, storage or Windows changes.

Validation: 13 portable regression groups pass. Release/signature/package checks are in evidence/more-back-*. No device is connected; native touch/animation behavior is not device-verified. Install over the existing app without uninstalling.

Device checklist: More -> each of My List/Genres/Seasonal/Settings -> toolbar Back and Android Back -> More, with More selected throughout. Open an anime from a subpage, Back to that listing, Back to More. Repeat after switching language/restarting Settings, with rapid taps and animations disabled. Confirm existing lists/progress remain and no Home fetch occurs on return to More.
