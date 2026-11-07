# Ten Percent Discount Pharmacy — Stock Manager

A Windows desktop program that watches your pharmacy stock and tells you when a medicine
falls below the shelf level you set, so nothing quietly runs out.

You set a shelf level once — "always keep 5 packs of Velosef 500mg" — and the program
checks every medicine against its level automatically, pops up a Windows notification the
moment one drops below, and builds a buying list grouped by distributor.

---

## The most important thing

**This program never writes to SeaSoft. It only reads.**

Everything it saves — your shelf levels, the buying list, alert history — goes into its
own separate file on the PC. SeaSoft's database cannot be changed, corrupted or locked
by this program. That separation is deliberate and is the whole safety design.

---

## Running it

1. Copy **`TenPercentStockManager.exe`** anywhere on the Windows PC (Desktop is fine).
2. Double-click it.
3. The program window opens.

Nothing to install. No .NET, no database, no browser, no setup — the whole program is
inside that one file. It is an ordinary Windows application: a normal window, normal
menus, and an icon in the notification area next to the clock.

**The first time only**, Windows shows a blue **"Windows protected your PC"** box,
because the program is not from a big publisher. Click **More info** → **Run anyway**.

### It keeps watching after you close the window

Pressing **X** shrinks the program to the notification area (next to the clock) and it
carries on checking stock and raising alerts. Double-click its green cross icon to open
it again, or right-click it and choose **Exit** to stop it completely.

You can turn that behaviour off in Settings, and you can have it start automatically with
Windows.

---

## First-time setup — about ten minutes

The first time it runs, it offers to set up your shelf levels. Say yes.

You do not have to type thousands of numbers. The program reads each medicine's own sales
history and calculates a sensible level:

> average daily sales × how long your distributor takes to deliver × a safety margin

Dead stock — anything with no sale for six months — is switched off automatically, so the
board is not filled with medicines nobody buys.

Then look at the **Fast movers** table on the **Shelf Levels** tab and hand-adjust your
top sellers. Those are the ones worth getting exactly right.

To edit a lot at once, press **Download spreadsheet**, change the `ShelfLevelPacks`
column in Excel, save as CSV, and press **Load edited file**.

---

## The six tabs

| Tab | What it is for |
|---|---|
| **Dashboard** | How many medicines are out, how many are short, what changed recently. Click any of the four big numbers to jump to that list. |
| **Stock Board** | Every medicine with its shelf level. Click a Shelf level box, type a number, press Enter. |
| **Shelf Levels** | Set them all automatically, or edit your fast movers by hand. Excel import/export. |
| **Buying List** | What to order, grouped by distributor. **Copy order for WhatsApp** puts the whole order on the clipboard, ready to paste into a chat with your supplier. Double-click a row to mark it ordered, then received. |
| **Alerts** | Only *changes* are listed. A medicine that stays short is not repeated every minute. |
| **Settings** | Stock source, how often to check, when to warn, start with Windows. |

### Why a warning sometimes does not clear immediately

If the shelf level is 5 and stock comes back to exactly 5, the warning stays until stock
reaches 6. Without that, a medicine sitting right on its level would flip between
"running short" and "fine" all day as customers buy it. The gap is adjustable in Settings.

---

## Where your data lives

```
C:\Users\<you>\AppData\Local\TenPercentStockManager\stock.db
```

Your shelf levels, buying list and alert history. The exact path is shown on the Settings
tab, with a button that opens the folder. It is kept outside the program folder on
purpose, so you can replace the `.exe` with a newer version without losing anything.

**Copy that file to a USB stick now and then.** It is the only thing that cannot be
rebuilt.

If the program ever misbehaves, `log.txt` sits in the same folder.

---

## Connecting SeaSoft

Right now the program runs on **demo data** — sample medicines, so you can see it working
and set up all your shelf levels before anything is connected. The shelf levels you set
now are kept and will apply to your real medicines.

There are two ways to connect the real thing:

### Option A — read the SeaSoft database directly (best)

Stock updates by itself within a minute of a purchase being entered in SeaSoft. This
needs to know which database engine SeaSoft uses. See
[`docs/CONNECTING-SEASOFT.md`](docs/CONNECTING-SEASOFT.md) for exactly what to collect
and how.

### Option B — a CSV exported from SeaSoft (works even if the vendor says no)

Export the stock report from SeaSoft to CSV, then on the **Settings** tab choose
*CSV / Excel file* and browse to the file. Column headings are matched loosely, so most
export formats work without editing. You refresh by re-exporting over the same file.

This means the project cannot be blocked: if the SeaSoft vendor refuses database access,
Option B still gives you the whole program.

---

## For a developer

.NET 8. Two projects plus a self-check:

```
src/PharmacyStock.Core/        Engine - no UI, no web, cross-platform
  Models/      Medicines, shelf levels, alerts, orders
  Providers/   Where stock is read from (IStockProvider)
                 DemoStockProvider  - sample data
                 CsvStockProvider   - a CSV/Excel export from SeaSoft
                 (SeasoftProvider)  - added once the database engine is known
  Data/        SQLite store for OUR data only
  Services/    Sync, alert state machine, shelf-level advisor, buying list
  Diagnostics/ File logging

src/PharmacyStock.Desktop/     Windows UI (WinForms), built entirely in code
  MainForm.cs  Window, tabs, tray icon, sync timer, notifications
  Ui/          One class per tab, plus shared theme and formatting

tests/PharmacyStock.SelfCheck/ End-to-end check of the engine, prints pass/fail
```

**Adding the SeaSoft connection is deliberately a one-file job.** Write a class
implementing `IStockProvider`, then add one line to `ProviderFactory.Resolve()`. Nothing
else changes.

### Build

```bash
./build/build-windows.sh
```

Runs the self-check, then produces `dist/TenPercentStockManager.exe` — self-contained,
single file, no runtime needed on the target PC. Cross-compiles from Linux.

> Note: Ubuntu's .NET SDK ships without the Windows Desktop SDK, so the usual
> `<UseWindowsForms>` switch cannot be used. The desktop project references
> `Microsoft.WindowsDesktop.App.WindowsForms` directly and sets `EnableWindowsTargeting`,
> which produces exactly the same result.

### Run the engine checks on their own

```bash
dotnet run --project tests/PharmacyStock.SelfCheck
```
