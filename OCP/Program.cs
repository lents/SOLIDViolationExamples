//В этом примере класс Shape нарушает OCP, поскольку он не закрыт для изменения.
//Если бы мы хотели добавить новую фигуру, например треугольник, нам нужно было бы изменить метод Area() внутри класса Shape, чтобы он обрабатывал новый тип фигуры.
//Это нарушает принцип, который гласит, что программные объекты (классы, модули, функции и т. д.) должны быть открыты для расширения, но закрыты для модификации.

//Чтобы придерживаться OCP, нам следует спроектировать класс Shape таким образом, чтобы он мог обрабатывать новые фигуры без изменения существующего кода.
//Этого можно достичь, например, с помощью наследования и полиморфизма, позволяя добавлять новые фигуры в качестве подклассов Shape и реализовывать собственные методы расчета площади.
using System;

public enum ShapeType
{
    Circle,
    Square
}

public class Shape
{
    public ShapeType Type { get; set; }

    public double Area()
    {
        switch (Type)
        {
            case ShapeType.Circle:
                return CalculateCircleArea();
            case ShapeType.Square:
                return CalculateSquareArea();
            default:
                throw new InvalidOperationException("Unknown shape type");
        }
    }

    private double CalculateCircleArea()
    {
        // Code to calculate circle area
        return 0; // Placeholder value
    }

    private double CalculateSquareArea()
    {
        // Code to calculate square area
        return 0; // Placeholder value
    }
}

class Program
{
    static void Main(string[] args)
    {
        Shape circle = new Shape { Type = ShapeType.Circle };
        Shape square = new Shape { Type = ShapeType.Square };

        Console.WriteLine("Area of circle: " + circle.Area());
        Console.WriteLine("Area of square: " + square.Area());
    }
}

