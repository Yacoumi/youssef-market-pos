## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: the clean shipping database. Every table is empty.

Publisher: **Homayk Studio**

## What's new in 3.4
- **Removing a product now asks first:** "هل تريد إزالة … من المتجر؟" نعم / لا.
  - Before, one tap on the red remove button took the product off the till with no warning.
  - This applies on both the Inventory list and the Add product list.
  - Putting a removed product back (Inventory → Show removed) does not ask.

## From 3.3
- **Find and install any printer:** Settings → Receipt printer → **Find printers**. It scans for:
  - printers already installed,
  - USB printers plugged in without a driver,
  - USB printer ports with no printer set up,
  - receipt printers and office printers on the shop's network.
- **Install** sets the printer up automatically:
  - Windows (and Windows Update) is asked for the driver first.
  - A receipt printer with no driver is set up on Windows' built-in receipt driver.
  - Office network printers are set up with Windows' own IPP driver.
  - Windows asks for permission; press **Yes**.
- **Install a driver from a file** takes the manufacturer's driver (.inf) from a download or the CD.
- After installing, the printer is selected. Press **Test print**, then **Save**.

## From 3.2
- **Worker accounts:**
  - When you add a worker, you set their password right in the form.
  - They sign in to the back office with their name and that password.
- **What a worker sees in the back office:**
  - They get **Add product**, **Categories** and **Inventory** only.
  - Money, suppliers, workers, sales history and reports stay hidden and blocked.
- To change a worker's password, edit the worker and type a new one. Leave the boxes empty to keep the current password.

## From 3.1
- **Update BOTH POS-Server.exe and POS-Till.exe on EVERY computer.**
  - An older till saved photos only on its own computer, so nobody else could see them.
  - A till older than the server is now told to update instead of losing photos.
- **Every photo sent to the server is logged in `server.log`** ("photo saved for product …").
- **A till no longer stays stuck on an empty picture** after a network hiccup.

## From 3.0
- **Products without a barcode now behave like products with one when stock runs out.**
  - Pressing **+** in the basket past the stock left opens the pop-up asking how many to add to stock.
  - Typing a bigger quantity, or using a weight button, does the same.
  - Before, these were only refused at payment.
  - After the stock is added, the basket gets the quantity you asked for.

## From 2.9
- **On-screen keyboard fixed on POS-Till.** The keyboard button no longer disappears after the activation window closes.
- **Photos fixed.**
  - Product and category pictures added from a till are now sent to the server and saved in the `Images` folder next to `marketpos.db`.
  - Every till shows them.
  - Products **without a barcode** can now have a photo.

## From 2.8
- **No AppData at all.** Everything the software keeps sits in the same folder as its exe:
  - `marketpos.db`
  - product photos (`Images`)
  - `settings.json`
  - `license.key`
  - `server.log`
- **POS-Server shows only what is in the `marketpos.db` next to it.** It ignores any old data in AppData.
- **POS-Till shows only what that server sends.** The till stores nothing itself.

## Setup
1. **Server PC:**
   1. Put **POS-Server.exe** and **marketpos.db** in the **same folder**.
   2. Run POS-Server.exe and enter this computer's activation key.
   3. To start fresh at any time, close POS-Server in Task Manager, replace the `marketpos.db` in that folder, and start it again.
2. **Each cashier PC:**
   1. Run **POS-Till.exe** and enter that computer's activation key.
   2. In **Settings → Shop server**, enter the server PC's address, e.g. `192.168.1.20:5000`.

- **Activation after this update:** each computer asks for its key once more, because the key is now kept beside the exe. The same key works.

## Also in this version (from 2.7)
- **Activation:** each computer needs its own key.
- **Pay:** asks هل تريد طباعة التذكرة؟
- **Refresh button** on the cashier page.
- **Payment icon** fixed.
- **Supplier purchases stay in the Supplier section,** without visible scrollbars.
- **Discount** is taken from the profit; the purchase cost never changes.
