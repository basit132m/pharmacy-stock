# Connecting the SeaSoft database

The program is built and working on demo data. This document is the remaining step:
pointing it at the real stock in SeaSoft.

Nothing here changes SeaSoft. Every step is read-only.

---

## The setup we are working with

- Two PCs at the counter: one **server**, one **client**
- Both run SeaSoft
- The database lives on the **server** PC only; the client talks to it over the LAN

Because it is a server/client setup, SeaSoft is almost certainly using a real database
engine (SQL Server Express, Firebird or MySQL) rather than a plain file.

---

## The safe way to do this

Work on a **copy**, not the live database.

| Phase | What happens | Risk to the pharmacy |
|---|---|---|
| 1 | Take a SeaSoft backup, restore it onto a spare laptop | None |
| 2 | Build and test the connection against that copy | None — break it freely, restore again |
| 3 | Point the finished app at the live server, read-only | Minimal — one connection string changes |

Nothing is ever installed on the server PC. The program runs on the client PC or a spare
machine, and only reads.

---

## Four things that must never be done on the live PC

1. Never double-click a database file (`.mdb`, `.fdb`, `.mdf`) — Access and similar tools
   can silently upgrade or lock it
2. Never use any "Repair", "Compact" or "Rebuild Database" option
3. Never stop or restart a SQL / Firebird / MySQL service
4. Never open the database read/write from any tool

---

## Step 0 — take a backup first

In SeaSoft, find **Backup** or **Utilities → Backup Database**. Run it and save the file
to a USB drive. Repeat weekly.

Worth doing even if this project never finishes. If the pharmacy's data sits on one PC
with no backup, a dead hard disk costs the entire history.

---

## Step 1 — the client PC (safe, five minutes)

The client holds a configuration file naming the server, the database and often the
login. Reading it touches nothing.

Run PowerShell **as administrator** and paste:

```powershell
$out = "$env:USERPROFILE\Desktop\seasoft-client.txt"
"=== FOLDERS ===" | Out-File $out
$d = Get-ChildItem C:\,'C:\Program Files','C:\Program Files (x86)','C:\ProgramData' -Directory -Force -ErrorAction SilentlyContinue | Where-Object {$_.Name -match 'sea|pharm|medic|stock'}
$d | Select-Object FullName | Out-File $out -Append -Width 300
"=== FILE LIST ===" | Out-File $out -Append
$d | ForEach-Object { Get-ChildItem $_.FullName -Force -ErrorAction SilentlyContinue | Select-Object Name,Length,LastWriteTime } | Out-File $out -Append -Width 300
"=== CONFIG CONTENTS ===" | Out-File $out -Append
$d | ForEach-Object { Get-ChildItem $_.FullName -Recurse -Force -ErrorAction SilentlyContinue -Include *.ini,*.udl,*.dsn,*.config,*.cfg,*.xml | Where-Object {$_.Length -lt 50000} } | ForEach-Object { "--- $($_.FullName) ---" | Out-File $out -Append; Get-Content $_.FullName -ErrorAction SilentlyContinue | Out-File $out -Append }
notepad $out
```

**Before sharing the result, replace any `Password=` or `PWD=` value with `XXXXX`.**
The structure is what matters, never the password itself.

---

## Step 2 — the server PC (after closing time)

This scans the whole disk, which is harmless but uses disk activity — so do it when
nobody is billing.

```powershell
$out = "$env:USERPROFILE\Desktop\seasoft-info.txt"
"=== SERVICES ===" | Out-File $out
Get-Service | Where-Object {$_.Name -match 'sql|firebird|mysql|maria|postgres|interbase|btrieve|pervasive'} | Select-Object Name,DisplayName,Status | Out-File $out -Append
"=== PROGRAMS ===" | Out-File $out -Append
Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*,HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -match 'sea|sql|firebird|mysql|access|crystal|pharm'} | Select-Object DisplayName,DisplayVersion,InstallLocation | Out-File $out -Append -Width 300
$f = Get-ChildItem C:\ -Recurse -Force -ErrorAction SilentlyContinue -Include *.mdb,*.accdb,*.fdb,*.gdb,*.mdf,*.dbf,*.db,*.ini,*.udl,*.dsn | Where-Object {$_.FullName -notmatch '\\Windows\\|\\AppData\\'}
"=== DATABASE FILES ===" | Out-File $out -Append
$f | Where-Object {$_.Extension -match 'mdb|accdb|fdb|gdb|mdf|dbf|db'} | Select-Object FullName,Length,LastWriteTime | Out-File $out -Append -Width 300
"=== ODBC ===" | Out-File $out -Append
Get-ItemProperty 'HKLM:\SOFTWARE\ODBC\ODBC.INI\ODBC Data Sources' -ErrorAction SilentlyContinue | Out-File $out -Append
notepad $out
```

