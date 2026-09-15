## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: an empty database for the server PC (optional).

## What's new in 2.2
- **The database is one file: `marketpos.db`.**
  - Every save goes straight into it. The app no longer keeps data in `marketpos.db-wal`.
  - Leftover `-wal` / `-shm` files from an old database are deleted when the app starts, and never read.
  - A database from an older version is folded into `marketpos.db` automatically the first time 2.2 opens it. No data is lost.
- **Arabic throughout.**
  - POS-Server now uses the shop's language, so notifications, errors and refusals shown on the tills are in Arabic.
  - Stock-movement notes, sale details, cart labels ("منتج", "معلقة"), permission messages, printer errors and exported files are translated.

## Setup
1. **Server PC:** close any running POS-Server first (Task Manager). Put `marketpos.db` in `%AppData%\MarketPos\`, or let POS-Server create an empty one. Then run **POS-Server.exe**.
2. **Server address:** run `ipconfig` on the server PC and note the IPv4 address, e.g. `192.168.1.20`.
3. **Each cashier PC:** run **POS-Till.exe**. In **Settings → Shop server**, enter `192.168.1.20:5000`.

- **Backups:** close POS-Server, then copy `%AppData%\MarketPos\marketpos.db`. That one file is the whole shop.
- **Updates:** replace **both** exes on every computer. The database is kept.
- **Unknown publisher warning:** click **More info → Run anyway**.
- **Back-office password:** starts as **123456**. Change it after the first login.
