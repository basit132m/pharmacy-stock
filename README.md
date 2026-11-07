# Ten Percent Discount Pharmacy — Stock Manager

A small Windows program that watches your pharmacy stock and tells you when a medicine
falls below the shelf level you set, so nothing quietly runs out.

You set a shelf level once — "always keep 5 packs of Velosef 500mg" — and the program
checks every medicine against its level automatically, raises an alert the moment one
drops below, and builds a buying list grouped by distributor.

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
3. A black window opens showing the address, and your browser opens the stock board.

Nothing to install. No .NET, no database, no setup — the whole program, including its
screens, is inside that one file.

**Keep the black window open.** Closing it stops the program and the alerts. You can
minimise it.

### Two warnings you will see the first time, both normal

| What you see | What to do |
|---|---|
| **"Windows protected your PC"** (blue SmartScreen box) | Click **More info** → **Run anyway**. This appears for any program not bought from a big publisher. |
| **"Allow TenPercentStockManager to communicate on these networks"** (firewall) | Tick **Private networks** and click **Allow access**. This is what lets the other counter PC and your phone open the board. |

### Opening it from another PC or your phone

The black window prints a second address like `http://192.168.1.5:8420`. Type that into
any browser on the shop WiFi and you get the same board. Handy for checking stock from
the other counter without walking over.

---

## First-time setup — about ten minutes

1. Open the **Shelf Levels** page.
2. Press **Work out shelf levels**.

   You do not have to type thousands of numbers. The program reads each medicine's own
   sales history and calculates a sensible level:

   > average daily sales × how long your distributor takes to deliver × a safety margin

   Dead stock — anything with no sale for six months — is switched off automatically, so
   the board is not filled with medicines nobody buys.

3. Look at the **Fast movers** table underneath and hand-adjust your top sellers. These
   are the ones worth getting exactly right.
4. That is it. The **Dashboard** now shows what is out of stock and what is running short.

To edit a lot of levels at once, download the spreadsheet, change the
`ShelfLevelPacks` column in Excel, save as CSV and load it back.

---

## Day to day

- **Dashboard** — how many medicines are out, how many are short, what changed recently.
- **Stock Board** — every medicine with its shelf level. Type in the Shelf level box to
  change it; it saves as soon as you click away.
- **Buying List** — what to order, grouped by distributor. Press **Copy for WhatsApp** and
  paste the order straight into a chat with your supplier. Mark a whole supplier as
  ordered in one click.
- **Alerts** — only *changes* are listed. A medicine that stays short is not repeated
  every minute, so the list stays worth reading.

### Why a warning sometimes does not clear immediately

If the shelf level is 5 and stock comes back to exactly 5, the warning stays until stock
reaches 6. Without that, a medicine sitting right on its level would flip between
"running short" and "fine" all day as customers buy it. The gap is adjustable in
Settings.

---

## Where your data lives

```
C:\Users\<you>\AppData\Local\TenPercentStockManager\stock.db
```

Your shelf levels, buying list and alert history. The exact path is shown on the Settings
page. It is kept outside the program folder on purpose, so you can replace the `.exe`
with a newer version without losing anything.

**Copy that file to a USB stick now and then.** It is the only thing that cannot be
rebuilt.

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

Export the stock report from SeaSoft to CSV, then on the **Settings** page choose
*CSV / Excel file* and give the file path. Column headings are matched loosely, so most
export formats work without editing. You refresh by re-exporting over the same file.

This means the project cannot be blocked: if the SeaSoft vendor refuses database access,
Option B still gives you the whole program.

---

## Settings worth knowing

| Setting | What it does |
|---|---|
| Check stock every | How often to re-read SeaSoft. 60 seconds suits a pharmacy. |
| Clear a warning only once stock is this many packs above the level | The anti-flapping gap described above. |
| Distributor delivery time | Used to calculate suggested shelf levels and expected delivery dates. |
| Safety margin | 1.5 keeps half again more than expected demand. Raise it if you run out too often; lower it to tie up less money in stock. |
| Treat as dead stock after | Medicines with no sale for this long are left switched off. |
| Let other PCs and phones open this board | Turn off to restrict the board to this one PC. |

---

## For a developer

Built with .NET 8. The application is cross-platform; only the published binary targets
Windows.

```
src/PharmacyStock.App/
  Models/      Domain types - medicines, shelf levels, alerts, orders
  Providers/   Where stock is read from (IStockProvider)
                 DemoStockProvider  - sample data
                 CsvStockProvider   - a CSV/Excel export from SeaSoft
                 (SeasoftProvider)  - added once the database engine is known
  Data/        SQLite store for OUR data only
  Services/    Sync loop, alert state machine, shelf-level advisor, buying list
  Api/         HTTP endpoints and the embedded UI
  wwwroot/     The screens - plain HTML/CSS/JS, embedded into the exe
```

**Adding the SeaSoft connection is deliberately a one-file job.** Write a class
implementing `IStockProvider`, then add one line to `ProviderFactory.Resolve()`. Nothing
else changes.

### Build

```bash
./build/build-windows.sh
```

Produces `dist/TenPercentStockManager.exe` — self-contained, single file, no runtime
needed on the target PC. Cross-compiles fine from Linux.

### Run locally while developing

```bash
dotnet run --project src/PharmacyStock.App
```

Set `PHARMACY_NO_BROWSER=1` to stop it opening a browser window.
