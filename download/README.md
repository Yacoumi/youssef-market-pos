## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: the clean shipping database. All tables are present and every table is empty.

## Fixes in 2.5
1. **Back-office sidebar opens its pages again.** The date buttons (Today, Yesterday…) work again too.
2. **Replacing `marketpos.db` now really gives a fresh shop.**
   - POS-Server no longer keeps the database file open between requests, so Windows allows it to be deleted or replaced while the server runs.
   - Leftover `-wal`/`-shm` files are checked and deleted on every open.
   - On first start the app only adds its list of default expense category names for the expense dropdown. It adds no products, sales, users or any other records.
3. **The on-screen keyboard button appears again** on PCs where Windows doesn't report a touchscreen. To hide it on a PC, set `"OnScreenKeyboard": false` in `settings.json`.

## New client setup
1. **Server PC:** copy `marketpos.db` to `%AppData%\MarketPos\`. Then run **POS-Server.exe**.
2. **Server address:** run `ipconfig` on the server PC and note the IPv4 address, e.g. `192.168.1.20`.
3. **Each cashier PC:** run **POS-Till.exe**. In **Settings → Shop server**, enter `192.168.1.20:5000`.

- **Updating an existing install:** end **POS-Server** and **POS-Till** in Task Manager, then replace both exes.
- **Back-office password:** starts as **123456**.
