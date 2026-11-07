# Connecting the SeaSoft database

The program can now read live stock straight out of SeaSoft. This is how.

**Everything described here is read-only.** The program refuses to send anything but a
`SELECT` to SeaSoft — writes, deletes, drops and even a second statement hidden behind a
semicolon are blocked inside the program before they reach the server.

---

## Where to do it in the program

**Settings tab → "Connect to SeaSoft..."**

That opens a three-step screen:

| Step | What you do |
|---|---|
| **1. Connect** | Pick the database type, type the server name and login, press **Test connection**. |
| **2. Find your medicines** | Click down the list of tables and watch the real rows appear on the right. When you recognise your own medicines, you have found it. |
| **3. Match the columns** | The program fills this in by itself from the column names. Check the top three, press **Preview what will be read**, then **Save and use SeaSoft**. |

Nothing is saved until the program has successfully read your medicines, so you cannot
end up switched over to a setup that does not work.

---

## What you need before you start

Three things, all from the SeaSoft vendor:

1. **Which database** SeaSoft uses — SQL Server, MySQL, Firebird or Access
2. **A read-only login** — username and password
3. **The database name** (or the database file's location, for Access and Firebird)

The email to ask for these is at the bottom of this page.

### If you do not know which database it is

You can often find out in five minutes. On the **server** PC, open PowerShell as
administrator and paste:

```powershell
Get-Service | Where-Object {$_.Name -match 'sql|firebird|mysql|maria|postgres|interbase'} | Format-Table Name,DisplayName,Status
```

| What you see | Choose in step 1 |
|---|---|
| `MSSQLSERVER` or `MSSQL$SQLEXPRESS` | Microsoft SQL Server |
| `MySQL` or `MariaDB` | MySQL / MariaDB |
| `FirebirdGuardianDefaultInstance` | Firebird |
| Nothing at all | Probably an Access file — search the SeaSoft folder for `.mdb` or `.accdb` |

To find a file-based database:

```powershell
Get-ChildItem C:\ -Recurse -Force -ErrorAction SilentlyContinue -Include *.mdb,*.accdb,*.fdb,*.gdb | Select-Object FullName,Length,LastWriteTime
```

The SeaSoft folder also usually holds a `.ini` or `.udl` file with the server name and
database in plain text. Opening it in Notepad changes nothing.

---

## Step 1 — Connect

Fill in only the boxes that light up for your database type.

| Database type | What to enter |
|---|---|
| **SQL Server** | Server PC name, often with an instance: `COUNTER-PC\SQLEXPRESS`. Database name. Either tick *Sign in as the current Windows user*, or enter the read-only username and password. |
| **MySQL / MariaDB** | Server PC name or IP, database name, username, password. Port only if it is not 3306. |
| **Firebird** | Server PC name, and the **full path of the .fdb file as seen by the server PC**. Username is usually `SYSDBA`. |
| **Access** | Just browse to the `.mdb` / `.accdb` file. Leave the rest blank. |
| **ODBC** | A DSN name you have already set up in Windows, or a full ODBC connection string. |

Press **Test connection**. On success it tells you how many tables it found and moves you
to step 2.

### If the test fails

The message tells you which problem it is:

- *"Could not reach the server PC"* — the server is off, the name is wrong, or the
  database is not accepting connections from other PCs on the network. For SQL Server
  Express this usually means TCP/IP is disabled; the vendor can switch it on.
- *"The database refused the username or password"* — ask the vendor for the read-only
  login.
- *"Windows does not have the Access database driver installed"* — install the free
  *Microsoft Access Database Engine 2016 Redistributable* (64-bit).
- *"The login does not have permission"* — the login exists but cannot read that table.

---

## Step 2 — Find your medicines

This is the step that removes the guesswork, and you can do it without the vendor telling
you anything about the schema.

The table list is on the left, with likely-looking names (anything containing *item*,
*medicine*, *product*, *drug*, *stock*, *inventory*) pushed to the top. Internal
bookkeeping tables are hidden.

Click a table and 25 real rows appear on the right. Work down the list until you see your
own medicines with a quantity beside them. That is the table.

**Prefer a view if there is one.** Many systems provide a ready-made
`VW_STOCK_IN_HAND` or similar that already joins the item list to the stock balance.
That is the easiest and safest thing to read.

---

## Step 3 — Match the columns

The program reads the column names and fills this in by itself. It gets common pharmacy
schemas right first time. Only three matter:

- **Medicine name** — required
- **Stock quantity** — required
- **Item code** — strongly recommended, because this is what your shelf levels are tied
  to. Without it the program falls back to row numbers, and your shelf levels will not
  survive a change in row order.

Everything else is optional and only makes the screens more useful.

### The one thing to check carefully: packs versus tablets

Pharmacy systems normally store stock in the **smallest unit** — tablets, capsules, ml —
plus a pack size. The program handles that: point *Units in one pack* at the pack-size
column and it shows "3 packs + 1 loose" instead of "3.08 packs".

If your system already stores whole packs, tick **"This quantity is already in packs"**.

Get this wrong and the alerts are nonsense, so press **Preview what will be read** and
check the sample against a medicine whose shelf you can actually see.

### If stock is calculated, not stored

Some systems have no stock column at all — the balance is
*opening + purchases − sales − purchase returns + sales returns ± adjustments*.

Tick **"Advanced: write my own SELECT"** and write the query that produces it, for
example:

```sql
SELECT i.ITEM_CODE, i.ITEM_NAME, i.PACK_SIZE,
       COALESCE(SUM(p.QTY), 0) - COALESCE(SUM(s.QTY), 0) AS STOCK_QTY
FROM ITEMMASTER i
LEFT JOIN PURCHASE_DETAIL p ON p.ITEM_CODE = i.ITEM_CODE
LEFT JOIN SALE_DETAIL     s ON s.ITEM_CODE = i.ITEM_CODE
GROUP BY i.ITEM_CODE, i.ITEM_NAME, i.PACK_SIZE
```

Press **Check this query** and the column list refreshes from the result, ready to match.

This arithmetic must match SeaSoft's own exactly — including how it treats returns,
damages and branch transfers. It is the highest-risk part of the whole setup.

---

## Before you trust it

Open SeaSoft's own stock report and this program side by side, and check **20–30
medicines agree exactly** — including a few with loose quantities and a few with several
batches.

If the board says 3 and SeaSoft says 0, staff will stop trusting the app within a week.
This check is not optional.

---

## Why this is safe

| Protection | What it does |
|---|---|
| **SELECT only** | Every statement is inspected before it is sent. Writes, drops, `EXEC`, `SELECT INTO`, and anything after a semicolon are refused — including a query you type yourself in the Advanced box. |
| **A separate database** | Shelf levels, buying list and alert history live in the program's own file under `%LOCALAPPDATA%`. Nothing is added to SeaSoft. |
| **Read-only login** | Asking the vendor for one means the database itself refuses writes, independently of this program. |
| **Access opened read-only** | Access files are opened with `Mode=Read`, so SeaSoft is never locked out of its own file. |
| **Password not in plain text** | The saved password is encrypted with your Windows login's own key. |
| **Short timeouts** | If the server PC is off, the program says so in ten seconds rather than freezing the counter screen. |
| **Last good figures kept** | If a read fails mid-afternoon, the board keeps showing the last known numbers with a warning, rather than going blank. |

A backup is still worth taking before you start. In SeaSoft, look for
**Backup** or **Utilities → Backup Database**, and keep a copy on a USB drive.

---

## Still worth asking the vendor

Send this. It is the step with the longest waiting time.

> **Subject: Request for read-only database access — Ten Percent Discount Pharmacy**
>
> Dear SeaSoft Support,
>
> We are a licensed user of your software at Ten Percent Discount Pharmacy,
> licence/customer ID [____].
>
> We have an internal tool that shows which medicines have fallen below our shelf level,
> so we can reorder on time. It only **reads** data — it never inserts, updates or
> deletes anything in your database.
>
> Could you please provide:
>
> 1. A **read-only** database user account for our SeaSoft database
> 2. Which database engine and version SeaSoft uses, and the connection details
> 3. The name of the table or view holding the item list and current stock quantity
> 4. Confirmation that read-only access does not affect our support agreement
>
> Thank you,
> [Name] — [Phone]

If they decline, the **CSV option** in Settings still gives you the whole program: export
the stock report from SeaSoft to a file and point the program at it.

---

## For a developer

```
src/PharmacyStock.Core/Providers/Seasoft/
  SeasoftConnection.cs     engine choice, connection strings, identifier quoting
  SchemaBrowser.cs         list tables, list columns, preview rows
  SeasoftMapping.cs        which column is which, plus the auto-guess
  SeasoftStockProvider.cs  reads MedicineStock rows; IStockProvider implementation
  SqlGuard.cs              refuses anything that is not a single SELECT
  Secrets.cs               DPAPI protection for the saved password

src/PharmacyStock.Desktop/Ui/SeasoftSetupForm.cs    the three-step screen
```

Supported engines: SQL Server, MySQL/MariaDB, Firebird, Access (OLE DB) and ODBC.
Adding another is a new `case` in `SeasoftConnection.CreateConnection`.

`SqlGuard` strips comments and quoted text before checking, so a medicine named
"Update Syrup" is fine while `/* hide */ DELETE ...` is not. Its rules are covered by
`tests/PharmacyStock.SelfCheck`.

The whole path — connect, browse, guess, read, feed the alert engine — was tested against
a real MariaDB server carrying a pharmacy-shaped schema and a read-only login.
