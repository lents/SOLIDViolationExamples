//В этом примере у нас есть интерфейс IWorker, который определяет методы, связанные с общим поведением работника: Work(), TakeBreak(), ClockIn() и ClockOut().
//Однако, когда мы реализуем этот интерфейс для класса Robot, становится очевидно, что роботы не придерживаются всего поведения, определенного в интерфейсе IWorker.
//Они не делают перерывов, и им не нужно приходить или уходить.

//Заставляя класс Robot реализовывать неприменимые к нему методы, мы нарушаем принцип разделения интерфейса.
//В нем говорится, что клиентов не следует заставлять зависеть от интерфейсов, которые они не используют.
//В этом случае клиент-робот не должен зависеть от таких методов, как TakeBreak(), ClockIn() и ClockOut().

//Чтобы придерживаться ISP, мы могли бы разделить интерфейс IWorker на более мелкие и более специализированные интерфейсы, предназначенные для определенных типов рабочих процессов,
//обеспечиваем, чтобы каждый класс реализовывал только те методы, которые соответствуют его функциональности.
using System;

public interface IWorker
{
    void Work();
    void TakeBreak();
    void ClockIn();
    void ClockOut();
}

public class Worker : IWorker
{
    public void Work()
    {
        Console.WriteLine("Working...");
    }

    public void TakeBreak()
    {
        Console.WriteLine("Taking a break...");
    }

    public void ClockIn()
    {
        Console.WriteLine("Clocking in...");
    }

    public void ClockOut()
    {
        Console.WriteLine("Clocking out...");
    }
}

public class Robot : IWorker
{
    public void Work()
    {
        Console.WriteLine("Working...");
    }

    public void TakeBreak()
    {
        // Robots don't take breaks
        throw new InvalidOperationException("Robots don't take breaks");
    }

    public void ClockIn()
    {
        // Robots don't need to clock in
        throw new InvalidOperationException("Robots don't need to clock in");
    }

    public void ClockOut()
    {
        // Robots don't need to clock out
        throw new InvalidOperationException("Robots don't need to clock out");
    }
}

class Program
{
    static void Main(string[] args)
    {
        IWorker worker = new Worker();
        IWorker robot = new Robot();

        worker.Work();
        worker.TakeBreak();

        robot.Work();
        robot.TakeBreak(); // This will throw an exception
    }
}

