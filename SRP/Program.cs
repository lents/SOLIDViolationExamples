//Согласно принципу единой ответственности, у класса должна быть только одна причина для изменения.
//Однако у этого класса есть несколько причин для изменения: если изменится схема базы данных, если изменится формат электронной почты или если изменится механизм печати.
//Следовательно, это нарушает SRP.
public class Order
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
    public string CustomerName { get; set; }
    public decimal TotalAmount { get; set; }

    public void SaveOrderToDatabase()
    {
        // Code to save the order details to the database
    }

    public void SendOrderConfirmationEmail()
    {
        // Code to send order confirmation email to the customer
    }

    public void PrintOrderInvoice()
    {
        // Code to print the order invoice
    }
}
