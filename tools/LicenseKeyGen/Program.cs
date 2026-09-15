using MarketPos.Services;

// Turns a computer's machine code into its activation key.
//   MarketPos-KeyGen.exe XXXX-XXXX-XXXX-XXXX     prints the key
//   MarketPos-KeyGen.exe                         asks for codes one after another

if (!License.IsRequired)
{
    Console.WriteLine("This key generator was built without license.secret, so it cannot make keys.");
    return 1;
}

if (args.Length > 0)
{
    Console.WriteLine(License.KeyFor(args[0]));
    return 0;
}

Console.WriteLine("Market POS - activation keys (Homayk Studio)");
Console.WriteLine("Type or paste the machine code shown on the client's computer. Empty line to quit.");
while (true)
{
    Console.WriteLine();
    Console.Write("Machine code: ");
    var code = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(code)) return 0;
    Console.WriteLine("Activation key: " + License.KeyFor(code));
}
