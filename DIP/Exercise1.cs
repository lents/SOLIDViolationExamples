namespace DIP
{
    // В этом примере класс OrderService напрямую создает экземпляр класса Logger внутри своего конструктора.
    // Это нарушает принцип внедрения зависимостей, поскольку класс OrderService тесно связан с реализацией класса Logger.

    //В идеале зависимости должны предоставляться классу извне, а не создаваться внутри.
    //Это обеспечивает лучшую гибкость, тестируемость и удобство сопровождения кода.

    //Чтобы придерживаться принципа внедрения зависимостей, мы должны изменить класс OrderService, чтобы он принимал экземпляр Logger через его конструктор,
    //или через установщик свойства, а не создавать его экземпляр внутри себя.
    //Таким образом, класс OrderService становится независимым от конкретной реализации средства ведения журнала и может работать с любым средством ведения журнала, реализующим требуемый интерфейс.
    using System;

    public class OrderService
    {
        private readonly Logger _logger;

        public OrderService()
        {
            _logger = new Logger();
        }

        public void PlaceOrder(string orderDetails)
        {
            // Business logic to place the order

            _logger.Log("Order placed successfully.");
        }
    }

    public class Logger
    {
        public void Log(string message)
        {
            Console.WriteLine($"Logging: {message}");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            OrderService orderService = new OrderService();
            orderService.PlaceOrder("Sample order details");
        }
    }


}