Also note the server's `hostname` and `ipconfig` output.

---

## Step 3 — ask the vendor

Longest lead time, so send it first.

> **Subject: Request for read-only database access — Ten Percent Discount Pharmacy**
>
> Dear SeaSoft Support,
>
> We are a licensed user of your software at Ten Percent Discount Pharmacy,
> licence/customer ID [____].
>
> We would like to build a small internal tool for our own use that shows which medicines
> have fallen below our shelf level, so we can reorder on time. This tool will only
> **read** data. It will never insert, update or delete anything in your database.
>
> Could you please provide:
>
> 1. A **read-only** database user account for our SeaSoft database
> 2. Which database engine and version SeaSoft uses, and the connection details
> 3. The names of the tables/columns for the item master and current stock quantity
> 4. Confirmation that read-only access does not affect our support agreement
>
> If you prefer, we are also happy to use an export or API that you recommend instead.
>
> Thank you,
> [Name] — [Phone]

If they decline, the CSV option in Settings still gives the full program.

---

## Step 4 — screenshots from inside SeaSoft

Safe: just open each screen and press **PrtScn**. Save nothing, change nothing.

| Screen | What it settles |
|---|---|
| Item master for one medicine (e.g. Velosef 500mg) | How pack size, units and conversion are stored |
| Stock report / Stock in hand | Whether quantity is in packs or tablets, and whether it is batch-wise |
| Purchase entry (just open it) | Which table and fields a purchase writes to |
| Sale / bill entry (just open it) | Same, for sales |
| Item ledger for one medicine | How transactions are totalled |
| Reports menu | Which exports already exist |
| Help → About | Version and vendor contact |

---

## The two questions that decide the work

### 1. Is stock stored, or calculated?

- **Stored** — a column such as `Items.CurrentStock`. Easy: read it, and our number can
  never disagree with SeaSoft's.
- **Calculated** — opening + purchases − sales − purchase returns + sales returns ±
  adjustments. Then the arithmetic must match SeaSoft's *exactly*, including how it
  treats returns, damages and transfers. This is the highest-risk part of the project.

### 2. Packs, strips or tablets?

Pharmacy systems usually store the smallest unit (tablets) plus a conversion factor, and
often track loose quantity separately. The app is already built for this — it holds
`QtyBaseUnits` and `UnitsPerPack` and converts to packs for display. We just need to know
which columns carry them.

Stock is normally batch-wise too (same medicine, several batches and expiries). Shelf
levels are per medicine, so stock is the sum across batches.

---

## How the live connection will work

Polling, not copying. The app asks SeaSoft "anything newer than the last transaction ID
I saw?" every 60 seconds and recomputes only the medicines affected. A full reconcile
runs nightly.

SeaSoft stays the single source of truth; there is no second copy of the ledger to drift
out of step. Push notifications (SQL Server Change Tracking) are deliberately *not* used
— they would require enabling features inside the vendor's database, which breaks the
read-only rule.

---

## What implementing it looks like in this codebase

Deliberately a one-file job:

1. Add `src/PharmacyStock.App/Providers/SeasoftStockProvider.cs` implementing
   `IStockProvider` — one method returning the medicine list with current stock.
2. Add one line to `ProviderFactory.Resolve()`:
   ```csharp
   "seasoft" => _seasoft,
   ```
3. The connection string, and the SQL itself, live in a config file rather than compiled
   in — so a SeaSoft upgrade that renames a table is a text edit, not a rebuild.

Nothing else in the application changes. The dashboard, alerts, buying list and shelf
levels all work the same regardless of where the stock figures came from.

---

## Before trusting it

Open SeaSoft's own stock report and this program side by side, and check 20–30 medicines
agree exactly — including a few with loose quantities and a few with several batches.

If the board says 3 and SeaSoft says 0, staff will stop trusting the app within a week.
This check is not optional.
