using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Suppliers, deliveries and what is still owed to them.
///
/// A purchase and a payment are separate records on purpose. The purchase says what arrived
/// and what it cost; the payment says when money left. Adding 50 crates of milk on credit
/// increases stock and debt but no cash moves, and the dashboard has to be able to show
/// exactly that.
/// </summary>
public static class SupplierRepository
{
    // ------------------------------ Suppliers ------------------------------

    public static List<Supplier> List(bool includeInactive = false, string? search = null)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (!includeInactive) where.Add("s.is_active = 1");
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(s.name LIKE $q OR s.contact LIKE $q OR s.phone LIKE $q)");
            command.With("$q", $"%{search.Trim()}%");
        }

        // Only Received purchases count towards debt — a cancelled delivery is not owed for.
        command.CommandText = $"""
            SELECT s.id, s.name, s.contact, s.phone, s.email, s.address, s.note, s.is_active,
                   COALESCE((SELECT SUM(CAST(p.total AS REAL)) FROM purchases p
                             WHERE p.supplier_id = s.id AND p.status = 'Received'), 0),
                   COALESCE((SELECT SUM(CAST(sp.amount AS REAL)) FROM supplier_payments sp
                             WHERE sp.supplier_id = s.id), 0)
            FROM suppliers s
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY s.name;
            """;

        var suppliers = new List<Supplier>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            suppliers.Add(new Supplier
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Contact = reader.Str(2),
                Phone = reader.Str(3),
                Email = reader.Str(4),
                Address = reader.Str(5),
                Note = reader.Str(6),
                IsActive = reader.Bool(7),
                // Summed by SQLite as doubles, so brought back to the centime here. Left raw,
                // 0.1 + 0.2 of a delivery paid in full shows as a debt of 0.00000000001 DH:
                // counted in "owed to N suppliers" and given a Pay button.
                TotalPurchased = Math.Round((decimal)reader.GetDouble(8), 2),
                TotalPaid = Math.Round((decimal)reader.GetDouble(9), 2),
            });
        }
        return suppliers;
    }

    public static int Create(Supplier supplier)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO suppliers (name, contact, phone, email, address, note, is_active, created_at)
            VALUES ($name, $contact, $phone, $email, $address, $note, 1, $now);
            SELECT last_insert_rowid();
            """;
        Bind(command, supplier);
        command.WithDate("$now", DateTime.Now);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("added supplier", "Supplier", id, newValue: supplier.Name,
                                  detail: ActivityRepository.Say("added supplier {0}", supplier.Name));
        return id;
    }

    public static void Update(Supplier supplier)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE suppliers SET name = $name, contact = $contact, phone = $phone,
                   email = $email, address = $address, note = $note
            WHERE id = $id;
            """;
        Bind(command, supplier);
        command.With("$id", supplier.Id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("edited supplier", "Supplier", supplier.Id,
            newValue: supplier.Name, detail: ActivityRepository.Say("edited supplier {0}", supplier.Name));
    }

    public static void SetActive(int id, string name, bool active)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE suppliers SET is_active = $active WHERE id = $id;";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated supplier" : "deactivated supplier",
            "Supplier", id, newValue: name, detail: ActivityRepository.Say(active ? "reactivated supplier {0}"
                                                : "deactivated supplier {0}", name));
    }

    /// <summary>
    /// Deletes a supplier, and everything recorded against them.
    ///
    /// <para>
    /// Their deliveries and the payments made to them go too, in one transaction, so what the
    /// shop owes and what it bought never counts a supplier who is no longer on the list. The
    /// stock those deliveries brought stays where it is: the goods are on the shelves, and a
    /// count that dropped because a name was deleted would be wrong about the shop.
    /// </para>
    ///
    /// <para>
    /// It used to hide a supplier with history instead — but the page lists hidden suppliers,
    /// so the row stayed exactly where it was after Remove and could never be got rid of.
    /// </para>
    /// </summary>
    public static bool Delete(int id, string name, out bool removed, out string problem)
    {
        Session.Require(Permission.ManageSuppliers);
        removed = false;
        problem = string.Empty;

        using var connection = Database.Open();
        using var work = connection.BeginTransaction();
        try
        {
            foreach (var sql in new[]
            {
                // The stock their deliveries brought stays, but its moves stop pointing at a
                // delivery that no longer exists: they say whose it was instead.
                """
                UPDATE stock_movements SET reference = $gone
                WHERE reference IN (SELECT 'Purchase #' || id FROM purchases WHERE supplier_id = $id)
                   OR reference IN (SELECT 'Purchase #' || id || ' cancelled' FROM purchases
                                    WHERE supplier_id = $id);
                """,
                // Products remember who they came from. That memory goes; the product stays.
                "UPDATE products SET supplier_id = NULL WHERE supplier_id = $id;",
                """
                DELETE FROM supplier_payments
                WHERE supplier_id = $id
                   OR purchase_id IN (SELECT id FROM purchases WHERE supplier_id = $id);
                """,
                "DELETE FROM purchase_lines WHERE purchase_id IN (SELECT id FROM purchases WHERE supplier_id = $id);",
                "DELETE FROM purchases WHERE supplier_id = $id;",
                "DELETE FROM suppliers WHERE id = $id;",
            })
            {
                using var command = connection.CreateCommand();
                command.Transaction = work;
                command.CommandText = sql;
                command.With("$id", id);
                if (sql.Contains("$gone"))
                    command.With("$gone", Loc.T("Delivery from {0} (supplier deleted)", name));
                command.ExecuteNonQuery();
            }

            ActivityRepository.Record("deleted supplier", "Supplier", id, oldValue: name,
                detail: ActivityRepository.Say("deleted supplier {0}", name), connection: connection);

            work.Commit();
        }
        catch (Microsoft.Data.Sqlite.SqliteException held)
        {
            work.Rollback();
            problem = Loc.T("{0} could not be deleted. ({1})", name, held.Message);
            return false;
        }

        removed = true;
        return true;
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, Supplier s) =>
        command.With("$name", s.Name).With("$contact", s.Contact).With("$phone", s.Phone)
               .With("$email", s.Email).With("$address", s.Address).With("$note", s.Note);

    // ------------------------------ Purchases ------------------------------

    /// <summary>
    /// Records a supplier purchase: the invoice, its lines and any money handed over at the
    /// door, in one transaction. It is kept in the supplier's records only and never touches
    /// Inventory.
    /// </summary>
    public static int RecordPurchase(Purchase purchase, decimal amountPaidNow)
    {
        Session.Require(Permission.ManagePurchases);
        if (purchase.Lines.Count == 0)
            throw new ArgumentException(Loc.T("A purchase needs at least one product line."));

        // What is bought from a supplier stays in the supplier's records. It is not created as
        // a product, not added to stock, and it does not change any product's cost or price:
        // Inventory is only ever changed from Inventory and Add product.
        var lines = purchase.Lines;

        var total = lines.Sum(l => l.LineTotal);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        int purchaseId;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO purchases
                    (supplier_id, invoice_number, purchased_on, due_on, total, method, note,
                     status, received, created_by, created_at)
                VALUES ($supplierId, $invoice, $on, $due, $total, $method, $note,
                        'Received', $received, $by, $now);
                SELECT last_insert_rowid();
                """;
            insert.With("$supplierId", purchase.SupplierId)
                  .With("$invoice", purchase.InvoiceNumber)
                  .WithDate("$on", purchase.PurchasedOn)
                  .WithDate("$due", purchase.DueOn)
                  .WithMoney("$total", total)
                  .With("$method", purchase.Method)
                  .With("$note", purchase.Note)
                  .With("$received", 0)
                  .With("$by", Session.CurrentId)
                  .WithDate("$now", DateTime.Now);
            purchaseId = Convert.ToInt32(insert.ExecuteScalar());
        }

        foreach (var line in lines)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO purchase_lines (purchase_id, product_id, name, quantity, unit_cost, line_total)
                VALUES ($purchaseId, $productId, $name, $qty, $cost, $total);
                """;
            insert.With("$purchaseId", purchaseId)
                  .With("$productId", line.ProductId > 0 ? line.ProductId : null)
                  .With("$name", line.Name)
                  .WithMoney("$qty", line.Quantity)
                  .WithMoney("$cost", line.UnitCost)
                  .WithMoney("$total", line.LineTotal);
            insert.ExecuteNonQuery();
        }

        if (amountPaidNow > 0m)
            InsertPayment(connection, purchase.SupplierId, purchaseId, amountPaidNow,
                          purchase.PurchasedOn, purchase.Method, Loc.T("Paid on delivery"));

        ActivityRepository.Record("recorded supplier purchase", "Purchase", purchaseId,
            newValue: $"{total:0.00} DH",
            detail: ActivityRepository.Say("recorded a {0} purchase from {1}",
                                          $"{total:0.00} DH", purchase.SupplierName),
            connection: connection);

        transaction.Commit();
        return purchaseId;
    }

    public static List<Purchase> ListPurchases(DateRange? range = null, int? supplierId = null,
                                               PaymentStatus? status = null, int limit = 300)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("p.purchased_on >= $from AND p.purchased_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (supplierId is { } sid)
        {
            where.Add("p.supplier_id = $sid");
            command.With("$sid", sid);
        }

        command.CommandText = $"""
            SELECT p.id, p.supplier_id, s.name, p.invoice_number, p.purchased_on, p.due_on,
                   p.total, p.method, p.note, p.status, p.received,
                   COALESCE((SELECT SUM(CAST(sp.amount AS REAL)) FROM supplier_payments sp
                             WHERE sp.purchase_id = p.id), 0)
            FROM purchases p
            JOIN suppliers s ON s.id = p.supplier_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY p.purchased_on DESC, p.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var purchases = new List<Purchase>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                purchases.Add(new Purchase
                {
                    Id = reader.Int(0),
                    SupplierId = reader.Int(1),
                    SupplierName = reader.Str(2),
                    InvoiceNumber = reader.Str(3),
                    PurchasedOn = reader.Date(4),
                    DueOn = reader.DateOrNull(5),
                    Total = reader.Dec(6),
                    Method = reader.Str(7),
                    Note = reader.Str(8),
                    Status = reader.Str(9),
                    Received = reader.Bool(10),
                    Paid = (decimal)reader.GetDouble(11),
                });
            }
        }

        return status is { } want ? purchases.Where(p => p.PaymentStatus == want).ToList() : purchases;
    }

    public static List<PurchaseLine> ListPurchaseLines(int purchaseId)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, product_id, name, quantity, unit_cost FROM purchase_lines
            WHERE purchase_id = $id ORDER BY id;
            """;
        command.With("$id", purchaseId);

        var lines = new List<PurchaseLine>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            lines.Add(new PurchaseLine
            {
                Id = reader.Int(0),
                ProductId = reader.Int(1),
                Name = reader.Str(2),
                Quantity = reader.Dec(3),
                UnitCost = reader.Dec(4),
            });
        }
        return lines;
    }

    /// <summary>
    /// What the shop buys from this supplier, rolled up by product.
    ///
    /// The per-delivery lines answer "what came in the van last Tuesday"; this answers the
    /// question the owner actually negotiates on — what do I buy from them, how much of it,
    /// and what am I paying. The last price is carried separately from the average because a
    /// supplier who has quietly put a price up is exactly what this is for.
    ///
    /// Cancelled deliveries are left out: they were reversed out of stock and out of the
    /// debt, so counting them here would say the shop buys more than it does.
    /// </summary>
    public static List<SupplierGoods> WhatWeBuy(int supplierId)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.product_id,
                   l.name,
                   COALESCE(SUM(CAST(l.quantity AS REAL)), 0),
                   COALESCE(SUM(CAST(l.line_total AS REAL)), 0),
                   COUNT(DISTINCT p.id),
                   MAX(p.purchased_on)
            FROM purchase_lines l
            JOIN purchases p ON p.id = l.purchase_id
            WHERE p.supplier_id = $id AND p.status <> 'Cancelled'
            GROUP BY l.product_id, l.name
            ORDER BY 4 DESC;
            """;
        command.With("$id", supplierId);

        var goods = new List<SupplierGoods>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                goods.Add(new SupplierGoods
                {
                    ProductId = reader.Int(0),
                    Name = reader.Str(1),
                    Quantity = (decimal)reader.GetDouble(2),
                    TotalCost = (decimal)reader.GetDouble(3),
                    Deliveries = reader.Int(4),
                    LastBought = Db.ParseStamp(reader.Str(5)),
                });
            }
        }

        // The most recent unit cost per product, so a price rise is visible next to the
        // average rather than hidden inside it.
        foreach (var item in goods)
        {
            using var latest = connection.CreateCommand();
            latest.CommandText = """
                SELECT l.unit_cost FROM purchase_lines l
                JOIN purchases p ON p.id = l.purchase_id
                WHERE p.supplier_id = $id AND l.name = $name AND p.status <> 'Cancelled'
                ORDER BY p.purchased_on DESC, l.id DESC LIMIT 1;
                """;
            latest.With("$id", supplierId).With("$name", item.Name);
            item.LastUnitCost = Db.ParseMoney(latest.ExecuteScalar() as string);
        }

        return goods;
    }

    /// <summary>
    /// Cancels a delivery — reverses the stock it added and stops it counting towards debt.
    /// The record itself stays, marked Cancelled, because a supplier invoice that vanished
    /// is exactly the kind of hole this system exists to prevent.
    /// </summary>
    public static void CancelPurchase(int purchaseId, string reason)
    {
        Session.Require(Permission.ManagePurchases);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        bool received;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT received, status FROM purchases WHERE id = $id;";
            read.With("$id", purchaseId);
            using var reader = read.ExecuteReader();
            if (!reader.Read()) return;
            received = reader.Bool(0);
            if (reader.Str(1) == "Cancelled") return;   // already done; do not double-reverse
        }

        if (received)
        {
            // Only purchases recorded before supplier goods were kept apart moved stock, and only
            // their lines that pointed at a product.
            foreach (var line in ListPurchaseLines(purchaseId).Where(l => l.ProductId > 0))
                InventoryRepository.Move(line.ProductId, -line.Quantity, StockReason.SupplierReturn,
                    reference: Loc.T("Purchase #{0} cancelled", purchaseId), note: reason,
                    unitCost: line.UnitCost, connection: connection);
        }

        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE purchases SET status = 'Cancelled', received = 0 WHERE id = $id;";
            update.With("$id", purchaseId);
            update.ExecuteNonQuery();
        }

        ActivityRepository.Record("cancelled supplier purchase", "Purchase", purchaseId,
            newValue: reason, detail: ActivityRepository.Say("cancelled purchase #{0}", purchaseId),
            connection: connection);

        transaction.Commit();
    }

    // ------------------------------- Payments -------------------------------

    /// <summary>Records money paid to a supplier. Never edits a previous payment.</summary>
    public static void Pay(int supplierId, string supplierName, decimal amount, DateTime paidOn,
                           string method = "Cash", string note = "", int? purchaseId = null)
    {
        Session.Require(Permission.ManagePurchases);
        if (amount <= 0m) throw new ArgumentException("A payment must be greater than zero.", nameof(amount));

        using var connection = Database.Open();
        InsertPayment(connection, supplierId, purchaseId, amount, paidOn, method, note);

        ActivityRepository.Record("recorded a supplier payment", "Supplier", supplierId,
            newValue: $"{amount:0.00} DH",
            detail: ActivityRepository.Say("recorded a {0} payment to {1}",
                                          $"{amount:0.00} DH", supplierName), connection: connection);
    }

    private static void InsertPayment(Microsoft.Data.Sqlite.SqliteConnection connection,
                                      int supplierId, int? purchaseId, decimal amount,
                                      DateTime paidOn, string method, string note)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO supplier_payments
                (supplier_id, purchase_id, amount, paid_on, method, note, created_by, created_at)
            VALUES ($supplierId, $purchaseId, $amount, $on, $method, $note, $by, $now);
            """;
        command.With("$supplierId", supplierId)
               .With("$purchaseId", purchaseId)
               .WithMoney("$amount", amount)
               .WithDate("$on", paidOn)
               .With("$method", method)
               .With("$note", note)
               .With("$by", Session.CurrentId)
               .WithDate("$now", DateTime.Now);
        command.ExecuteNonQuery();
    }

    public static List<SupplierPayment> ListPayments(DateRange? range = null, int? supplierId = null,
                                                     int limit = 300)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("sp.paid_on >= $from AND sp.paid_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (supplierId is { } sid)
        {
            where.Add("sp.supplier_id = $sid");
            command.With("$sid", sid);
        }

        command.CommandText = $"""
            SELECT sp.id, sp.supplier_id, s.name, sp.purchase_id, sp.amount, sp.paid_on,
                   sp.method, sp.note
            FROM supplier_payments sp
            JOIN suppliers s ON s.id = sp.supplier_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY sp.paid_on DESC, sp.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var payments = new List<SupplierPayment>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            payments.Add(new SupplierPayment
            {
                Id = reader.Int(0),
                SupplierId = reader.Int(1),
                SupplierName = reader.Str(2),
                PurchaseId = reader.IsDBNull(3) ? null : reader.Int(3),
                Amount = reader.Dec(4),
                PaidOn = reader.Date(5),
                Method = reader.Str(6),
                Note = reader.Str(7),
            });
        }
        return payments;
    }

    /// <summary>Total still owed across every supplier — the dashboard's "money owed" figure.</summary>
    public static decimal TotalOwed() => List().Sum(s => s.Owed);
}
