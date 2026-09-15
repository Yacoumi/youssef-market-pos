## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: the clean shipping database. Every table is empty.

Publisher: **Homayk Studio**

## What's new in 3.0
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
