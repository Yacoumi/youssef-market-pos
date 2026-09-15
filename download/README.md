## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: an empty database for the server PC (optional).

## What's new
- **Everything opens inside the same page.** Forms like add product, pay, expense, confirm and settings open over the current screen, not as separate windows.
- **Supplier purchases stay off the till.** Goods from a supplier go into stock only. A product reaches the cashier when you save it through **Add product**.
- **Arabic everywhere.** Pay, confirm, refund, sign-out, supplier and delivery messages are all translated.
- **Simple add/edit product form:** barcode, name, category, bought for, selling for, quantity, expiry, profit.
- **Expenses show up after saving.** The date problem is fixed, and so is the empty "paid by" box.
- **Date ranges from a till are correct.** "Yesterday" no longer includes today.

## Setup
1. **Server PC:** put `marketpos.db` in `%AppData%\MarketPos\`, create the folder if needed. Then run **POS-Server.exe** and allow it through the firewall.
2. **Server address:** run `ipconfig` on the server PC and note the IPv4 address, e.g. `192.168.1.20`.
3. **Each cashier PC:** run **POS-Till.exe**. In **Settings → Shop server**, enter `192.168.1.20:5000`.

- **Backups:** copy `%AppData%\MarketPos\marketpos.db` from the server PC.
- **Updates:** replacing the exes never touches the database.
- **Unknown publisher warning:** click **More info → Run anyway**.
- **Back-office password:** starts as **123456**. Change it after the first login.
