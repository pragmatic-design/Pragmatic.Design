using System.Globalization;

namespace Pragmatic.Persistence.Workload;

/// <summary>
///     Deterministic data: the same customers, orders and lines on every machine and run, so the
///     engines' answers can be compared row for row. Guids are drawn from the seeded generator, not from
///     <see cref="Guid.NewGuid" />.
/// </summary>
public sealed class Seed
{
    public const int CustomerCount = 100;
    public const int OrderCount = 10_000;
    public const int LinesPerOrder = 3;

    private static readonly string[] Countries = ["IT", "DE", "FR", "ES", "NL", "AT", "CH", "BE"];
    private static readonly string[] Skus = ["WIDGET", "GADGET", "SPROCKET", "BOLT", "NUT", "GEAR", "LEVER", "PULLEY"];

    public Seed()
    {
        var random = new Random(20261018);
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var customers = new List<Customer>(CustomerCount);
        for (var i = 0; i < CustomerCount; i++)
        {
            customers.Add(new Customer
            {
                Id = NextGuid(random),
                Name = $"Customer {i:D3}",
                Email = $"customer{i:D3}@example.test",
                Country = Countries[i % Countries.Length],
                CreatedAt = epoch.AddDays(i),
            });
        }

        var orders = new List<Order>(OrderCount);
        var lines = new List<OrderLine>(OrderCount * LinesPerOrder);
        for (var i = 0; i < OrderCount; i++)
        {
            var order = new Order
            {
                Id = NextGuid(random),
                CustomerId = customers[random.Next(CustomerCount)].Id,
                Number = "ORD-" + (100000 + i).ToString(CultureInfo.InvariantCulture),
                Status = (OrderStatus)random.Next(4),
                CreatedAt = epoch.AddMinutes(i * 37 + random.Next(30)),
                IsDeleted = random.Next(50) == 0,
            };
            decimal total = 0;
            for (var l = 0; l < LinesPerOrder; l++)
            {
                var line = new OrderLine
                {
                    Id = NextGuid(random),
                    OrderId = order.Id,
                    Sku = Skus[random.Next(Skus.Length)],
                    Quantity = 1 + random.Next(9),
                    UnitPrice = Math.Round((decimal)(5 + random.NextDouble() * 495), 2),
                };
                total += line.Quantity * line.UnitPrice;
                lines.Add(line);
            }

            order.Total = total;
            orders.Add(order);
        }

        Customers = customers;
        Orders = orders;
        Lines = lines;
    }

    public IReadOnlyList<Customer> Customers { get; }

    public IReadOnlyList<Order> Orders { get; }

    public IReadOnlyList<OrderLine> Lines { get; }

    /// <summary>A customer with open orders, the one the paged reads ask for.</summary>
    public Customer BusyCustomer => Customers.OrderByDescending(c => Orders.Count(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open && !o.IsDeleted)).First();

    private static Guid NextGuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }
}
