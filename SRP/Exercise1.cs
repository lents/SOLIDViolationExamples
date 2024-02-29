using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SRP
{
    // Class representing an Employee
    public class Employee
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }
        // Other properties related to employee data can be added here

        // Method to calculate salary
        public decimal CalculateSalary(DateTime startDate, DateTime endDate)
        {
            // Code to calculate hours worked based on the employee's time records
            decimal hoursWorked = CalculateHoursWorked(startDate, endDate);

            // Example hourly rate
            decimal hourlyRate = 10;

            // Code to calculate salary based on hours worked and hourly rate
            decimal salary = hoursWorked * hourlyRate;

            return salary;
        }

        // Method to calculate hours worked
        private decimal CalculateHoursWorked(DateTime startDate, DateTime endDate)
        {
            // Code to calculate hours worked based on the employee's time records
            return 40; // Placeholder value
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Employee employee = new Employee
            {
                EmployeeId = 1,
                Name = "John Doe",
                Position = "Software Developer"
                // Other employee data can be initialized here
            };

            DateTime startDate = DateTime.Today.AddDays(-7); // Example start date
            DateTime endDate = DateTime.Today; // Example end date

            // Calculate salary
            decimal salary = employee.CalculateSalary(startDate, endDate);

            Console.WriteLine($"Employee {employee.Name} earned ${salary}.");
        }
    }
}
