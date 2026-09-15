## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: a clean, empty database for a new client (no products, sales, users or records).

## What's new in 2.3
- **One main page for everyone.**
  - The back office opens inside the till window, next to the same rail, not as a separate window.
  - The admin sees the extra pages and controls their permissions allow; a cashier doesn't.
  - Press **Sale / Items / Tickets** on the rail, or **Back to the till**, to return.
- **Fixed:** the "'Pulse' name cannot be found" error when opening the scan popup from **Add product**. The same fix covers every popup that uses animations or its own styles.

## Also included (from 2.2)
- **Single database file:** the database is only `marketpos.db`. Leftover `-wal` / `-shm` files are deleted at startup and never read.
- **Arabic throughout,** including everything POS-Server sends to the tills.

## New client setup
1. **Server PC:** copy `marketpos.db` to `%AppData%\MarketPos\`, create the folder if needed. Then run **POS-Server.exe**.
2. **Server address:** run `ipconfig` on the server PC and note the IPv4 address, e.g. `192.168.1.20`.
3. **Each cashier PC:** run **POS-Till.exe**. In **Settings → Shop server**, enter `192.168.1.20:5000`.

- **Updating an existing install:** close POS-Server in Task Manager first, then replace **both** exes.
- **Back-office password:** starts as **123456**. Change it after the first login.
