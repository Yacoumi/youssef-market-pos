## Downloads
- **POS-Server.exe**: for the back-office PC. It holds the database.
- **POS-Till.exe**: for each cashier PC.
- **marketpos.db**: the clean shipping database. Every table is empty.

Publisher: **Homayk Studio**

## What's new in 2.7
- **Activation.** Each computer needs its own activation key.
  - On first start, POS-Server and POS-Till show this computer's **machine code**.
  - Homayk Studio sends back the key for that code.
  - A copy moved to another computer shows a different code and does not open without a new key.
- **Pay** first asks **هل تريد طباعة التذكرة؟** (نعم / لا). The payment is saved either way; the ticket is printed only on نعم.
- **Refresh button** on the cashier page reloads the products.
- **Payment confirmed icon** fixed; it was drawn mirrored.
- **Supplier purchases stay in the Supplier section.**
  - They no longer create products, add stock, or change a product's cost or price.
  - Purchases recorded before this update are kept.
- **Supplier section:** no visible scrollbars. The mouse wheel, touchpad and touch dragging still scroll.
- **Discount (remise)** comes out of the profit in every report. The purchase cost never changes.
  - Example: bought for 20, sells for 30, discount 5 → sold for 25, profit 5, cost still 20.

## Setup
1. **Server PC:**
   1. Copy `marketpos.db` to `%AppData%\MarketPos\`.
   2. Run **POS-Server.exe**.
   3. Enter the activation key when asked.
2. **Each cashier PC:**
   1. Run **POS-Till.exe**.
   2. Enter the activation key for that PC.
   3. In **Settings → Shop server**, enter the server's address, e.g. `192.168.1.20:5000`.
