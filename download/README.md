## 1. Server PC (back office): **POS-Server.exe** + **marketpos.db**
1. Put **marketpos.db** in this folder (type it into the Explorer address bar):
   `%AppData%\MarketPos\`
   Create the `MarketPos` folder if it isn't there.
2. Run **POS-Server.exe**. It has no window. It serves port 5000 and stores all the shop's data in that database.
   Say **Yes** when Windows asks to allow it through the firewall.
3. Find this PC's address with `ipconfig` (IPv4, e.g. `192.168.1.20`). Give it a fixed address on the router.
4. To start it automatically, put a shortcut to POS-Server.exe in `shell:startup`.

*If you skip marketpos.db, POS-Server creates the same empty database on first run.*

## 2. Each cashier PC: **POS-Till.exe**
Run it. In **Settings → Shop server**, enter the server PC's address, e.g. `192.168.1.20:5000`.
The till stores nothing important itself. Sales go to the server's database.

## Notes
- **Backups:** copy `%AppData%\MarketPos\marketpos.db` from the server PC.
- **Updates:** replacing the exes never touches the database.
- **Unknown publisher warning:** click **More info → Run anyway**.
- **Back-office password:** starts as **123456**. Change it after the first login.
