namespace Fixtures.Opinionated;

public class GodFunctionFixture
{
    public void ProcessOrder(string orderId, string customerId, string productId,
        decimal amount, string currency, string shippingAddress)
    {
        if (orderId == null) throw new ArgumentNullException(nameof(orderId));
        if (customerId == null) throw new ArgumentNullException(nameof(customerId));
        if (productId == null) throw new ArgumentNullException(nameof(productId));
        if (amount <= 0) throw new ArgumentException("Amount must be positive", nameof(amount));
        if (string.IsNullOrEmpty(currency)) throw new ArgumentException("Currency required", nameof(currency));
        if (string.IsNullOrEmpty(shippingAddress)) throw new ArgumentException("Address required", nameof(shippingAddress));

        var order = new { Id = orderId, Customer = customerId };
        Console.WriteLine($"Processing order {orderId}");
        Console.WriteLine($"Customer: {customerId}");
        Console.WriteLine($"Product: {productId}");
        Console.WriteLine($"Amount: {amount} {currency}");
        Console.WriteLine($"Shipping to: {shippingAddress}");

        if (amount > 1000) Console.WriteLine("High value order");
        else if (amount > 500) Console.WriteLine("Medium value order");
        else Console.WriteLine("Standard order");

        if (currency == "USD") Console.WriteLine("USD payment");
        else if (currency == "EUR") Console.WriteLine("EUR payment");
        else if (currency == "GBP") Console.WriteLine("GBP payment");
        else Console.WriteLine($"Other currency: {currency}");

        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"Step {i + 1}");
            if (i == 2) Console.WriteLine("Halfway");
        }

        Console.WriteLine("Order processed");
        Console.WriteLine("Sending confirmation");
        Console.WriteLine("Updating inventory");
        Console.WriteLine("Notifying warehouse");
        Console.WriteLine("Logging audit trail");
        Console.WriteLine("Complete");
    }
}
