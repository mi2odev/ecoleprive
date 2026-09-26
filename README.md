# Centre de soutien — gestion d'un centre de cours de soutien

Windows desktop application for the **owner of a private tutoring center** (Future Leaders Academy).
One owner, one account, full access to every module. Everything runs locally on the school PC:
no server, no cloud, no MySQL/XAMPP/Docker.

UI language: French · Currency: DZD · Visual direction: **B · Marine** from the Claude Design prototype
(`project/Centre de soutien.dc.html`, original handoff notes in `project/README-handoff.md`).

## Features

| Area | What the owner can do |
|---|---|
| **Tableau de bord** | Students (total / active / new), teachers (total / active / payments due), courses (active, today's sessions, full groups), today's attendance (present / absent / late), finance (today's and monthly revenue, unpaid balances, teacher payments, expenses, estimated profit), today's sessions, next session, free rooms. |
| **Élèves** | List with search and level / payment filters, Excel export. Profile with direct actions: edit, add payment (receipt printed), enroll / change group / remove from group, apply discount, add documents, deactivate, delete. Tabs: overview, attendance, grades, payment history (reprint / cancel receipts), documents. |
| **Parents, Enseignants, Matières, Cours, Groupes, Salles** | Add / edit / delete. Teacher profile: schedule, courses, students, earnings (percentage, per session or fixed monthly), record payments. Course page: change price, teacher, room, timetable; add/remove students; revenue and attendance. Timetable conflicts (same room or same teacher) are refused. |
| **Emploi du temps, Séances, Présences** | Weekly timetable (Saturday → Thursday), sessions generated from the timetable or created ad hoc, attendance per session (present / absent / late / excused). |
| **Notes, Examens** | Evaluations per group (test, homework, exam; max score, coefficient), grade entry, weighted averages on the configured scale. |
| **Paiements, Paiements enseignants, Dépenses** | Monthly fees per student (course prices − discount), partial payments, numbered receipts, discounts, teacher compensation, expenses by category. |
| **Rapports, Documents** | Monthly financial summary, per-course revenue, unpaid students, teacher pay, attendance and results per group; print / PDF, Excel export. Documents attached to the center, students, teachers or parents. |
| **Paramètres, Mon compte** | Center identity and logo, pricing and payment rules, discount and compensation defaults, attendance and grading rules, receipts, backup / restore / export, language and theme, security (auto-lock delay, session timeout). Owner username, profile, photo and password. |

### Owner productivity

- **Dashboard insights:** an "À traiter" list of prioritized alerts (unpaid fees past the due day, students over the absence threshold, teacher pay due, full groups, attendance not taken, backup overdue, new students without a group), each with a direct link; 6-month charts of revenue / expenses / teacher pay / profit, collection rate, weekly attendance rate and students per level.
- **Parent reminders ("Relances"):** in Paiements, the list of unpaid students with days overdue and a ready-to-send message (templates editable in Paramètres → Messages aux parents): copy, open WhatsApp (wa.me link, local numbers converted with the country code), SMS text, printed reminder letters in one batch, Excel export. After taking attendance, "Prévenir les parents" prepares absence messages the same way.
- **Printable school documents:** report cards (bulletins: averages, rank, mention, attendance, appréciation) for one student or a whole group, enrollment certificates and attendance certificates — print or save as PDF.
- **Excel import** of students (with parents, groups and discounts): downloadable template, preview with errors / warnings (duplicates, full or unknown groups), import in one transaction.
- **Global search (Ctrl+K):** students, parents, teachers, groups, documents and receipts, accent-insensitive.

### Ease of use

- **Actions rapides** on the dashboard (collect a payment, enroll a student, take attendance, add an expense, chase unpaid fees, search) and a **Premiers pas** checklist that guides the setup of a new center (hideable, disappears when done).
- **Help on every page** ("Aide" button in the header), sidebar icons, a "← Retour" button, clear empty states with the next action to take, required fields marked with *, and less-used actions grouped in a **Plus…** menu (nothing removed).
- **Text size** Normal / Grand / Très grand (F1 panel), remembered on the computer.
- **Keyboard shortcuts:** `Ctrl+K` search · `Ctrl+N` new item on the current page · `Ctrl+P` print (reports) · `F5` refresh · `Alt+←` back · `Ctrl+1…9` jump to the main pages · `Ctrl+L` lock · `F1` all shortcuts · attendance: `P` présent, `A` absent, `R` retard, `E` excusé · `Enter` confirms simple dialogs, `Échap` closes them.

There is deliberately **no** user management, roles, permissions, or teacher / staff / accountant login.

## Security

- **Single owner account.** Created on first start as `admin` / `admin`. The owner **must** choose a new password at first login (8+ characters, letters and digits/symbols). The login screen only shows the default username while the default password is still in place.
- **Password hashing:** PBKDF2-HMAC-SHA256, 600 000 iterations, random 128-bit salt, constant-time comparison. Hashes are upgraded automatically if the policy changes. No plain-text password is ever stored.
- **Brute-force protection:** after 5 wrong attempts, login is locked for 1, 2, 4, 8, then 15 minutes.
- **Automatic lock** after N minutes of inactivity (configurable: never, 5, 10, 15, 30, 60). The lock screen asks for the password. **Session timeout:** a session left locked too long ends and requires a full login. Closing the app signs the owner out. `Ctrl+L` locks immediately.
- **Encrypted database:** SQLite with SQLCipher (AES-256). The random 256-bit key is stored in `keys.json`, protected by Windows DPAPI (bound to the Windows user account).
- **Encrypted backups (`.csbak`):** the database copy, images and documents are encrypted (AES-256-GCM). The key is wrapped with the owner's password, so a backup can be restored **on another PC** with the password in use when the backup was made. The current database is kept as `avant-restauration-*.db` before a restore.
- Single instance per Windows session (two copies can't write the database at the same time).

## Architecture

```
src/
  CentreSoutien.Domain          Entities, enums, business rules (billing, teacher earnings, grading). No dependencies.
  CentreSoutien.Application     Service interfaces + read models used by the UI (the seam for a future network version).
  CentreSoutien.Infrastructure  Local implementation: EF Core + SQLite/SQLCipher, PBKDF2, DPAPI key store,
                                backups, file storage, Excel export (ClosedXML), demo data, migrations.
  CentreSoutien.Presentation    MVVM view models (CommunityToolkit.Mvvm), navigation, dialogs. Platform-neutral (net10.0).
  CentreSoutien.Desktop         WPF views, Marine theme (light/dark), converters, Windows services (dialogs, printing),
                                composition root (Microsoft.Extensions.Hosting DI + appsettings.json).
tests/
  CentreSoutien.Tests           xUnit: security, billing, services on a real encrypted database, backups,
                                and view-model flows (login, lock, every page loading with demo data, CRUD flows).
tools/
  publish.ps1                   Self-contained Windows build.
  check_xaml.py                 Static XAML checks (unknown resources, view ↔ view-model naming).
  gen_codebehind.py             Creates the minimal code-behind for new views.
```

- **ONE OWNER → ONE ACCOUNT → FULL ACCESS → ALL MODULES.** `AppSession` is a small state machine (signed out / password change required / active / locked); there is no authorization layer because there is nothing to authorize against.
- The UI only talks to the interfaces in `CentreSoutien.Application.Abstractions`. A future multi-computer version can add an HTTP-backed implementation of these interfaces (and a server hosting the Infrastructure project) without changing view models or views.
- Views are resolved by convention: `FooViewModel` is displayed by `FooView` (`Views/ViewLocator.cs`).
- Database schema changes go through EF Core migrations (`src/CentreSoutien.Infrastructure/Data/Migrations`), applied automatically at startup and after a restore.

## Data location

`%LOCALAPPDATA%\CentreSoutien\` (configurable in `appsettings.json` → `Storage:DataFolder`):

| Path | Content |
|---|---|
| `centre.db` | Encrypted database |
| `keys.json` | Database key (DPAPI-protected) and its password-wrapped copy for backups |
| `Images\`, `Documents\` | Logo, photos, attached documents |
| `Backups\` | Default backup folder (daily automatic backup, 30 kept by default) |
| `Logs\` | Error log |

> Keep backups on an external drive or USB key as well: if the PC's Windows account is lost, `keys.json` can't be decrypted, and only a `.csbak` backup plus the owner's password can recover the data.

## Build and run

Requirements: .NET 10 SDK. The WPF app runs on Windows 10/11 (x64).

```powershell
dotnet build CentreSoutien.slnx
dotnet test tests/CentreSoutien.Tests
dotnet run --project src/CentreSoutien.Desktop
./tools/publish.ps1          # self-contained release in publish/CentreSoutien
```

To try the application with realistic data (50 students, 9 teachers, 11 groups, a month of payments),
open **Paramètres → Sauvegarde et export → Charger des données de démonstration** on an empty database.

On Linux/macOS the whole solution builds (`EnableWindowsTargeting`) and the tests run, but the WPF app itself only runs on Windows.

## Fonts and licences

Public Sans and Newsreader (SIL Open Font License 1.1) are embedded; licence texts are copied next to the executable in `Licenses/`.
